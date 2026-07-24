import { useTranslation } from 'react-i18next';
import { Modal, Badge } from '@/shared/ui';
import { cn } from '@/shared/lib/cn';
import { formatSum, formatTime, formatShortDate } from '@/shared/lib/format';
import type { Shift } from './api';

const STATUS_TONE: Record<string, { key: 'open' | 'balanced' | 'discrepancy'; tone: 'success' | 'warn' | 'danger' | 'neutral' }> = {
  Open: { key: 'open', tone: 'neutral' },
  Balanced: { key: 'balanced', tone: 'success' },
  Discrepancy: { key: 'discrepancy', tone: 'danger' },
};

/**
 * Смена detali — view-only (dizayn: force-close tugmasi bu yerda yo'q, u
 * Журнал qatoridagi alohida tugma). Per-tender savdo + naqd sverkasi.
 */
export function ShiftDetailModal({ shift, lang, onClose }: { shift: Shift | null; lang: string; onClose: () => void }) {
  const { t } = useTranslation();
  if (!shift) return null;

  const st = STATUS_TONE[shift.reconStatus] ?? STATUS_TONE.Open!;
  const period = `${formatTime(shift.openedAt)} — ${shift.closedAt ? formatTime(shift.closedAt) : '…'}`;
  const pays: Array<{ label: string; sum: number }> = [
    { label: t('shifts.tender.cash'), sum: shift.cashIn },
    { label: t('shifts.tender.card'), sum: shift.cardIn },
    { label: t('shifts.tender.debt'), sum: shift.debtIn },
  ];

  // Naqd sverkasi faqat yopilgan smenada mantiqli (kassir sanagan bo'lsa).
  const reconciled = !shift.isOpen && shift.countedCash !== null;
  const diff = shift.discrepancy;
  const diffTone = diff === 0 ? 'text-success' : shift.reconStatus === 'Discrepancy' ? 'text-danger' : 'text-warn-strong';

  return (
    <Modal
      open
      onClose={onClose}
      width="md"
      title={`${t('shifts.detail.title')} №${shift.shiftNumber} · ${shift.cashierName}`}
      subtitle={`${formatShortDate(shift.openedAt, lang)} · ${period} · ${t('shifts.detail.checks', { count: shift.checkCount })}`}
    >
      <div className="mb-4 flex items-center">
        <Badge tone={st.tone}>{t(`shifts.status.${st.key}`)}</Badge>
      </div>

      {/* Выручка по способам оплаты */}
      <h3 className="mb-2.5 text-[13.5px] font-semibold">{t('shifts.detail.revenueByTender')}</h3>
      <div className="flex flex-col gap-2">
        {pays.map((p) => (
          <div key={p.label} className="flex items-center justify-between text-[13px]">
            <span className="text-muted">{p.label}</span>
            <span className="font-semibold nums">
              {formatSum(p.sum)} <span className="text-[11px] font-normal text-muted-2">{t('common.currency')}</span>
            </span>
          </div>
        ))}
        {shift.returnAmount > 0 && (
          <div className="flex items-center justify-between text-[13px]">
            <span className="text-muted">{t('shifts.tender.returns')}</span>
            <span className="font-semibold nums text-danger">
              − {formatSum(shift.returnAmount)} <span className="text-[11px] font-normal text-muted-2">{t('common.currency')}</span>
            </span>
          </div>
        )}
        <div className="mt-1 flex items-center justify-between border-t border-hairline pt-2.5 text-[13.5px]">
          <span className="font-semibold">{t('shifts.detail.totalRevenue')}</span>
          <span className="font-bold nums text-primary">
            {formatSum(shift.revenue)} <span className="text-[11px] font-normal text-muted-2">{t('common.currency')}</span>
          </span>
        </div>
      </div>

      {/* Сверка наличных при закрытии */}
      <div
        className={cn(
          'mt-5 rounded-card border px-4 py-3.5',
          !reconciled
            ? 'border-border bg-bg/40'
            : diff === 0
              ? 'border-success/25 bg-success-soft'
              : shift.reconStatus === 'Discrepancy'
                ? 'border-danger/25 bg-danger-soft'
                : 'border-warn-amber/30 bg-warn-soft',
        )}
      >
        <div className="mb-2.5 text-[13px] font-semibold">{t('shifts.detail.cashRecon')}</div>
        {reconciled ? (
          <>
            <ReconRow label={t('shifts.detail.expectedCash')} value={formatSum(shift.expectedCash)} />
            <ReconRow label={t('shifts.detail.countedCash')} value={formatSum(shift.countedCash ?? 0)} />
            <div className="mt-2 flex items-center justify-between border-t border-hairline/60 pt-2.5 text-[13.5px]">
              <span className="font-semibold">{t('shifts.detail.discrepancy')}</span>
              <span className={cn('font-bold nums', diffTone)}>
                {diff === 0 ? '0' : `${diff > 0 ? '+' : ''}${formatSum(diff)}`}
              </span>
            </div>
          </>
        ) : (
          <p className="text-[12.5px] text-muted-2">{t('shifts.detail.notClosed')}</p>
        )}
      </div>
    </Modal>
  );
}

function ReconRow({ label, value }: { label: string; value: string }) {
  const { t } = useTranslation();
  return (
    <div className="mb-1 flex items-center justify-between text-[13px]">
      <span className="text-muted">{label}</span>
      <span className="font-semibold nums">
        {value} <span className="text-[11px] font-normal text-muted-2">{t('common.currency')}</span>
      </span>
    </div>
  );
}
