import { useState } from 'react';
import { useQuery, useMutation, useQueryClient } from '@tanstack/react-query';
import { useTranslation } from 'react-i18next';
import { Lock, Unlock, Banknote } from 'lucide-react';
import { PageHeader, Button, Card, Badge, Spinner } from '@/shared/ui';
import { cn } from '@/shared/lib/cn';
import { formatSum, formatTime, formatShortDate } from '@/shared/lib/format';
import { useAuth } from '@/shared/auth/useAuth';
import { PERMISSIONS, ROLES } from '@/shared/config/permissions';
import { shiftsApi, type Shift, type ShiftRange } from '@/features/shifts/api';
import { CloseShiftModal } from '@/features/shifts/CloseShiftModal';
import { WithdrawModal } from '@/features/shifts/WithdrawModal';

const STATUS: Record<string, { key: 'open' | 'balanced' | 'discrepancy'; tone: 'success' | 'warn' | 'danger' | 'neutral' }> = {
  Open: { key: 'open', tone: 'neutral' },
  Balanced: { key: 'balanced', tone: 'success' },
  Discrepancy: { key: 'discrepancy', tone: 'danger' },
};

const RANGES: ShiftRange[] = ['week', 'month', 'all'];
const HISTORY_GRID = 'grid-cols-[1.1fr_1.1fr_1fr_1fr_1fr_70px_100px]';

/** Seller's own shift: open/close, per-tender breakdown, and self-service history. */
export default function SellerShiftsPage() {
  const { t, i18n } = useTranslation();
  const { hasPermission, hasRole } = useAuth();
  const canManageCash = hasPermission(PERMISSIONS.cashregister.manage);
  const isOwner = hasRole(ROLES.Owner, ROLES.SuperAdmin);
  const qc = useQueryClient();
  const [closing, setClosing] = useState<Shift | null>(null);
  const [withdrawOpen, setWithdrawOpen] = useState(false);
  const [range, setRange] = useState<ShiftRange>('week');

  const currentQuery = useQuery({ queryKey: ['shift-current'], queryFn: shiftsApi.current });
  // Self-service: /Shifts (market-wide) needs users.shift, which sellers lack.
  const historyQuery = useQuery({
    queryKey: ['shift-my', range],
    queryFn: () => shiftsApi.myHistory(range),
  });

  const openMutation = useMutation({
    mutationFn: shiftsApi.open,
    onSuccess: () => {
      void qc.invalidateQueries({ queryKey: ['shift-current'] });
      void qc.invalidateQueries({ queryKey: ['shift-my'] });
    },
  });

  const current = currentQuery.data;
  const history = historyQuery.data;
  const avgCurrent = current && current.checkCount > 0 ? Math.round(current.revenue / current.checkCount) : 0;

  return (
    <>
      <PageHeader
        title={t('seller.nav.shifts')}
        actions={
          <>
            {canManageCash && (
              <Button variant="secondary" onClick={() => setWithdrawOpen(true)}>
                <Banknote size={15} />
                {t('shifts.withdraw.action')}
              </Button>
            )}
            {current ? (
              <Button variant="danger" onClick={() => setClosing(current)}>
                <Lock size={15} />
                {t('shifts.closeShift')}
              </Button>
            ) : (
              <Button loading={openMutation.isPending} onClick={() => openMutation.mutate()}>
                <Unlock size={15} />
                {t('shifts.openShift')}
              </Button>
            )}
          </>
        }
      />

      <div className="mx-auto flex w-full max-w-[1240px] flex-1 flex-col gap-[18px] p-8">
        {/* Current shift */}
        {currentQuery.isLoading ? (
          <Card className="flex items-center justify-center py-16 text-primary">
            <Spinner size={24} />
          </Card>
        ) : current ? (
          <Card className="p-6">
            <div className="mb-5 flex items-center justify-between">
              <div className="flex items-center gap-3">
                <h2 className="text-[16px] font-semibold">
                  {t('shifts.currentShift')}
                  {current.shiftNumber > 0 && <span className="nums"> №{current.shiftNumber}</span>}
                  {' · '}
                  {formatShortDate(current.openedAt, i18n.language)}
                </h2>
                <Badge tone="success" className="gap-1.5">
                  <span className="h-[7px] w-[7px] rounded-full bg-success" />
                  {t('shifts.open')} · {formatTime(current.openedAt)}
                </Badge>
              </div>
              <span className="text-[13px] text-muted">
                {t('shifts.cashier')}: <span className="font-medium text-text">{current.cashierName}</span>
              </span>
            </div>

            {/* Per-tender breakdown — matches the design's 5 tiles. */}
            <div className="grid grid-cols-5 gap-4">
              <Metric label={t('sales.stats.sum')} value={current.revenue} hint={`${current.checkCount}`} />
              <Metric label={t('pos.payment.cash')} value={current.cashIn} hint={`${current.cashCount}`} positive />
              <Metric label={t('pos.payment.card')} value={current.cardIn} hint={`${current.cardCount}`} />
              <Metric label={t('pos.payment.debt')} value={current.debtIn} hint={`${current.debtCount}`} tone="warn" />
              <Metric label={t('sales.stats.avg')} value={avgCurrent} highlight />
            </div>

            {/* Drawer reconciliation + returns */}
            <div className="mt-4 grid grid-cols-4 gap-4 border-t border-hairline pt-4">
              <Metric label={t('shifts.metrics.opening')} value={current.openingCash} small />
              <Metric label={t('shifts.metrics.withdrawals')} value={-current.withdrawals} small />
              <Metric
                label={t('sales.status.returned')}
                value={-current.returnAmount}
                hint={`${current.returnCount}`}
                small
              />
              <Metric label={t('shifts.metrics.expected')} value={current.expectedCash} small highlight />
            </div>
          </Card>
        ) : (
          <Card className="flex flex-col items-center gap-3 py-14 text-muted-2">
            <Lock size={26} />
            <p className="text-[14px]">{t('shifts.noOpenShift')}</p>
          </Card>
        )}

        {/* History */}
        <Card className="overflow-hidden">
          <div className="flex items-center justify-between border-b border-hairline px-6 py-4">
            <span className="text-[15px] font-semibold">{t('shifts.history')}</span>
            <div className="inline-flex rounded-input bg-hairline p-1">
              {RANGES.map((r) => (
                <button
                  key={r}
                  type="button"
                  onClick={() => setRange(r)}
                  className={cn(
                    'rounded-md px-4 py-1.5 text-[13px] font-medium transition-colors',
                    range === r ? 'bg-surface text-text shadow-card' : 'text-muted hover:text-text',
                  )}
                >
                  {t(`seller.shifts.range.${r}` as never)}
                </button>
              ))}
            </div>
          </div>

          <div className={cn('grid items-center gap-3 border-b border-hairline bg-bg/40 px-6 py-3 text-[11.5px] font-semibold tracking-[0.4px] text-muted-2', HISTORY_GRID)}>
            <span>{t('shifts.cols.date')}</span>
            <span>{t('shifts.cols.period')}</span>
            <span className="text-right">{t('shifts.cols.revenue')}</span>
            <span className="text-right">{t('pos.payment.cash')}</span>
            <span className="text-right">{t('pos.payment.card')}</span>
            <span className="text-center">{t('shifts.cols.checks')}</span>
            <span className="text-right">{t('shifts.cols.status')}</span>
          </div>

          {historyQuery.isLoading ? (
            <div className="flex justify-center py-16 text-primary">
              <Spinner size={22} />
            </div>
          ) : history && history.items.length > 0 ? (
            <>
              {history.items.map((s) => (
                <HistoryRow key={s.id} shift={s} lang={i18n.language} />
              ))}
              <div className={cn('grid items-center gap-3 bg-bg/60 px-6 py-3 text-[12.5px] font-semibold', HISTORY_GRID)}>
                <span className="col-span-2 text-muted">{t('seller.shifts.total')}</span>
                <span className="text-right nums">{formatSum(history.totalRevenue)}</span>
                <span />
                <span />
                <span className="text-center nums">{history.totalChecks}</span>
                <span className="text-right text-muted nums">{formatSum(history.avgCheck)}</span>
              </div>
            </>
          ) : (
            <div className="py-16 text-center text-[14px] text-muted-2">{t('shifts.empty')}</div>
          )}
        </Card>
      </div>

      <CloseShiftModal shift={closing} onClose={() => setClosing(null)} />
      <WithdrawModal open={withdrawOpen} onClose={() => setWithdrawOpen(false)} isOwner={isOwner} />
    </>
  );
}

