import { useState } from 'react';
import { useQuery, useMutation, useQueryClient } from '@tanstack/react-query';
import { useTranslation } from 'react-i18next';
import { Lock, Unlock, Check, Banknote } from 'lucide-react';
import { PageHeader, Button, Card, Badge, Spinner } from '@/shared/ui';
import { cn } from '@/shared/lib/cn';
import { formatSum, formatTime, formatShortDate, formatRelative } from '@/shared/lib/format';
import { useAuth } from '@/shared/auth/useAuth';
import { PERMISSIONS, ROLES } from '@/shared/config/permissions';
import { shiftsApi, cashApi, type Shift } from './api';
import { CloseShiftModal } from './CloseShiftModal';
import { WithdrawModal } from './WithdrawModal';

const STATUS: Record<string, { key: 'open' | 'balanced' | 'discrepancy'; tone: 'success' | 'warn' | 'danger' | 'neutral' }> = {
  Open: { key: 'open', tone: 'neutral' },
  Balanced: { key: 'balanced', tone: 'success' },
  Discrepancy: { key: 'discrepancy', tone: 'danger' },
};

export default function ShiftsPage() {
  const { t, i18n } = useTranslation();
  const { hasPermission, hasRole } = useAuth();
  const canViewHistory = hasPermission(PERMISSIONS.users.shift);
  const canCash = hasPermission(PERMISSIONS.cashregister.access);
  const canManageCash = hasPermission(PERMISSIONS.cashregister.manage);
  const isOwner = hasRole(ROLES.Owner, ROLES.SuperAdmin);
  const qc = useQueryClient();
  const [closing, setClosing] = useState<Shift | null>(null);
  const [withdrawOpen, setWithdrawOpen] = useState(false);

  const currentQuery = useQuery({ queryKey: ['shift-current'], queryFn: shiftsApi.current });
  const historyQuery = useQuery({
    queryKey: ['shift-history'],
    queryFn: () => shiftsApi.history(20),
    enabled: canViewHistory,
  });
  const pendingQuery = useQuery({
    queryKey: ['withdrawals', 'Pending'],
    queryFn: () => cashApi.withdrawals('Pending'),
    enabled: canCash,
  });

  const openMutation = useMutation({
    mutationFn: shiftsApi.open,
    onSuccess: () => {
      void qc.invalidateQueries({ queryKey: ['shift-current'] });
      void qc.invalidateQueries({ queryKey: ['shift-history'] });
    },
  });

  const decide = useMutation({
    mutationFn: ({ id, approve }: { id: string; approve: boolean }) =>
      approve ? cashApi.approve(id) : cashApi.reject(id),
    onSuccess: () => {
      void qc.invalidateQueries({ queryKey: ['withdrawals'] });
      void qc.invalidateQueries({ queryKey: ['shift-current'] });
    },
  });

  const current = currentQuery.data;
  const pending = pendingQuery.data ?? [];

  return (
    <>
      <PageHeader
        title={t('shifts.title')}
        subtitle={t('shifts.subtitle')}
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

      <div className="flex flex-1 flex-col gap-[18px] p-8">
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
                  {t('shifts.currentShift')} · {formatShortDate(current.openedAt, i18n.language)}
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
            <div className="grid grid-cols-5 gap-4">
              <Metric label={t('shifts.metrics.opening')} value={current.openingCash} />
              <Metric label={t('shifts.metrics.cashIn')} value={current.cashIn} positive />
              <Metric label={t('shifts.metrics.cardIn')} value={current.cardIn} />
              <Metric label={t('shifts.metrics.withdrawals')} value={-current.withdrawals} />
              <Metric label={t('shifts.metrics.expected')} value={current.expectedCash} highlight />
            </div>
          </Card>
        ) : (
          <Card className="flex flex-col items-center gap-3 py-14 text-muted-2">
            <Lock size={26} />
            <p className="text-[14px]">{t('shifts.noOpenShift')}</p>
          </Card>
        )}

        {/* Pending withdrawal requests */}
        {canCash && pending.length > 0 && (
          <Card className="overflow-hidden">
            <div className="border-b border-hairline px-6 py-4 text-[15px] font-semibold">
              {t('shifts.withdraw.pendingTitle')}
              <Badge tone="warn" className="ml-2">{pending.length}</Badge>
            </div>
            {pending.map((w) => (
              <div
                key={w.id}
                className="flex items-center justify-between gap-4 border-b border-hairline px-6 py-3.5 last:border-0"
              >
                <div className="min-w-0">
                  <div className="text-[14px] font-semibold nums">
                    {formatSum(w.amount)} <span className="text-[12px] font-normal text-muted-2">{t('common.currency')}</span>
                  </div>
                  <div className="truncate text-[12px] text-muted-2">
                    {t('shifts.withdraw.from', { name: w.requestedByName ?? '—' })}
                    {' · '}
                    {formatRelative(w.requestedAt, i18n.language)}
                    {w.comment && ` · ${w.comment}`}
                  </div>
                </div>
                {isOwner ? (
                  <div className="flex flex-none gap-2">
                    <Button
                      size="sm"
                      variant="danger"
                      loading={decide.isPending && decide.variables?.id === w.id && !decide.variables?.approve}
                      onClick={() => decide.mutate({ id: w.id, approve: false })}
                    >
                      {t('shifts.withdraw.reject')}
                    </Button>
                    <Button
                      size="sm"
                      loading={decide.isPending && decide.variables?.id === w.id && decide.variables?.approve}
                      onClick={() => decide.mutate({ id: w.id, approve: true })}
                    >
                      {t('shifts.withdraw.approve')}
                    </Button>
                  </div>
                ) : (
                  <Badge tone="warn">{t('shifts.withdraw.status.pending')}</Badge>
                )}
              </div>
            ))}
          </Card>
        )}

        {/* History + rules */}
        {canViewHistory && (
          <div className="grid grid-cols-[2.2fr_1fr] items-start gap-[18px]">
            <Card className="overflow-hidden">
              <div className="border-b border-hairline px-6 py-4 text-[15px] font-semibold">
                {t('shifts.history')}
              </div>
              <div className="grid grid-cols-shifts items-center gap-3 border-b border-hairline bg-bg/40 px-6 py-3 text-[11.5px] font-semibold tracking-[0.4px] text-muted-2">
                <span>{t('shifts.cols.date')}</span>
                <span>{t('shifts.cols.cashier')}</span>
                <span>{t('shifts.cols.period')}</span>
                <span className="text-center">{t('shifts.cols.checks')}</span>
                <span className="text-right">{t('shifts.cols.revenue')}</span>
                <span className="text-right">{t('shifts.cols.discrepancy')}</span>
                <span>{t('shifts.cols.status')}</span>
              </div>
              {historyQuery.isLoading ? (
                <div className="flex justify-center py-16 text-primary">
                  <Spinner size={22} />
                </div>
              ) : historyQuery.data && historyQuery.data.length > 0 ? (
                historyQuery.data.map((s) => <HistoryRow key={s.id} shift={s} lang={i18n.language} />)
              ) : (
                <div className="py-16 text-center text-[14px] text-muted-2">{t('shifts.empty')}</div>
              )}
            </Card>

            <Card className="p-5">
              <h3 className="mb-4 text-[15px] font-semibold">{t('shifts.rules.title')}</h3>
              <div className="flex flex-col gap-3">
                {(['r1', 'r2', 'r3', 'r4'] as const).map((r) => (
                  <div key={r} className="flex items-start gap-2.5 text-[13px] text-label">
                    <Check size={16} className="mt-0.5 flex-none text-success" />
                    <span>{t(`shifts.rules.${r}`)}</span>
                  </div>
                ))}
              </div>
            </Card>
          </div>
        )}
      </div>

      <CloseShiftModal shift={closing} onClose={() => setClosing(null)} />
      <WithdrawModal open={withdrawOpen} onClose={() => setWithdrawOpen(false)} isOwner={isOwner} />
    </>
  );
}

