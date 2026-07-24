import { useState } from 'react';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { useTranslation } from 'react-i18next';
import { FileDown, Ban, Undo2, Check, X } from 'lucide-react';
import { Modal, Button, Badge } from '@/shared/ui';
import { cn } from '@/shared/lib/cn';
import { formatSum, formatQty, formatShortDate, formatTime } from '@/shared/lib/format';
import { unitLabel } from '@/shared/lib/units';
import { downloadBlob } from '@/shared/lib/download';
import { useAuth } from '@/shared/auth/useAuth';
import { PERMISSIONS } from '@/shared/config/permissions';
import type { ApiError } from '@/shared/api/types';
import { salesApi, type Sale, type SalePayment, type SaleItem } from './api';

/** Statuses whose money/stock can still be reversed. */
const REVERSIBLE = new Set(['Paid', 'Debt', 'Closed']);

const STATUS_TONE: Record<string, 'success' | 'warn' | 'danger' | 'neutral'> = {
  Paid: 'success',
  Closed: 'success',
  Debt: 'warn',
  Draft: 'neutral',
  Cancelled: 'danger',
};

/**
 * A single receipt, opened by clicking its row in the sales list.
 *
 * The list row already carries items, payments, customer, seller, discount and
 * paid/remaining, so it is used as placeholder data and the modal paints
 * instantly. A by-id read then fills in what the list projection leaves out —
 * the shift number, the time of each payment, and who collected it when a debt
 * was paid off later by a different cashier.
 */
