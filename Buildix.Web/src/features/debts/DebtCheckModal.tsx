import { useEffect, useMemo, useState } from 'react';
import { useTranslation } from 'react-i18next';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { AlertTriangle } from 'lucide-react';
import { Modal, Button, Spinner } from '@/shared/ui';
import { formatSum, formatQty, formatShortDate } from '@/shared/lib/format';
import { cn } from '@/shared/lib/cn';
import { debtsApi, type DebtCheck } from './api';

const METHODS = [
  { key: 'cash', value: 'Cash' },
  { key: 'card', value: 'Terminal' },
  { key: 'transfer', value: 'Transfer' },
  { key: 'click', value: 'Click' },
] as const;

/**
 * Per-check debt detail + payment. Shows the sale's line items, a paid/total
 * progress bar and a payment form that settles THIS specific check (debtId) —
 * so a client's multiple debts never get mixed. `canManage` gates the pay form.
 */
export function DebtCheckModal({
  check,
  canManage,
  onClose,
}: {
  check: DebtCheck | null;
  canManage: boolean;
  onClose: () => void;
}) {
  const { t, i18n } = useTranslation();
  const qc = useQueryClient();
  const open = !!check;

  const [amount, setAmount] = useState('');
  const [method, setMethod] = useState<string>('Cash');

  // Line items come from the customer's debts (each carries its sale items).
  const debtsQuery = useQuery({
    queryKey: ['customer-debts', check?.customerId],
    queryFn: () => debtsApi.customerDebts(check!.customerId),
    enabled: open,
  });
  const items = useMemo(
    () => debtsQuery.data?.find((d) => d.id === check?.debtId)?.saleItems ?? [],
    [debtsQuery.data, check?.debtId],
  );

  useEffect(() => {
    if (open) {
      setAmount('');
      setMethod('Cash');
    }
  }, [open, check?.debtId]);

  const mutation = useMutation({
    mutationFn: () => debtsApi.pay(check!.debtId, Number(amount), method),
    onSuccess: () => {
      void qc.invalidateQueries({ queryKey: ['debt-checks'] });
      void qc.invalidateQueries({ queryKey: ['debtors'] });
      void qc.invalidateQueries({ queryKey: ['debt-summary'] });
      void qc.invalidateQueries({ queryKey: ['debt-today'] });
      void qc.invalidateQueries({ queryKey: ['customer-debts', check?.customerId] });
      onClose();
    },
  });

  if (!check) return null;

  const total = check.totalDebt;
  const remaining = check.remainingDebt;
  const paid = Math.max(0, total - remaining);
  const paidPct = total > 0 ? Math.min(100, (paid / total) * 100) : 0;
  const value = Number(amount);
  const valid = value > 0 && value <= remaining;
  const afterPay = Math.max(0, remaining - (valid ? value : 0));

  const dueLabel = check.dueDate
    ? check.isOverdue
      ? t('debts.overdueTag')
      : formatShortDate(check.dueDate, i18n.language)
    : t('debts.noDue');

  return (
    <Modal
      open={open}
      onClose={onClose}
      width="lg"
      title={t('debts.check.title', {
        name: check.customerName ?? check.customerPhone,
        check: t('cash.desc.receipt', { n: check.saleNumber }),
      })}
      subtitle={undefined}
      footer={
        canManage ? (
          <>
            <Button variant="secondary" onClick={onClose}>
              {t('common.cancel')}
            </Button>
            <Button disabled={!valid} loading={mutation.isPending} onClick={() => mutation.mutate()}>
              {t('debts.pay')}
            </Button>
          </>
        ) : (
          <Button variant="secondary" onClick={onClose}>
            {t('common.close')}
          </Button>
        )
      }
    >
      <div className="flex flex-col gap-4">
        {/* Meta row */}
        <div className="flex flex-wrap items-center gap-x-6 gap-y-1.5 rounded-input bg-bg px-4 py-3 text-[12.5px]">
          <span>
            <span className="text-muted-2">{t('debts.check.sold')}:</span>{' '}
            <span className="font-medium nums">{formatShortDate(check.createdAt, i18n.language)}</span>
          </span>
          <span>
            <span className="text-muted-2">{t('debts.dueDate')}:</span>{' '}
            <span className={cn('font-medium nums', check.isOverdue && 'text-danger')}>{dueLabel}</span>
          </span>
          <span>
            <span className="text-muted-2">{t('debts.cols.phone')}:</span>{' '}
            <span className="font-medium nums">{check.customerPhone}</span>
          </span>
        </div>

        {check.customerDebtCount > 1 && (
          <div className="flex items-start gap-2 rounded-input border border-primary/20 bg-primary/5 px-3.5 py-2.5 text-[12.5px] text-primary">
            <AlertTriangle size={14} className="mt-0.5 flex-none" />
            <span>{t('debts.check.hasOthers', { count: check.customerDebtCount - 1 })}</span>
          </div>
        )}

        {/* Line items */}
        <div className="overflow-hidden rounded-input border border-hairline">
          <div className="grid grid-cols-[minmax(0,1.6fr)_80px_110px_120px] gap-3 border-b border-hairline bg-bg/40 px-4 py-2 text-[11px] font-semibold tracking-[0.4px] text-muted-2">
            <span>{t('debts.check.cols.product')}</span>
            <span className="text-right">{t('debts.check.cols.qty')}</span>
            <span className="text-right">{t('debts.check.cols.price')}</span>
            <span className="text-right">{t('debts.check.cols.sum')}</span>
          </div>
          {debtsQuery.isLoading ? (
            <div className="flex justify-center py-6 text-primary">
              <Spinner size={18} />
            </div>
          ) : items.length === 0 ? (
            <p className="py-5 text-center text-[12.5px] text-muted-2">{t('debts.check.noItems')}</p>
          ) : (
            items.map((it) => (
              <div
                key={it.id}
                className="grid grid-cols-[minmax(0,1.6fr)_80px_110px_120px] gap-3 border-b border-hairline px-4 py-2.5 text-[13px] last:border-0"
              >
                <span className="truncate font-medium">{it.productName}</span>
                <span className="text-right text-muted nums">{formatQty(it.quantity)}</span>
                <span className="text-right text-muted nums">{formatSum(it.salePrice)}</span>
                <span className="text-right font-semibold nums">{formatSum(it.totalPrice)}</span>
              </div>
            ))
          )}
        </div>

        {/* Totals + progress */}
        <div className="rounded-input border border-hairline px-4 py-3">
          <div className="mb-1.5 flex justify-between text-[13px]">
            <span className="text-muted">{t('debts.check.checkTotal')}</span>
            <span className="font-semibold nums">
              {formatSum(total)} {t('common.currency')}
            </span>
          </div>
          <div className="mb-1.5 flex justify-between text-[13px]">
            <span className="text-muted">{t('debts.check.paid')}</span>
            <span className="font-bold text-success nums">
              {formatSum(paid)} {t('common.currency')}
            </span>
          </div>
          <div className="mb-2.5 flex justify-between text-[13px]">
            <span className="font-semibold">{t('debts.remaining')}</span>
            <span className="text-[15px] font-bold text-danger nums">
              {formatSum(remaining)} {t('common.currency')}
            </span>
          </div>
          <div className="h-[7px] overflow-hidden rounded-pill bg-danger-soft">
            <div className="h-[7px] rounded-pill bg-success" style={{ width: `${paidPct}%` }} />
          </div>
        </div>

        {/* Pay form */}
        {canManage && (
          <div className="flex flex-col gap-3 rounded-input bg-bg px-4 py-3.5">
            <div className="text-[13px] font-semibold">{t('debts.check.payThis')}</div>
            <div className="flex gap-2">
              <input
                type="number"
                step="any"
                placeholder="0"
                value={amount}
                onChange={(e) => setAmount(e.target.value)}
                className="h-11 flex-1 rounded-input border border-input-border bg-surface px-3.5 text-[15px] outline-none focus:border-primary focus:shadow-focus-ring nums"
              />
              <Button variant="secondary" onClick={() => setAmount(String(remaining))} disabled={remaining <= 0}>
                {t('debts.payFull')}
              </Button>
            </div>
            <div className="grid grid-cols-2 lg:grid-cols-4 gap-2">
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
                  {t(`debts.methods.${m.key}`)}
                </button>
              ))}
            </div>
            {value > 0 && (
              <div className="flex items-center justify-between text-[12.5px]">
                <span className="text-muted">{t('debts.check.afterPay')}</span>
                <span className={cn('font-semibold nums', afterPay > 0 ? 'text-warn-text' : 'text-success')}>
                  {afterPay > 0 ? `${formatSum(afterPay)} ${t('common.currency')}` : t('debts.check.settled')}
                </span>
              </div>
            )}
          </div>
        )}
      </div>
    </Modal>
  );
}
