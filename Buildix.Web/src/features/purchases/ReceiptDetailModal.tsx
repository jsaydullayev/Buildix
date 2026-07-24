import { useState } from 'react';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { useTranslation } from 'react-i18next';
import { Modal, Button, Badge, Spinner } from '@/shared/ui';
import { cn } from '@/shared/lib/cn';
import { formatSum, formatQty, formatShortDate, formatTime } from '@/shared/lib/format';
import type { ApiError } from '@/shared/api/types';
import { useAuth } from '@/shared/auth/useAuth';
import { PERMISSIONS } from '@/shared/config/permissions';
import { purchasesApi } from './api';

const STATUS_TONE: Record<string, 'success' | 'warn' | 'danger'> = {
  Paid: 'success',
  Partial: 'warn',
  Unpaid: 'danger',
};
const STATUS_KEY: Record<string, 'paid' | 'partial' | 'unpaid'> = {
  Paid: 'paid',
  Partial: 'partial',
  Unpaid: 'unpaid',
};

/**
 * A single goods-receipt: its lines, supplier, and payment state, with a form
 * to pay down the outstanding supplier debt.
 *
 * Unlike the sale-detail modal, the row list carries only a summary — line
 * items and the running paid/outstanding split live on GET /Zakups/GetReceipt,
 * so this fetches by id when opened.
 */
