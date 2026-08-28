import { useEffect, useMemo, useRef, useState } from 'react';
import { useBlocker, useNavigate, useParams } from 'react-router-dom';
import { useQuery, useMutation, useQueryClient, keepPreviousData } from '@tanstack/react-query';
import { useTranslation } from 'react-i18next';
import {
  ArrowLeft,
  Search,
  Plus,
  Minus,
  X,
  Package,
  Check,
  UserPlus,
  Clock,
  Pause,
  PackagePlus,
  AlertTriangle,
  Pencil,
} from 'lucide-react';
import { Button, Card, Spinner, Badge, useConfirm } from '@/shared/ui';
import { useAuth } from '@/shared/auth/useAuth';
import { PERMISSIONS } from '@/shared/config/permissions';
import { cn } from '@/shared/lib/cn';
import { formatSum, formatQty } from '@/shared/lib/format';
import { unitLabel } from '@/shared/lib/units';
import { printPdfBlob } from '@/shared/lib/printPdf';
import { desktopBridge, printRawViaDesktop, toBase64 } from '@/shared/lib/desktopPrint';
import { printReceiptImage } from '@/shared/lib/printReceipt';
import { useDebounce } from '@/shared/hooks/useDebounce';
import type { ApiError } from '@/shared/api/types';
import { posApi, type PosCustomer, type PosSale } from './api';
import { bumpPending, mergePending, settlePending, type PendingLine, type PendingMap } from './pending';
import { useGlobalScanner } from './useGlobalScanner';
import {
  EMPTY_MIX,
  MIX_ROWS,
  formatMixInput,
  mixPayments,
  mixSumOf,
  money,
  parseMixInput,
  type MixParts,
} from './mix';
import { ReceiptModal } from './ReceiptModal';
import { ExternalItemModal } from './ExternalItemModal';
import { shiftsApi } from '@/features/shifts/api';
import { publicMarketApi } from '@/shared/api/auth';

// Click olib tashlangan (2026-07-26) — do'kon uni qabul qilmaydi; ro'yxatda
// turgani kassirni chalg'itardi. Backend enum'da qoladi (eski cheklar buzilmaydi).
const METHODS = [
  { key: 'cash', value: 'Cash' },
  { key: 'card', value: 'Terminal' },
  { key: 'transfer', value: 'Transfer' },
] as const;
// Aralash va Qarzga — alohida qatorda, kengroq tugmalar.
const WIDE_METHODS = [
  { key: 'mixed', value: 'Mixed' },
  { key: 'debt', value: 'Debt' },
] as const;

// Miks mantig'i ./mix.ts da — kassir kassasi ham SHU manbadan oladi.

