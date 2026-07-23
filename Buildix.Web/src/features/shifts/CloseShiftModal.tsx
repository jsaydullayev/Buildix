import { useEffect, useState } from 'react';
import { useTranslation } from 'react-i18next';
import { useMutation, useQueryClient } from '@tanstack/react-query';
import { Modal, Button } from '@/shared/ui';
import { cn } from '@/shared/lib/cn';
import { formatSum } from '@/shared/lib/format';
import { shiftsApi, type Shift } from './api';

export function CloseShiftModal({ shift, onClose }: { shift: Shift | null; onClose: () => void }) {
  const { t } = useTranslation();
  const qc = useQueryClient();
  const open = !!shift;
  const [counted, setCounted] = useState('');

  useEffect(() => {
    if (open) setCounted(shift ? String(shift.expectedCash) : '');
  }, [open, shift]);

  const mutation = useMutation({
    mutationFn: () => shiftsApi.close(counted === '' ? null : Number(counted)),
    onSuccess: () => {
      void qc.invalidateQueries({ queryKey: ['shift-current'] });
      void qc.invalidateQueries({ queryKey: ['shift-history'] });
      onClose();
    },
  });

  const expected = shift?.expectedCash ?? 0;
  const discrepancy = counted === '' ? 0 : Number(counted) - expected;

  return (
    <Modal
      open={open}
      onClose={onClose}
      title={t('shifts.close.title')}
      footer={
        <>
          <Button variant="secondary" onClick={onClose}>
            {t('common.cancel')}
          </Button>
          <Button variant="danger" loading={mutation.isPending} onClick={() => mutation.mutate()}>
            {t('shifts.close.confirm')}
          </Button>
        </>
      }
    >
      <div className="flex flex-col gap-4">
        <div className="flex items-center justify-between rounded-input bg-bg px-4 py-3 text-[14px]">
          <span className="text-muted">{t('shifts.close.expected')}</span>
          <span className="font-semibold nums">
            {formatSum(expected)} {t('common.currency')}
          </span>
        </div>

        <div className="flex flex-col gap-1.5">
          <label className="text-[13px] font-medium text-label">{t('shifts.close.counted')}</label>
          <input
            type="number"
            step="any"
            placeholder="0"
            value={counted}
            onChange={(e) => setCounted(e.target.value)}
            className="h-12 rounded-input border border-input-border bg-surface px-4 text-[16px] outline-none focus:border-primary focus:shadow-focus-ring nums"
          />
        </div>

        <div className="flex items-center justify-between rounded-input border border-hairline px-4 py-3 text-[14px]">
          <span className="text-muted">{t('shifts.close.discrepancy')}</span>
          <span
            className={cn(
              'font-semibold nums',
              discrepancy === 0 ? 'text-success' : 'text-danger',
            )}
          >
            {discrepancy > 0 ? '+' : ''}
            {formatSum(discrepancy)} {t('common.currency')}
          </span>
        </div>
      </div>
    </Modal>
  );
}