export function ReceiptDetailModal({ receiptId, onClose }: { receiptId: string | null; onClose: () => void }) {
  const { t, i18n } = useTranslation();
  const { hasPermission } = useAuth();
  const qc = useQueryClient();
  const canPay = hasPermission(PERMISSIONS.zakup.create);
  const [payInput, setPayInput] = useState('');
  const [error, setError] = useState<string | null>(null);

  const query = useQuery({
    queryKey: ['receipt', receiptId],
    queryFn: () => purchasesApi.receipt(receiptId!),
    enabled: !!receiptId,
  });
  const receipt = query.data;

  const pay = useMutation({
    mutationFn: (amount: number) => purchasesApi.registerPayment(receiptId!, amount),
    onSuccess: (updated) => {
      setPayInput('');
      setError(null);
      qc.setQueryData(['receipt', receiptId], updated);
      void qc.invalidateQueries({ queryKey: ['receipts'] });
      void qc.invalidateQueries({ queryKey: ['receipts-all'] });
      void qc.invalidateQueries({ queryKey: ['suppliers'] });
    },
    onError: (e) => setError((e as unknown as ApiError).message ?? t('common.somethingWrong')),
  });

  const outstanding = receipt?.outstandingAmount ?? 0;
  const payAmount = Math.min(Math.max(0, Number(payInput) || 0), outstanding);

  const inputCls =
    'h-10 rounded-input border border-input-border bg-surface px-3 text-[14px] outline-none focus:border-primary focus:shadow-focus-ring';

  return (
    <Modal
      open={!!receiptId}
      onClose={onClose}
      width="lg"
      title={receipt ? `${t('purchases.detail.title')} №${receipt.receiptNumber}` : t('purchases.detail.title')}
      subtitle={
        receipt
          ? `${formatShortDate(receipt.createdAt, i18n.language)}, ${formatTime(receipt.createdAt)} · ${receipt.createdBy}`
          : undefined
      }
      footer={
        <Button variant="secondary" onClick={onClose}>
          {t('common.close')}
        </Button>
      }
    >
      {query.isLoading || !receipt ? (
        <div className="flex justify-center py-16 text-primary">
          <Spinner size={24} />
        </div>
      ) : (
        <div className="flex flex-col gap-5">
          {/* Meta */}
          <div className="flex flex-wrap items-center gap-x-6 gap-y-2 text-[13px]">
            <div className="flex items-center gap-2">
              <span className="text-muted">{t('purchases.cols.supplier')}</span>
              <span className="font-medium">{receipt.supplierName ?? t('purchases.noSupplier')}</span>
            </div>
            {receipt.invoiceNumber && (
              <div className="flex items-center gap-2">
                <span className="text-muted">{t('purchases.newModal.invoice')}</span>
                <span className="font-medium nums">{receipt.invoiceNumber}</span>
              </div>
            )}
            <div className="flex items-center gap-2">
              <span className="text-muted">{t('sales.detail.status')}</span>
              <Badge tone={STATUS_TONE[receipt.paymentStatus] ?? 'neutral'}>
                {t(`purchases.status.${STATUS_KEY[receipt.paymentStatus] ?? 'unpaid'}`)}
              </Badge>
            </div>
          </div>

          {/* Lines */}
          <div>
            <div className="grid grid-cols-[1fr_90px_120px_120px] gap-3 border-b border-hairline pb-2 text-[11.5px] font-semibold tracking-[0.4px] text-muted-2">
              <span>{t('purchases.cols.items')}</span>
              <span className="text-right">{t('sales.detail.qty')}</span>
              <span className="text-right">{t('warehouse.form.costPrice')}</span>
              <span className="text-right">{t('sales.cols.sum')}</span>
            </div>
            {receipt.items.map((it) => (
              <div
                key={it.id}
                className="grid grid-cols-[1fr_90px_120px_120px] items-center gap-3 border-b border-hairline py-2.5 text-[13px] last:border-0"
              >
                <span className="truncate font-medium">{it.productName}</span>
                <span className="text-right text-muted nums">{formatQty(it.quantity)}</span>
                <span className="text-right text-muted nums">{formatSum(it.costPrice)}</span>
                <span className="text-right font-semibold nums">{formatSum(it.lineTotal)}</span>
              </div>
            ))}
          </div>

          {/* Totals */}
          <div className="flex flex-col gap-1.5 rounded-input bg-bg px-4 py-3 text-[13px]">
            <Row label={t('pos.total')} value={formatSum(receipt.totalAmount)} strong />
            <Row label={t('purchases.detail.paid')} value={formatSum(receipt.paidAmount)} />
            {outstanding > 0 && <Row label={t('purchases.detail.outstanding')} value={formatSum(outstanding)} warn />}
          </div>

          {/* Payment */}
          {canPay && outstanding > 0 && (
            <div className="flex flex-col gap-2">
              <label className="text-[13px] font-medium text-label">{t('purchases.detail.payTitle')}</label>
              <div className="flex items-center gap-2">
                <input
                  type="number"
                  step="any"
                  placeholder="0"
                  value={payInput}
                  onChange={(e) => setPayInput(e.target.value)}
                  className={cn(inputCls, 'flex-1 text-right nums')}
                />
                <button
                  type="button"
                  onClick={() => setPayInput(String(outstanding))}
                  className="whitespace-nowrap text-[12px] font-medium text-primary hover:text-primary-hover"
                >
                  {t('purchases.newModal.payFull')}
                </button>
                <Button
                  size="sm"
                  disabled={payAmount <= 0 || pay.isPending}
                  loading={pay.isPending}
                  onClick={() => pay.mutate(payAmount)}
                >
                  {t('purchases.detail.pay')}
                </Button>
              </div>
              {error && <div className="text-[12.5px] text-danger">{error}</div>}
            </div>
          )}

          {receipt.comment && (
            <p className="rounded-input bg-bg px-4 py-2.5 text-[13px] text-muted">{receipt.comment}</p>
          )}
        </div>
      )}
    </Modal>
  );
}

function Row({ label, value, strong, warn }: { label: string; value: string; strong?: boolean; warn?: boolean }) {
  const { t } = useTranslation();
  return (
    <div className="flex items-baseline justify-between">
      <span className={strong ? 'font-semibold' : 'text-muted'}>{label}</span>
      <span className={cn('nums', warn ? 'font-semibold text-warn-text' : strong ? 'text-[16px] font-bold' : 'font-medium')}>
        {value} <span className="text-[11.5px] font-normal text-muted-2">{t('common.currency')}</span>
      </span>
    </div>
  );
}