export default function PosPage() {
  const { subdomain } = useParams();
  const navigate = useNavigate();
  const { t, i18n } = useTranslation();
  const qc = useQueryClient();
  const confirm = useConfirm();
  const { hasPermission } = useAuth();
  // Narx ustida savdolashish — auditlanadigan huquq, shuning uchun ruxsat
  // ortida. Owner/Admin uni sukut bo'yicha oladi.
  const canEditPrice = hasPermission(PERMISSIONS.sales.edit);

  const [saleId, setSaleId] = useState<string | null>(null);
  const [search, setSearch] = useState('');
  const debouncedSearch = useDebounce(search);
  const [method, setMethod] = useState<string>('Cash');
  const [customer, setCustomer] = useState<PosCustomer | null>(null);
  const [custOpen, setCustOpen] = useState(false);
  // Blank, not '0' — the field shows a faint "0" placeholder so the cashier can
  // type straight away instead of deleting a literal zero first.
  const [discountInput, setDiscountInput] = useState('');
  // Aralash to'lov: kassir uch usul bo'yicha summalarni o'zi taqsimlaydi.
  const [mixParts, setMixParts] = useState<MixParts>(EMPTY_MIX);
  const [externalOpen, setExternalOpen] = useState(false);
  const [success, setSuccess] = useState(false);
  // Yakunlangan chek — «Rasmiylashtirish»dan keyingi oyna shundan chiziladi.
  // Sotuv holati serverdan qayta o'qiladi: to'langan summa, qoldiq va status
  // to'lovlar qo'llanganidan KEYIN aniq bo'ladi.
  const [done, setDone] = useState<PosSale | null>(null);
  const [startError, setStartError] = useState<string | null>(null);
  const [actionError, setActionError] = useState<string | null>(null);

  // Chek (Draft) YARATILMAYDI, toki birinchi mahsulot savatga tushmaguncha.
  // Ilgari u mount'da yaratilardi — kassir POS'ni ochib, hech narsa sotmasdan
  // chiqib ketsa ham bazada ЧЕК № olgan bo'sh sotuv qolardi (StrictMode'da
  // effekt ikki marta ishlab, har kirishda ikkitadan), va checkout'dan keyin
  // darhol yana bittasi. Endi raqam faqat haqiqiy chek uchun sarflanadi.
  //
  // Promise ref'da saqlanadi: ikkita tez klik ham bitta createDraft'ni kutadi,
  // ya'ni ikkita parallel chek ochilmaydi.
  const draftRef = useRef<Promise<PosSale> | null>(null);
  const ensureSale = async (): Promise<string> => {
    if (saleId) return saleId;
    draftRef.current ??= posApi.createDraft(customer?.id ?? null);
    try {
      const s = await draftRef.current;
      setSaleId(s.id);
      return s.id;
    } catch (e) {
      draftRef.current = null; // keyingi urinish yangidan boshlansin
      throw e;
    }
  };

  const saleQuery = useQuery({
    queryKey: ['pos-sale', saleId],
    queryFn: () => posApi.getSale(saleId!),
    enabled: !!saleId,
  });
  // Chek header'i uchun: joriy smena raqami va do'kon nomi. Ikkalasi ham
  // bo'lmasa chek shusiz chiziladi — sotuvni bloklamaydi.
  const shiftQuery = useQuery({ queryKey: ['shift-current'], queryFn: shiftsApi.current });
  // Chek eni (58/80 mm) — do'kon sozlamasidan. Uzoq keshlanadi: u kuniga
  // o'zgaradigan qiymat emas, lekin qattiq yozib qo'yilsa 58 mm printerli
  // do'konda chek qog'ozga sig'masdi.
  const printSettingsQuery = useQuery({
    queryKey: ['pos-print-settings'],
    queryFn: posApi.printSettings,
    staleTime: 30 * 60_000,
  });

  const marketQuery = useQuery({
    queryKey: ['public-market', subdomain],
    queryFn: () => publicMarketApi.getState(subdomain!),
    enabled: !!subdomain,
    staleTime: 30 * 60_000,
  });
  const productsQuery = useQuery({
    queryKey: ['pos-products', debouncedSearch],
    queryFn: () => posApi.searchProducts({ page: 1, size: 30, search: debouncedSearch }),
    placeholderData: keepPreviousData,
  });

  const sale = saleQuery.data;
  const refresh = () => qc.invalidateQueries({ queryKey: ['pos-sale', saleId] });

  // Parked receipts — server-side Drafts, the same list the cashier shell shows.
  const draftsQuery = useQuery({ queryKey: ['pos-drafts'], queryFn: posApi.myDrafts });
  const refreshDrafts = () => qc.invalidateQueries({ queryKey: ['pos-drafts'] });
  const heldDrafts = (draftsQuery.data ?? []).filter((d) => d.id !== saleId && d.items.length > 0);

  /** Set the current receipt aside: it stays a Draft and returns in the strip. */
  function park() {
    draftRef.current = null;
    setSaleId(null);
    setCustomer(null);
    setMethod('Cash');
    setMixParts(EMPTY_MIX);
    setActionError(null);
    void refreshDrafts();
  }

  function resume(d: PosSale) {
    draftRef.current = null;
    setSaleId(d.id);
    setCustomer(
      d.customerId
        ? ({ id: d.customerId, fullName: d.customerName, phone: d.customerPhone ?? '' } as PosCustomer)
        : null,
    );
    setMixParts(EMPTY_MIX);
    setActionError(null);
  }

  /**
   * Leaving mid-sale — same prompt the cashier shell shows. The Draft survives
   * on the server either way, but a basket disappearing off the screen reads as
   * data loss, so we ask and park explicitly.
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
        title: t('pos.leaveTitle'),
        message: t('pos.leaveMessage'),
        confirmLabel: t('pos.park'),
        cancelLabel: t('pos.stay'),
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
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [blocker.state]);

  const discardDraft = useMutation({
    mutationFn: (id: string) => posApi.deleteMyDraft(id),
    onSuccess: (_res, id) => {
      if (id === saleId) park();
      // Discarding restocks every line, so the catalogue's own numbers are the
      // only trustworthy ones now — refetch rather than guess the deltas.
      void qc.invalidateQueries({ queryKey: ['pos-products'] });
      void refreshDrafts();
    },
    onError: (e) => setActionError((e as unknown as ApiError).message ?? ''),
  });
  /**
   * Serverga yuborilgan, lekin javobi hali kelmagan qatorlar — savat DARHOL
   * chizilishi uchun. Kassir qobig'idagi bilan bir xil yondashuv.
   */
  const [pending, setPending] = useState<PendingMap>({});

  /**
   * Serverga hali yuborilmagan bosishlar — tovar bo'yicha to'planadi.
   *
   * <p>Kassir bir soniyada uch marta bosishi mumkin. Har bosish uchun alohida
   * so'rov yuborish uchta muammoni birdan keltirib chiqarardi: so'rovlar
   * parallel ketib javoblari TARTIBSIZ kelardi, har javob chekni qayta
   * o'qishga majbur qilardi, va oxirgi javob kelguncha summa eski qolardi.
   * Bu yerda esa bosishlar birlashadi: navbatdagi so'rov tugagunicha
   * to'planganlari BITTA so'rovda ketadi.</p>
   */
  const cartBuffer = useRef(new Map<string, { line: PendingLine; quantity: number }>());
  const draining = useRef(false);

  /** Shtrix-kod → tovar: ikkinchi skanerlashda tarmoqqa chiqilmaydi. */
  const barcodeIndex = useRef(new Map<string, NonNullable<typeof productsQuery.data>['items'][number]>());
  useEffect(() => {
    for (const p of productsQuery.data?.items ?? []) {
      if (p.barcode) barcodeIndex.current.set(p.barcode, p);
    }
  }, [productsQuery.data]);

  // M-5: after any cart mutation, also refresh the product grid so displayed
  // stock (and the out-of-stock disable) reflects the sold quantities.
  const refreshAll = () => {
    void qc.invalidateQueries({ queryKey: ['pos-sale', saleId] });
    void qc.invalidateQueries({ queryKey: ['pos-products'] });
  };

  /**
   * Enter qidiruv maydonida — skanerning tugatish signali. Kassir qobig'idagi
   * bilan bir xil uch bosqich: aniq shtrix-kod → ro'yxatda yagona natija →
   * aks holda xabar. Skaner klaviatura kabi ishlagani va maydon doim fokusda
   * turgani uchun global tugma tutuvchi kerak emas.
   */
  /**
   * Tovarni savatga qo'shadi.
   *
   * <p>Ekran DARHOL yangilanadi, so'rov esa navbatga tushadi. Kassir
   * tarmoqni kutmaydi va necha marta tez bosishidan qat'i nazar ekran
   * sakramaydi.</p>
   */
  function addProduct(p: { id: string; name: string; salePrice: number; minSalePrice: number; unit: number; unitName: string }) {
    const line: PendingLine = {
      productId: p.id,
      productName: p.name,
      salePrice: p.salePrice,
      minSalePrice: p.minSalePrice,
      // Product da `unit` — UnitType raqami, `unitName` — qisqartma;
      // savat qatorida esa teskarisi ataladi.
      unit: p.unitName,
      unitValue: p.unit,
    };

    setActionError(null);
    setPending((map) => bumpPending(map, line, 1));

    const buffered = cartBuffer.current.get(p.id);
    if (buffered) buffered.quantity += 1;
    else cartBuffer.current.set(p.id, { line, quantity: 1 });

    void drainCart();
  }

  /**
   * Navbatni bo'shatadi: so'rovlar KETMA-KET yuboriladi.
   *
   * <p><b>Nega ketma-ket.</b> Parallel so'rovlarning javoblari istalgan
   * tartibda keladi va har biri chekni qayta o'qishga majbur qiladi. Uchta
   * tez bosishda ekran goh bo'shab, goh birdan to'lib ko'rinardi. Ketma-ket
   * yuborishda esa har lahzada bitta haqiqat bo'ladi.</p>
   *
   * <p><b>Nega birlashtirish.</b> Navbatdagi so'rov ketayotganda kassir yana
   * bossa, ular BITTA so'rovga yig'iladi: uch bosish uchun uch emas, ikki
   * so'rov ketadi (birinchisi darhol, qolgani birlashib).</p>
   *
   * <p>Chek faqat navbat BO'SHAGANDAN keyin bir marta qayta o'qiladi — har
   * bosishdan keyin emas.</p>
   */
  async function drainCart() {
    if (draining.current) return;
    draining.current = true;

    const confirmed: Record<string, number> = {};
    let lastSaleId: string | null = saleId;
    try {
      while (cartBuffer.current.size > 0) {
        const batch = [...cartBuffer.current.values()];
        cartBuffer.current.clear();

        // Birinchi tovar chekni ham yaratadi (lazy draft). ensureSale bitta
        // va'daga tayanadi, ya'ni tez bosishlarda ikkinchi qoralama
        // yaratilmaydi.
        lastSaleId = await ensureSale();

        for (const item of batch) {
          await posApi.addItem(lastSaleId, {
            isExternal: false,
            productId: item.line.productId,
            quantity: item.quantity,
            salePrice: item.line.salePrice,
            minSalePrice: item.line.minSalePrice,
          });
          confirmed[item.line.productId] = (confirmed[item.line.productId] ?? 0) + item.quantity;
        }
      }

      // Kalit ATAYLAB shu yerdagi qiymatdan: birinchi tovarda `saleId` holati
      // hali bo'sh bo'ladi va eski kod aynan shu sababli `['pos-sale', null]`
      // ni yangilar edi — ya'ni chek qayta o'qilmasdi, optimistik qator esa
      // o'chirilardi va tovar ekrandan YO'QOLARDI.
      await qc.invalidateQueries({ queryKey: ['pos-sale', lastSaleId] });
      void qc.invalidateQueries({ queryKey: ['pos-products'] });

      // Faqat tasdiqlangan miqdor ayiriladi: kutish paytida kassir yana
      // bosgan bo'lsa, o'sha bosish yo'qolmaydi.
      setPending((map) => settlePending(map, confirmed));
    } catch (e) {
      const err = e as unknown as ApiError;
      // Xatoda taxmin qilmaymiz: butun optimistik holat tashlanadi va
      // haqiqat serverdan qayta o'qiladi. Yarim to'g'ri savat eng yomon
      // holat bo'lardi — kassir noto'g'ri summani aytib yuborardi.
      cartBuffer.current.clear();
      setPending({});
      if (lastSaleId) void qc.invalidateQueries({ queryKey: ['pos-sale', lastSaleId] });

      if (err.code === 'SHIFT_NOT_OPEN') setStartError(err.message ?? '');
      else setActionError(err.message ?? '');
    } finally {
      draining.current = false;
    }

    // Drenaj paytida yangi bosishlar kelgan bo'lsa — davom etamiz.
    if (cartBuffer.current.size > 0) void drainCart();
  }

  async function handleScan() {
    const code = search.trim();
    if (!code) return;

    // 1) Lokal indeks — katalogdan yuklangan yoki ilgari skanerlangan kod
    //    bo'lsa tarmoqqa umuman chiqilmaydi.
    const known = barcodeIndex.current.get(code);
    if (known) {
      addProduct(known);
      setSearch('');
      return;
    }

    // 2) Noma'lum kod — bu yagona kutish, va u ham faqat birinchi marta.
    const product = await posApi.findByBarcode(code).catch(() => null);
    if (product) {
      if (product.barcode) barcodeIndex.current.set(product.barcode, product);
      addProduct(product);
      setSearch('');
      return;
    }

    // Raqamli uzun satr — shtrix-kod urinishi. Zaxira yo'l ATAYLAB ishlatilmaydi:
    // noma'lum kodga ro'yxatdagi tasodifiy tovarni qo'shish kassaning eng yomon
    // xatosi bo'lardi.
    if (/^\d{6,}$/.test(code)) {
      setActionError(t('pos.scan.notFound', { code }));
      return;
    }

    // Nom bo'yicha qidiruv: ro'yxat aynan shu so'rovga tegishli bo'lsagina.
    // debouncedSearch tekshiruvisiz bu yerda eski natijalar turadi.
    if (debouncedSearch.trim() !== code || productsQuery.isFetching) return;
    const found = productsQuery.data?.items ?? [];
    const only = found.length === 1 ? found[0] : undefined;
    if (only) {
      addProduct(only);
      setSearch('');
    }
  }

  /**
   * Skanerdan kelgan kod — fokus qayerda bo'lishidan qat'i nazar. Nom bo'yicha
   * qidiruv zaxirasi bu yerda YO'Q: skaner har doim shtrix-kod beradi.
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

  // Qo'shimcha oyna ochilganda o'chiriladi — u yerda skanerlangan kod savatga
  // tushmasligi kerak.
  useGlobalScanner(
    (code) => void handleScannedCode(code),
    !done && !success && !externalOpen && !custOpen,
  );

  /**
   * Katalogda yo'q tovar. Ombor qoldig'iga TEGMAYDI — tovar bizniki emas,
   * qo'shni do'kondan olinadi; server ham uni shunday qabul qiladi
   * (SaleItem.IsExternal, ProductId null).
   */
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
      void refresh();
    },
    onError: (e) => setActionError((e as unknown as ApiError).message ?? ''),
  });

  const removeOne = useMutation({
    mutationFn: (itemId: string) => posApi.removeItem(saleId!, itemId, 1),
    onSuccess: refreshAll,
  });
  // Miqdorni AYNAN qiymatga o'rnatish — «100 dona» uchun 100 marta «+» bosish
  // shart emas: son ustiga bosib, qo'lda kiritiladi (kasr ham mumkin: 3.5 kg).
  const setQty = useMutation({
    mutationFn: (p: { itemId: string; quantity: number }) =>
      posApi.setItemQuantity(saleId!, p.itemId, p.quantity),
    onSuccess: () => {
      setActionError(null);
      refreshAll();
    },
    onError: (e) => setActionError((e as unknown as ApiError).message ?? ''),
  });
  // Bitta qatorning narxini o'zgartirish (торг). Kassir ekranida bu allaqachon
  // bor edi, admin kassasida esa yo'q edi — ya'ni ruxsati bor rol imkoniyatdan
  // mahrum edi. Serverda auditga yoziladi va tannarxdan past narx (sozlama
  // yoqilgan bo'lsa) rad etiladi.
  const setLinePrice = useMutation({
    mutationFn: (p: { itemId: string; price: number }) => posApi.updateItemPrice(p.itemId, p.price),
    onSuccess: () => {
      setActionError(null);
      refreshAll();
    },
    onError: (e) => setActionError((e as unknown as ApiError).message ?? ''),
  });
  // Chegirma va mijoz chek hali yaratilmagan bo'lsa serverga bormaydi — qiymat
  // lokal state'da turadi. Mijoz keyin createDraft'ga uzatiladi; chegirma esa
  // savat to'lgach, kassir maydondan chiqqanda (blur) yuboriladi — baribir
  // backend bo'sh chekka chegirmani qabul qilmaydi (chegirma > jami summa).
  const applyDiscount = useMutation({
    mutationFn: (amount: number) => posApi.setDiscount(saleId!, amount),
    onSuccess: refresh,
  });
  const attachCustomer = useMutation({
    mutationFn: (c: PosCustomer | null) => posApi.attachCustomer(saleId!, c?.id ?? null),
    onSuccess: refresh,
  });

  const items = useMemo(() => mergePending(sale?.items ?? [], pending), [sale?.items, pending]);

  /**
   * Jami summa — ekrandagi qatorlardan hisoblanadi, server maydonidan EMAS.
   *
   * <p>Avval u `sale.totalAmount` dan olinardi, ya'ni har bosishdan keyin
   * chek qayta o'qilguncha eski qiymatni ko'rsatib turardi. Tez bosishlarda
   * bu «summa oxirida sekin hisoblanadi» bo'lib ko'rinardi. Formula
   * serverdagi bilan AYNAN bir xil (SaleTotals): qatorlar yig'indisi minus
   * chegirma, noldan past emas — shuning uchun tasdiq kelganda son
   * sakramaydi.</p>
   */
  const total = useMemo(() => {
    const gross = items.reduce((sum, it) => sum + it.salePrice * it.quantity, 0);
    return Math.max(0, gross - (sale?.discountAmount ?? 0));
  }, [items, sale?.discountAmount]);
  // Aralash: uch ulush yig'indisi jami bilan teng bo'lishi shart.
  const mixSum = mixSumOf(mixParts);
  const mixRemainder = money(total - mixSum);

  const checkout = useMutation({
    mutationFn: async (): Promise<PosSale | null> => {
      if (!sale) return null;
      const id = saleId!;
      if (method === 'Debt') {
        await posApi.markDebt(saleId!);
      } else if (method === 'Mixed') {
        // Barcha ulushlar BITTA so'rovda: server ularni atomar qo'llaydi, shuning
        // uchun chek "qisman to'langan ⇒ qarz" holatidan o'tib ketmaydi.
        // Nolga teng ulushlar yuborilmaydi (server amount > 0 talab qiladi).
        await posApi.checkout(saleId!, mixPayments(mixParts));
      } else {
        // Summa EKRANDAGI jamidan olinadi. `sale.totalAmount` — serverdan
        // oxirgi o'qilgan nusxa va u chegirmadan yoki hali yozilmagan
        // qatordan orqada qolishi mumkin edi; o'shanda kassir ko'rgan
        // summadan boshqa raqam yuborilardi. Kam yuborilsa chek jimgina
        // qarzga aylanardi.
        await posApi.addPayment(saleId!, { paymentType: method, amount: total });
      }
      // Chek uchun AVTORITAR holat: to'lovlardan keyingi paidAmount/qoldiq.
      // Lokal `sale` to'lovgacha bo'lgan nusxa — undan chek chizilsa,
      // «to'langan 0» ko'rinardi.
      return posApi.getSale(id);
    },
    onSuccess: (finished) => {
      setDone(finished);
      setSuccess(true);
      setActionError(null);
      void qc.invalidateQueries({ queryKey: ['pos-products'] });
      // Kassani bo'shatamiz, lekin YANGI chek ochmaymiz — keyingi chek o'zining
      // birinchi mahsuloti bilan tug'iladi. Aks holda har yakunlangan sotuvdan
      // keyin bazada bo'sh, raqam olgan chek qolib ketardi.
      draftRef.current = null;
      setSaleId(null);
      setCustomer(null);
      setDiscountInput('');
      setMixParts(EMPTY_MIX);
      setMethod('Cash');
      setTimeout(() => setSuccess(false), 2500);
    },
    onError: (e) => setActionError((e as unknown as ApiError).message ?? ''),
  });

  /** Chekni PDF ko'rinishida ochish. Sotuv allaqachon yakunlangan — chop etish
   *  muvaffaqiyatsiz bo'lsa ham pul harakati o'zgarmaydi, qayta urinsa bo'ladi. */
  /**
   * Chekni chop etadi — eng tez yo'ldan boshlab.
   *
   * <p>1. <b>ESC/POS</b>: bir necha kilobayt matn va buyruq printerga XOM
   * holda ketadi. Chizishni printer o'zi bajaradi va qog'ozni qirqadi —
   * qog'oz deyarli darhol chiqadi.</p>
   *
   * <p>2. <b>Rasm</b>: printer ESC/POS ni tushunmasa yoki XOM yo'l
   * yiqilsa. Sekinroq (server rasterlaydi, drayver qayta rasterlaydi),
   * lekin o'lcham baribir aniq.</p>
   *
   * <p>3. <b>PDF</b>: brauzerda yoki chek printeri tanlanmaganda —
   * odatdagi chop etish oynasi.</p>
   *
   * <p>Har uchala yo'l ham bir xil hujjatdan chiqadi, ya'ni chek
   * ko'rinishi yo'lga qarab o'zgarmaydi.</p>
   */
  async function printReceipt(id: string) {
    const widthMm = printSettingsQuery.data?.receiptWidthMm ?? 80;
    try {
      if (desktopBridge('receipt')) {
        const escpos = await posApi.receiptEscPos(id, i18n.language, widthMm);
        if (await printRawViaDesktop(await toBase64(escpos))) return;

        const png = await posApi.receiptImage(id, i18n.language, widthMm);
        if ((await printReceiptImage(png, widthMm)) === 'printed') return;
      }
      const blob = await posApi.receiptPdf(id, i18n.language, widthMm);
      await printPdfBlob(blob, `chek-${id}.pdf`);
    } catch {
      /* best-effort: the sale is already finalised, printing can be retried */
    }
  }

  // Aralashda kiritilgan ulushlar yig'indisi jami bilan AYNAN teng bo'lishi
  // shart — kam bo'lsa chek yopilmaydi, ko'p bo'lsa ortiqcha pul qayd bo'lardi.
  // Serverga yetib bormagan qatorlar bormi. Ular bo'lsa ekrandagi jami
  // serverdagidan katta va yakunlash noto'g'ri summa yuborardi — kassir
  // tovarni urib, javobni kutmasdan «Yakunlash» bosishi mumkin.
  const settling = Object.keys(pending).length > 0;

  const canCheckout =
    items.length > 0 &&
    !checkout.isPending &&
    !settling &&
    (method !== 'Mixed' || (total > 0 && mixRemainder === 0));

  return (
    <div className="flex h-full min-h-screen flex-col bg-bg">
      {/* Header */}
      <header className="flex items-center justify-between border-b border-border bg-surface px-6 py-4">
        <div className="flex items-center gap-3">
          <button
            type="button"
            onClick={() => navigate(`/${subdomain}/sales`)}
            className="flex h-9 w-9 items-center justify-center rounded-btn border border-input-border text-muted hover:text-text"
            aria-label={t('pos.back')}
          >
            <ArrowLeft size={17} />
          </button>
          <div>
            <h1 className="text-[18px] font-semibold">{t('pos.title')}</h1>
            {sale && <div className="text-[12px] text-muted-2 nums">№{sale.saleNumber}</div>}
          </div>
        </div>
        {success && (
          <span className="flex items-center gap-1.5 text-[14px] font-semibold text-success">
            <Check size={17} /> {t('pos.success')}
          </span>
        )}
      </header>

      {startError ? (
        <div className="flex flex-1 flex-col items-center justify-center gap-4 p-8 text-center">
          <div className="flex h-16 w-16 items-center justify-center rounded-2xl bg-warn-soft text-warn">
            <Clock size={28} />
          </div>
          <p className="max-w-md text-[15px] text-muted">{startError}</p>
          <div className="flex gap-3">
            <Button variant="secondary" onClick={() => navigate(`/${subdomain}/shifts`)}>
              {t('shifts.openShift')}
            </Button>
            <Button onClick={() => setStartError(null)}>{t('common.retry')}</Button>
          </div>
        </div>
      ) : (
      // Kassir kassasi bilan bir xil qoida: lg dan boshlab ikki ustun,
      // undan pastda ustma-ust joylashadi va sahifaning o'zi suriladi.
      <div className="grid flex-1 grid-cols-1 gap-0 lg:grid-cols-[1.5fr_1fr] lg:overflow-hidden">
        {/* LEFT — product search */}
        <div className="flex min-w-0 flex-col border-b border-border p-4 sm:p-6 lg:border-b-0 lg:border-r">
          <div className="mb-4 flex gap-2">
          <div className="relative flex-1">
            <Search size={18} className="absolute left-4 top-1/2 -translate-y-1/2 text-muted-2" />
            <input
              value={search}
              onChange={(e) => {
                setSearch(e.target.value);
                if (actionError) setActionError(null);
              }}
              onKeyDown={(e) => {
                if (e.key !== 'Enter') return;
                e.preventDefault();
                void handleScan();
              }}
              placeholder={t('pos.searchPlaceholder')}
              autoFocus
              className="h-12 w-full rounded-input border border-input-border bg-surface pl-12 pr-4 text-[15px] outline-none focus:border-primary focus:shadow-focus-ring"
            />
          </div>
          {/* Katalogda yo'q tovar — mijoz so'ragan narsa bizda bo'lmasa, uni
              qo'shni do'kondan olib berish odatiy hol. Ilgari bu imkoniyat
              faqat kassir qobig'ida bor edi. */}
          <Button variant="secondary" className="h-12 flex-none" onClick={() => setExternalOpen(true)}>
            <PackagePlus size={16} />
            {t('seller.pos.external.button')}
          </Button>
          </div>
          <div className="flex-1 lg:overflow-y-auto">
            {productsQuery.isLoading ? (
              <div className="flex justify-center py-16 text-primary">
                <Spinner size={24} />
              </div>
            ) : (
              <div className="grid grid-cols-2 gap-2.5 xl:grid-cols-3">
                {(productsQuery.data?.items ?? []).map((p) => (
                  <button
                    key={p.id}
                    type="button"
                    disabled={p.quantity <= 0}
                    onClick={() => addProduct(p)}
                    className="flex flex-col rounded-card border border-border bg-surface p-3 text-left transition-colors hover:border-primary disabled:opacity-50"
                  >
                    <div className="mb-2 flex h-8 w-8 items-center justify-center rounded-lg bg-hairline text-muted-2">
                      <Package size={15} />
                    </div>
                    <div className="line-clamp-2 text-[13px] font-medium leading-tight">{p.name}</div>
                    <div className="mt-1 text-[11.5px] text-muted-2 nums">
                      {formatQty(p.quantity)} {unitLabel(t, p.unit, p.unitName)}
                    </div>
                    <div className="mt-1.5 text-[14px] font-semibold text-primary nums">
                      {formatSum(p.salePrice)}
                    </div>
                  </button>
                ))}
              </div>
            )}
          </div>
        </div>

        {/* RIGHT — cart */}
        <div className="flex flex-col bg-surface">
          {/* Parked receipts. The admin register had no way to set a basket
              aside: a customer who went back for one more bag of cement blocked
              the till until the sale was finished or thrown away. Same strip and
              same server-side Drafts as the cashier shell. */}
          {heldDrafts.length > 0 && (
            <div className="border-b border-hairline bg-warn-soft/60 px-5 py-3">
              <div className="mb-2 text-[11px] font-semibold uppercase tracking-[0.5px] text-warn-strong">
                {t('pos.held')} · {heldDrafts.length}
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
                      title={t('pos.discard')}
                      disabled={discardDraft.isPending}
                      onClick={async () => {
                        if (
                          await confirm({
                            title: t('pos.discardConfirm'),
                            tone: 'danger',
                            confirmLabel: t('pos.discard'),
                          })
                        )
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
              <div className="flex items-center justify-between">
                <div className="min-w-0">
                  <div className="truncate text-[13.5px] font-medium">{customer.fullName ?? customer.phone}</div>
                  <div className="text-[11.5px] text-muted-2 nums">{customer.phone}</div>
                </div>
                <button
                  type="button"
                  onClick={() => {
                    setCustomer(null);
                    if (saleId) attachCustomer.mutate(null);
                  }}
                  className="text-muted-2 hover:text-danger"
                >
                  <X size={16} />
                </button>
              </div>
            ) : (
              <button
                type="button"
                onClick={() => setCustOpen(true)}
                className="flex items-center gap-2 text-[13.5px] font-medium text-muted hover:text-primary"
              >
                <UserPlus size={16} /> {t('pos.customer.add')}
                <span className="text-muted-2">· {t('pos.customer.walkIn')}</span>
              </button>
            )}
          </div>

          {/* Items */}
          <div className="flex-1 px-5 lg:overflow-y-auto">
            {items.length === 0 ? (
              <div className="flex h-full flex-col items-center justify-center gap-3 py-16 text-center text-muted-2">
                <Package size={28} />
                <p className="max-w-[220px] text-[13.5px]">{t('pos.emptyCart')}</p>
              </div>
            ) : (
              items.map((it) => (
                <div key={it.id} className="flex items-center gap-3 border-b border-hairline py-3">
                  <div className="min-w-0 flex-1">
                    <div className="truncate text-[13.5px] font-medium">{it.productName}</div>
                    <div className="flex items-center gap-1 text-[12px] text-muted-2 nums">
                      <CartPrice
                        price={it.salePrice}
                        editable={canEditPrice}
                        title={t('seller.pos.editPrice')}
                        onCommit={(price) => setLinePrice.mutate({ itemId: it.id, price })}
                      />
                      <span>· {formatQty(it.quantity)} {unitLabel(t, it.unitValue, it.unit)}</span>
                    </div>
                  </div>
                  <div className="flex items-center gap-1.5">
                    <button
                      type="button"
                      onClick={() => removeOne.mutate(it.id)}
                      className="flex h-7 w-7 items-center justify-center rounded-md border border-input-border text-muted hover:text-danger"
                    >
                      <Minus size={14} />
                    </button>
                    <CartQty
                      quantity={it.quantity}
                      onCommit={(q) => setQty.mutate({ itemId: it.id, quantity: q })}
                    />
                    <button
                      type="button"
                      disabled={!it.productId}
                      onClick={() =>
                        it.productId &&
                        addProduct({
                          id: it.productId,
                          name: it.productName,
                          salePrice: it.salePrice,
                          // Qator narxi allaqachon tasdiqlangan — quyi chegara
                          // sifatida o'shani beramiz, aks holda server torg'
                          // qilingan narxni rad etardi.
                          minSalePrice: it.salePrice,
                          unit: it.unitValue,
                          unitName: it.unit,
                        })
                      }
                      className="flex h-7 w-7 items-center justify-center rounded-md border border-input-border text-muted hover:text-primary disabled:opacity-40"
                    >
                      <Plus size={14} />
                    </button>
                  </div>
                  <div className="w-[92px] text-right text-[13.5px] font-semibold nums">{formatSum(it.totalPrice)}</div>
                </div>
              ))
            )}
          </div>

          {/* Footer: discount, total, payment, checkout */}
          <div className="border-t border-border px-5 py-4">
            <div className="mb-3 flex items-center justify-between">
              <label className="text-[13px] text-muted">{t('pos.discount')}</label>
              <div className="flex items-center gap-1.5">
                <input
                  type="number"
                  step="any"
                  placeholder="0"
                  value={discountInput}
                  onChange={(e) => setDiscountInput(e.target.value)}
                  onBlur={() => saleId && applyDiscount.mutate(Math.max(0, Number(discountInput) || 0))}
                  className="h-9 w-[120px] rounded-input border border-input-border bg-surface px-3 text-right text-[14px] outline-none focus:border-primary nums"
                />
                <span className="text-[12px] text-muted-2">{t('common.currency')}</span>
              </div>
            </div>

            <div className="mb-4 flex items-baseline justify-between">
              <span className="text-[15px] font-semibold">{t('pos.total')}</span>
              <span className="text-[24px] font-bold tracking-[-0.3px] nums">
                {formatSum(total)} <span className="text-[13px] font-medium text-muted-2">{t('common.currency')}</span>
              </span>
            </div>

            {/* Tanlangani to'liq bo'yalgan, tanlanmagani ham to'q matnli —
                oldingi kulrang variant «o'chiq» ko'rinardi. */}
            <div className="mb-1.5 grid grid-cols-3 gap-1.5">
              {METHODS.map((m) => (
                <button
                  key={m.value}
                  type="button"
                  onClick={() => setMethod(m.value)}
                  className={cn(
                    'h-11 rounded-input border-[1.5px] text-[13px] font-semibold transition-colors',
                    method === m.value
                      ? 'border-primary bg-primary text-white shadow-btn'
                      : 'border-input-border bg-surface text-text hover:border-primary hover:text-primary',
                  )}
                >
                  {t(`pos.payment.${m.key}`)}
                </button>
              ))}
            </div>
            <div className="mb-3 grid grid-cols-2 gap-1.5">
              {WIDE_METHODS.map((m) => (
                <button
                  key={m.value}
                  type="button"
                  onClick={() => setMethod(m.value)}
                  className={cn(
                    'h-11 rounded-input border-[1.5px] text-[13px] font-semibold transition-colors',
                    method === m.value
                      ? m.key === 'debt'
                        ? 'border-warn bg-warn text-white'
                        : 'border-primary bg-primary text-white shadow-btn'
                      : 'border-input-border bg-surface text-text hover:border-primary hover:text-primary',
                  )}
                >
                  {t(`pos.payment.${m.key}`)}
                </button>
              ))}
            </div>

            {method === 'Mixed' && (
              <div className="mb-3 flex flex-col gap-2">
                {/* Uch usulning har biriga summa kiritiladi; «qoldiq» tugmasi
                    yetishmayotgan qismini o'sha qatorga to'ldiradi. */}
                {MIX_ROWS.map((r) => (
                  <div key={r.key} className="flex items-center justify-between gap-3">
                    <label className="text-[13px] text-muted">{t(`pos.payment.${r.key}`)}</label>
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
                          className="h-9 rounded-md border border-input-border px-2.5 text-[11.5px] font-medium text-muted hover:border-primary hover:text-primary"
                        >
                          {t('pos.mix.rest')}
                        </button>
                      )}
                      <input
                        type="text"
                        inputMode="decimal"
                        placeholder="0"
                        value={formatMixInput(mixParts[r.key])}
                        onChange={(e) =>
                          setMixParts((prev) => ({ ...prev, [r.key]: parseMixInput(e.target.value) }))
                        }
                        className="h-9 w-[120px] rounded-input border border-input-border bg-surface px-3 text-right text-[14px] outline-none focus:border-primary nums"
                      />
                      <span className="text-[12px] text-muted-2">{t('common.currency')}</span>
                    </div>
                  </div>
                ))}
                <div
                  className={cn(
                    'flex items-center justify-between rounded-input px-3 py-2 text-[13px]',
                    mixRemainder === 0 && mixSum > 0
                      ? 'bg-success-soft text-success-text'
                      : 'bg-warn-soft text-warn-text',
                  )}
                >
                  <span>{t('pos.mix.remainder')}</span>
                  <span className="font-semibold nums">
                    {/* formatQty — kasr qoldiq (0.5) yaxlitlanmay ko'rinadi;
                        formatSum uni «0»/«1» qilib yashirib yuborardi. */}
                    {formatQty(mixRemainder)} {t('common.currency')}
                  </span>
                </div>
              </div>
            )}

            {actionError && (
              <div className="mb-2 flex items-center gap-2 rounded-input bg-danger-soft px-3 py-2 text-[12.5px] text-danger">
                <AlertTriangle size={14} className="flex-none" />
                <span>{actionError}</span>
              </div>
            )}
            {method === 'Debt' && !customer && (
              <div className="mb-2 text-[12px] text-warn-text">{t('pos.customer.label')}: {t('pos.customer.walkIn')}</div>
            )}

            <div className="flex gap-2">
              <Button
                variant="secondary"
                size="lg"
                className="flex-none"
                disabled={!saleId || items.length === 0}
                onClick={park}
              >
                <Pause size={15} />
                {t('pos.park')}
              </Button>
              <Button
                fullWidth
                size="lg"
                variant={method === 'Debt' ? 'secondary' : 'primary'}
                disabled={!canCheckout}
                loading={checkout.isPending}
                onClick={() => checkout.mutate()}
              >
                {t('pos.checkout')}
              </Button>
            </div>
          </div>
        </div>
      </div>
      )}

      <ExternalItemModal
        open={externalOpen}
        onClose={() => setExternalOpen(false)}
        pending={addExternal.isPending}
        error={addExternal.isError ? ((addExternal.error as unknown as ApiError).message ?? '') : null}
        onSubmit={(p) => addExternal.mutate(p)}
      />

      <ReceiptModal
        sale={done}
        shiftNumber={shiftQuery.data?.shiftNumber ?? 0}
        storeName={marketQuery.data?.marketName ?? null}
        closeLabel={t('pos.done.finish')}
        onClose={() => setDone(null)}
        onPrint={printReceipt}
      />

      {custOpen && (
        <CustomerPicker
          onClose={() => setCustOpen(false)}
          onPick={(c) => {
            setCustomer(c);
            if (saleId) attachCustomer.mutate(c);
            setCustOpen(false);
          }}
        />
      )}
    </div>
  );
}

