import { useMemo, useState } from 'react';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { useTranslation } from 'react-i18next';
import { Search, Plus, X, Package } from 'lucide-react';
import { Modal, Button, Spinner } from '@/shared/ui';
import { cn } from '@/shared/lib/cn';
import { formatSum, formatQty, formatMoneyInput, parseMoneyInput } from '@/shared/lib/format';
import { unitLabel } from '@/shared/lib/units';
import { useDebounce } from '@/shared/hooks/useDebounce';
import type { ApiError } from '@/shared/api/types';
import { productsApi, type Product } from '@/features/warehouse/api';
import { purchasesApi, type CreateReceiptLine } from './api';

/**
 * A cart line being built — product snapshot plus the entered qty/cost.
 *
 * Miqdor va narx SATR sifatida saqlanadi, raqam emas: raqamli state bo'sh
 * maydonni majburan «0» ga aylantirib, foydalanuvchini avval 0 ni o'chirishga
 * majbur qilardi («0500» muammosi). Raqamlar faqat hisob-kitob va yuborish
 * paytida olinadi.
 */
interface DraftLine {
  productId: string;
  name: string;
  unit: number;
  unitName: string;
  /** O'nlik miqdor matni (kg uchun kasr mumkin), masalan "5" yoki "2.5". */
  qtyText: string;
  /** Kelish narxi — faqat raqamlar; ko'rinishda «1 000» deb guruhlanadi. */
  costText: string;
}

const lineQty = (l: DraftLine): number => Number(l.qtyText.replace(',', '.')) || 0;
const lineCost = (l: DraftLine): number => Number(l.costText) || 0;

/** Miqdor maydoni: raqamlar + bitta o'nlik ajratgich, boshqa hech narsa. */
function sanitizeQty(raw: string): string {
  const cleaned = raw.replace(/[^\d.,]/g, '').replace(/,/g, '.');
  const firstDot = cleaned.indexOf('.');
  if (firstDot === -1) return cleaned;
  return cleaned.slice(0, firstDot + 1) + cleaned.slice(firstDot + 1).replace(/\./g, '');
}

