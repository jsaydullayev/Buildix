import { useEffect, useState } from 'react';
import { useNavigate, useParams } from 'react-router-dom';
import { useQuery, useMutation, useQueryClient, keepPreviousData } from '@tanstack/react-query';
import { useTranslation } from 'react-i18next';
import { ArrowLeft, Search, Plus, Minus, X, Package, Check, UserPlus, Clock, AlertTriangle } from 'lucide-react';
import { Button, Card, Spinner, Badge } from '@/shared/ui';
import { cn } from '@/shared/lib/cn';
import { formatSum, formatQty } from '@/shared/lib/format';
import { useDebounce } from '@/shared/hooks/useDebounce';
import type { ApiError } from '@/shared/api/types';
import { posApi, type PosCustomer } from './api';

const METHODS = [
  { key: 'cash', value: 'Cash' },
  { key: 'card', value: 'Terminal' },
  { key: 'transfer', value: 'Transfer' },
  { key: 'click', value: 'Click' },
  { key: 'debt', value: 'Debt' },
] as const;

export default function PosPage() {
  const { subdomain } = useParams();
  const navigate = useNavigate();
  const { t } = useTranslation();
  const qc = useQueryClient();

  const [saleId, setSaleId] = useState<string | null>(null);
  const [search, setSearch] = useState('');
  const debouncedSearch = useDebounce(search);
  const [method, setMethod] = useState<string>('Cash');
  const [customer, setCustomer] = useState<PosCustomer | null>(null);
  const [custOpen, setCustOpen] = useState(false);
  const [discountInput, setDiscountInput] = useState('0');
  const [success, setSuccess] = useState(false);
  const [startError, setStartError] = useState<string | null>(null);
  const [actionError, setActionError] = useState<string | null>(null);
  const [startKey, setStartKey] = useState(0); // bump to retry draft creation

  // Create a fresh draft sale on mount (retried when startKey changes).
  useEffect(() => {
    let active = true;
    setStartError(null);
    posApi
      .createDraft()
      .then((s) => {
        if (active) setSaleId(s.id);
      })
      .catch((e) => {
        if (active) setStartError((e as ApiError).message ?? '');
      });
    return () => {
      active = false;
    };
  }, [startKey]);

  const saleQuery = useQuery({
    queryKey: ['pos-sale', saleId],
    queryFn: () => posApi.getSale(saleId!),
    enabled: !!saleId,
  });
  const productsQuery = useQuery({
    queryKey: ['pos-products', debouncedSearch],
    queryFn: () => posApi.searchProducts({ page: 1, size: 30, search: debouncedSearch }),
    placeholderData: keepPreviousData,
  });

  const sale = saleQuery.data;
  const refresh = () => qc.invalidateQueries({ queryKey: ['pos-sale', saleId] });
  // M-5: after any cart mutation, also refresh the product grid so displayed
  // stock (and the out-of-stock disable) reflects the sold quantities.
  const refreshAll = () => {
    void qc.invalidateQueries({ queryKey: ['pos-sale', saleId] });
    void qc.invalidateQueries({ queryKey: ['pos-products'] });
  };

  // M-4: add a line by productId + price only — works from the product grid AND
  // from the cart "+" (which no longer depends on the current search results).
  const addItem = useMutation({
    mutationFn: (p: { productId: string; salePrice: number; minSalePrice: number }) =>
      posApi.addItem(saleId!, {
        isExternal: false,
        productId: p.productId,
        quantity: 1,
        salePrice: p.salePrice,
        minSalePrice: p.minSalePrice,
      }),
    onSuccess: () => {
      setActionError(null);
      refreshAll();
    },
    onError: (e) => setActionError((e as unknown as ApiError).message ?? ''),
  });
  const removeOne = useMutation({
    mutationFn: (itemId: string) => posApi.removeItem(saleId!, itemId, 1),
    onSuccess: refreshAll,
  });
  const applyDiscount = useMutation({
    mutationFn: (amount: number) => posApi.setDiscount(saleId!, amount),
    onSuccess: refresh,
  });
  const attachCustomer = useMutation({
    mutationFn: (c: PosCustomer | null) => posApi.attachCustomer(saleId!, c?.id ?? null),
    onSuccess: refresh,
  });

  const checkout = useMutation({
    mutationFn: async () => {
      if (!sale) return;
      if (method === 'Debt') {
        await posApi.markDebt(saleId!);
      } else {
        await posApi.addPayment(saleId!, { paymentType: method, amount: sale.totalAmount });
      }
    },
    onSuccess: async () => {
      setSuccess(true);
      setActionError(null);
      void qc.invalidateQueries({ queryKey: ['pos-products'] });
      // Start a fresh sale.
      const fresh = await posApi.createDraft();
      setSaleId(fresh.id);
      setCustomer(null);
      setDiscountInput('0');
      setMethod('Cash');
      setTimeout(() => setSuccess(false), 2500);
    },
    onError: (e) => setActionError((e as unknown as ApiError).message ?? ''),
  });

  const items = sale?.items ?? [];
  const total = sale?.totalAmount ?? 0;
  const canCheckout = items.length > 0 && !checkout.isPending;

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
            <Button onClick={() => setStartKey((k) => k + 1)}>{t('common.retry')}</Button>
          </div>
        </div>
      ) : (
      <div className="grid flex-1 grid-cols-[1.5fr_1fr] gap-0 overflow-hidden">
        {/* LEFT — product search */}
        <div className="flex flex-col border-r border-border p-6">
          <div className="relative mb-4">
            <Search size={18} className="absolute left-4 top-1/2 -translate-y-1/2 text-muted-2" />
            <input
              value={search}
              onChange={(e) => setSearch(e.target.value)}
              placeholder={t('pos.searchPlaceholder')}
              autoFocus
              className="h-12 w-full rounded-input border border-input-border bg-surface pl-12 pr-4 text-[15px] outline-none focus:border-primary focus:shadow-focus-ring"
            />
          </div>
          <div className="flex-1 overflow-y-auto">
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
                    disabled={!saleId || p.quantity <= 0}
                    onClick={() => addItem.mutate({ productId: p.id, salePrice: p.salePrice, minSalePrice: p.minSalePrice })}
                    className="flex flex-col rounded-card border border-border bg-surface p-3 text-left transition-colors hover:border-primary disabled:opacity-50"
                  >
                    <div className="mb-2 flex h-8 w-8 items-center justify-center rounded-lg bg-hairline text-muted-2">
                      <Package size={15} />
                    </div>
                    <div className="line-clamp-2 text-[13px] font-medium leading-tight">{p.name}</div>
                    <div className="mt-1 text-[11.5px] text-muted-2 nums">
                      {formatQty(p.quantity)} {p.unitName}
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
                    attachCustomer.mutate(null);
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
          <div className="flex-1 overflow-y-auto px-5">
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
                    <div className="text-[12px] text-muted-2 nums">
                      {formatSum(it.salePrice)} · {formatQty(it.quantity)} {it.unit}
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
                    <span className="w-7 text-center text-[13px] font-semibold nums">{formatQty(it.quantity)}</span>
                    <button
                      type="button"
                      disabled={!it.productId}
                      onClick={() =>
                        it.productId &&
                        addItem.mutate({ productId: it.productId, salePrice: it.salePrice, minSalePrice: it.salePrice })
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
                  value={discountInput}
                  onChange={(e) => setDiscountInput(e.target.value)}
                  onBlur={() => applyDiscount.mutate(Math.max(0, Number(discountInput) || 0))}
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

            <div className="mb-3 grid grid-cols-5 gap-1.5">
              {METHODS.map((m) => (
                <button
                  key={m.value}
                  type="button"
                  onClick={() => setMethod(m.value)}
                  className={cn(
                    'h-10 rounded-input border text-[12px] font-medium transition-colors',
                    method === m.value
                      ? m.key === 'debt'
                        ? 'border-warn bg-warn-soft text-warn-text'
                        : 'border-primary bg-primary-soft text-primary-hover'
                      : 'border-input-border bg-surface text-muted hover:text-text',
                  )}
                >
                  {t(`pos.payment.${m.key}`)}
                </button>
              ))}
            </div>

            {actionError && (
              <div className="mb-2 flex items-center gap-2 rounded-input bg-danger-soft px-3 py-2 text-[12.5px] text-danger">
                <AlertTriangle size={14} className="flex-none" />
                <span>{actionError}</span>
              </div>
            )}
            {method === 'Debt' && !customer && (
              <div className="mb-2 text-[12px] text-warn-text">{t('pos.customer.label')}: {t('pos.customer.walkIn')}</div>
            )}

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
      )}

      {custOpen && (
        <CustomerPicker
          onClose={() => setCustOpen(false)}
          onPick={(c) => {
            setCustomer(c);
            attachCustomer.mutate(c);
            setCustOpen(false);
          }}
        />
      )}
    </div>
  );
}