/** Telefonni tozalash: faqat raqamlar, bosh «+» saqlanadi (seller kassasidagi bilan bir xil). */
function normalisePhone(raw: string): string {
  const digits = raw.replace(/[^\d]/g, '');
  return raw.trim().startsWith('+') ? `+${digits}` : digits;
}

/**
 * Mijoz tanlash oynasi.
 *
 * Ochilishi bilanoq mijozlar RO'YXATI ko'rinadi (avval qidiruvga 2 belgi
 * yozilmaguncha bo'm-bo'sh turardi — kassir ro'yxat borligini ham bilmasdi).
 * Yangi mijoz shu yerning o'zida qo'shiladi: «Mijoz qo'shish» formasi ism +
 * telefon so'raydi, saqlangach mijoz darhol chekka biriktiriladi. Qidiruvga
 * yozilgan matn formaga o'tadi: raqam bo'lsa — telefonga, matn bo'lsa — ismga.
 */
function CustomerPicker({ onClose, onPick }: { onClose: () => void; onPick: (c: PosCustomer) => void }) {
  const { t } = useTranslation();
  const qc = useQueryClient();
  const [q, setQ] = useState('');
  const debounced = useDebounce(q);
  const [creating, setCreating] = useState(false);
  const [newName, setNewName] = useState('');
  const [newPhone, setNewPhone] = useState('');
  const [createError, setCreateError] = useState<string | null>(null);

  // Bo'sh qidiruv ham so'raladi — birinchi sahifa darhol ko'rinadi.
  const query = useQuery({
    queryKey: ['pos-customers', debounced],
    queryFn: () => posApi.searchCustomers(debounced),
    placeholderData: keepPreviousData,
  });

  const create = useMutation({
    mutationFn: () =>
      posApi.createCustomer({
        phone: normalisePhone(newPhone),
        fullName: newName.trim() || null,
      }),
    onSuccess: (c) => {
      // Mijozlar sahifasi va shu oynaning ro'yxatlari yangilansin.
      void qc.invalidateQueries({ queryKey: ['pos-customers'] });
      void qc.invalidateQueries({ queryKey: ['customers'] });
      onPick(c);
    },
    onError: (e) => setCreateError((e as unknown as ApiError).message ?? t('common.somethingWrong')),
  });

  // Backend telefon uchun 9-15 raqam talab qiladi - tugma shuni aks ettiradi.
  const phoneValid = /^\+?[0-9]{9,15}$/.test(normalisePhone(newPhone));

  const openCreate = () => {
    const typed = q.trim();
    if (/^[+\d\s-]{3,}$/.test(typed)) setNewPhone(typed);
    else if (typed) setNewName(typed);
    setCreateError(null);
    setCreating(true);
  };

  const pickerInputCls =
    'h-11 w-full rounded-input border border-input-border bg-surface px-3.5 text-[14px] outline-none focus:border-primary focus:shadow-focus-ring';

  return (
    <div
      className="fixed inset-0 z-50 flex items-start justify-center bg-text/40 px-4 py-16"
      onMouseDown={(e) => e.target === e.currentTarget && onClose()}
    >
      <Card className="w-full max-w-md animate-fade-in p-5">
        <div className="mb-3 flex items-center justify-between">
          <h2 className="text-[16px] font-semibold">
            {creating ? t('customers.form.addTitle') : t('pos.customer.label')}
          </h2>
          <button type="button" onClick={onClose} className="text-muted-2 hover:text-text">
            <X size={18} />
          </button>
        </div>
        {creating ? (
          <div className="flex flex-col gap-3">
            <div className="flex flex-col gap-1.5">
              <label className="text-[13px] font-medium text-label">{t('customers.form.name')}</label>
              <input value={newName} onChange={(e) => setNewName(e.target.value)} autoFocus className={pickerInputCls} />
            </div>
            <div className="flex flex-col gap-1.5">
              <label className="text-[13px] font-medium text-label">{t('customers.form.phone')}</label>
              <input
                value={newPhone}
                onChange={(e) => setNewPhone(e.target.value)}
                placeholder="+998 90 123 45 67"
                className={cn(pickerInputCls, 'nums')}
              />
            </div>
            {createError && (
              <div className="rounded-input bg-danger-soft px-3 py-2 text-[12.5px] text-danger">{createError}</div>
            )}
            <div className="flex justify-end gap-2">
              <Button variant="ghost" onClick={() => setCreating(false)}>
                {t('common.cancel')}
              </Button>
              <Button
                disabled={!phoneValid || create.isPending}
                loading={create.isPending}
                onClick={() => create.mutate()}
              >
                {t('common.save')}
              </Button>
            </div>
          </div>
        ) : (
          <>
            <input
              value={q}
              onChange={(e) => setQ(e.target.value)}
              autoFocus
              placeholder={t('pos.customer.searchPlaceholder')}
              className={cn(pickerInputCls, 'mb-3')}
            />
            <div className="max-h-[320px] overflow-y-auto">
              {query.isLoading ? (
                <div className="flex justify-center py-8 text-primary">
                  <Spinner size={20} />
                </div>
              ) : (query.data?.items ?? []).length === 0 ? (
                <p className="py-8 text-center text-[13px] text-muted-2">{t('customers.empty')}</p>
              ) : (
                (query.data?.items ?? []).map((c) => (
                  <button
                    key={c.id}
                    type="button"
                    onClick={() => onPick(c)}
                    className="flex w-full items-center justify-between border-b border-hairline py-2.5 text-left last:border-0 hover:bg-bg/40"
                  >
                    <div>
                      <div className="text-[13.5px] font-medium">{c.fullName ?? c.phone}</div>
                      <div className="text-[12px] text-muted-2 nums">{c.phone}</div>
                    </div>
                    {c.totalDebt > 0 && <Badge tone="warn">{formatSum(c.totalDebt)}</Badge>}
                  </button>
                ))
              )}
            </div>
            {/* Yangi mijoz — ro'yxat ostidagi doimiy tugma: topilmasa ham,
                shunchaki yangi mijoz kelsa ham shu yerdan. */}
            <button
              type="button"
              onClick={openCreate}
              className="mt-3 flex h-11 w-full items-center justify-center gap-2 rounded-input border-[1.5px] border-dashed border-input-border text-[13.5px] font-medium text-primary transition-colors hover:border-primary"
            >
              <UserPlus size={15} />
              {t('pos.customer.add')}
            </button>
          </>
        )}
      </Card>
    </div>
  );
}