export function SaleDetailModal({ sale: listRow, onClose }: { sale: Sale | null; onClose: () => void }) {
  const { t, i18n } = useTranslation();
  const { hasPermission } = useAuth();
  const qc = useQueryClient();
  const [downloading, setDownloading] = useState(false);
  const [actionError, setActionError] = useState<string | null>(null);
  // Which line is mid-return, plus the quantity typed for it.
  const [returning, setReturning] = useState<{ id: string; qty: string } | null>(null);

  const detailQuery = useQuery({
    queryKey: ['sale-detail', listRow?.id],
    queryFn: () => salesApi.byId(listRow!.id),
    enabled: !!listRow,
    // Instant paint from the row we already have; the fetch only enriches it.
    placeholderData: listRow ?? undefined,
  });

  const sale = detailQuery.data ?? listRow;

  const canCancel = hasPermission(PERMISSIONS.sales.delete);
  const canReturn = hasPermission(PERMISSIONS.sales.edit);
  const reversible = !!sale && REVERSIBLE.has(sale.status);

  const invalidate = () => {
    void qc.invalidateQueries({ queryKey: ['sales'] });
    void qc.invalidateQueries({ queryKey: ['today-sales'] });
    void qc.invalidateQueries({ queryKey: ['sale-detail', sale?.id] });
  };
  const onErr = (e: unknown) => setActionError((e as ApiError).message ?? t('common.somethingWrong'));

  const cancel = useMutation({
    mutationFn: () => salesApi.cancel(sale!.id),
    onSuccess: () => {
      setActionError(null);
      invalidate();
      onClose();
    },
    onError: onErr,
  });

  const returnItem = useMutation({
    mutationFn: ({ itemId, qty }: { itemId: string; qty: number }) => salesApi.returnItem(sale!.id, itemId, qty),
    onSuccess: () => {
      setActionError(null);
      setReturning(null);
      invalidate();
    },
    onError: onErr,
  });

  if (!sale) return null;

  const gross = sale.items.reduce((sum, i) => sum + i.totalPrice, 0);
  const statusKey = sale.status.toLowerCase();

  async function downloadInvoice(id: string, saleNumber: number) {
    setDownloading(true);
    try {
      const blob = await salesApi.invoicePdf(id, i18n.language);
      downloadBlob(blob, `invoice-${saleNumber}.pdf`);
    } catch {
      // best-effort; ignore
    } finally {
      setDownloading(false);
    }
  }

  function askCancel() {
    if (window.confirm(t('sales.detail.cancelConfirm', { number: sale!.saleNumber }))) cancel.mutate();
  }

  return (
    <Modal
      open
      onClose={onClose}
      width="lg"
      title={`${t('sales.detail.title')} №${sale.saleNumber}`}
      subtitle={`${formatShortDate(sale.createdAt, i18n.language)}, ${formatTime(sale.createdAt)} · ${sale.sellerName}`}
      footer={
        <>
          <Button variant="secondary" onClick={onClose}>
            {t('common.close')}
          </Button>
          {canCancel && reversible && (
            <Button variant="danger" loading={cancel.isPending} onClick={askCancel}>
              <Ban size={15} />
              {t('sales.detail.cancel')}
            </Button>
          )}
          {hasPermission(PERMISSIONS.sales.invoice) && (
            <Button loading={downloading} onClick={() => void downloadInvoice(sale.id, sale.saleNumber)}>
              <FileDown size={15} />
              {t('sales.detail.invoice')}
            </Button>
          )}
        </>
      }
    >
      <div className="flex flex-col gap-5">
        {/* Meta */}
        <div className="flex flex-wrap items-center gap-x-6 gap-y-2 text-[13px]">
          <Meta label={t('sales.detail.customer')} value={sale.customerName ?? t('sales.walkIn')} />
          {sale.customerPhone && <Meta label={t('seller.clients.cols.phone')} value={sale.customerPhone} />}
          {/* Chek qaysi smenaga tegishli ekani — kassa rekonsiliatsiyasida
              «bu chek mening smenamdami?» degan savolga yagona javob. 0 =
              smena raqamlanishidan oldingi eski sotuv. */}
          {!!sale.shiftNumber && (
            <Meta label={t('seller.pos.shift')} value={`№${sale.shiftNumber}`} />
          )}
          <div className="flex items-center gap-2">
            <span className="text-muted">{t('sales.detail.status')}</span>
            <Badge tone={STATUS_TONE[sale.status] ?? 'neutral'}>
              {t(`sales.status.${statusKey}` as never, { defaultValue: sale.status })}
            </Badge>
          </div>
        </div>

        {/* Items */}
        <div>
          <div className="grid grid-cols-[1fr_90px_110px_110px_32px] gap-3 border-b border-hairline pb-2 text-[11.5px] font-semibold tracking-[0.4px] text-muted-2">
            <span>{t('sales.cols.items')}</span>
            <span className="text-right">{t('sales.detail.qty')}</span>
            <span className="text-right">{t('sales.detail.price')}</span>
            <span className="text-right">{t('sales.cols.sum')}</span>
            <span />
          </div>
          {sale.items.length === 0 ? (
            <p className="py-6 text-center text-[13px] text-muted-2">{t('sales.detail.noItems')}</p>
          ) : (
            sale.items.map((it) => (
              <ItemRow
                key={it.id}
                item={it}
                canReturn={canReturn && reversible}
                returning={returning?.id === it.id ? returning.qty : null}
                busy={returnItem.isPending}
                onStartReturn={() => setReturning({ id: it.id, qty: String(it.quantity) })}
                onChangeQty={(qty) => setReturning({ id: it.id, qty })}
                onCancelReturn={() => setReturning(null)}
                onConfirmReturn={() => {
                  const qty = Math.min(Math.max(0, Number(returning?.qty) || 0), it.quantity);
                  if (qty > 0) returnItem.mutate({ itemId: it.id, qty });
                }}
              />
            ))
          )}
        </div>

        {actionError && (
          <div className="rounded-input bg-danger-soft px-3 py-2 text-[12.5px] text-danger">{actionError}</div>
        )}

        {/* Totals */}
        <div className="flex flex-col gap-1.5 rounded-input bg-bg px-4 py-3 text-[13px]">
          {/* Chegirma bo'lsagina oraliq (chegirmasiz) summani ko'rsatamiz — aks
              holda u pastdagi "Jami" ning aynan nusxasi bo'lardi. */}
          {sale.discountAmount > 0 && (
            <>
              <TotalRow label={t('sales.detail.subtotal')} value={formatSum(gross)} />
              <TotalRow label={t('pos.discount')} value={`− ${formatSum(sale.discountAmount)}`} />
            </>
          )}
          <TotalRow label={t('pos.total')} value={formatSum(sale.totalAmount)} strong />
          <TotalRow label={t('sales.detail.paid')} value={formatSum(sale.paidAmount)} />
          {sale.remainingAmount > 0 && (
            <TotalRow label={t('sales.detail.remaining')} value={formatSum(sale.remainingAmount)} warn />
          )}
        </div>

        {/* Payments */}
        <div>
          <h3 className="mb-2 text-[13px] font-semibold">{t('sales.detail.payments')}</h3>
          {sale.payments.length === 0 ? (
            <p className="text-[13px] text-muted-2">{t('sales.detail.noPayments')}</p>
          ) : (
            <div className="flex flex-col gap-1.5">
              {sale.payments.map((p, i) => (
                <PaymentRow key={p.paymentId ?? i} payment={p} />
              ))}
            </div>
          )}
        </div>
      </div>
    </Modal>
  );
}

/**
 * One sale line. When a return is in progress on this line it swaps the "return"
 * button for an inline qty field + confirm/cancel, so a partial return needs no
 * separate dialog.
 */
