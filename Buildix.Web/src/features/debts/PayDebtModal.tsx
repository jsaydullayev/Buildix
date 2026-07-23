import { useEffect, useState } from 'react';
import { useTranslation } from 'react-i18next';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { Modal, Button } from '@/shared/ui';
import { formatSum } from '@/shared/lib/format';
import { cn } from '@/shared/lib/cn';
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
  const { t } = useTranslation();
  const qc = useQueryClient();
  const open = !!debtor;

  const [amount, setAmount] = useState('');
  const [method, setMethod] = useState<string>('Cash');

  // The customer's open debts (oldest first) — we settle against the oldest.
  const debtsQuery = useQuery({
    queryKey: ['customer-debts', debtor?.customerId],
    queryFn: () => debtsApi.customerDebts(debtor!.customerId),
    enabled: open,
  });

  const oldest = debtsQuery.data
    ?.filter((d) => d.remainingDebt > 0)
    .slice()
    .sort((a, b) => a.createdAt.localeCompare(b.createdAt))[0];

  useEffect(() => {
    if (open) {
      setAmount('');
      setMethod('Cash');
    }
  }, [open, debtor?.customerId]);

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
        <div className="flex items-center justify-between rounded-input bg-bg px-4 py-3 text-[14px]">
          <span className="text-muted">{t('debts.remaining')}</span>
          <span className="font-semibold nums">
            {formatSum(oldest?.remainingDebt ?? debtor?.remainingDebt ?? 0)} {t('common.currency')}
          </span>
        </div>

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
          <div className="grid grid-cols-4 gap-2">
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