/**
 * Savatdagi miqdor — bosilganda tahrirlanadigan maydonga aylanadi.
 *
 * «+» tugmasi 1 taga oshiradi, lekin 100 dona uchun 100 marta bosish o'rniga
 * kassir son ustiga bosib, qiymatni qo'lda yozadi (kasr ham: 3.5). Enter/blur —
 * saqlaydi, Escape — bekor qiladi. Escape'da `blur()` onBlur'ni sinxron
 * chaqiradi, shuning uchun bekor qilish bayrog'i commit'dan oldin tekshiriladi.
 */
/**
 * Qator narxi — bosilganda joyida tahrirlanadi (торг).
 *
 * <p>Miqdor maydoni bilan bir xil xulq: fokusda hammasi belgilanadi, Enter
 * tasdiqlaydi, Escape bekor qiladi, bo'sh qoldirilsa hech narsa yubormaydi.
 * Ruxsat bo'lmasa oddiy matn bo'lib qoladi — tugma umuman chiqmaydi.</p>
 */
function CartPrice({
  price,
  editable,
  title,
  onCommit,
}: {
  price: number;
  editable: boolean;
  title: string;
  onCommit: (p: number) => void;
}) {
  const [draft, setDraft] = useState<string | null>(null);
  const cancelRef = useRef(false);

  const commit = () => {
    if (cancelRef.current) {
      cancelRef.current = false;
      setDraft(null);
      return;
    }
    if (draft === null) return;
    const raw = draft.trim();
    setDraft(null);
    if (raw === '') return;
    const p = Number(raw.replace(',', '.'));
    if (!Number.isFinite(p) || p < 0 || p === price) return;
    onCommit(p);
  };

  if (draft !== null) {
    return (
      <input
        autoFocus
        onFocus={(e) => e.currentTarget.select()}
        type="text"
        inputMode="decimal"
        value={draft}
        onChange={(e) => setDraft(e.target.value.replace(/[^\d.,]/g, ''))}
        onBlur={commit}
        onKeyDown={(e) => {
          if (e.key === 'Enter') e.currentTarget.blur();
          if (e.key === 'Escape') {
            cancelRef.current = true;
            e.currentTarget.blur();
          }
        }}
        className="h-6 w-24 rounded border border-primary bg-surface px-1.5 text-right text-[12px] outline-none nums"
      />
    );
  }

  if (!editable) return <span className="nums">{formatSum(price)}</span>;

  return (
    <button
      type="button"
      title={title}
      onClick={() => setDraft(String(price))}
      className="flex items-center gap-1 rounded px-0.5 text-muted-2 transition-colors hover:text-primary"
    >
      <span className="nums">{formatSum(price)}</span>
      <Pencil size={11} />
    </button>
  );
}