function CustomerPicker({ onClose, onPick }: { onClose: () => void; onPick: (c: PosCustomer) => void }) {
  const { t } = useTranslation();
  const [q, setQ] = useState('');
  const debounced = useDebounce(q);
  const query = useQuery({
    queryKey: ['pos-customers', debounced],
    queryFn: () => posApi.searchCustomers(debounced),
    enabled: debounced.length >= 2,
  });

  return (
    <div
      className="fixed inset-0 z-50 flex items-start justify-center bg-text/40 px-4 py-16"
      onMouseDown={(e) => e.target === e.currentTarget && onClose()}
    >
      <Card className="w-full max-w-md animate-fade-in p-5">
        <div className="mb-3 flex items-center justify-between">
          <h2 className="text-[16px] font-semibold">{t('pos.customer.label')}</h2>
          <button type="button" onClick={onClose} className="text-muted-2 hover:text-text">
            <X size={18} />
          </button>
        </div>
        <input
          value={q}
          onChange={(e) => setQ(e.target.value)}
          autoFocus
          placeholder={t('pos.customer.searchPlaceholder')}
          className="mb-3 h-11 w-full rounded-input border border-input-border bg-surface px-3.5 text-[14px] outline-none focus:border-primary focus:shadow-focus-ring"
        />
        <div className="max-h-[320px] overflow-y-auto">
          {query.isLoading ? (
            <div className="flex justify-center py-8 text-primary">
              <Spinner size={20} />
            </div>
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
      </Card>
    </div>
  );
}
