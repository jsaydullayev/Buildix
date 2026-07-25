import { useEffect, useMemo, useRef, useState } from 'react';
import { useParams } from 'react-router-dom';
import { useQuery, useMutation, useQueryClient, keepPreviousData } from '@tanstack/react-query';
import { useTranslation } from 'react-i18next';
import { Search, Plus, Minus, X, Package, Check, UserPlus, Clock, Printer, Pause } from 'lucide-react';
import { Button, Card, Spinner, Badge, Modal } from '@/shared/ui';
import { cn } from '@/shared/lib/cn';
import { formatSum, formatQty, formatTime } from '@/shared/lib/format';
import { unitLabel } from '@/shared/lib/units';
import { useDebounce } from '@/shared/hooks/useDebounce';
import type { ApiError } from '@/shared/api/types';
import { publicMarketApi } from '@/shared/api/auth';
import { categoriesApi } from '@/features/warehouse/api';
import { shiftsApi } from '@/features/shifts/api';
import { posApi, type PosCustomer, type PosSale } from '@/features/pos/api';

type Method = 'Cash' | 'Terminal' | 'Mixed' | 'Debt';
const METHODS: { key: string; value: Method }[] = [
  { key: 'cash', value: 'Cash' },
  { key: 'card', value: 'Terminal' },
  { key: 'mixed', value: 'Mixed' },
  { key: 'debt', value: 'Debt' },
];

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
 * Seller register (Касса) — lives inside the seller top-nav shell.
 *
 * The Draft receipt is created lazily by the first added product (same rule as
 * the admin POS), so opening the register never burns a ЧЕК № on an empty sale.
 */