function CartQty({ quantity, onCommit }: { quantity: number; onCommit: (q: number) => void }) {
  const [draft, setDraft] = useState<string | null>(null);
  const cancelRef = useRef(false);

  const commit = () => {
    if (cancelRef.current) {
      cancelRef.current = false;
      setDraft(null);
      return;
    }
    if (draft === null) return;
    // Bo'sh qoldirilgan maydon — bekor qilish, o'chirish EMAS: fokusda hammasi
    // belgilanadi, Backspace + tashqariga bosish qatorni yo'qotib yuborardi.
    // Qator faqat ataylab «0» yozilganda o'chadi (server shunday hujjatlangan).
    if (draft.trim() === '') {
      setDraft(null);
      return;
    }
    const q = Number(draft.replace(',', '.'));
    setDraft(null);
    if (!Number.isFinite(q) || q < 0 || q === quantity) return;
    onCommit(q);
  };

  if (draft === null) {
    return (
      <button
        type="button"
        onClick={() => setDraft(String(quantity))}
        className="h-7 w-14 rounded-md border border-transparent text-center text-[13px] font-semibold transition-colors hover:border-input-border hover:bg-bg nums"
      >
        {formatQty(quantity)}
      </button>
    );
  }
  return (
    <input
      autoFocus
      onFocus={(e) => e.currentTarget.select()}
      type="text"
      inputMode="decimal"
      value={draft}
      onChange={(e) => setDraft(e.target.value.replace(/[^\d.,]/g, ''))}
      onBlur={commit}
      onKeyDown={(e) => {
        if (e.key === 'Enter') e.currentTarget.blur();
        if (e.key === 'Escape') {
          cancelRef.current = true;
          e.currentTarget.blur();
        }
      }}
      className="h-7 w-14 rounded-md border border-primary bg-surface text-center text-[13px] font-semibold outline-none nums"
    />
  );
}
