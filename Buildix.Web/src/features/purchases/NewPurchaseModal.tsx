import { useMemo, useState } from 'react';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { useTranslation } from 'react-i18next';
import { Search, Plus, X, Package } from 'lucide-react';
import { Modal, Button, Spinner } from '@/shared/ui';
import { cn } from '@/shared/lib/cn';
import { formatSum, formatQty } from '@/shared/lib/format';
import { unitLabel } from '@/shared/lib/units';
import { useDebounce } from '@/shared/hooks/useDebounce';
import type { ApiError } from '@/shared/api/types';
import { productsApi, type Product } from '@/features/warehouse/api';
import { purchasesApi, type CreateReceiptLine } from './api';

/** A cart line being built — product snapshot plus the entered qty/cost. */
interface DraftLine extends CreateReceiptLine {
  name: string;
  unit: number;
  unitName: string;
}

export function NewPurchaseModal({ open, onClose }: { open: boolean; onClose: () => void }) {
  const { t } = useTranslation();
  const qc = useQueryClient();

  const [supplierId, setSupplierId] = useState<string>('');
  const [invoice, setInvoice] = useState('');
  const [comment, setComment] = useState('');
  const [paid, setPaid] = useState('');
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

  const total = useMemo(() => lines.reduce((s, l) => s + l.quantity * l.costPrice, 0), [lines]);

  function reset() {
    setSupplierId('');
    setInvoice('');
    setComment('');
    setPaid('');
    setLines([]);
    setSearch('');
    setError(null);
  }

  function addProduct(p: Product) {
    setLines((prev) => {
      const existing = prev.find((l) => l.productId === p.id);
      if (existing) {
        return prev.map((l) => (l.productId === p.id ? { ...l, quantity: l.quantity + 1 } : l));
      }
      // Kelish narxi standart sifatida mahsulotning oxirgi tannarxidan olinadi;
      // kassir uni har qatorda o'zgartira oladi.
      return [
        ...prev,
        { productId: p.id, name: p.name, unit: p.unit, unitName: p.unitName, quantity: 1, costPrice: p.costPrice },
      ];
    });
  }

  const setLine = (productId: string, patch: Partial<DraftLine>) =>
    setLines((prev) => prev.map((l) => (l.productId === productId ? { ...l, ...patch } : l)));
  const removeLine = (productId: string) => setLines((prev) => prev.filter((l) => l.productId !== productId));

  const create = useMutation({
    mutationFn: () =>
      purchasesApi.createReceipt({
        supplierId: supplierId || null,
        invoiceNumber: invoice.trim() || null,
        paidAmount: Math.min(Math.max(0, Number(paid) || 0), total),
        comment: comment.trim() || null,
        items: lines.map((l) => ({ productId: l.productId, quantity: l.quantity, costPrice: l.costPrice })),
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

  const canSubmit = lines.length > 0 && lines.every((l) => l.quantity > 0 && l.costPrice >= 0) && !create.isPending;

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
                <input
                  type="number"
                  step="any"
                  value={l.quantity}
                  onChange={(e) => setLine(l.productId, { quantity: Math.max(0, Number(e.target.value) || 0) })}
                  className={cn(inputCls, 'w-full text-right nums')}
                />
                <input
                  type="number"
                  step="any"
                  value={l.costPrice}
                  onChange={(e) => setLine(l.productId, { costPrice: Math.max(0, Number(e.target.value) || 0) })}
                  className={cn(inputCls, 'w-full text-right nums')}
                />
                <span className="text-right text-[13px] font-semibold nums">{formatSum(l.quantity * l.costPrice)}</span>
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
          <div className="flex items-center justify-between gap-3">
            <label className="text-[13px] text-muted">{t('purchases.newModal.paidNow')}</label>
            <div className="flex items-center gap-2">
              <input
                type="number"
                step="any"
                placeholder="0"
                value={paid}
                onChange={(e) => setPaid(e.target.value)}
                className={cn(inputCls, 'w-[140px] text-right nums')}
              />
              <button
                type="button"
                onClick={() => setPaid(String(total))}
                className="text-[12px] font-medium text-primary hover:text-primary-hover"
              >
                {t('purchases.newModal.payFull')}
              </button>
            </div>
          </div>
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
