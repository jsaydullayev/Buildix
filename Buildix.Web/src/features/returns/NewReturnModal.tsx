import { useEffect, useMemo, useState } from 'react';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { useTranslation } from 'react-i18next';
import { Search } from 'lucide-react';
import { Modal, Button, Spinner } from '@/shared/ui';
import { cn } from '@/shared/lib/cn';
import { formatSum } from '@/shared/lib/format';
import type { ApiError } from '@/shared/api/types';
import { salesApi, type Sale } from '@/features/sales/api';
import { invalidateSaleLists } from '@/features/sales/invalidate';
import { returnsApi } from './api';

const REASONS = ['Defect', 'NotFit', 'SellerError', 'Other'] as const;
const REASON_KEY: Record<string, string> = { Defect: 'defect', NotFit: 'notFit', SellerError: 'sellerError', Other: 'other' };
const REFUNDS = ['Cash', 'Terminal'] as const;
const REFUND_KEY: Record<string, string> = { Cash: 'cash', Terminal: 'card' };

/**
 * «Оформить возврат» — find a receipt by number, pick the lines and quantities to
 * return, choose a reason and how to refund. The server restores stock, refunds
 * the overpaid amount and files a return document (В-##).
 */
export function NewReturnModal({ open, onClose, presetSearch }: { open: boolean; onClose: () => void; presetSearch?: string }) {
  const { t } = useTranslation();
  const qc = useQueryClient();

  const [receiptSearch, setReceiptSearch] = useState('');
  const [submitted, setSubmitted] = useState('');
  const [saleId, setSaleId] = useState<string | null>(null);

  // Deep-link from a sale ("Оформить возврат" on the receipt): prefill + run the
  // receipt lookup so the user lands straight on the line picker.
  useEffect(() => {
    if (open && presetSearch) {
      setReceiptSearch(presetSearch);
      setSubmitted(presetSearch);
    }
  }, [open, presetSearch]);
  const [qtys, setQtys] = useState<Record<string, string>>({});
  const [reason, setReason] = useState<string>('Defect');
  const [refund, setRefund] = useState<string>('Cash');
  const [error, setError] = useState<string | null>(null);

  // Look up the receipt by number once the user submits the search.
  const findQuery = useQuery({
    queryKey: ['return-find', submitted],
    queryFn: () => salesApi.listPaged({ page: 1, size: 5, search: submitted }),
    enabled: open && submitted.trim().length > 0,
  });
  const saleQuery = useQuery({
    queryKey: ['return-sale', saleId],
    queryFn: () => salesApi.byId(saleId!),
    enabled: open && !!saleId,
  });
  const sale = saleQuery.data;

  // When a deep-link (or an exact number) matches a single receipt, skip the
  // pick-a-match step and land straight on the line picker.
  const matches = findQuery.data?.items;
  useEffect(() => {
    if (open && submitted && !saleId && matches?.length === 1) setSaleId(matches[0]!.id);
  }, [open, submitted, saleId, matches]);

  const total = useMemo(() => {
    if (!sale) return 0;
    return sale.items.reduce((s, it) => s + (Number(qtys[it.id]) || 0) * it.salePrice, 0);
  }, [sale, qtys]);

  const close = () => {
    setReceiptSearch('');
    setSubmitted('');
    setSaleId(null);
    setQtys({});
    setReason('Defect');
    setRefund('Cash');
    setError(null);
    onClose();
  };

  const create = useMutation({
    mutationFn: () => {
      const items = sale!.items
        .filter((it) => (Number(qtys[it.id]) || 0) > 0)
        .map((it) => ({ saleItemId: it.id, quantity: Math.min(Number(qtys[it.id]) || 0, it.quantity) }));
      return returnsApi.create({ saleId: sale!.id, reason, refundMethod: refund, items });
    },
    onSuccess: () => {
      // Qaytarish chekka manfiy to'lov yozadi va `PaidAmount` ni siljitadi —
      // ya'ni chek kartochkasi, smena va qarz ekranlariga ham tegadi.
      // Ilgari bu yerda faqat to'rtta kalit yangilanardi, ustiga sotuvchi
      // qobig'ining `seller-returns` kaliti `returns` prefiksiga MOS
      // KELMAS edi: sotuvchi o'z qaytarishini ro'yxatida ko'rmasdi.
      invalidateSaleLists(qc);
      void qc.invalidateQueries({ queryKey: ['products'] });
      close();
    },
    onError: (e) => setError((e as unknown as ApiError).message ?? t('common.somethingWrong')),
  });

  const canSubmit = !!sale && total > 0 && !create.isPending;
  const inputCls = 'h-10 rounded-input border border-input-border bg-surface px-3 text-[14px] outline-none focus:border-primary';

  return (
    <Modal
      open={open}
      onClose={close}
      width="lg"
      title={t('returns.create')}
      footer={
        <>
          <Button variant="secondary" onClick={close}>
            {t('common.cancel')}
          </Button>
          <Button disabled={!canSubmit} loading={create.isPending} onClick={() => create.mutate()}>
            {t('returns.refundMoney')}{total > 0 ? ` · ${formatSum(total)}` : ''}
          </Button>
        </>
      }
    >
      <div className="flex flex-col gap-4">
        {/* Receipt lookup */}
        <div>
          <label className="mb-1.5 block text-[13px] font-medium text-label">{t('returns.findReceipt')}</label>
          <div className="flex gap-2">
            <div className="relative flex-1">
              <Search size={16} className="absolute left-3 top-1/2 -translate-y-1/2 text-muted-2" />
              <input
                value={receiptSearch}
                onChange={(e) => setReceiptSearch(e.target.value)}
                onKeyDown={(e) => e.key === 'Enter' && setSubmitted(receiptSearch)}
                placeholder={t('returns.findPlaceholder')}
                className={cn(inputCls, 'w-full pl-9')}
              />
            </div>
            <Button variant="secondary" onClick={() => setSubmitted(receiptSearch)}>
              {t('common.search')}
            </Button>
          </div>

          {/* Matches */}
          {submitted && !saleId && (
            <div className="mt-2 rounded-input border border-hairline">
              {findQuery.isLoading ? (
                <div className="flex justify-center py-4 text-primary"><Spinner size={16} /></div>
              ) : (findQuery.data?.items ?? []).length === 0 ? (
                <p className="py-4 text-center text-[13px] text-muted-2">{t('sales.empty')}</p>
              ) : (
                (findQuery.data?.items ?? []).map((s: Sale) => (
                  <button
                    key={s.id}
                    type="button"
                    onClick={() => setSaleId(s.id)}
                    className="flex w-full items-center justify-between border-b border-hairline px-3 py-2 text-left last:border-0 hover:bg-bg/50"
                  >
                    <span className="text-[13px] font-medium text-primary nums">Ч-{s.saleNumber}</span>
                    <span className="text-[12px] text-muted-2 nums">{formatSum(s.totalAmount)}</span>
                  </button>
                ))
              )}
            </div>
          )}
        </div>

        {/* Selected sale lines */}
        {sale && (
          <>
            <div className="rounded-input border border-hairline">
              <div className="flex items-center justify-between border-b border-hairline bg-bg/40 px-3 py-2 text-[12px] font-semibold text-muted-2">
                <span>Ч-{sale.saleNumber}</span>
                <button type="button" onClick={() => { setSaleId(null); setQtys({}); }} className="text-primary hover:text-primary-hover">
                  {t('returns.changeReceipt')}
                </button>
              </div>
              {sale.items.map((it) => (
                <div key={it.id} className="flex items-center gap-3 border-b border-hairline px-3 py-2.5 last:border-0">
                  <div className="min-w-0 flex-1">
                    <div className="truncate text-[13px] font-medium">{it.productName}</div>
                    <div className="text-[11.5px] text-muted-2 nums">
                      {formatSum(it.salePrice)} · {t('returns.sold', { qty: it.quantity })}
                    </div>
                  </div>
                  <input
                    type="number"
                    step="any"
                    min="0"
                    max={it.quantity}
                    placeholder="0"
                    value={qtys[it.id] ?? ''}
                    onChange={(e) => {
                      const v = Math.min(Math.max(0, Number(e.target.value) || 0), it.quantity);
                      setQtys((prev) => ({ ...prev, [it.id]: v ? String(v) : '' }));
                    }}
                    className={cn(inputCls, 'w-[90px] text-right nums')}
                  />
                </div>
              ))}
            </div>

            {/* Reason */}
            <div>
              <label className="mb-1.5 block text-[13px] font-medium text-label">{t('returns.cols.reason')}</label>
              <div className="grid grid-cols-2 gap-2">
                {REASONS.map((r) => (
                  <button
                    key={r}
                    type="button"
                    onClick={() => setReason(r)}
                    className={cn(
                      'h-10 rounded-input border text-[13px] transition-colors',
                      reason === r ? 'border-primary bg-primary-soft text-primary-hover' : 'border-input-border bg-surface text-muted hover:text-text',
                    )}
                  >
                    {t(`returns.reason.${REASON_KEY[r]}` as never)}
                  </button>
                ))}
              </div>
            </div>

            {/* Refund method */}
            <div>
              <label className="mb-1.5 block text-[13px] font-medium text-label">{t('returns.refundMethod')}</label>
              <div className="grid grid-cols-2 gap-2">
                {REFUNDS.map((r) => (
                  <button
                    key={r}
                    type="button"
                    onClick={() => setRefund(r)}
                    className={cn(
                      'h-10 rounded-input border text-[13px] transition-colors',
                      refund === r ? 'border-primary bg-primary-soft text-primary-hover' : 'border-input-border bg-surface text-muted hover:text-text',
                    )}
                  >
                    {t(`returns.refund.${REFUND_KEY[r]}` as never)}
                  </button>
                ))}
              </div>
            </div>
          </>
        )}

        {error && <p className="text-[12.5px] text-danger">{error}</p>}
      </div>
    </Modal>
  );
}