function ItemRow({
  item: it,
  canReturn,
  returning,
  busy,
  onStartReturn,
  onChangeQty,
  onCancelReturn,
  onConfirmReturn,
}: {
  item: SaleItem;
  canReturn: boolean;
  /** The typed quantity when this row is mid-return, else null. */
  returning: string | null;
  busy: boolean;
  onStartReturn: () => void;
  onChangeQty: (qty: string) => void;
  onCancelReturn: () => void;
  onConfirmReturn: () => void;
}) {
  const { t } = useTranslation();
  return (
    <div className="grid grid-cols-[1fr_90px_110px_110px_32px] items-center gap-3 border-b border-hairline py-2.5 text-[13px] last:border-0">
      <span className="truncate font-medium">{it.productName}</span>
      {returning !== null ? (
        <input
          type="number"
          step="any"
          autoFocus
          value={returning}
          onChange={(e) => onChangeQty(e.target.value)}
          className="h-8 w-full rounded-input border border-input-border bg-surface px-2 text-right text-[13px] outline-none focus:border-primary nums"
        />
      ) : (
        <span className="text-right text-muted nums">
          {formatQty(it.quantity)} {unitLabel(t, it.unitValue, it.unit)}
        </span>
      )}
      <span className="text-right text-muted nums">{formatSum(it.salePrice)}</span>
      <span className="text-right font-semibold nums">{formatSum(it.totalPrice)}</span>
      <div className="flex items-center justify-end">
        {!canReturn ? null : returning !== null ? (
          <div className="flex items-center gap-0.5">
            <button
              type="button"
              disabled={busy}
              onClick={onConfirmReturn}
              title={t('sales.detail.returnConfirm')}
              className="flex h-7 w-7 items-center justify-center rounded-md text-success hover:bg-success-soft"
            >
              <Check size={15} />
            </button>
            <button
              type="button"
              onClick={onCancelReturn}
              className="flex h-7 w-7 items-center justify-center rounded-md text-muted-2 hover:text-text"
            >
              <X size={15} />
            </button>
          </div>
        ) : (
          <button
            type="button"
            onClick={onStartReturn}
            title={t('sales.detail.return')}
            className={cn('flex h-7 w-7 items-center justify-center rounded-md text-muted-2 hover:text-warn-text')}
          >
            <Undo2 size={14} />
          </button>
        )}
      </div>
    </div>
  );
}

/**
 * One line of the payment history. A refund is stored as a NEGATIVE payment, so
 * it is labelled as a return rather than rendered as a smaller payment — on a
 * partly-refunded receipt the two are otherwise indistinguishable.
 */
function PaymentRow({ payment }: { payment: SalePayment }) {
  const { t } = useTranslation();
  const isRefund = payment.amount < 0;
  return (
    <div className="flex items-baseline justify-between gap-3 text-[13px]">
      <span className="min-w-0 text-muted">
        {isRefund
          ? t('sales.payment.returned')
          : t(`sales.payment.${payment.paymentType.toLowerCase()}` as never, { defaultValue: payment.paymentType })}
        {payment.createdAt && <span className="ml-2 text-[11.5px] text-muted-2 nums">{formatTime(payment.createdAt)}</span>}
        {/* Qarz keyinroq boshqa kassir tomonidan yig'ilgan bo'lishi mumkin —
            pulni kim olgani chekda ko'rinib tursin. */}
        {payment.collectedByName && (
          <span className="ml-2 truncate text-[11.5px] text-muted-2">· {payment.collectedByName}</span>
        )}
      </span>
      <span className={isRefund ? 'font-semibold text-warn-text nums' : 'font-semibold nums'}>
        {formatSum(payment.amount)} {t('common.currency')}
      </span>
    </div>
  );
}

function Meta({ label, value }: { label: string; value: string }) {
  return (
    <div className="flex items-center gap-2">
      <span className="text-muted">{label}</span>
      <span className="font-medium">{value}</span>
    </div>
  );
}

function TotalRow({
  label,
  value,
  strong,
  warn,
}: {
  label: string;
  value: string;
  strong?: boolean;
  warn?: boolean;
}) {
  const { t } = useTranslation();
  return (
    <div className="flex items-baseline justify-between">
      <span className={strong ? 'font-semibold' : 'text-muted'}>{label}</span>
      <span
        className={
          warn
            ? 'font-semibold text-warn-text nums'
            : strong
              ? 'text-[16px] font-bold nums'
              : 'font-medium nums'
        }
      >
        {value} <span className="text-[11.5px] font-normal text-muted-2">{t('common.currency')}</span>
      </span>
    </div>
  );
}
