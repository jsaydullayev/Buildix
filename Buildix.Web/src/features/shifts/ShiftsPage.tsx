import { useState } from 'react';
import { useQuery, useMutation, useQueryClient } from '@tanstack/react-query';
import { useTranslation } from 'react-i18next';
import { Lock, Unlock, Check, Banknote, ChevronRight } from 'lucide-react';
import { PageHeader, Button, Card, Badge, Spinner } from '@/shared/ui';
import { cn } from '@/shared/lib/cn';
import { formatSum, formatTime, formatShortDate, formatRelative } from '@/shared/lib/format';
import { useAuth } from '@/shared/auth/useAuth';
import { PERMISSIONS, ROLES } from '@/shared/config/permissions';
import { employeesApi } from '@/features/employees/api';
import { shiftsApi, cashApi, type Shift, type AttendanceRow } from './api';
import { CloseShiftModal } from './CloseShiftModal';
import { WithdrawModal } from './WithdrawModal';
import { ShiftDetailModal } from './ShiftDetailModal';

type ShiftsTab = 'journal' | 'attendance';
type JournalFilter = 'all' | 'open' | 'discrepancy';

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
  // A different cashier's open shift being force-closed (vs. `closing` = own).
  const [forceClosing, setForceClosing] = useState<Shift | null>(null);
  const [withdrawOpen, setWithdrawOpen] = useState(false);
  const [historyUser, setHistoryUser] = useState<string>('');
  const [tab, setTab] = useState<ShiftsTab>('journal');
  const [journalFilter, setJournalFilter] = useState<JournalFilter>('all');
  const [attRange, setAttRange] = useState<'week' | 'month'>('month');
  const [detail, setDetail] = useState<Shift | null>(null);

  const currentQuery = useQuery({ queryKey: ['shift-current'], queryFn: shiftsApi.current });
  const historyQuery = useQuery({
    queryKey: ['shift-history', historyUser],
    queryFn: () => shiftsApi.history(50, historyUser || null),
    enabled: canViewHistory,
  });
  const usersQuery = useQuery({
    queryKey: ['sellers-filter'],
    queryFn: () => employeesApi.list(),
    enabled: canViewHistory && hasPermission(PERMISSIONS.users.access),
  });
  const pendingQuery = useQuery({
    queryKey: ['withdrawals', 'Pending'],
    queryFn: () => cashApi.withdrawals('Pending'),
    enabled: canCash,
  });
  const attendanceQuery = useQuery({
    queryKey: ['shift-attendance', attRange],
    queryFn: () => shiftsApi.attendance(attRange),
    enabled: canViewHistory && tab === 'attendance',
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
            {/* Терминал and Click are split apart — they settle to different
                accounts, so a merged "Картой" figure could not be reconciled
                against either statement. cardIn is still their sum. */}
            <div className="grid grid-cols-6 gap-4">
              <Metric label={t('shifts.metrics.opening')} value={current.openingCash} />
              <Metric label={t('shifts.metrics.cashIn')} value={current.cashIn} positive />
              <Metric label={t('shifts.metrics.terminal')} value={current.terminalIn} />
              <Metric label={t('shifts.metrics.click')} value={current.clickIn} />
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

        {/* Журнал смен / Посещаемость */}
        {canViewHistory && (
          <>
            <div className="flex items-center gap-1 rounded-input border border-input-border bg-surface p-1 self-start">
              {(['journal', 'attendance'] as const).map((tk) => (
                <button
                  key={tk}
                  type="button"
                  onClick={() => setTab(tk)}
                  className={cn(
                    'rounded-[7px] px-4 py-2 text-[13px] font-medium transition-colors',
                    tab === tk ? 'bg-primary text-white' : 'text-muted hover:text-text',
                  )}
                >
                  {t(`shifts.tabs.${tk}`)}
                </button>
              ))}
            </div>

            {tab === 'journal' ? (
              <div className="grid grid-cols-[2.2fr_1fr] items-start gap-[18px]">
                <Card className="overflow-hidden">
                  <div className="flex items-center justify-between gap-3 border-b border-hairline px-6 py-4">
                    <div className="flex items-center gap-1.5">
                      {(['all', 'open', 'discrepancy'] as const).map((f) => (
                        <button
                          key={f}
                          type="button"
                          onClick={() => setJournalFilter(f)}
                          className={cn(
                            'rounded-pill border px-3 py-1.5 text-[12px] font-medium transition-colors',
                            journalFilter === f
                              ? 'border-primary bg-primary text-white'
                              : 'border-input-border bg-surface text-muted hover:text-text',
                          )}
                        >
                          {t(`shifts.filter.${f}`)}
                        </button>
                      ))}
                    </div>
                    {hasPermission(PERMISSIONS.users.access) && (
                      <select
                        value={historyUser}
                        onChange={(e) => setHistoryUser(e.target.value)}
                        className="h-9 rounded-input border border-input-border bg-surface px-3 text-[13px] outline-none focus:border-primary"
                      >
                        <option value="">{t('sales.allSellers')}</option>
                        {(usersQuery.data ?? []).map((u) => (
                          <option key={u.id} value={u.id}>
                            {u.fullName}
                          </option>
                        ))}
                      </select>
                    )}
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
                  ) : (
                    (() => {
                      const rows = (historyQuery.data ?? []).filter((s) =>
                        journalFilter === 'open' ? s.isOpen : journalFilter === 'discrepancy' ? s.reconStatus === 'Discrepancy' : true,
                      );
                      return rows.length > 0 ? (
                        rows.map((s) => (
                          <HistoryRow
                            key={s.id}
                            shift={s}
                            lang={i18n.language}
                            onOpen={() => setDetail(s)}
                            onForceClose={s.isOpen ? () => setForceClosing(s) : undefined}
                          />
                        ))
                      ) : (
                        <div className="py-16 text-center text-[14px] text-muted-2">{t('shifts.empty')}</div>
                      );
                    })()
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
            ) : (
              <AttendanceCard
                data={attendanceQuery.data}
                loading={attendanceQuery.isLoading}
                range={attRange}
                onRange={setAttRange}
              />
            )}
          </>
        )}
      </div>

      <CloseShiftModal shift={closing} onClose={() => setClosing(null)} />
      <CloseShiftModal shift={forceClosing} forced onClose={() => setForceClosing(null)} />
      <WithdrawModal open={withdrawOpen} onClose={() => setWithdrawOpen(false)} isOwner={isOwner} />
      <ShiftDetailModal shift={detail} lang={i18n.language} onClose={() => setDetail(null)} />
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

function HistoryRow({
  shift: s,
  lang,
  onOpen,
  onForceClose,
}: {
  shift: Shift;
  lang: string;
  onOpen: () => void;
  onForceClose?: () => void;
}) {
  const { t } = useTranslation();
  const st = STATUS[s.reconStatus] ?? STATUS.Open!;
  const period = `${formatTime(s.openedAt)}–${s.closedAt ? formatTime(s.closedAt) : '…'}`;
  return (
    <div
      role="button"
      tabIndex={0}
      onClick={onOpen}
      onKeyDown={(e) => (e.key === 'Enter' || e.key === ' ') && (e.preventDefault(), onOpen())}
      className="grid grid-cols-shifts items-center gap-3 border-b border-hairline px-6 py-3.5 text-[13px] last:border-0 cursor-pointer hover:bg-bg/40"
    >
      <span className="flex items-center gap-1.5 font-medium">
        <ChevronRight size={13} className="flex-none text-muted-2" />
        {formatShortDate(s.openedAt, lang)}
      </span>
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
      {/* Ochiq smenani majburiy yopish — kassir uni yopmasdan ketgan holat. */}
      {s.isOpen && onForceClose ? (
        <button
          type="button"
          onClick={(e) => {
            e.stopPropagation();
            onForceClose();
          }}
          className="flex items-center gap-1.5 rounded-input border border-warn/40 bg-warn-soft px-2.5 py-1 text-[12px] font-medium text-warn-text hover:border-warn"
        >
          <Lock size={12} />
          {t('shifts.forceClose.action')}
        </button>
      ) : (
        <span>
          <Badge tone={st.tone}>{t(`shifts.status.${st.key}`)}</Badge>
        </span>
      )}
    </div>
  );
}

/** Посещаемость — davomat jadvali (smena vaqtlaridan hisoblangan). */
function AttendanceCard({
  data,
  loading,
  range,
  onRange,
}: {
  data: import('./api').Attendance | undefined;
  loading: boolean;
  range: 'week' | 'month';
  onRange: (r: 'week' | 'month') => void;
}) {
  const { t } = useTranslation();
  const planHours = data?.planHours ?? 0;
  return (
    <Card className="overflow-hidden">
      <div className="flex items-center justify-between gap-3 border-b border-hairline px-6 py-4">
        <div>
          <div className="text-[15px] font-semibold">{t('shifts.attendance.title')}</div>
          {data && (
            <div className="mt-0.5 text-[12px] text-muted-2">
              {t('shifts.attendance.scheduleHint', {
                from: data.scheduleFrom,
                to: data.scheduleTo,
                plan: planHours,
              })}
            </div>
          )}
        </div>
        <div className="flex items-center gap-1 rounded-input border border-input-border bg-surface p-0.5">
          {(['week', 'month'] as const).map((r) => (
            <button
              key={r}
              type="button"
              onClick={() => onRange(r)}
              className={cn(
                'rounded-[6px] px-3 py-1.5 text-[12.5px] font-medium transition-colors',
                range === r ? 'bg-primary text-white' : 'text-muted hover:text-text',
              )}
            >
              {t(`shifts.attendance.range.${r}`)}
            </button>
          ))}
        </div>
      </div>
      <div className="grid grid-cols-attendance items-center gap-3 border-b border-hairline bg-bg/40 px-6 py-3 text-[11.5px] font-semibold tracking-[0.4px] text-muted-2">
        <span>{t('shifts.attendance.cols.employee')}</span>
        <span className="text-right">{t('shifts.attendance.cols.shifts')}</span>
        <span className="text-right">{t('shifts.attendance.cols.days')}</span>
        <span className="text-right">{t('shifts.attendance.cols.hours')}</span>
        <span className="text-right">{t('shifts.attendance.cols.avg')}</span>
        <span className="text-right">{t('shifts.attendance.cols.late')}</span>
        <span>{t('shifts.attendance.cols.plan')}</span>
      </div>
      {loading ? (
        <div className="flex justify-center py-16 text-primary">
          <Spinner size={22} />
        </div>
      ) : data && data.items.length > 0 ? (
        data.items.map((a) => <AttendanceRowView key={a.userId} row={a} planHours={planHours} />)
      ) : (
        <div className="py-16 text-center text-[14px] text-muted-2">{t('shifts.attendance.empty')}</div>
      )}
    </Card>
  );
}

function initials(name: string): string {
  const parts = name.trim().split(/\s+/);
  return ((parts[0]?.[0] ?? '') + (parts[1]?.[0] ?? '')).toUpperCase();
}

function AttendanceRowView({ row: a, planHours }: { row: AttendanceRow; planHours: number }) {
  const { t } = useTranslation();
  const pct = planHours > 0 ? Math.min(100, Math.round((a.totalHours / planHours) * 100)) : 0;
  const barTone = pct >= 95 ? 'bg-success' : pct >= 85 ? 'bg-warn-amber' : 'bg-danger';
  const pctTone = pct >= 95 ? 'text-success' : pct >= 85 ? 'text-warn-strong' : 'text-danger';
  const lateTone = a.lateCount === 0 ? 'text-success' : a.lateCount > 2 ? 'text-danger' : 'text-warn-strong';
  return (
    <div className="grid grid-cols-attendance items-center gap-3 border-b border-hairline px-6 py-3.5 text-[13px] last:border-0 hover:bg-bg/40">
      <span className="flex min-w-0 items-center gap-2.5">
        <span className="flex h-8 w-8 flex-none items-center justify-center rounded-pill bg-primary-soft text-[11px] font-semibold text-primary">
          {initials(a.name)}
        </span>
        <span className="truncate font-medium">{a.name}</span>
      </span>
      <span className="text-right text-muted nums">{a.shiftCount}</span>
      <span className="text-right text-muted nums">{a.dayCount}</span>
      <span className="text-right font-semibold nums">{t('shifts.attendance.hoursValue', { value: a.totalHours })}</span>
      <span className="text-right text-muted nums">{t('shifts.attendance.hoursValue', { value: a.avgShiftHours })}</span>
      <span className={cn('text-right font-medium nums', lateTone)}>
        {a.lateCount === 0 ? t('shifts.attendance.noLate') : t('shifts.attendance.lateTimes', { count: a.lateCount })}
      </span>
      <span className="flex items-center gap-2">
        <span className="h-1.5 flex-1 rounded-pill bg-hairline">
          <span className={cn('block h-1.5 rounded-pill', barTone)} style={{ width: `${pct}%` }} />
        </span>
        <span className={cn('w-9 flex-none text-right text-[12px] font-semibold nums', pctTone)}>{pct}%</span>
      </span>
    </div>
  );
}