function Metric({
  label,
  value,
  hint,
  positive,
  highlight,
  small,
  tone,
}: {
  label: string;
  value: number;
  hint?: string;
  positive?: boolean;
  highlight?: boolean;
  small?: boolean;
  tone?: 'warn';
}) {
  return (
    <div className={cn('rounded-card border px-4 py-3.5', highlight ? 'border-primary/25 bg-primary-soft' : 'border-border bg-bg/40')}>
      <div className="flex items-baseline justify-between gap-2">
        <span className="text-[12px] text-muted">{label}</span>
        {hint && <span className="text-[11px] text-muted-2 nums">{hint}</span>}
      </div>
      <div
        className={cn(
          'mt-1.5 font-bold nums',
          small ? 'text-[16px]' : 'text-[19px]',
          highlight
            ? 'text-primary-hover'
            : tone === 'warn'
              ? 'text-warn-text'
              : positive && value > 0
                ? 'text-success'
                : value < 0
                  ? 'text-danger'
                  : 'text-text',
        )}
      >
        {value > 0 && positive ? '+ ' : ''}
        {formatSum(value)}
      </div>
    </div>
  );
}

function HistoryRow({ shift: s, lang }: { shift: Shift; lang: string }) {
  const { t } = useTranslation();
  const st = STATUS[s.reconStatus] ?? STATUS.Open!;
  return (
    <div className={cn('grid items-center gap-3 border-b border-hairline px-6 py-3.5 text-[13px] last:border-0 hover:bg-bg/40', HISTORY_GRID)}>
      <span className="font-medium">{formatShortDate(s.openedAt, lang)}</span>
      <span className="text-muted-2 nums">
        {formatTime(s.openedAt)}–{s.closedAt ? formatTime(s.closedAt) : '…'}
      </span>
      <span className="text-right font-semibold nums">{formatSum(s.revenue)}</span>
      <span className="text-right text-muted nums">{formatSum(s.cashIn)}</span>
      <span className="text-right text-muted nums">{formatSum(s.cardIn)}</span>
      <span className="text-center text-muted nums">{s.checkCount}</span>
      <span className="text-right">
        <Badge tone={st.tone}>{t(`shifts.status.${st.key}`)}</Badge>
      </span>
    </div>
  );
}
