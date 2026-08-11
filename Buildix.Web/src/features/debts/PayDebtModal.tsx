import { useEffect, useState } from 'react';
import { useTranslation } from 'react-i18next';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { CalendarClock, AlertTriangle } from 'lucide-react';
import { Modal, Button } from '@/shared/ui';
import { formatSum, formatShortDate } from '@/shared/lib/format';
import { cn } from '@/shared/lib/cn';
import { useAuth } from '@/shared/auth/useAuth';
import { PERMISSIONS } from '@/shared/config/permissions';
import { debtsApi, type DebtorSummary } from './api';

const METHODS = [
  { key: 'cash', value: 'Cash' },
  { key: 'card', value: 'Terminal' },
  { key: 'transfer', value: 'Transfer' },
  { key: 'click', value: 'Click' },
] as const;

export function PayDebtModal({
  debtor,
  onClose,
}: {
  debtor: DebtorSummary | null;
  onClose: () => void;
}) {
  const { t, i18n } = useTranslation();
  const { hasPermission } = useAuth();
  const canEditDue = hasPermission(PERMISSIONS.debts.dueDate);
  const qc = useQueryClient();
  const open = !!debtor;

  const [amount, setAmount] = useState('');
  const [method, setMethod] = useState<string>('Cash');
  const [dueEditing, setDueEditing] = useState(false);
  const [due, setDue] = useState('');

  // The customer's open debts (oldest first) — we settle against the oldest.
  const debtsQuery = useQuery({
    queryKey: ['customer-debts', debtor?.customerId],
    queryFn: () => debtsApi.customerDebts(debtor!.customerId),
    enabled: open,
  });

  const openDebts = (debtsQuery.data ?? []).filter((d) => d.remainingDebt > 0);
  const oldest = openDebts.slice().sort((a, b) => a.createdAt.localeCompare(b.createdAt))[0];
  // «У клиента есть ещё долги» — bir nechta ochiq chek bo'lsa (dizayn banneri).
  const hasMore = openDebts.length > 1;
  const totalOwed = openDebts.reduce((sum, d) => sum + d.remainingDebt, 0);

  useEffect(() => {
    if (open) {
      setAmount('');
      setMethod('Cash');
      setDueEditing(false);
      setDue('');
    }
  }, [open, debtor?.customerId]);

  const updateDue = useMutation({
    mutationFn: () => debtsApi.updateDueDate(oldest!.id, due || null),
    onSuccess: () => {
      setDueEditing(false);
      void qc.invalidateQueries({ queryKey: ['customer-debts', debtor?.customerId] });
      void qc.invalidateQueries({ queryKey: ['debtors'] });
    },
  });

  const mutation = useMutation({
    mutationFn: () => {
      const value = Number(amount);
      return debtsApi.pay(oldest!.id, value, method);
    },
    onSuccess: () => {
      void qc.invalidateQueries({ queryKey: ['debtors'] });
      void qc.invalidateQueries({ queryKey: ['debt-summary'] });
      onClose();
    },
  });

  const max = oldest?.remainingDebt ?? 0;
  const value = Number(amount);
  const valid = value > 0 && value <= max;

  return (
    <Modal
      open={open}
      onClose={onClose}
      title={t('debts.payTitle')}
      subtitle={debtor?.customerName ?? debtor?.customerPhone}
      footer={
        <>
          <Button variant="secondary" onClick={onClose}>
            {t('common.cancel')}
          </Button>
          <Button disabled={!valid} loading={mutation.isPending} onClick={() => mutation.mutate()}>
            {t('debts.pay')}
          </Button>
        </>
      }
    >
      <div className="flex flex-col gap-4">
        {hasMore && (
          <div className="flex items-start gap-2 rounded-input border border-warn-amber/30 bg-warn-soft px-3.5 py-2.5 text-[12.5px] text-warn-strong">
            <AlertTriangle size={14} className="mt-0.5 flex-none" />
            <span>{t('debts.moreDebts', { count: openDebts.length, total: formatSum(totalOwed) })}</span>
          </div>
        )}

        <div className="flex items-center justify-between rounded-input bg-bg px-4 py-3 text-[14px]">
          <span className="text-muted">{t('debts.remaining')}</span>
          <span className="font-semibold nums">
            {formatSum(oldest?.remainingDebt ?? debtor?.remainingDebt ?? 0)} {t('common.currency')}
          </span>
        </div>

        {/* Muddat — eng eski qarzning to'lash muddati (debts.dueDate ruxsati). */}
        {oldest && canEditDue && (
          <div className="flex items-center justify-between gap-3 text-[13px]">
            <span className="flex items-center gap-1.5 text-muted">
              <CalendarClock size={14} />
              {t('debts.dueDate')}
            </span>
            {dueEditing ? (
              <div className="flex items-center gap-2">
                <input
                  type="date"
                  value={due}
                  onChange={(e) => setDue(e.target.value)}
                  className="h-9 rounded-input border border-input-border bg-surface px-2.5 text-[13px] outline-none focus:border-primary nums"
                />
                <Button size="sm" loading={updateDue.isPending} onClick={() => updateDue.mutate()}>
                  {t('common.save')}
                </Button>
              </div>
            ) : (
              <button
                type="button"
                onClick={() => {
                  setDue(oldest.dueDate ? oldest.dueDate.slice(0, 10) : '');
                  setDueEditing(true);
                }}
                className="font-medium text-primary hover:text-primary-hover nums"
              >
                {oldest.dueDate ? formatShortDate(oldest.dueDate, i18n.language) : t('debts.setDueDate')}
              </button>
            )}
          </div>
        )}

        <div className="flex flex-col gap-1.5">
          <label className="text-[13px] font-medium text-label">{t('debts.payAmount')}</label>
          <div className="flex gap-2">
            <input
              type="number"
              step="any"
              placeholder="0"
              value={amount}
              onChange={(e) => setAmount(e.target.value)}
              className="h-11 flex-1 rounded-input border border-input-border bg-surface px-3.5 text-[15px] outline-none focus:border-primary focus:shadow-focus-ring"
            />
            <Button variant="secondary" onClick={() => setAmount(String(max))} disabled={!max}>
              {t('debts.payFull')}
            </Button>
          </div>
        </div>

        <div className="flex flex-col gap-1.5">
          <label className="text-[13px] font-medium text-label">{t('debts.payMethod')}</label>
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
        </div>
      </div>
    </Modal>
  );
}
