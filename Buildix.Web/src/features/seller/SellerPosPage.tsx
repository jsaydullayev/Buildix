import { useEffect, useMemo, useRef, useState } from 'react';
import { useBlocker, useParams } from 'react-router-dom';
import { useQuery, useMutation, useQueryClient, keepPreviousData } from '@tanstack/react-query';
import { useTranslation } from 'react-i18next';
import {
  Search,
  Plus,
  Minus,
  X,
  Package,
  UserPlus,
  Clock,
  Pause,
  Pencil,
  PackagePlus,
  ChevronDown,
  ArrowLeft,
} from 'lucide-react';
import { Button, Spinner, Badge, Modal, useConfirm } from '@/shared/ui';
import { cn } from '@/shared/lib/cn';
import { formatSum, formatQty } from '@/shared/lib/format';
import { unitLabel } from '@/shared/lib/units';
import { useDebounce } from '@/shared/hooks/useDebounce';
import { useAuth } from '@/shared/auth/useAuth';
import { PERMISSIONS, ROLES } from '@/shared/config/permissions';
import type { ApiError } from '@/shared/api/types';
import type { PagedResult } from '@/shared/api/paged';
import { publicMarketApi } from '@/shared/api/auth';
import { categoriesApi, type Product } from '@/features/warehouse/api';
import { shiftsApi } from '@/features/shifts/api';
import { posApi, type PosCustomer, type PosSale } from '@/features/pos/api';
import { mergePending, type PendingLine } from '@/features/pos/pending';
import { useGlobalScanner } from '@/features/pos/useGlobalScanner';
import {
  EMPTY_MIX,
  MIX_ROWS,
  formatMixInput,
  mixPayments,
  mixSumOf,
  money,
  parseMixInput,
  type MixParts,
} from '@/features/pos/mix';
import { ReceiptModal } from '@/features/pos/ReceiptModal';
import { ExternalItemModal } from '@/features/pos/ExternalItemModal';

type Method = 'Cash' | 'Terminal' | 'Mixed' | 'Debt';
const METHODS: { key: string; value: Method }[] = [
  { key: 'cash', value: 'Cash' },
  { key: 'card', value: 'Terminal' },
  { key: 'mixed', value: 'Mixed' },
  { key: 'debt', value: 'Debt' },
];

/** Catalogue page size. Grows by this step each «Показать ещё» (server caps at 200). */
const PAGE_STEP = 40;
const MAX_PAGE_SIZE = 200;

/** Round-up suggestions above the exact total, for the cash "received" chips. */
function cashChips(total: number): number[] {
  if (total <= 0) return [];
  const steps = [10_000, 50_000, 100_000, 500_000];
  const out: number[] = [];
  for (const s of steps) {
    const up = Math.ceil(total / s) * s;
    if (up > total && !out.includes(up)) out.push(up);
  }
  return out.slice(0, 3);
}

/**
 * Parse a cashier-typed quantity. Accepts a comma as the decimal separator
 * (the RU/UZ keyboard habit) and clamps to the column's 3 decimals, so what the
 * cashier sees is exactly what the server stores.
 */
function parseQty(raw: string): number | null {
  const text = raw.replace(',', '.').trim();
  // A blank field is "no answer", not zero. Number('') is 0, and treating that
  // as a quantity would delete the line of anyone who cleared the box to retype.
  if (text === '') return null;
  const n = Number(text);
  if (!Number.isFinite(n) || n < 0) return null;
  return Math.round(n * 1000) / 1000;
}

/**
 * Recompute the receipt totals the same way the server does (SUM of lines −
 * discount, clamped at 0). Used for the optimistic cache patch so the receipt
 * updates on the keystroke rather than after the round-trip.
 */
function withTotals(sale: PosSale, items: PosSale['items']): PosSale {
  const gross = items.reduce((acc, it) => acc + it.totalPrice, 0);
  return { ...sale, items, totalAmount: Math.max(0, gross - sale.discountAmount) };
}

/**
 * Strip a typed phone down to what the server's regex accepts (`^\+?[0-9]{9,15}$`).
 * The field's own placeholder is "+998 __ ___ __ __", so a cashier following it
 * literally would have every attempt rejected on formatting — during a credit
 * sale, with the customer waiting.
 */
function normalisePhone(raw: string): string {
  const digits = raw.replace(/[^\d]/g, '');
  return raw.trim().startsWith('+') ? `+${digits}` : digits;
}

/**
 * Seller register (Касса) — lives inside the seller top-nav shell.
 *
 * The Draft receipt is created lazily by the first added product (same rule as
 * the admin POS), so opening the register never burns a ЧЕК № on an empty sale.
 *
 * Quantity is the register's hot path, so it is an editable decimal field, not
 * a click-per-unit stepper: "30 qop" is one call and "3.5 m" is expressible at
 * all. Every basket mutation patches the react-query cache optimistically and
 * refetches only the sale — the catalogue's stock badges are adjusted in place
 * instead of being re-fetched on every click.
 */