function Metric({
  label,
  value,
  positive,
  highlight,
}: {
  label: string;
  value: number;
  positive?: boolean;
  highlight?: boolean;
}) {
  return (
    <div className={cn('rounded-card border px-4 py-4', highlight ? 'border-primary/25 bg-primary-soft' : 'border-border bg-bg/40')}>
      <div className="text-[12px] text-muted">{label}</div>
      <div
        className={cn(
          'mt-1.5 text-[19px] font-bold nums',
          highlight ? 'text-primary-hover' : positive && value > 0 ? 'text-success' : value < 0 ? 'text-danger' : 'text-text',
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
  const period = `${formatTime(s.openedAt)}–${s.closedAt ? formatTime(s.closedAt) : '…'}`;
  return (
    <div className="grid grid-cols-shifts items-center gap-3 border-b border-hairline px-6 py-3.5 text-[13px] last:border-0 hover:bg-bg/40">
      <span className="font-medium">{formatShortDate(s.openedAt, lang)}</span>
      <span className="truncate">{s.cashierName}</span>
      <span className="text-muted-2 nums">{period}</span>
      <span className="text-center text-muted nums">{s.checkCount}</span>
      <span className="text-right font-semibold nums">{formatSum(s.revenue)}</span>
      <span
        className={cn(
          'text-right nums',
          s.reconStatus === 'Discrepancy' ? 'font-semibold text-danger' : 'text-muted-2',
        )}
      >
        {s.isOpen ? '—' : s.discrepancy === 0 ? '0' : `${s.discrepancy > 0 ? '+' : ''}${formatSum(s.discrepancy)}`}
      </span>
      <span>
        <Badge tone={st.tone}>{t(`shifts.status.${st.key}`)}</Badge>
      </span>
    </div>
  );
}
