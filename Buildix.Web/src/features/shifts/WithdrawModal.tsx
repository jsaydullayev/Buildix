import { useEffect, useState } from 'react';
import { useTranslation } from 'react-i18next';
import { useMutation, useQueryClient } from '@tanstack/react-query';
import { Modal, Button } from '@/shared/ui';
import type { ApiError } from '@/shared/api/types';
import { cashApi } from './api';

export function WithdrawModal({
  open,
  onClose,
  isOwner,
}: {
  open: boolean;
  onClose: () => void;
  isOwner: boolean;
}) {
  const { t } = useTranslation();
  const qc = useQueryClient();
  const [amount, setAmount] = useState('');
  const [comment, setComment] = useState('');
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    if (open) {
      setAmount('');
      setComment('');
      setError(null);
    }
  }, [open]);

  const mutation = useMutation({
    mutationFn: () => {
      const value = Number(amount);
      // Owner withdraws immediately; everyone else submits a request for approval.
      return isOwner ? cashApi.withdraw(value, comment) : cashApi.request(value, comment);
    },
    onSuccess: () => {
      void qc.invalidateQueries({ queryKey: ['withdrawals'] });
      void qc.invalidateQueries({ queryKey: ['shift-current'] });
      onClose();
    },
    onError: (e) => setError((e as unknown as ApiError).message ?? t('common.somethingWrong')),
  });

  const value = Number(amount);
  const valid = value > 0;
  const inputCls =
    'h-12 rounded-input border border-input-border bg-surface px-4 text-[15px] outline-none focus:border-primary focus:shadow-focus-ring';

  return (
    <Modal
      open={open}
      onClose={onClose}
      title={isOwner ? t('shifts.withdraw.action') : t('shifts.withdraw.request')}
      footer={
        <>
          <Button variant="secondary" onClick={onClose}>
            {t('common.cancel')}
          </Button>
          <Button disabled={!valid} loading={mutation.isPending} onClick={() => mutation.mutate()}>
            {isOwner ? t('shifts.withdraw.submitWithdraw') : t('shifts.withdraw.submitRequest')}
          </Button>
        </>
      }
    >
      <div className="flex flex-col gap-4">
        <div className="flex flex-col gap-1.5">
          <label className="text-[13px] font-medium text-label">{t('shifts.withdraw.amount')}</label>
          <input
            type="number"
            step="any"
            autoFocus
            placeholder="0"
            value={amount}
            onChange={(e) => setAmount(e.target.value)}
            className={`${inputCls} nums`}
          />
        </div>
        <div className="flex flex-col gap-1.5">
          <label className="text-[13px] font-medium text-label">{t('shifts.withdraw.comment')}</label>
          <input value={comment} onChange={(e) => setComment(e.target.value)} className={inputCls} />
        </div>
        {error && <p className="text-[12.5px] text-danger">{error}</p>}
      </div>
    </Modal>
  );
}