export default function SellerPosPage() {
  const { t, i18n } = useTranslation();
  const confirm = useConfirm();
  const qc = useQueryClient();
  const { hasPermission, hasRole } = useAuth();
  // Line-price override is the «торг» lever. It is the same authority as the
  // admin price edit (audited server-side), so it stays behind sales.edit —
  // a plain cashier does not get it just by standing at the register.
  const canEditPrice = hasPermission(PERMISSIONS.sales.edit);
  const canManageCustomers = hasPermission(PERMISSIONS.customers.manage);

  const [saleId, setSaleId] = useState<string | null>(null);
  const [search, setSearch] = useState('');
  const debouncedSearch = useDebounce(search);
  const [categoryId, setCategoryId] = useState<number | null>(null);
  const [pageSize, setPageSize] = useState(PAGE_STEP);
  const [customer, setCustomer] = useState<PosCustomer | null>(null);
  const [custOpen, setCustOpen] = useState(false);
  const [externalOpen, setExternalOpen] = useState(false);
  const [method, setMethod] = useState<Method>('Cash');
  const [checkoutOpen, setCheckoutOpen] = useState(false);
  const [done, setDone] = useState<PosSale | null>(null);
  const [startError, setStartError] = useState<string | null>(null);
  const [actionError, setActionError] = useState<string | null>(null);
  // Blank, not '0', so the field shows a faint placeholder instead of a zero
  // the cashier has to delete first.
  const [discountInput, setDiscountInput] = useState('');

  const searchRef = useRef<HTMLInputElement>(null);
  const draftRef = useRef<Promise<PosSale> | null>(null);

  /** Create the Draft on demand; concurrent clicks share one request. */
  const ensureSale = async (): Promise<string> => {
    if (saleId) return saleId;
    draftRef.current ??= posApi.createDraft(customer?.id ?? null);
    try {
      const s = await draftRef.current;
      setSaleId(s.id);
      return s.id;
    } catch (e) {
      draftRef.current = null;
      throw e;
    }
  };

  // F2 jumps to the product search, as the design's hint chip promises.
  useEffect(() => {
    const onKey = (e: KeyboardEvent) => {
      if (e.key === 'F2') {
        e.preventDefault();
        searchRef.current?.focus();
      }
    };
    window.addEventListener('keydown', onKey);
    return () => window.removeEventListener('keydown', onKey);
  }, []);

  // A new search / category is a new result set — start it at one page again,
  // otherwise an earlier «Показать ещё» keeps every later query oversized.
  useEffect(() => {
    setPageSize(PAGE_STEP);
  }, [debouncedSearch, categoryId]);

  const categoriesQuery = useQuery({ queryKey: ['categories'], queryFn: categoriesApi.list });
  const productsQuery = useQuery({
    queryKey: ['pos-products', debouncedSearch, categoryId, pageSize],
    queryFn: () => posApi.searchProducts({ page: 1, size: pageSize, search: debouncedSearch, categoryId }),
    placeholderData: keepPreviousData,
  });
  const saleQuery = useQuery({
    queryKey: ['pos-sale', saleId],
    queryFn: () => posApi.getSale(saleId!),
    enabled: !!saleId,
  });
  const draftsQuery = useQuery({ queryKey: ['pos-drafts'], queryFn: posApi.myDrafts });
  // Only for printing "Смена №N" on the receipt — the sale itself is stamped
  // with the shift server-side at creation.
  const shiftQuery = useQuery({ queryKey: ['shift-current'], queryFn: shiftsApi.current });
  // Do'kon nomi — chek header'i uchun (ochiq endpoint, uzoq kesh).
  const { subdomain } = useParams();
  const marketQuery = useQuery({
    queryKey: ['public-market', subdomain],
    queryFn: () => publicMarketApi.getState(subdomain!),
    enabled: !!subdomain,
    staleTime: 30 * 60_000,
  });

  const sale = saleQuery.data;
  /**
   * Skanerdan qo'shilgan, lekin server hali tasdiqlamagan qatorlar.
   *
   * <p>Kassada tezlik hal qiluvchi: mijoz turibdi, kassir esa ketma-ket
   * skanerlaydi. Ilgari har «bip» dan keyin 3 ta so'rov ketma-ket ketardi
   * (kodni izlash → qatorni qo'shish → chekni qayta o'qish) va tovar ekranda
   * faqat uchalasi tugagach ko'rinardi. Endi qator DARHOL chiziladi, server
   * ishi esa orqada davom etadi.</p>
   *
   * <p>Nega alohida holat, so'rov keshini yamash emas: chekning birinchi
   * tovarida `saleId` hali yo'q (qoralama aynan shu paytda yaratiladi), ya'ni
   * yamash uchun kalit yo'q. Bu ro'yxat esa saleId dan mustaqil.</p>
   */
  const [pending, setPending] = useState<PendingLine[]>([]);
  const dropPending = (key: string) => setPending((rows) => rows.filter((r) => r.key !== key));
  // Tez ketma-ket skanerlashda kalitlar takrorlanmasin.
  const pendingSeq = useRef(0);

  /**
   * Shtrix-kod → tovar. Katalogdan yuklangan har bir tovar va serverdan
   * topilgan har bir kod shu yerga tushadi, shuning uchun ikkinchi marta
   * skanerlashda tarmoqqa umuman chiqilmaydi.
   */
  const barcodeIndex = useRef(new Map<string, Product>());
  useEffect(() => {
    for (const p of productsQuery.data?.items ?? []) {
      if (p.barcode) barcodeIndex.current.set(p.barcode, p);
    }
  }, [productsQuery.data]);

  // Ko'rinadigan savat: server qatorlari + hali tasdiqlanmaganlari. Bir xil
  // tovar bo'lsa miqdor qo'shiladi — server ham aynan shunday birlashtiradi,
  // ya'ni tasdiqlangach ro'yxat sakramaydi.
  const items = useMemo(() => mergePending(sale?.items ?? [], pending), [sale?.items, pending]);
  const total = sale?.totalAmount ?? 0;
  // Chegirmasiz oraliq summa — chegirma qatorini ko'rsatish uchun kerak
  // (total allaqachon chegirma ayirilgan holda keladi).
  const gross = items.reduce((acc, it) => acc + it.totalPrice, 0);

  const shownCount = productsQuery.data?.items.length ?? 0;
  const totalCount = productsQuery.data?.total ?? 0;
  const canLoadMore = shownCount < totalCount && pageSize < MAX_PAGE_SIZE;

  // Kutayotgan chekda chegirma bo'lishi mumkin — uni davom ettirganda input
  // serverdagi qiymatni ko'rsatsin, aks holda maydon bo'sh turib, jami esa
  // kamaytirilgan bo'lardi. Yozayotganda qayta ishga tushmaydi: saleId ham,
  // discountAmount ham o'zgarmaydi (faqat muvaffaqiyatli qo'llashdan keyin).
  useEffect(() => {
    setDiscountInput(sale?.discountAmount ? String(sale.discountAmount) : '');
  }, [saleId, sale?.discountAmount]);

  /**
   * Adjust a product's stock badge in the catalogue cache by `delta`.
   *
   * The basket used to invalidate ['pos-products'] on every click, which meant
   * a full catalogue refetch per unit added. The only thing that actually
   * changed is the one product's remaining quantity, and we know it exactly —
   * so we patch it. The server still owns the real number and rejects
   * over-selling; this only keeps the badge honest between refetches.
   */
  const bumpStock = (productId: string | null | undefined, delta: number) => {
    if (!productId || delta === 0) return;
    qc.setQueriesData<PagedResult<Product>>({ queryKey: ['pos-products'] }, (old) =>
      old
        ? {
            ...old,
            items: old.items.map((p) => {
              if (p.id !== productId) return p;
              const quantity = p.quantity + delta;
              return { ...p, quantity, isInStock: quantity > 0, isLowStock: quantity <= p.minThreshold };
            }),
          }
        : old,
    );
  };

  /** Pull the authoritative receipt back after a basket mutation. */
  const refreshSale = (id: string | null = saleId) =>
    qc.invalidateQueries({ queryKey: ['pos-sale', id] });

  /**
   * «Narxni sotuvchidan yashirish» — faqat SOTUV oqimida va faqat Seller
   * rolida. Egasi yoki administrator kassaga kirsa narxni ko'radi: belgi
   * aynan sotuvchiga qaratilgan.
   *
   * <p>Bu maxfiylik chorasi emas, ish tartibi: narx «Tovarlar» bo'limida
   * sotuvchiga ochiq turadi (foydalanuvchi shunday xohladi). Maqsad — kassir
   * ekrandan narx o'qib mijozga aytmasin, egasidan so'rasin. Shuning uchun
   * serverda maskalash qilinmadi: bir xil endpoint ikkala ekranga xizmat
   * qiladi va maskalash «Tovarlar» ni ham buzardi.</p>
   */
  const isSeller = hasRole(ROLES.Seller);
  const hidePriceOf = (p: Product) => isSeller && p.hidePriceFromSellers;

  /** Drafts only move when a receipt is parked, resumed, discarded or closed. */
  const refreshDrafts = () => qc.invalidateQueries({ queryKey: ['pos-drafts'] });

  const onMutationError = (e: unknown) => {
    const err = e as unknown as ApiError;
    // No open shift blocks the whole register, so it gets the full-screen state.
    if (err.code === 'SHIFT_NOT_OPEN') setStartError(err.message ?? '');
    else setActionError(err.message ?? '');
  };

  /**
   * Savatga qo'shish. Qator DARHOL chiziladi, server ishi orqada davom etadi.
   *
   * <p>`onMutate` React Query da so'rov yuborilishidan OLDIN ishlaydi, ya'ni
   * kassir tarmoqni umuman kutmaydi. Server javob bergach `onSettled` chekni
   * qayta o'qiydi va vaqtincha qator o'rnini haqiqiysiga bo'shatadi.</p>
   */
  const addItem = useMutation({
    mutationFn: async (p: {
      productId: string;
      salePrice: number;
      minSalePrice: number;
      quantity?: number;
      optimistic?: PendingLine;
    }) => {
      const id = await ensureSale();
      return posApi.addItem(id, {
        isExternal: false,
        productId: p.productId,
        quantity: p.quantity ?? 1,
        salePrice: p.salePrice,
        minSalePrice: p.minSalePrice,
      });
    },
    onMutate: (vars) => {
      setActionError(null);
      // Qoldiq ham darhol kamayadi — katalogdagi son bilan savat bir vaqtda
      // yangilansin, aks holda kassir «qo'shildimi?» deb ikkilanadi.
      bumpStock(vars.productId, -(vars.quantity ?? 1));
      if (vars.optimistic) setPending((rows) => [...rows, vars.optimistic!]);
    },
    onError: (e, vars) => {
      // Xatoda ham qoldiqni tiklaymiz: server qatorni qabul qilmadi.
      bumpStock(vars.productId, vars.quantity ?? 1);
      onMutationError(e);
    },
    onSettled: async (_data, _err, vars) => {
      // Avval haqiqiy chekni tortamiz, KEYIN vaqtincha qatorni olib tashlaymiz —
      // teskarisi bo'lsa ro'yxat bir lahzaga bo'shab, ko'zga tashlanardi.
      await refreshSale();
      if (vars.optimistic) dropPending(vars.optimistic.key);
    },
  });

  const addExternal = useMutation({
    mutationFn: async (p: { name: string; salePrice: number; costPrice: number; quantity: number }) => {
      const id = await ensureSale();
      return posApi.addItem(id, {
        isExternal: true,
        externalProductName: p.name,
        externalCostPrice: p.costPrice,
        quantity: p.quantity,
        salePrice: p.salePrice,
        minSalePrice: 0,
      });
    },
    onSuccess: () => {
      setActionError(null);
      setExternalOpen(false);
      void refreshSale();
    },
    onError: onMutationError,
  });

  /**
   * Set a line to an exact quantity. Optimistic: the receipt re-renders from
   * the patched cache immediately, and the invalidate that follows replaces it
   * with the server's numbers (which also re-apply customer credit).
   */
  const setQuantity = useMutation({
    mutationFn: (p: { itemId: string; quantity: number; productId: string | null }) =>
      posApi.setItemQuantity(saleId!, p.itemId, p.quantity),
    onMutate: async (p) => {
      await qc.cancelQueries({ queryKey: ['pos-sale', saleId] });
      const prev = qc.getQueryData<PosSale>(['pos-sale', saleId]);
      if (prev) {
        const line = prev.items.find((i) => i.id === p.itemId);
        const nextItems =
          p.quantity <= 0
            ? prev.items.filter((i) => i.id !== p.itemId)
            : prev.items.map((i) =>
                i.id === p.itemId ? { ...i, quantity: p.quantity, totalPrice: p.quantity * i.salePrice } : i,
              );
        qc.setQueryData<PosSale>(['pos-sale', saleId], withTotals(prev, nextItems));
        if (line) bumpStock(p.productId, line.quantity - p.quantity);
      }
      return { prev, previousQty: prev?.items.find((i) => i.id === p.itemId)?.quantity };
    },
    onError: (e, p, ctx) => {
      // Roll the receipt AND the stock badge back — a rejected change must not
      // leave the catalogue claiming units that were never taken.
      if (ctx?.prev) qc.setQueryData(['pos-sale', saleId], ctx.prev);
      if (ctx?.previousQty !== undefined) bumpStock(p.productId, p.quantity - ctx.previousQty);
      onMutationError(e);
    },
    onSuccess: () => setActionError(null),
    onSettled: () => void refreshSale(),
  });

  /** Override one line's price (торг). Audited server-side. */
  const setLinePrice = useMutation({
    mutationFn: (p: { itemId: string; price: number }) => posApi.updateItemPrice(p.itemId, p.price),
    onMutate: async (p) => {
      await qc.cancelQueries({ queryKey: ['pos-sale', saleId] });
      const prev = qc.getQueryData<PosSale>(['pos-sale', saleId]);
      if (prev) {
        const nextItems = prev.items.map((i) =>
          i.id === p.itemId ? { ...i, salePrice: p.price, totalPrice: i.quantity * p.price } : i,
        );
        qc.setQueryData<PosSale>(['pos-sale', saleId], withTotals(prev, nextItems));
      }
      return { prev };
    },
    onError: (e, _p, ctx) => {
      if (ctx?.prev) qc.setQueryData(['pos-sale', saleId], ctx.prev);
      onMutationError(e);
    },
    onSuccess: () => setActionError(null),
    onSettled: () => void refreshSale(),
  });

  /** Throw a parked receipt away for good. Server-side this is the narrow
   *  "own Draft only" delete — a cashier has no sales.delete and cannot touch
   *  a paid receipt this way. */
  const discardDraft = useMutation({
    mutationFn: (id: string) => posApi.deleteMyDraft(id),
    onSuccess: (_res, id) => {
      // Clearing the ACTIVE receipt has to reset the register too, or the UI
      // keeps polling a sale that no longer exists.
      if (id === saleId) {
        draftRef.current = null;
        setSaleId(null);
        setCustomer(null);
        setMethod('Cash');
      }
      setActionError(null);
      // Discarding restocks every line, so the catalogue's own numbers are the
      // only trustworthy ones now — refetch rather than guess the deltas.
      void qc.invalidateQueries({ queryKey: ['pos-products'] });
      void refreshDrafts();
    },
    onError: (e) => setActionError((e as unknown as ApiError).message ?? ''),
  });

  /**
   * Sale-level chegirma. Server tomonda auditlanadi (kim, eski→yangi) va jami
   * item summasidan oshib keta olmaydi, shuning uchun bu yerda faqat manfiy
   * qiymat to'siladi — qolgan chegaralarni server qo'yadi va uning xabari
   * ko'rsatiladi.
   */
  const applyDiscount = useMutation({
    mutationFn: (amount: number) => posApi.setDiscount(saleId!, amount),
    onSuccess: () => {
      setActionError(null);
      void refreshSale();
    },
    onError: (e) => {
      setActionError((e as unknown as ApiError).message ?? '');
      // Rad etilgan chegirmani inputda qoldirish "qo'llandi" degan taassurot
      // berardi — serverdagi haqiqiy qiymatga qaytaramiz.
      setDiscountInput(sale?.discountAmount ? String(sale.discountAmount) : '');
    },
  });

  const attachCustomer = useMutation({
    mutationFn: (c: PosCustomer | null) => posApi.attachCustomer(saleId!, c?.id ?? null),
    onSuccess: () => void refreshSale(),
  });

  const openShift = useMutation({
    mutationFn: shiftsApi.open,
    onSuccess: () => {
      setStartError(null);
      void qc.invalidateQueries({ queryKey: ['shift-current'] });
    },
    onError: (e) => setActionError((e as unknown as ApiError).message ?? ''),
  });

  /**
   * The discount PATCH currently in flight, resolving to whether it succeeded.
   * Clicking «Оплата» blurs the discount field, so by the time the click lands
   * the write has already been started by onBlur — the checkout has to wait for
   * that same request rather than issue its own.
   */
  const discountInFlight = useRef<Promise<boolean> | null>(null);

  /** Send the discount once, and remember the request so checkout can await it. */
  function commitDiscount(next: number) {
    if (!saleId || next === (sale?.discountAmount ?? 0)) return;
    const p = applyDiscount
      .mutateAsync(next)
      .then(
        () => true,
        () => false, // onError already surfaced the reason and reset the field
      )
      .finally(() => {
        if (discountInFlight.current === p) discountInFlight.current = null;
      });
    discountInFlight.current = p;
  }

  /**
   * Open the checkout on the FINAL amount.
   *
   * The modal used to open against the pre-discount total while the PATCH was
   * still in flight; the server then rejected the over-payment and the cashier
   * got an error they could not explain. Awaiting the write and its refetch
   * fixes that — but the wait has to be on the request onBlur already sent.
   * Re-sending here would write a second, identical discount audit row for
   * every discounted receipt.
   */
  async function openCheckout() {
    if (discountInFlight.current) {
      const ok = await discountInFlight.current;
      if (!ok) return; // refused discount — opening on a total the server
      await refreshSale(); // rejected would be worse than not opening at all
    }
    setCheckoutOpen(true);
  }

  function pickCustomer(c: PosCustomer | null) {
    setCustomer(c);
    setCustOpen(false);
    // Only push to the server once a Draft exists; otherwise it rides along on
    // createDraft when the first product is added.
    if (saleId) attachCustomer.mutate(c);
  }

  /**
   * Enter — skanerning tugatish signali.
   *
   * <p>Apparat skaner klaviatura kabi ishlaydi: kodni juda tez teradi va Enter
   * bosadi. Qidiruv maydoni kassada doim fokusda turadi (autoFocus + F2),
   * shuning uchun kod shu yerga tushadi va global tugma tutuvchi kerak emas —
   * u boshqa maydonlarga (miqdor, chegirma, mijoz ismi) aralashib ketardi.</p>
   *
   * <p>Uch bosqich: aniq shtrix-kod → ro'yxatda bitta natija qolgan bo'lsa
   * o'sha → aks holda xabar. Ikkinchi bosqich skanersiz ham foydali: kassir
   * nomni terib Enter bossa, tovar qo'shiladi.</p>
   */
  /** Tovarni savatga qo'shadi va uni darhol ekranga chizadi. */
  function addProduct(product: Product) {
    addItem.mutate({
      productId: product.id,
      salePrice: product.salePrice,
      minSalePrice: product.minSalePrice,
      optimistic: {
        key: `pending-${product.id}-${pendingSeq.current++}`,
        productId: product.id,
        productName: product.name,
        quantity: 1,
        salePrice: product.salePrice,
        // Product da `unit` — UnitType raqami, `unitName` — qisqartma;
        // savat qatorida esa teskarisi ataladi.
        unit: product.unitName,
        unitValue: product.unit,
      },
    });
  }

  async function handleScan() {
    const code = search.trim();
    if (!code) return;

    // 1) Lokal indeks. Katalogdan yuklangan yoki ilgari skanerlangan kod
    //    bo'lsa tarmoqqa UMUMAN chiqilmaydi — tovar shu zahoti savatda.
    const known = barcodeIndex.current.get(code);
    if (known) {
      addProduct(known);
      setSearch('');
      return;
    }

    // 2) Noma'lum kod — serverdan so'raymiz. Bu yagona kutish, va u ham faqat
    //    birinchi marta: topilgan kod indeksga tushadi.
    const product = await posApi.findByBarcode(code).catch(() => null);
    if (product) {
      if (product.barcode) barcodeIndex.current.set(product.barcode, product);
      addProduct(product);
      setSearch('');
      return;
    }

    // Raqamlardan iborat uzun satr — bu shtrix-kod urinishi. Bunday holatda
    // ZAXIRA YO'L ISHLATILMAYDI: noma'lum kod uchun ro'yxatdagi tasodifiy
    // tovarni qo'shish kassaning eng yomon xatosi bo'lardi — kassir buni
    // sezmasdan mijozga boshqa narsani yozib yuboradi.
    if (/^\d{6,}$/.test(code)) {
      setActionError(t('pos.scan.notFound', { code }));
      return;
    }

    // Nom bo'yicha qidiruv: ro'yxat AYNAN shu so'rovga tegishli bo'lsagina
    // yagona natijani qo'shamiz. debouncedSearch tekshiruvisiz bu yerda hali
    // eski natijalar turadi (qidiruv debounce bilan kechikadi) va Enter
    // butunlay boshqa tovarni chekka tushirardi.
    if (debouncedSearch.trim() !== code || productsQuery.isFetching) return;
    const found = productsQuery.data?.items ?? [];
    const only = found.length === 1 ? found[0] : undefined;
    if (only) {
      addProduct(only);
      setSearch('');
    }
  }

  /**
   * Skanerdan kelgan kod — fokus qayerda bo'lishidan qat'i nazar.
   *
   * <p>Nom bo'yicha qidiruv zaxirasi bu yerda YO'Q: skaner har doim shtrix-kod
   * beradi, ro'yxatdagi tasodifiy tovarni qo'shish esa kassaning eng yomon
   * xatosi bo'lardi.</p>
   */
  async function handleScannedCode(code: string) {
    const known = barcodeIndex.current.get(code);
    if (known) {
      addProduct(known);
      setSearch('');
      return;
    }
    const product = await posApi.findByBarcode(code).catch(() => null);
    if (product) {
      if (product.barcode) barcodeIndex.current.set(product.barcode, product);
      addProduct(product);
      setSearch('');
      return;
    }
    setActionError(t('pos.scan.notFound', { code }));
  }

  // Kassa ochiq turganda skaner butun sahifa bo'ylab ishlaydi. Chek yakunlangan
  // yoki qo'shimcha oyna ochilgan paytda o'chiriladi — u yerda skanerlangan kod
  // savatga tushmasligi kerak.
  useGlobalScanner(
    (code) => void handleScannedCode(code),
    !done && !externalOpen && !custOpen && !checkoutOpen,
  );

  /** Park the current receipt: it simply stays a Draft and reappears in the strip. */
  function park() {
    draftRef.current = null;
    setSaleId(null);
    setCustomer(null);
    setMethod('Cash');
    setActionError(null);
    void refreshDrafts();
  }

  /**
   * Leaving the register mid-sale.
   *
   * The Draft is already on the server, so nothing is actually lost — it comes
   * back in the parked strip. But a cashier who taps «Mijozlar» while a customer
   * is standing there has no way to know that, and the basket vanishing off the
   * screen reads as data loss. So we ask, and the "yes" branch parks explicitly
   * (which also refreshes the strip) rather than letting the state fall on the
   * floor.
   *
   * Only in-app navigation is intercepted; a tab close is the browser's own
   * business and the Draft survives it anyway.
   */
  const blocker = useBlocker(
    ({ currentLocation, nextLocation }) =>
      items.length > 0 && currentLocation.pathname !== nextLocation.pathname,
  );

  useEffect(() => {
    if (blocker.state !== 'blocked') return;
    let cancelled = false;
    void (async () => {
      const leave = await confirm({
        title: t('seller.pos.leaveTitle'),
        message: t('seller.pos.leaveMessage'),
        confirmLabel: t('seller.pos.park'),
        cancelLabel: t('seller.pos.stay'),
      });
      if (cancelled) return;
      if (leave) {
        park();
        blocker.proceed();
      } else {
        blocker.reset();
      }
    })();
    return () => {
      cancelled = true;
    };
    // `park` and `confirm` are stable enough for this one-shot prompt; re-running
    // on every render would re-open the dialog under the cashier's finger.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [blocker.state]);

  function resume(d: PosSale) {
    draftRef.current = null;
    setSaleId(d.id);
    setCustomer(
      d.customerId
        ? ({ id: d.customerId, fullName: d.customerName, phone: d.customerPhone ?? '' } as PosCustomer)
        : null,
    );
    setActionError(null);
  }

  const heldDrafts = (draftsQuery.data ?? []).filter((d) => d.id !== saleId && d.items.length > 0);

  async function printReceipt(id: string) {
    try {
      const blob = await posApi.receiptPdf(id, i18n.language);
      const url = URL.createObjectURL(blob);
      window.open(url, '_blank', 'noopener');
      setTimeout(() => URL.revokeObjectURL(url), 60_000);
    } catch {
      /* best-effort: the sale is already finalised, printing can be retried */
    }
  }

  // ── shift blocked ───────────────────────────────────────────────
  if (startError) {
    return (
      <div className="flex flex-1 flex-col items-center justify-center gap-4 p-8 text-center">
        <div className="flex h-16 w-16 items-center justify-center rounded-2xl bg-warn-soft text-warn">
          <Clock size={28} />
        </div>
        <p className="max-w-md text-[15px] text-muted">{startError}</p>
        {actionError && <p className="text-[12.5px] text-danger">{actionError}</p>}
        <div className="flex gap-2">
          {/* The blocker is "no open shift", so the fix belongs here. Sending the
              cashier to the Смены screen and back cost the whole basket's worth
              of navigation for a one-click action. */}
          <Button loading={openShift.isPending} onClick={() => openShift.mutate()}>
            <Clock size={15} />
            {t('seller.pos.openShift')}
          </Button>
          <Button variant="secondary" onClick={() => setStartError(null)}>
            {t('common.retry')}
          </Button>
        </div>
      </div>
    );
  }

  return (
    // Katta ekranda ikki ustun: katalog va chek yonma-yon, ikkalasi ham o'z
    // ichida suriladi (sahifa qimirlamaydi — kassada shu qulay). Telefon va
    // planshetda 420px lik chek ustuni sig'maydi, shuning uchun ular ustma-ust
    // joylashadi va sahifaning o'zi suriladi: avval katalog, ostida chek.
    <div className="grid flex-1 grid-cols-1 lg:grid-cols-[1fr_420px] lg:overflow-hidden">
      {/* ── LEFT: catalogue ─────────────────────────────────────── */}
      <div className="flex min-w-0 flex-col p-4 sm:p-6 lg:overflow-hidden">
        <div className="mb-3 flex gap-2">
          <div className="relative flex-1">
            <Search size={18} className="absolute left-4 top-1/2 -translate-y-1/2 text-muted-2" />
            <input
              ref={searchRef}
              value={search}
              onChange={(e) => {
                setSearch(e.target.value);
                if (actionError) setActionError(null);
              }}
              onKeyDown={(e) => {
                if (e.key !== 'Enter') return;
                e.preventDefault(); // Enter formani yubormasin
                void handleScan();
              }}
              placeholder={t('pos.searchPlaceholder')}
              autoFocus
              className="h-12 w-full rounded-input border border-input-border bg-surface pl-12 pr-24 text-[15px] outline-none focus:border-primary focus:shadow-focus-ring"
            />
            <div className="absolute right-3 top-1/2 flex -translate-y-1/2 items-center gap-2">
              {/* Stale-results tell: without it the grid silently showed the
                  previous query's cards and the cashier could ring up the
                  wrong product. */}
              {productsQuery.isFetching && <Spinner size={15} />}
              <kbd className="rounded border border-input-border px-1.5 py-0.5 text-[11px] text-muted-2">F2</kbd>
            </div>
          </div>
          {/* Off-catalogue line: goods physically on the shelf but not in the
              system used to be unsellable at the register. */}
          <Button
            variant="secondary"
            className="flex-none"
            onClick={() => {
              // Clear the previous attempt's error, or a reopened dialog greets
              // the cashier with a failure that no longer applies.
              addExternal.reset();
              setExternalOpen(true);
            }}
          >
            <PackagePlus size={15} />
            {t('seller.pos.external.button')}
          </Button>
        </div>

        <div className="mb-4 flex flex-wrap items-center gap-1.5">
          <Chip label={t('seller.pos.allProducts')} active={categoryId === null} onClick={() => setCategoryId(null)} />
          {(categoriesQuery.data ?? []).map((c) => (
            <Chip key={c.id} label={c.name} active={categoryId === c.id} onClick={() => setCategoryId(c.id)} />
          ))}
        </div>

        <div className="flex-1 lg:overflow-y-auto">
          {productsQuery.isLoading ? (
            <div className="flex justify-center py-20 text-primary">
              <Spinner size={26} />
            </div>
          ) : shownCount === 0 ? (
            <div className="py-20 text-center text-[14px] text-muted-2">{t('warehouse.empty')}</div>
          ) : (
            <>
              <div className="grid grid-cols-2 lg:grid-cols-4 gap-3">
                {productsQuery.data!.items.map((p) => {
                  const out = p.quantity <= 0;
                  return (
                    <button
                      key={p.id}
                      type="button"
                      // Only an out-of-stock card is unclickable. It used to be
                      // disabled while ANY add was in flight, which capped the
                      // cashier at one product per round-trip.
                      disabled={out}
                      onClick={() => addProduct(p)}
                      className={cn(
                        'flex flex-col gap-2 rounded-card border bg-surface p-3 text-left transition-colors',
                        out
                          ? 'cursor-not-allowed border-border opacity-50'
                          : 'border-border hover:border-primary hover:shadow-card',
                      )}
                    >
                      <div className="flex items-start justify-between gap-2">
                        <span className="flex h-8 w-8 items-center justify-center rounded-lg bg-primary-soft text-primary">
                          <Package size={16} />
                        </span>
                        <span
                          className={cn(
                            'text-[11.5px] nums',
                            out ? 'text-danger' : p.isLowStock ? 'text-warn' : 'text-muted-2',
                          )}
                        >
                          {out
                            ? t('seller.pos.outOfStock')
                            : `${formatQty(p.quantity)} ${unitLabel(t, p.unit, p.unitName)}`}
                        </span>
                      </div>
                      <span className="line-clamp-2 text-[13px] font-medium leading-tight">{p.name}</span>
                      {/* «Narxni sotuvchidan yashirish» belgilangan tovarlarda
                          kassir narxni katalogda ko'rmaydi — narxni egasi
                          aytadi. Tovarning o'zi haqidagi ma'lumot «Tovarlar»
                          bo'limida ochiq qoladi. */}
                      {hidePriceOf(p) ? (
                        <span className="text-[13px] font-medium text-muted-2">
                          {t('seller.pos.priceOnRequest')}
                        </span>
                      ) : (
                        <span className="text-[14px] font-semibold text-primary nums">
                          {formatSum(p.salePrice)}
                          <span className="ml-1 text-[11px] font-normal text-muted-2">
                            {t('common.currency')}/{unitLabel(t, p.unit, p.unitName)}
                          </span>
                        </span>
                      )}
                    </button>
                  );
                })}
              </div>

              {/* The grid used to stop at 40 with no way forward — in a large
                  catalogue the wanted item was simply unreachable. */}
              <div className="flex flex-col items-center gap-2 py-5">
                <span className="text-[11.5px] text-muted-2 nums">
                  {t('seller.pos.shownOf', { shown: shownCount, total: totalCount })}
                </span>
                {canLoadMore && (
                  <Button
                    variant="secondary"
                    size="sm"
                    loading={productsQuery.isFetching}
                    onClick={() => setPageSize((s) => Math.min(MAX_PAGE_SIZE, s + PAGE_STEP))}
                  >
                    <ChevronDown size={15} />
                    {t('seller.pos.loadMore')}
                  </Button>
                )}
                {!canLoadMore && shownCount < totalCount && (
                  <span className="text-[11.5px] text-warn">{t('seller.pos.refineSearch')}</span>
                )}
              </div>
            </>
          )}
        </div>
      </div>

      {/* ── RIGHT: the receipt ──────────────────────────────────── */}
      {/* Ustma-ust turganda chap chegara emas, ustki chegara ajratadi. */}
      <div className="flex flex-col border-t border-border bg-surface lg:overflow-hidden lg:border-l lg:border-t-0">
        <div className="flex items-center justify-between border-b border-hairline px-5 py-3.5">
          <h2 className="text-[15px] font-semibold">
            {t('seller.pos.receipt')} {sale ? <span className="nums">№{sale.saleNumber}</span> : ''}
          </h2>
          {!!saleId && (
            // This used to call park(), so "Очистить" silently left the receipt
            // sitting in the parked strip — the cashier had no way to throw an
            // abandoned basket away. It now really discards it; parking is the
            // separate «Отложить» button below.
            <button
              type="button"
              disabled={discardDraft.isPending}
              onClick={async () => {
                if (items.length === 0 || (await confirm({ title: t('seller.pos.discardConfirm'), tone: 'danger', confirmLabel: t('seller.pos.discard') })))
                  discardDraft.mutate(saleId);
              }}
              className="text-[12.5px] text-muted-2 transition-colors hover:text-danger disabled:opacity-40"
            >
              {t('seller.pos.clear')}
            </button>
          )}
        </div>

        {/* Parked receipts */}
        {heldDrafts.length > 0 && (
          <div className="border-b border-hairline bg-warn-soft/60 px-5 py-3">
            <div className="mb-2 text-[11px] font-semibold uppercase tracking-[0.5px] text-warn-strong">
              {t('seller.pos.held')} · {heldDrafts.length}
            </div>
            <div className="flex gap-2 overflow-x-auto pb-1">
              {heldDrafts.map((d) => (
                <div
                  key={d.id}
                  className="group relative flex-none rounded-lg border border-warn/30 bg-surface transition-colors hover:border-warn"
                >
                  <button type="button" onClick={() => resume(d)} className="px-3 py-2 pr-7 text-left">
                    <div className="text-[12.5px] font-semibold nums">№{d.saleNumber}</div>
                    <div className="text-[11px] text-muted-2 nums">
                      {d.items.length} · {formatSum(d.totalAmount)}
                    </div>
                  </button>
                  <button
                    type="button"
                    title={t('seller.pos.discard')}
                    disabled={discardDraft.isPending}
                    onClick={async () => {
                      if (await confirm({ title: t('seller.pos.discardConfirm'), tone: 'danger', confirmLabel: t('seller.pos.discard') }))
                        discardDraft.mutate(d.id);
                    }}
                    className="absolute right-1 top-1 rounded p-0.5 text-muted-2 opacity-0 transition-opacity hover:bg-danger-soft hover:text-danger focus:opacity-100 group-hover:opacity-100"
                  >
                    <X size={13} strokeWidth={2.4} />
                  </button>
                </div>
              ))}
            </div>
          </div>
        )}

        {/* Customer */}
        <div className="border-b border-hairline px-5 py-3">
          {customer ? (
            <div className="flex items-center justify-between gap-2">
              <div className="min-w-0">
                <div className="truncate text-[13px] font-medium">{customer.fullName ?? customer.phone}</div>
                <div className="truncate text-[11.5px] text-muted-2 nums">{customer.phone}</div>
              </div>
              <button type="button" aria-label={t('common.close')} onClick={() => pickCustomer(null)} className="text-muted-2 hover:text-danger">
                <X size={16} />
              </button>
            </div>
          ) : (
            <button
              type="button"
              onClick={() => setCustOpen(true)}
              className="flex w-full items-center justify-center gap-2 rounded-input border border-dashed border-input-border py-2.5 text-[13px] text-muted transition-colors hover:border-primary hover:text-primary"
            >
              <UserPlus size={15} />
              {t('pos.customer.add')}
            </button>
          )}
        </div>

        {/* Lines */}
        <div className="flex-1 px-5 lg:overflow-y-auto">
          {items.length === 0 ? (
            <div className="flex h-full items-center justify-center px-6 text-center text-[13.5px] text-muted-2">
              {t('pos.emptyCart')}
            </div>
          ) : (
            items.map((it) => (
              <ReceiptLine
                key={it.id}
                item={it}
                canEditPrice={canEditPrice}
                // Scoped to THIS line on purpose. Serialising a line's own ±
                // clicks keeps the last click the winning quantity; a shared
                // flag would freeze every other line behind one round-trip —
                // the exact queue-up this rework exists to remove.
                busy={
                  (setQuantity.isPending && setQuantity.variables?.itemId === it.id) ||
                  (setLinePrice.isPending && setLinePrice.variables?.itemId === it.id)
                }
                onQuantity={(q) => setQuantity.mutate({ itemId: it.id, quantity: q, productId: it.productId })}
                onPrice={(p) => setLinePrice.mutate({ itemId: it.id, price: p })}
              />
            ))
          )}
        </div>

        {/* Footer */}
        <div className="border-t border-border px-5 py-4">
          {actionError && <p className="mb-2 text-[12.5px] text-danger">{actionError}</p>}
          <div className="mb-1 flex items-center justify-between text-[12.5px] text-muted">
            <span>{t('seller.pos.positions')}</span>
            <span className="nums">{items.length}</span>
          </div>

          {/* Chegirma. Faqat chek qurilgandan keyin — Draft yo'q bo'lsa
              serverda qo'llaydigan sotuv ham yo'q. Server auditlaydi
              (kim, eski→yangi) va jami summadan oshirmaydi. */}
          <div className="mb-2 flex items-center justify-between">
            <label htmlFor="pos-discount" className="text-[12.5px] text-muted">
              {t('pos.discount')}
            </label>
            <div className="flex items-center gap-1.5">
              <input
                id="pos-discount"
                type="number"
                step="any"
                min="0"
                placeholder="0"
                disabled={!saleId || applyDiscount.isPending}
                value={discountInput}
                onChange={(e) => setDiscountInput(e.target.value)}
                // Faqat haqiqatan o'zgargan bo'lsa yuboradi — har fokus
                // yo'qotishda audit qatori yozilishining oldini oladi.
                onBlur={() => commitDiscount(Math.max(0, Number(discountInput) || 0))}
                className="h-9 w-[110px] rounded-input border border-input-border bg-surface px-3 text-right text-[13px] outline-none focus:border-primary disabled:opacity-50 nums"
              />
              <span className="text-[11.5px] text-muted-2">{t('common.currency')}</span>
            </div>
          </div>

          {/* Chegirma qo'llangandagina oraliq summa ko'rsatiladi — aks holda u
              pastdagi "Jami" ning aynan nusxasi bo'lardi. */}
          {!!sale?.discountAmount && (
            <div className="mb-2 flex items-baseline justify-between text-[12.5px]">
              <span className="text-muted-2">{t('sales.detail.subtotal')}</span>
              <span className="text-muted-2 line-through nums">{formatSum(gross)}</span>
            </div>
          )}

          <div className="mb-3 flex items-baseline justify-between">
            <span className="text-[13px] text-muted">{t('pos.total')}</span>
            <span className="text-[22px] font-bold nums">
              {formatSum(total)}
              <span className="ml-1 text-[12px] font-normal text-muted-2">{t('common.currency')}</span>
            </span>
          </div>

          <div className="mb-3 grid grid-cols-4 gap-2">
            {METHODS.map((m) => (
              <button
                key={m.value}
                type="button"
                onClick={() => setMethod(m.value)}
                className={cn(
                  'h-10 rounded-input border text-[13px] font-medium transition-colors',
                  method === m.value
                    ? 'border-primary bg-primary-soft text-primary-hover'
                    : 'border-input-border bg-surface text-muted hover:text-text',
                )}
              >
                {t(`pos.payment.${m.key}` as never)}
              </button>
            ))}
          </div>

          <div className="flex gap-2">
            <Button variant="secondary" className="flex-none" disabled={!saleId} onClick={park}>
              <Pause size={15} />
              {t('seller.pos.park')}
            </Button>
            <Button
              fullWidth
              disabled={items.length === 0}
              loading={applyDiscount.isPending}
              onClick={openCheckout}
            >
              {t('pos.checkout')}
            </Button>
          </div>
        </div>
      </div>

      <CustomerPicker
        open={custOpen}
        canCreate={canManageCustomers}
        onClose={() => setCustOpen(false)}
        onPick={pickCustomer}
      />

      <ExternalItemModal
        open={externalOpen}
        onClose={() => setExternalOpen(false)}
        pending={addExternal.isPending}
        error={addExternal.isError ? ((addExternal.error as unknown as ApiError).message ?? '') : null}
        onSubmit={(p) => addExternal.mutate(p)}
      />

      {sale && (
        <CheckoutModal
          open={checkoutOpen}
          onClose={() => setCheckoutOpen(false)}
          sale={sale}
          method={method}
          setMethod={setMethod}
          customer={customer}
          onNeedCustomer={() => {
            setCheckoutOpen(false);
            setCustOpen(true);
          }}
          onDone={async (finished) => {
            setCheckoutOpen(false);
            setDone(finished);
            draftRef.current = null;
            setSaleId(null);
            setCustomer(null);
            setMethod('Cash');
            void qc.invalidateQueries({ queryKey: ['pos-products'] });
            void refreshDrafts();
            void qc.invalidateQueries({ queryKey: ['shift-current'] });
          }}
        />
      )}

      <ReceiptModal
        sale={done}
        shiftNumber={shiftQuery.data?.shiftNumber ?? 0}
        storeName={marketQuery.data?.marketName ?? null}
        closeLabel={t('seller.pos.newSale')}
        onClose={() => setDone(null)}
        onPrint={printReceipt}
      />
    </div>
  );
}

// ── small pieces ───────────────────────────────────────────────────

/**
 * One receipt line. Quantity is a real decimal field — the shop sells qop, m,
 * kg and tonna, so "3.5" has to be typeable and "30" must not cost 30 clicks.
 * The steppers stay for the ±1 nudge, but they go through the same set-exact
 * call, so there is one quantity path rather than an add path and a remove path.
 */
function ReceiptLine({
  item,
  canEditPrice,
  busy,
  onQuantity,
  onPrice,
}: {
  item: PosSale['items'][number];
  canEditPrice: boolean;
  busy: boolean;
  onQuantity: (q: number) => void;
  onPrice: (p: number) => void;
}) {
  const { t } = useTranslation();
  const [qtyDraft, setQtyDraft] = useState<string | null>(null);
  const [priceDraft, setPriceDraft] = useState<string | null>(null);

  // While the cashier is typing, the field belongs to them; otherwise it mirrors
  // the server. Without the draft state an in-flight refetch would overwrite a
  // half-typed "3." back to "3".
  const qtyValue = qtyDraft ?? String(item.quantity);

  const commitQty = () => {
    if (qtyDraft === null) return;
    const parsed = parseQty(qtyDraft);
    setQtyDraft(null);
    // parseQty returns null for a blank field on purpose: Number('') is 0, and
    // committing that would silently delete the line of a cashier who cleared
    // the box to retype it. Blank reverts; only an explicit 0 removes.
    if (parsed === null || parsed === item.quantity) return;
    onQuantity(parsed);
  };

  const commitPrice = () => {
    if (priceDraft === null) return;
    const raw = priceDraft.replace(',', '.').trim();
    const parsed = Number(raw);
    setPriceDraft(null);
    // Same blank guard, and here it matters more: an empty field committing as
    // Number('') === 0 would hand the goods over for free.
    if (raw === '' || !Number.isFinite(parsed) || parsed < 0 || parsed === item.salePrice) return;
    onPrice(parsed);
  };

  const step = (delta: number) => {
    const next = Math.max(0, Math.round((item.quantity + delta) * 1000) / 1000);
    if (next !== item.quantity) onQuantity(next);
  };

  return (
    <div className="border-b border-hairline py-3 last:border-0">
      <div className="flex items-center gap-2">
        <div className="min-w-0 flex-1">
          <div className="truncate text-[13px] font-medium">{item.productName}</div>
          <div className="flex items-center gap-1 text-[11.5px] text-muted-2">
            {priceDraft !== null ? (
              <input
                autoFocus
                onFocus={(e) => e.currentTarget.select()}
                value={priceDraft}
                onChange={(e) => setPriceDraft(e.target.value)}
                onBlur={commitPrice}
                onKeyDown={(e) => {
                  if (e.key === 'Enter') e.currentTarget.blur();
                  if (e.key === 'Escape') setPriceDraft(null);
                }}
                inputMode="decimal"
                className="h-6 w-24 rounded border border-primary bg-surface px-1.5 text-right text-[11.5px] outline-none nums"
              />
            ) : (
              <span className="nums">{formatSum(item.salePrice)}</span>
            )}
            <span>
              {t('common.currency')}/{unitLabel(t, item.unitValue, item.unit)}
            </span>
            {canEditPrice && priceDraft === null && (
              <button
                type="button"
                title={t('seller.pos.editPrice')}
                onClick={() => setPriceDraft(String(item.salePrice))}
                className="ml-0.5 text-muted-2 transition-colors hover:text-primary"
              >
                <Pencil size={12} />
              </button>
            )}
          </div>
        </div>

        <div className="flex flex-none items-center gap-1">
          <StepBtn onClick={() => step(-1)} disabled={busy}>
            <Minus size={14} />
          </StepBtn>
          {/* The heart of the fix: type "12" or "3.5" once instead of clicking. */}
          <input
            value={qtyValue}
            onChange={(e) => setQtyDraft(e.target.value)}
            onFocus={(e) => {
              setQtyDraft(String(item.quantity));
              e.currentTarget.select();
            }}
            onBlur={commitQty}
            onKeyDown={(e) => {
              if (e.key === 'Enter') e.currentTarget.blur();
              // Escape must NOT blur: the blur handler would still be holding
              // this render's qtyDraft — the very value being cancelled — and
              // would commit it. Dropping the draft alone snaps the field back
              // to the server's number and makes the later blur a no-op.
              if (e.key === 'Escape') setQtyDraft(null);
            }}
            inputMode="decimal"
            aria-label={t('seller.pos.qty')}
            title={t('seller.pos.qtyHint')}
            className="h-7 w-16 rounded-md border border-input-border bg-surface px-1 text-center text-[13px] font-semibold outline-none focus:border-primary nums"
          />
          <StepBtn onClick={() => step(1)} disabled={busy}>
            <Plus size={14} />
          </StepBtn>
        </div>

        <span className="w-24 flex-none text-right text-[13px] font-semibold nums">
          {formatSum(item.totalPrice)}
        </span>
      </div>
    </div>
  );
}

function StepBtn({
  children,
  onClick,
  disabled,
}: {
  children: React.ReactNode;
  onClick: () => void;
  disabled?: boolean;
}) {
  return (
    <button
      type="button"
      onClick={onClick}
      disabled={disabled}
      className="flex h-7 w-7 items-center justify-center rounded-md border border-input-border text-muted transition-colors hover:border-primary hover:text-primary disabled:opacity-40"
    >
      {children}
    </button>
  );
}

function Chip({ label, active, onClick }: { label: string; active: boolean; onClick: () => void }) {
  return (
    <button
      type="button"
      onClick={onClick}
      className={cn(
        'rounded-pill px-3.5 py-1.5 text-[12.5px] font-medium transition-colors',
        active ? 'bg-primary text-white' : 'border border-input-border bg-surface text-muted hover:text-text',
      )}
    >
      {label}
    </button>
  );
}

/**
 * Off-catalogue line: something on the shelf (or a delivery charge) that is not
 * a Product row. It moves no stock. The cost field is optional but asked for,
 * because a line booked at cost 0 reports as pure profit and quietly inflates
 * the owner's margin report.
 */

/**
 * Customer lookup, with the create form folded in. Selling on credit needs a
 * customer, and a first-time buyer had none — so the cashier had to leave the
 * register, create the client on another screen, and come back to a parked
 * receipt. The form is gated on customers.manage, the same key the Клиенты
 * screen uses.
 */
function CustomerPicker({
  open,
  canCreate,
  onClose,
  onPick,
}: {
  open: boolean;
  canCreate: boolean;
  onClose: () => void;
  onPick: (c: PosCustomer) => void;
}) {
  const { t } = useTranslation();
  const qc = useQueryClient();
  const [q, setQ] = useState('');
  const [creating, setCreating] = useState(false);
  const [fullName, setFullName] = useState('');
  const [phone, setPhone] = useState('');
  const [error, setError] = useState<string | null>(null);
  const debounced = useDebounce(q);

  const query = useQuery({
    queryKey: ['pos-customers', debounced],
    queryFn: () => posApi.searchCustomers(debounced),
    enabled: open && !creating,
  });

  useEffect(() => {
    if (open) {
      setQ('');
      setCreating(false);
      setFullName('');
      setPhone('');
      setError(null);
    }
  }, [open]);

  const create = useMutation({
    mutationFn: () => posApi.createCustomer({ phone: normalisePhone(phone), fullName: fullName.trim() || null }),
    onSuccess: (c) => {
      // The client directory now has one more row; the register screen is not
      // the only place that reads it.
      void qc.invalidateQueries({ queryKey: ['seller-clients'] });
      void qc.invalidateQueries({ queryKey: ['pos-customers'] });
      onPick(c); // straight onto the receipt — that is why they opened this
    },
    onError: (e) => setError((e as unknown as ApiError).message ?? t('common.somethingWrong')),
  });

  const inputCls =
    'h-11 w-full rounded-input border border-input-border bg-surface px-4 text-[15px] outline-none focus:border-primary focus:shadow-focus-ring';

  const phoneAccepted = /^\+?[0-9]{9,15}$/.test(normalisePhone(phone));

  if (creating) {
    return (
      <Modal
        open={open}
        onClose={onClose}
        title={t('seller.clients.add')}
        footer={
          <>
            <Button variant="secondary" onClick={() => setCreating(false)}>
              <ArrowLeft size={15} />
              {t('seller.pos.backToSearch')}
            </Button>
            <Button
              // Mirror of the server's rule (9–15 digits) rather than a looser
              // guess — a client check that lets through what the API rejects
              // just moves the failure to after the round-trip.
              disabled={!phoneAccepted}
              loading={create.isPending}
              onClick={() => create.mutate()}
            >
              <UserPlus size={15} />
              {t('common.save')}
            </Button>
          </>
        }
      >
        <div className="flex flex-col gap-4">
          <p className="text-[13px] text-muted">{t('seller.clients.addHint')}</p>
          <div className="flex flex-col gap-1.5">
            <label className="text-[13px] font-medium text-label">{t('seller.clients.form.name')}</label>
            <input autoFocus value={fullName} onChange={(e) => setFullName(e.target.value)} className={inputCls} />
          </div>
          <div className="flex flex-col gap-1.5">
            <label className="text-[13px] font-medium text-label">{t('seller.clients.form.phone')}</label>
            <input
              value={phone}
              onChange={(e) => setPhone(e.target.value)}
              placeholder="+998 __ ___ __ __"
              className={`${inputCls} nums`}
            />
          </div>
          {error && <p className="text-[12.5px] text-danger">{error}</p>}
        </div>
      </Modal>
    );
  }

  return (
    <Modal open={open} onClose={onClose} title={t('pos.customer.label')}>
      <div className="flex flex-col gap-3">
        <div className="relative">
          <Search size={17} className="absolute left-3.5 top-1/2 -translate-y-1/2 text-muted-2" />
          <input
            autoFocus
            value={q}
            onChange={(e) => setQ(e.target.value)}
            placeholder={t('pos.customer.searchPlaceholder')}
            className="h-11 w-full rounded-input border border-input-border bg-surface pl-11 pr-4 text-[14px] outline-none focus:border-primary focus:shadow-focus-ring"
          />
        </div>

        {canCreate && (
          <button
            type="button"
            onClick={() => {
              // Carry the typed text over: a phone number goes in the phone
              // field, anything else is a name. Re-typing it would be the whole
              // reason this shortcut exists.
              const typed = q.trim();
              if (/^[+\d][\d\s()-]*$/.test(typed)) setPhone(normalisePhone(typed));
              else setFullName(typed);
              setError(null);
              setCreating(true);
            }}
            className="flex w-full items-center justify-center gap-2 rounded-input border border-dashed border-input-border py-2.5 text-[13px] text-muted transition-colors hover:border-primary hover:text-primary"
          >
            <UserPlus size={15} />
            {t('seller.clients.add')}
          </button>
        )}

        <div className="max-h-[320px] overflow-y-auto">
          {query.isLoading ? (
            <div className="flex justify-center py-10 text-primary">
              <Spinner size={22} />
            </div>
          ) : (query.data?.items.length ?? 0) === 0 ? (
            <div className="py-10 text-center text-[13.5px] text-muted-2">{t('seller.clients.empty')}</div>
          ) : (
            query.data!.items.map((c) => (
              <button
                key={c.id}
                type="button"
                onClick={() => onPick(c)}
                className="flex w-full items-center justify-between gap-3 border-b border-hairline px-1 py-2.5 text-left last:border-0 hover:bg-bg/60"
              >
                <span className="min-w-0">
                  <span className="block truncate text-[13.5px] font-medium">{c.fullName ?? c.phone}</span>
                  <span className="block truncate text-[11.5px] text-muted-2 nums">{c.phone}</span>
                </span>
                {c.totalDebt > 0 && (
                  <Badge tone="warn">
                    {t('debts.title')} {formatSum(c.totalDebt)}
                  </Badge>
                )}
              </button>
            ))
          )}
        </div>
      </div>
    </Modal>
  );
}

function CheckoutModal({
  open,
  onClose,
  sale,
  method,
  setMethod,
  customer,
  onNeedCustomer,
  onDone,
}: {
  open: boolean;
  onClose: () => void;
  sale: PosSale;
  method: Method;
  setMethod: (m: Method) => void;
  customer: PosCustomer | null;
  onNeedCustomer: () => void;
  onDone: (finished: PosSale) => void;
}) {
  const { t } = useTranslation();
  const [received, setReceived] = useState('');
  const [mixParts, setMixParts] = useState<MixParts>(EMPTY_MIX);
  const [paidNow, setPaidNow] = useState('');
  const [due, setDue] = useState('');
  const [error, setError] = useState<string | null>(null);
  const total = sale.totalAmount;

  useEffect(() => {
    if (open) {
      setReceived('');
      setMixParts(EMPTY_MIX);
      setPaidNow('');
      setDue('');
      setError(null);
    }
  }, [open]);

  const got = Number(received) || 0;
  const change = got - total;
  const paid = Number(paidNow) || 0;
  const debtRest = Math.max(0, total - paid);
  const mixSum = mixSumOf(mixParts);
  const mixRemainder = money(total - mixSum);

  const canConfirm = useMemo(() => {
    if (total <= 0) return false;
    if (method === 'Debt') return !!customer && debtRest > 0;
    if (method === 'Cash') return got >= total;
    // Chek to'liq yopilishi shart: qoldiq nolga tushmaguncha tasdiqlab bo'lmaydi.
    if (method === 'Mixed') return mixRemainder === 0 && mixSum > 0;
    return true;
  }, [method, total, got, customer, debtRest, mixRemainder, mixSum]);

  const confirm = useMutation({
    mutationFn: async () => {
      if (method === 'Debt') {
        // A partial down-payment creates the Debt (with its due date) in one
        // call; with nothing paid now the whole sale is marked as debt.
        if (paid > 0) {
          await posApi.addPayment(sale.id, { paymentType: 'Cash', amount: paid, dueDate: due || null });
        } else {
          await posApi.markDebt(sale.id, due || null);
        }
      } else if (method === 'Mixed') {
        // Every share in ONE request: the server applies them atomically, so the
        // sale never passes through a "partially paid ⇒ debt" state.
        await posApi.checkout(sale.id, mixPayments(mixParts));
      } else {
        // Never send more than the total — the server rejects over-payment, so
        // the change stays a counter-side calculation.
        await posApi.addPayment(sale.id, { paymentType: method, amount: total });
      }
      return posApi.getSale(sale.id);
    },
    onSuccess: (finished) => onDone(finished),
    onError: (e) => setError((e as unknown as ApiError).message ?? t('common.somethingWrong')),
  });

  const inputCls =
    'h-12 w-full rounded-input border border-input-border bg-surface px-4 text-[16px] outline-none focus:border-primary focus:shadow-focus-ring nums';

  return (
    <Modal
      open={open}
      onClose={onClose}
      title={`${t('seller.pos.checkoutTitle')} · ${t('seller.pos.receipt')} №${sale.saleNumber}`}
      footer={
        <>
          <Button variant="secondary" onClick={onClose}>
            {t('common.cancel')}
          </Button>
          <Button disabled={!canConfirm} loading={confirm.isPending} onClick={() => confirm.mutate()}>
            {t('seller.pos.confirm')}
          </Button>
        </>
      }
    >
      <div className="flex flex-col gap-4">
        <div className="flex items-baseline justify-between rounded-input bg-bg px-4 py-3">
          <span className="text-[13px] text-muted">{t('seller.pos.toPay')}</span>
          <span className="text-[22px] font-bold text-primary nums">
            {formatSum(total)} <span className="text-[12px] font-normal text-muted-2">{t('common.currency')}</span>
          </span>
        </div>

        <div className="grid grid-cols-2 lg:grid-cols-4 gap-2">
          {METHODS.map((m) => (
            <button
              key={m.value}
              type="button"
              onClick={() => setMethod(m.value)}
              className={cn(
                'h-11 rounded-input border text-[13px] font-medium transition-colors',
                method === m.value
                  ? 'border-primary bg-primary-soft text-primary-hover'
                  : 'border-input-border bg-surface text-muted hover:text-text',
              )}
            >
              {t(`pos.payment.${m.key}` as never)}
            </button>
          ))}
        </div>

        {method === 'Cash' && (
          <div className="flex flex-col gap-2">
            <label className="text-[13px] font-medium text-label">{t('seller.pos.received')}</label>
            <input
              type="number"
              step="any"
              autoFocus
              placeholder="0"
              value={received}
              onChange={(e) => setReceived(e.target.value)}
              className={inputCls}
            />
            <div className="flex flex-wrap gap-2">
              <ChipSm label={t('seller.pos.noChange')} onClick={() => setReceived(String(total))} />
              {cashChips(total).map((c) => (
                <ChipSm key={c} label={formatSum(c)} onClick={() => setReceived(String(c))} />
              ))}
            </div>
            {got > 0 && (
              <div
                className={cn(
                  'flex items-center justify-between rounded-input px-4 py-2.5 text-[14px]',
                  change >= 0 ? 'bg-success-soft text-success-text' : 'bg-danger-soft text-danger',
                )}
              >
                <span>{change >= 0 ? t('seller.pos.change') : t('seller.pos.short')}</span>
                <span className="font-semibold nums">
                  {formatSum(Math.abs(change))} {t('common.currency')}
                </span>
              </div>
            )}
          </div>
        )}

        {/* Uch usulning har biriga alohida summa — admin POS bilan bir xil.
            Ilgari bu yerda faqat naqd maydoni turardi va qolgani AVTOMAT kartaga
            yozilardi, ya'ni kassir o'tkazma aralashgan chekni umuman yopa
            olmasdi. «Qoldiq» tugmasi yetishmayotgan qismni o'sha qatorga
            to'ldiradi. */}
        {method === 'Mixed' && (
          <div className="flex flex-col gap-2">
            {MIX_ROWS.map((r, i) => (
              <div key={r.key} className="flex items-center justify-between gap-3">
                <label className="text-[13px] text-muted">{t(`pos.payment.${r.key}` as never)}</label>
                <div className="flex items-center gap-1.5">
                  {mixRemainder > 0 && (
                    <button
                      type="button"
                      onClick={() =>
                        setMixParts((prev) => ({
                          ...prev,
                          [r.key]: String(money((Number(prev[r.key]) || 0) + mixRemainder)),
                        }))
                      }
                      className="h-10 rounded-md border border-input-border px-2.5 text-[11.5px] font-medium text-muted hover:border-primary hover:text-primary"
                    >
                      {t('pos.mix.rest')}
                    </button>
                  )}
                  <input
                    type="text"
                    inputMode="decimal"
                    autoFocus={i === 0}
                    placeholder="0"
                    value={formatMixInput(mixParts[r.key])}
                    onChange={(e) =>
                      setMixParts((prev) => ({ ...prev, [r.key]: parseMixInput(e.target.value) }))
                    }
                    className="h-10 w-[140px] rounded-input border border-input-border bg-surface px-3 text-right text-[15px] outline-none focus:border-primary focus:shadow-focus-ring nums"
                  />
                  <span className="text-[12px] text-muted-2">{t('common.currency')}</span>
                </div>
              </div>
            ))}
            <div
              className={cn(
                'flex items-center justify-between rounded-input px-4 py-2.5 text-[13px]',
                mixRemainder === 0 && mixSum > 0
                  ? 'bg-success-soft text-success-text'
                  : 'bg-warn-soft text-warn-text',
              )}
            >
              <span>{t('pos.mix.remainder')}</span>
              {/* formatQty — kasr qoldiq (0.5) yaxlitlanmay ko'rinadi. */}
              <span className="font-semibold nums">
                {formatQty(mixRemainder)} {t('common.currency')}
              </span>
            </div>
          </div>
        )}

        {method === 'Terminal' && (
          <p className="rounded-input bg-primary-soft px-4 py-3 text-[13px] text-primary-hover">
            {t('seller.pos.cardHint')}
          </p>
        )}

        {method === 'Debt' && (
          <div className="flex flex-col gap-3">
            {!customer ? (
              <div className="flex flex-col gap-2 rounded-input bg-warn-soft px-4 py-3">
                <span className="text-[13px] text-warn-text">{t('seller.pos.debtNeedsCustomer')}</span>
                <Button size="sm" variant="secondary" onClick={onNeedCustomer}>
                  <UserPlus size={14} />
                  {t('pos.customer.add')}
                </Button>
              </div>
            ) : (
              <>
                <div className="rounded-input bg-bg px-4 py-2.5 text-[13px]">
                  {customer.fullName ?? customer.phone}
                </div>
                <div className="flex flex-col gap-1.5">
                  <label className="text-[13px] font-medium text-label">{t('seller.pos.paidNow')}</label>
                  <input
                    type="number"
                    step="any"
                    placeholder="0"
                    value={paidNow}
                    onChange={(e) => setPaidNow(e.target.value)}
                    className={inputCls}
                  />
                </div>
                <div className="flex flex-col gap-1.5">
                  <label className="text-[13px] font-medium text-label">{t('pos.dueDate')}</label>
                  <input
                    type="date"
                    value={due}
                    onChange={(e) => setDue(e.target.value)}
                    className="h-12 w-full rounded-input border border-input-border bg-surface px-4 text-[15px] outline-none focus:border-primary focus:shadow-focus-ring"
                  />
                </div>
                <div className="flex items-center justify-between rounded-input bg-warn-soft px-4 py-2.5 text-[14px] text-warn-text">
                  <span>{t('seller.pos.debtRest')}</span>
                  <span className="font-semibold nums">
                    {formatSum(debtRest)} {t('common.currency')}
                  </span>
                </div>
              </>
            )}
          </div>
        )}

        {error && <p className="text-[12.5px] text-danger">{error}</p>}
      </div>
    </Modal>
  );
}

function ChipSm({ label, onClick }: { label: string; onClick: () => void }) {
  return (
    <button
      type="button"
      onClick={onClick}
      className="rounded-pill border border-input-border bg-surface px-3 py-1.5 text-[12.5px] text-muted transition-colors hover:border-primary hover:text-primary nums"
    >
      {label}
    </button>
  );
}