export function NewPurchaseModal({ open, onClose }: { open: boolean; onClose: () => void }) {
  const { t } = useTranslation();
  const qc = useQueryClient();

  const [supplierId, setSupplierId] = useState<string>('');
  const [invoice, setInvoice] = useState('');
  const [comment, setComment] = useState('');
  const [paid, setPaid] = useState('');
  const [payMethod, setPayMethod] = useState<'Cash' | 'Transfer'>('Transfer');
  const [inTransit, setInTransit] = useState(false);
  const [expectedDate, setExpectedDate] = useState('');
  const [lines, setLines] = useState<DraftLine[]>([]);
  const [search, setSearch] = useState('');
  const [error, setError] = useState<string | null>(null);
  const debouncedSearch = useDebounce(search);

  const suppliersQuery = useQuery({
    queryKey: ['suppliers-all'],
    queryFn: purchasesApi.suppliers,
    enabled: open,
  });
  const productsQuery = useQuery({
    queryKey: ['purchase-products', debouncedSearch],
    queryFn: () => productsApi.listPaged({ page: 1, size: 20, search: debouncedSearch }),
    enabled: open,
  });
  // Quick-add: low-stock products one click away (design Card4). Not yet in the cart.
  const reorderQuery = useQuery({
    queryKey: ['reorder'],
    queryFn: () => purchasesApi.reorderSuggestions(8),
    enabled: open,
  });
  const quickAdd = (reorderQuery.data ?? []).filter((r) => !lines.some((l) => l.productId === r.productId));

  const total = useMemo(() => lines.reduce((s, l) => s + lineQty(l) * lineCost(l), 0), [lines]);

  function reset() {
    setSupplierId('');
    setInvoice('');
    setComment('');
    setPaid('');
    setPayMethod('Transfer');
    setInTransit(false);
    setExpectedDate('');
    setLines([]);
    setSearch('');
    setError(null);
  }

  function addProduct(p: Product) {
    setLines((prev) => {
      const existing = prev.find((l) => l.productId === p.id);
      if (existing) {
        return prev.map((l) =>
          l.productId === p.id ? { ...l, qtyText: String(lineQty(l) + 1) } : l,
        );
      }
      // Kelish narxi standart sifatida mahsulotning oxirgi tannarxidan olinadi;
      // kassir uni har qatorda o'zgartira oladi. 0 tannarx = bo'sh maydon.
      return [
        ...prev,
        {
          productId: p.id,
          name: p.name,
          unit: p.unit,
          unitName: p.unitName,
          qtyText: '1',
          costText: p.costPrice > 0 ? String(Math.round(p.costPrice)) : '',
        },
      ];
    });
  }

  const setLine = (productId: string, patch: Partial<DraftLine>) =>
    setLines((prev) => prev.map((l) => (l.productId === productId ? { ...l, ...patch } : l)));
  const removeLine = (productId: string) => setLines((prev) => prev.filter((l) => l.productId !== productId));

  // Quick-add a low-stock product: seed the suggested qty; cost is entered per line.
  function addSuggestion(s: { productId: string; name: string; unit: number; unitName: string; suggestedQty: number }) {
    setLines((prev) =>
      prev.some((l) => l.productId === s.productId)
        ? prev
        : [
            ...prev,
            {
              productId: s.productId,
              name: s.name,
              unit: s.unit,
              unitName: s.unitName,
              qtyText: String(Math.max(1, Math.round(s.suggestedQty)) || 1),
              costText: '',
            },
          ],
    );
  }

  const create = useMutation({
    mutationFn: () =>
      purchasesApi.createReceipt({
        supplierId: supplierId || null,
        invoiceNumber: invoice.trim() || null,
        paidAmount: Math.min(Math.max(0, Number(paid) || 0), total),
        comment: comment.trim() || null,
        items: lines.map(
          (l): CreateReceiptLine => ({ productId: l.productId, quantity: lineQty(l), costPrice: lineCost(l) }),
        ),
        inTransit,
        expectedDate: inTransit && expectedDate ? new Date(expectedDate).toISOString() : null,
        paymentMethod: Number(paid) > 0 ? payMethod : null,
      }),
    onSuccess: () => {
      // Xarid ombor qoldig'i, tannarx va yetkazib beruvchi qarzini o'zgartiradi —
      // shu bog'liq ro'yxatlarning hammasini yangilaymiz.
      void qc.invalidateQueries({ queryKey: ['receipts'] });
      void qc.invalidateQueries({ queryKey: ['receipts-all'] });
      void qc.invalidateQueries({ queryKey: ['suppliers'] });
      void qc.invalidateQueries({ queryKey: ['products'] });
      void qc.invalidateQueries({ queryKey: ['products-all'] });
      void qc.invalidateQueries({ queryKey: ['reorder'] });
      reset();
      onClose();
    },
    onError: (e) => setError((e as unknown as ApiError).message ?? t('common.somethingWrong')),
  });

  const canSubmit = lines.length > 0 && lines.every((l) => lineQty(l) > 0) && !create.isPending;

  const inputCls =
    'h-10 rounded-input border border-input-border bg-surface px-3 text-[14px] outline-none focus:border-primary focus:shadow-focus-ring';

  return (
    <Modal
      open={open}
      onClose={onClose}
      width="xl"
      title={t('purchases.newModal.title')}
      subtitle={t('purchases.newModal.subtitle')}
      footer={
        <>
          <Button variant="secondary" onClick={onClose}>
            {t('common.cancel')}
          </Button>
          <Button disabled={!canSubmit} loading={create.isPending} onClick={() => create.mutate()}>
            {t('purchases.newModal.submit')}
          </Button>
        </>
      }
    >
      <div className="flex flex-col gap-5">
        {/* Supplier + invoice */}
        <div className="grid grid-cols-2 gap-3">
          <div className="flex flex-col gap-1.5">
            <label className="text-[13px] font-medium text-label">{t('purchases.cols.supplier')}</label>
            <select value={supplierId} onChange={(e) => setSupplierId(e.target.value)} className={inputCls}>
              <option value="">{t('purchases.noSupplier')}</option>
              {(suppliersQuery.data ?? []).map((s) => (
                <option key={s.id} value={s.id}>
                  {s.name}
                </option>
              ))}
            </select>
          </div>
          <div className="flex flex-col gap-1.5">
            <label className="text-[13px] font-medium text-label">{t('purchases.newModal.invoice')}</label>
            <input value={invoice} onChange={(e) => setInvoice(e.target.value)} className={inputCls} />
          </div>
        </div>

        {/* Quick-add: low-stock products (design Card4) */}
        {quickAdd.length > 0 && (
          <div className="flex flex-col gap-2">
            <label className="text-[13px] font-medium text-label">{t('purchases.newModal.quickAdd')}</label>
            <div className="flex flex-wrap gap-1.5">
              {quickAdd.map((s) => (
                <button
                  key={s.productId}
                  type="button"
                  onClick={() => addSuggestion(s)}
                  className="flex items-center gap-1.5 rounded-pill border border-warn/30 bg-warn-soft/60 px-3 py-1.5 text-[12.5px] font-medium text-warn-strong transition-colors hover:border-warn"
                >
                  <Plus size={13} />
                  {s.name}
                </button>
              ))}
            </div>
          </div>
        )}

        {/* Product search */}
        <div className="flex flex-col gap-2">
          <label className="text-[13px] font-medium text-label">{t('purchases.newModal.addItems')}</label>
          <div className="relative">
            <Search size={17} className="absolute left-3.5 top-1/2 -translate-y-1/2 text-muted-2" />
            <input
              value={search}
              onChange={(e) => setSearch(e.target.value)}
              placeholder={t('warehouse.searchPlaceholder')}
              className={cn(inputCls, 'w-full pl-11')}
            />
          </div>
          {search.trim().length > 0 && (
            <div className="max-h-[180px] overflow-y-auto rounded-input border border-hairline">
              {productsQuery.isLoading ? (
                <div className="flex justify-center py-6 text-primary">
                  <Spinner size={18} />
                </div>
              ) : (productsQuery.data?.items ?? []).length === 0 ? (
                <p className="py-6 text-center text-[13px] text-muted-2">{t('warehouse.empty')}</p>
              ) : (
                (productsQuery.data?.items ?? []).map((p) => (
                  <button
                    key={p.id}
                    type="button"
                    onClick={() => addProduct(p)}
                    className="flex w-full items-center justify-between gap-3 border-b border-hairline px-3 py-2 text-left last:border-0 hover:bg-bg/50"
                  >
                    <span className="flex min-w-0 items-center gap-2">
                      <Package size={14} className="flex-none text-muted-2" />
                      <span className="truncate text-[13px] font-medium">{p.name}</span>
                    </span>
                    <span className="flex-none text-[12px] text-muted-2 nums">
                      {formatQty(p.quantity)} {unitLabel(t, p.unit, p.unitName)}
                    </span>
                  </button>
                ))
              )}
            </div>
          )}
        </div>

        {/* Cart lines */}
        {lines.length === 0 ? (
          <div className="flex flex-col items-center gap-2 rounded-input border border-dashed border-input-border py-8 text-center text-muted-2">
            <Plus size={20} />
            <p className="text-[13px]">{t('purchases.newModal.empty')}</p>
          </div>
        ) : (
          <div className="flex flex-col">
            <div className="grid grid-cols-[1fr_92px_120px_120px_32px] items-center gap-2 border-b border-hairline pb-1.5 text-[11px] font-semibold tracking-[0.3px] text-muted-2">
              <span>{t('purchases.cols.items')}</span>
              <span className="text-right">{t('sales.detail.qty')}</span>
              <span className="text-right">{t('warehouse.form.costPrice')}</span>
              <span className="text-right">{t('sales.cols.sum')}</span>
              <span />
            </div>
            {lines.map((l) => (
              <div
                key={l.productId}
                className="grid grid-cols-[1fr_92px_120px_120px_32px] items-center gap-2 border-b border-hairline py-2 last:border-0"
              >
                <span className="truncate text-[13px] font-medium">{l.name}</span>
                {/* Fokusda matn belgilanadi — yozish eski qiymatni almashtiradi,
                    hech narsani o'chirish shart emas. */}
                <input
                  type="text"
                  inputMode="decimal"
                  placeholder="0"
                  value={l.qtyText}
                  onFocus={(e) => e.currentTarget.select()}
                  onChange={(e) => setLine(l.productId, { qtyText: sanitizeQty(e.target.value) })}
                  className={cn(inputCls, 'w-full text-right nums')}
                />
                <input
                  type="text"
                  inputMode="numeric"
                  placeholder="0"
                  value={formatMoneyInput(l.costText)}
                  onFocus={(e) => e.currentTarget.select()}
                  onChange={(e) => setLine(l.productId, { costText: parseMoneyInput(e.target.value) })}
                  className={cn(inputCls, 'w-full text-right nums')}
                />
                <span className="text-right text-[13px] font-semibold nums">{formatSum(lineQty(l) * lineCost(l))}</span>
                <button
                  type="button"
                  onClick={() => removeLine(l.productId)}
                  className="flex h-7 w-7 items-center justify-center rounded-md text-muted-2 hover:text-danger"
                >
                  <X size={15} />
                </button>
              </div>
            ))}
          </div>
        )}

        {/* Totals + payment */}
        <div className="flex flex-col gap-2.5 rounded-input bg-bg px-4 py-3">
          <div className="flex items-baseline justify-between">
            <span className="text-[14px] font-semibold">{t('pos.total')}</span>
            <span className="text-[18px] font-bold nums">
              {formatSum(total)} <span className="text-[12px] font-normal text-muted-2">{t('common.currency')}</span>
            </span>
          </div>
          {/* Delivery mode — «В пути» defers stock until the goods are accepted. */}
          <label className="flex cursor-pointer select-none items-center justify-between gap-3 rounded-input border border-input-border bg-surface px-3.5 py-2.5">
            <span className="min-w-0">
              <span className="block text-[13px] font-medium">{t('purchases.newModal.inTransit')}</span>
              <span className="block text-[11.5px] text-muted-2">{t('purchases.newModal.inTransitHint')}</span>
            </span>
            <input
              type="checkbox"
              checked={inTransit}
              onChange={(e) => setInTransit(e.target.checked)}
              className="h-4 w-4 flex-none accent-primary"
            />
          </label>

          {/* «В пути» ETA — arrival date, only relevant for a deferred delivery. */}
          {inTransit && (
            <div className="flex items-center justify-between gap-3">
              <label className="text-[13px] text-muted">{t('purchases.newModal.expectedDate')}</label>
              <input
                type="date"
                value={expectedDate}
                onChange={(e) => setExpectedDate(e.target.value)}
                className={cn(inputCls, 'w-[170px] nums')}
              />
            </div>
          )}

          <div className="flex items-center justify-between gap-3">
            <label className="text-[13px] text-muted">{t('purchases.newModal.paidNow')}</label>
            <div className="flex items-center gap-2">
              <input
                type="text"
                inputMode="numeric"
                placeholder="0"
                value={formatMoneyInput(paid)}
                onChange={(e) => setPaid(parseMoneyInput(e.target.value))}
                className={cn(inputCls, 'w-[140px] text-right nums')}
              />
              <button
                type="button"
                onClick={() => setPaid(String(Math.round(total)))}
                className="text-[12px] font-medium text-primary hover:text-primary-hover"
              >
                {t('purchases.newModal.payFull')}
              </button>
            </div>
          </div>
          {/* Способ оплаты — how the paid part is tendered (metadata). */}
          {Number(paid) > 0 && (
            <div className="flex items-center justify-between gap-3">
              <label className="text-[13px] text-muted">{t('purchases.newModal.payMethod')}</label>
              <div className="inline-flex rounded-input bg-hairline p-1">
                {(['Cash', 'Transfer'] as const).map((m) => (
                  <button
                    key={m}
                    type="button"
                    onClick={() => setPayMethod(m)}
                    className={cn(
                      'rounded-md px-3.5 py-1 text-[12.5px] font-medium transition-colors',
                      payMethod === m ? 'bg-surface text-text shadow-card' : 'text-muted hover:text-text',
                    )}
                  >
                    {t(`purchases.newModal.payMethods.${m === 'Cash' ? 'cash' : 'transfer'}`)}
                  </button>
                ))}
              </div>
            </div>
          )}

          <div className="flex items-center justify-between text-[13px]">
            <span className="text-muted">{t('purchases.newModal.debtRest')}</span>
            <span className="font-semibold text-warn-text nums">
              {formatSum(Math.max(0, total - (Number(paid) || 0)))} {t('common.currency')}
            </span>
          </div>
        </div>

        <input
          value={comment}
          onChange={(e) => setComment(e.target.value)}
          placeholder={t('purchases.newModal.comment')}
          className={cn(inputCls, 'w-full')}
        />

        {error && (
          <div className="rounded-input bg-danger-soft px-3 py-2 text-[12.5px] text-danger">{error}</div>
        )}
      </div>
    </Modal>
  );
}