export default function SellerPosPage() {
  const { t, i18n } = useTranslation();
  const qc = useQueryClient();

  const [saleId, setSaleId] = useState<string | null>(null);
  const [search, setSearch] = useState('');
  const debouncedSearch = useDebounce(search);
  const [categoryId, setCategoryId] = useState<number | null>(null);
  const [customer, setCustomer] = useState<PosCustomer | null>(null);
  const [custOpen, setCustOpen] = useState(false);
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

  const categoriesQuery = useQuery({ queryKey: ['categories'], queryFn: categoriesApi.list });
  const productsQuery = useQuery({
    queryKey: ['pos-products', debouncedSearch, categoryId],
    queryFn: () => posApi.searchProducts({ page: 1, size: 40, search: debouncedSearch, categoryId }),
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
  const items = sale?.items ?? [];
  const total = sale?.totalAmount ?? 0;
  // Chegirmasiz oraliq summa — chegirma qatorini ko'rsatish uchun kerak
  // (total allaqachon chegirma ayirilgan holda keladi).
  const gross = items.reduce((acc, it) => acc + it.totalPrice, 0);

  // Kutayotgan chekda chegirma bo'lishi mumkin — uni davom ettirganda input
  // serverdagi qiymatni ko'rsatsin, aks holda maydon bo'sh turib, jami esa
  // kamaytirilgan bo'lardi. Yozayotganda qayta ishga tushmaydi: saleId ham,
  // discountAmount ham o'zgarmaydi (faqat muvaffaqiyatli qo'llashdan keyin).
  useEffect(() => {
    setDiscountInput(sale?.discountAmount ? String(sale.discountAmount) : '');
  }, [saleId, sale?.discountAmount]);

  const refreshAll = () => {
    void qc.invalidateQueries({ queryKey: ['pos-sale', saleId] });
    void qc.invalidateQueries({ queryKey: ['pos-products'] });
    void qc.invalidateQueries({ queryKey: ['pos-drafts'] });
  };

  const addItem = useMutation({
    mutationFn: async (p: { productId: string; salePrice: number; minSalePrice: number }) => {
      const id = await ensureSale();
      return posApi.addItem(id, {
        isExternal: false,
        productId: p.productId,
        quantity: 1,
        salePrice: p.salePrice,
        minSalePrice: p.minSalePrice,
      });
    },
    onSuccess: () => {
      setActionError(null);
      refreshAll();
    },
    onError: (e) => {
      const err = e as unknown as ApiError;
      // No open shift blocks the whole register, so it gets the full-screen state.
      if (err.code === 'SHIFT_NOT_OPEN') setStartError(err.message ?? '');
      else setActionError(err.message ?? '');
    },
  });

  const removeOne = useMutation({
    mutationFn: (itemId: string) => posApi.removeItem(saleId!, itemId, 1),
    onSuccess: refreshAll,
    onError: (e) => setActionError((e as unknown as ApiError).message ?? ''),
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
      void qc.invalidateQueries({ queryKey: ['pos-drafts'] });
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
      void qc.invalidateQueries({ queryKey: ['pos-sale', saleId] });
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
    onSuccess: () => void qc.invalidateQueries({ queryKey: ['pos-sale', saleId] }),
  });

  function pickCustomer(c: PosCustomer | null) {
    setCustomer(c);
    setCustOpen(false);
    // Only push to the server once a Draft exists; otherwise it rides along on
    // createDraft when the first product is added.
    if (saleId) attachCustomer.mutate(c);
  }

  /** Park the current receipt: it simply stays a Draft and reappears in the strip. */
  function park() {
    draftRef.current = null;
    setSaleId(null);
    setCustomer(null);
    setMethod('Cash');
    setActionError(null);
    void qc.invalidateQueries({ queryKey: ['pos-drafts'] });
  }

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
      const blob = await posApi.invoicePdf(id, i18n.language);
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
        <Button onClick={() => setStartError(null)}>{t('common.retry')}</Button>
      </div>
    );
  }

  return (
    <div className="grid flex-1 grid-cols-[1fr_420px] overflow-hidden">
      {/* ── LEFT: catalogue ─────────────────────────────────────── */}
      <div className="flex min-w-0 flex-col overflow-hidden p-6">
        <div className="relative mb-3">
          <Search size={18} className="absolute left-4 top-1/2 -translate-y-1/2 text-muted-2" />
          <input
            ref={searchRef}
            value={search}
            onChange={(e) => setSearch(e.target.value)}
            placeholder={t('pos.searchPlaceholder')}
            autoFocus
            className="h-12 w-full rounded-input border border-input-border bg-surface pl-12 pr-16 text-[15px] outline-none focus:border-primary focus:shadow-focus-ring"
          />
          <kbd className="absolute right-4 top-1/2 -translate-y-1/2 rounded border border-input-border px-1.5 py-0.5 text-[11px] text-muted-2">
            F2
          </kbd>
        </div>

        <div className="mb-4 flex flex-wrap items-center gap-1.5">
          <Chip label={t('seller.pos.allProducts')} active={categoryId === null} onClick={() => setCategoryId(null)} />
          {(categoriesQuery.data ?? []).map((c) => (
            <Chip key={c.id} label={c.name} active={categoryId === c.id} onClick={() => setCategoryId(c.id)} />
          ))}
        </div>

        <div className="flex-1 overflow-y-auto">
          {productsQuery.isLoading ? (
            <div className="flex justify-center py-20 text-primary">
              <Spinner size={26} />
            </div>
          ) : (productsQuery.data?.items.length ?? 0) === 0 ? (
            <div className="py-20 text-center text-[14px] text-muted-2">{t('warehouse.empty')}</div>
          ) : (
            <div className="grid grid-cols-4 gap-3">
              {productsQuery.data!.items.map((p) => {
                const out = p.quantity <= 0;
                return (
                  <button
                    key={p.id}
                    type="button"
                    disabled={out || addItem.isPending}
                    onClick={() =>
                      addItem.mutate({ productId: p.id, salePrice: p.salePrice, minSalePrice: p.minSalePrice })
                    }
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
                        {out ? t('seller.pos.outOfStock') : `${formatQty(p.quantity)} ${unitLabel(t, p.unit, p.unitName)}`}
                      </span>
                    </div>
                    <span className="line-clamp-2 text-[13px] font-medium leading-tight">{p.name}</span>
                    <span className="text-[14px] font-semibold text-primary nums">
                      {formatSum(p.salePrice)}
                      <span className="ml-1 text-[11px] font-normal text-muted-2">
                        {t('common.currency')}/{unitLabel(t, p.unit, p.unitName)}
                      </span>
                    </span>
                  </button>
                );
              })}
            </div>
          )}
        </div>
      </div>

      {/* ── RIGHT: the receipt ──────────────────────────────────── */}
      <div className="flex flex-col overflow-hidden border-l border-border bg-surface">
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
              onClick={() => {
                if (items.length === 0 || window.confirm(t('seller.pos.discardConfirm')))
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
                    onClick={() => {
                      if (window.confirm(t('seller.pos.discardConfirm'))) discardDraft.mutate(d.id);
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
        <div className="flex-1 overflow-y-auto px-5">
          {items.length === 0 ? (
            <div className="flex h-full items-center justify-center px-6 text-center text-[13.5px] text-muted-2">
              {t('pos.emptyCart')}
            </div>
          ) : (
            items.map((it) => (
              <div key={it.id} className="flex items-center gap-2 border-b border-hairline py-3 last:border-0">
                <div className="min-w-0 flex-1">
                  <div className="truncate text-[13px] font-medium">{it.productName}</div>
                  <div className="text-[11.5px] text-muted-2 nums">
                    {formatSum(it.salePrice)} {t('common.currency')}/{unitLabel(t, it.unitValue, it.unit)}
                  </div>
                </div>
                <div className="flex flex-none items-center gap-1">
                  <StepBtn onClick={() => removeOne.mutate(it.id)} disabled={removeOne.isPending}>
                    <Minus size={14} />
                  </StepBtn>
                  <span className="w-10 text-center text-[13px] font-semibold nums">{formatQty(it.quantity)}</span>
                  <StepBtn
                    disabled={!it.productId || addItem.isPending}
                    onClick={() =>
                      it.productId &&
                      addItem.mutate({ productId: it.productId, salePrice: it.salePrice, minSalePrice: 0 })
                    }
                  >
                    <Plus size={14} />
                  </StepBtn>
                </div>
                <span className="w-24 flex-none text-right text-[13px] font-semibold nums">
                  {formatSum(it.totalPrice)}
                </span>
              </div>
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
                onBlur={() => {
                  const next = Math.max(0, Number(discountInput) || 0);
                  // Faqat haqiqatan o'zgargan bo'lsa yuboramiz — har fokus
                  // yo'qotishda audit qatori yozilishining oldini oladi.
                  if (saleId && next !== (sale?.discountAmount ?? 0)) applyDiscount.mutate(next);
                }}
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
            <Button fullWidth disabled={items.length === 0} onClick={() => setCheckoutOpen(true)}>
              {t('pos.checkout')}
            </Button>
          </div>
        </div>
      </div>

      <CustomerPicker open={custOpen} onClose={() => setCustOpen(false)} onPick={pickCustomer} />

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
            void qc.invalidateQueries({ queryKey: ['pos-drafts'] });
            void qc.invalidateQueries({ queryKey: ['shift-current'] });
          }}
        />
      )}

      <ReceiptModal
        sale={done}
        shiftNumber={shiftQuery.data?.shiftNumber ?? 0}
        storeName={marketQuery.data?.marketName ?? null}
        onClose={() => setDone(null)}
        onPrint={printReceipt}
      />
    </div>
  );
}

// ── small pieces ───────────────────────────────────────────────────

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

function CustomerPicker({
  open,
  onClose,
  onPick,
}: {
  open: boolean;
  onClose: () => void;
  onPick: (c: PosCustomer) => void;
}) {
  const { t } = useTranslation();
  const [q, setQ] = useState('');
  const debounced = useDebounce(q);
  const query = useQuery({
    queryKey: ['pos-customers', debounced],
    queryFn: () => posApi.searchCustomers(debounced),
    enabled: open,
  });

  useEffect(() => {
    if (open) setQ('');
  }, [open]);

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
  const [cashPart, setCashPart] = useState('');
  const [paidNow, setPaidNow] = useState('');
  const [due, setDue] = useState('');
  const [error, setError] = useState<string | null>(null);
  const total = sale.totalAmount;

  useEffect(() => {
    if (open) {
      setReceived('');
      setCashPart('');
      setPaidNow('');
      setDue('');
      setError(null);
    }
  }, [open]);

  const got = Number(received) || 0;
  const change = got - total;
  const paid = Number(paidNow) || 0;
  const debtRest = Math.max(0, total - paid);
  // Микс: the cashier types the cash half, the card covers the remainder.
  const cashHalf = Number(cashPart) || 0;
  const cardHalf = Math.max(0, total - cashHalf);

  const canConfirm = useMemo(() => {
    if (total <= 0) return false;
    if (method === 'Debt') return !!customer && debtRest > 0;
    if (method === 'Cash') return got >= total;
    if (method === 'Mixed') return cashHalf > 0 && cardHalf > 0;
    return true;
  }, [method, total, got, customer, debtRest, cashHalf, cardHalf]);

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
        // Both halves in ONE request: the server applies them atomically, so the
        // sale never passes through a "partially paid ⇒ debt" state.
        await posApi.checkout(sale.id, [
          { paymentType: 'Cash', amount: cashHalf },
          { paymentType: 'Terminal', amount: cardHalf },
        ]);
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

        <div className="grid grid-cols-4 gap-2">
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

        {method === 'Mixed' && (
          <div className="flex flex-col gap-2">
            <label className="text-[13px] font-medium text-label">{t('seller.pos.cashPart')}</label>
            <input
              type="number"
              step="any"
              autoFocus
              placeholder="0"
              value={cashPart}
              onChange={(e) => setCashPart(e.target.value)}
              className={inputCls}
            />
            <div className="flex flex-wrap gap-2">
              {[30, 50, 70].map((pct) => (
                <ChipSm
                  key={pct}
                  label={`${pct}%`}
                  onClick={() => setCashPart(String(Math.round((total * pct) / 100)))}
                />
              ))}
            </div>
            <div className="flex items-center justify-between rounded-input bg-primary-soft px-4 py-2.5 text-[14px] text-primary-hover">
              <span>{t('seller.pos.cardPart')}</span>
              <span className="font-semibold nums">
                {formatSum(cardHalf)} {t('common.currency')}
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

function ReceiptModal({
  sale,
  shiftNumber,
  storeName,
  onClose,
  onPrint,
}: {
  sale: PosSale | null;
  shiftNumber: number;
  storeName: string | null;
  onClose: () => void;
  onPrint: (id: string) => void;
}) {
  const { t } = useTranslation();
  return (
    <Modal
      open={!!sale}
      onClose={onClose}
      title={t('seller.pos.doneTitle')}
      footer={
        <>
          <Button variant="secondary" onClick={() => sale && onPrint(sale.id)}>
            <Printer size={15} />
            {t('seller.pos.print')}
          </Button>
          <Button onClick={onClose}>{t('seller.pos.newSale')}</Button>
        </>
      }
    >
      {sale && (
        <div className="flex flex-col gap-3">
          <div className="flex items-center justify-center gap-2 text-success">
            <Check size={20} />
            <span className="text-[15px] font-semibold">
              {t('seller.pos.receipt')} №{sale.saleNumber}
            </span>
          </div>
          <Card className="p-4 font-mono text-[12.5px]">
            {storeName && (
              <div className="mb-2 border-b border-hairline pb-2 text-center text-[13px] font-semibold">{storeName}</div>
            )}
            <div className="mb-2 flex justify-between text-muted-2">
              <span>{formatTime(sale.createdAt)}</span>
              {shiftNumber > 0 && (
                <span className="nums">
                  {t('seller.pos.shift')} №{shiftNumber}
                </span>
              )}
              <span>{sale.sellerName}</span>
            </div>
            {sale.items.map((it) => (
              <div key={it.id} className="flex justify-between gap-2 py-0.5">
                <span className="min-w-0 truncate">
                  {it.productName} × {formatQty(it.quantity)}
                </span>
                <span className="flex-none nums">{formatSum(it.totalPrice)}</span>
              </div>
            ))}
            <div className="mt-2 flex justify-between border-t border-hairline pt-2 text-[14px] font-bold">
              <span>{t('pos.total')}</span>
              <span className="nums">{formatSum(sale.totalAmount)}</span>
            </div>
            <div className="flex justify-between pt-1 text-muted">
              <span>{t('pos.amount')}</span>
              <span className="nums">{formatSum(sale.paidAmount)}</span>
            </div>
            {sale.remainingAmount > 0 && (
              <div className="flex justify-between text-warn-text">
                <span>{t('pos.payment.debt')}</span>
                <span className="nums">{formatSum(sale.remainingAmount)}</span>
              </div>
            )}
            {sale.customerName && (
              <div className="mt-1 border-t border-hairline pt-1 text-muted">{sale.customerName}</div>
            )}
            <div className="mt-2 border-t border-hairline pt-2 text-center text-[11.5px] text-muted-2">
              {t('seller.pos.thanks')}
            </div>
          </Card>
        </div>
      )}
    </Modal>
  );
}
