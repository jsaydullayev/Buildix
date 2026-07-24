import { useState } from 'react';
import { useMutation, useQueryClient } from '@tanstack/react-query';
import { useTranslation } from 'react-i18next';
import { Modal, Button } from '@/shared/ui';
import { cn } from '@/shared/lib/cn';
import { formatSum } from '@/shared/lib/format';
import type { ApiError } from '@/shared/api/types';
import { purchasesApi, type Supplier } from '@/features/purchases/api';

/**
 * «Погасить долг» — pay down a supplier's outstanding debt. The server settles
 * it FIFO across the supplier's unpaid receipts (oldest order first); any amount
 * beyond the total debt is clamped to it.
 */
export function PaySupplierDebtModal({
  open,
  supplier,
  onClose,
}: {
  open: boolean;
  supplier: Supplier;
  onClose: () => void;
}) {
  const { t } = useTranslation();
  const qc = useQueryClient();
  const [amount, setAmount] = useState('');
  const [error, setError] = useState<string | null>(null);

  const debt = supplier.outstandingDebt;
  const payAmount = Math.min(Math.max(0, Number(amount) || 0), debt);

  const close = () => {
    setAmount('');
    setError(null);
    onClose();
  };

  const pay = useMutation({
    mutationFn: () => purchasesApi.paySupplierDebt(supplier.id, payAmount),
    onSuccess: () => {
      void qc.invalidateQueries({ queryKey: ['suppliers'] });
      void qc.invalidateQueries({ queryKey: ['supplier-receipts', supplier.id] });
      void qc.invalidateQueries({ queryKey: ['receipts'] });
      close();
    },
    onError: (e) => setError((e as unknown as ApiError).message ?? t('common.somethingWrong')),
  });

  return (
    <Modal
      open={open}
      onClose={close}
      title={t('suppliers.payDebt')}
      subtitle={supplier.name}
      footer={
        <>
          <Button variant="secondary" onClick={close}>
            {t('common.cancel')}
          </Button>
          <Button loading={pay.isPending} disabled={payAmount <= 0} onClick={() => pay.mutate()}>
            {t('purchases.detail.pay')}
          </Button>
        </>
      }
    >
      <div className="flex flex-col gap-4">
        <div className="flex items-center justify-between rounded-input bg-bg px-4 py-3 text-[13px]">
          <span className="text-muted">{t('suppliers.debtLabel')}</span>
          <span className="font-semibold text-danger nums">{formatSum(debt)} {t('common.currency')}</span>
        </div>

        <div>
          <label className="mb-1.5 block text-[13px] font-medium text-label">{t('cash.newOp.amount')}</label>
          <div className="flex items-center gap-2">
            <input
              type="number"
              step="any"
              min="0"
              placeholder="0"
              value={amount}
              onChange={(e) => setAmount(e.target.value)}
              autoFocus
              className="h-11 flex-1 rounded-input border border-input-border bg-surface px-3.5 text-right text-[14px] outline-none focus:border-primary nums"
            />
            <button
              type="button"
              onClick={() => setAmount(String(debt))}
              className="whitespace-nowrap text-[12.5px] font-medium text-primary hover:text-primary-hover"
            >
              {t('purchases.detail.payFullDebt')}
            </button>
          </div>
          <p className="mt-1.5 text-[11.5px] text-muted-2">{t('suppliers.fifoHint')}</p>
        </div>

        {error && <p className={cn('text-[12.5px] text-danger')}>{error}</p>}
      </div>
    </Modal>
  );
}
