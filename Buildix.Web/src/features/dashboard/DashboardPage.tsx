import { useMemo, type ReactNode } from 'react';
import { useNavigate, useParams } from 'react-router-dom';
import { useQuery } from '@tanstack/react-query';
import { useTranslation } from 'react-i18next';
import type { TFunction } from 'i18next';
import { format, startOfDay, isSameDay, parseISO, differenceInCalendarDays } from 'date-fns';
import { Plus } from 'lucide-react';
import {
  BarChart,
  Bar,
  Cell,
  XAxis,
  Tooltip,
  ResponsiveContainer,
  type TooltipProps,
} from 'recharts';
import { PageHeader, Button, Card, StatCard, Badge, Spinner } from '@/shared/ui';
import { cn } from '@/shared/lib/cn';
import { formatSum, formatQty, formatFullDate, formatShortDate, formatTime } from '@/shared/lib/format';
import { useAuth } from '@/shared/auth/useAuth';
import { PERMISSIONS } from '@/shared/config/permissions';
import { purchasesApi, type ReorderSuggestion } from '@/features/purchases/api';
import { debtsApi, type DebtorSummary } from '@/features/debts/api';
import { dashboardApi, type DailySale, type WeeklyPoint } from './api';

const PAY_BADGE: Record<string, { key: string; tone: 'success' | 'info' | 'warn' | 'neutral' }> = {
  cash: { key: 'cash', tone: 'success' },
  card: { key: 'card', tone: 'info' },
  click: { key: 'click', tone: 'info' },
  transfer: { key: 'transfer', tone: 'info' },
  debt: { key: 'debt', tone: 'warn' },
};

function pct(value: number): string {
  return `${(value * 100).toFixed(1).replace('.', ',')}%`;
}

export default function DashboardPage() {
  const { t, i18n } = useTranslation();
  const { subdomain } = useParams();
  const navigate = useNavigate();
  const { hasPermission } = useAuth();

  const canCash = hasPermission(PERMISSIONS.cashregister.access);
  const canReports = hasPermission(PERMISSIONS.reports.access);
  const canProfit = hasPermission(PERMISSIONS.data.profit);
  const canSales = hasPermission(PERMISSIONS.sales.access);
  const canShift = hasPermission(PERMISSIONS.users.shift);
  const canZakup = hasPermission(PERMISSIONS.zakup.access);
  const canDebts = hasPermission(PERMISSIONS.debts.access);
  const canCreateSale = hasPermission(PERMISSIONS.sales.create);

  // H-13: send the Tashkent calendar date (no time/offset) so the server's
  // LocalDayToUtcRange resolves the correct business day. A UTC instant would
  // shift Tashkent 00:00–05:00 to the previous UTC day on a UTC server.
  const today = useMemo(() => format(new Date(), 'yyyy-MM-dd'), []);

  const todayQuery = useQuery({ queryKey: ['dash-today'], queryFn: dashboardApi.todaySales, enabled: canCash });
  const cashQuery = useQuery({ queryKey: ['dash-cash'], queryFn: dashboardApi.cashRegister, enabled: canCash });
  const profitQuery = useQuery({ queryKey: ['dash-profit'], queryFn: dashboardApi.profitSummary, enabled: canProfit });
  const summaryQuery = useQuery({ queryKey: ['dash-summary'], queryFn: dashboardApi.summary, enabled: canReports });
  const weeklyQuery = useQuery({
    queryKey: ['dash-weekly'],
    queryFn: () => dashboardApi.weeklySeries(7, true),
    enabled: canReports,
  });
  const salesQuery = useQuery({
    queryKey: ['dash-daily-sales', today],
    queryFn: () => dashboardApi.dailySales(today),
    enabled: canSales,
  });
  const shiftQuery = useQuery({ queryKey: ['dash-shift'], queryFn: dashboardApi.currentShift, enabled: canShift });
  const lowStockQuery = useQuery({
    queryKey: ['dash-lowstock'],
    queryFn: () => purchasesApi.reorderSuggestions(4),
    enabled: canZakup,
  });
  const debtorsQuery = useQuery({
    queryKey: ['dash-debtors'],
    queryFn: () => debtsApi.debtors(),
    enabled: canDebts,
  });

  const todaySales = todayQuery.data;
  const cash = cashQuery.data;
  const summary = summaryQuery.data;
  const weekly = weeklyQuery.data;
  const shift = shiftQuery.data ?? null;

  // Sales growth vs yesterday from the weekly series (last two points).
  const salesGrowth = useMemo(() => {
    const pts = weekly?.points ?? [];
    if (pts.length < 2) return null;
    const prev = pts[pts.length - 2]!.revenue;
    const last = pts[pts.length - 1]!.revenue;
    if (prev <= 0) return null;
    return (last - prev) / prev;
  }, [weekly]);

  // Withdrawals made today, for the cash card hint.
  const todayWithdrawals = useMemo(() => {
    const now = new Date();
    const list = (cash?.withdrawals ?? []).filter((w) => isSameDay(parseISO(w.withdrawalDate), now));
    return { count: list.length, amount: list.reduce((s, w) => s + w.amount, 0) };
  }, [cash]);

  const recentSales = useMemo(() => {
    const list = salesQuery.data?.sales ?? [];
    return [...list].sort((a, b) => b.createdAt.localeCompare(a.createdAt)).slice(0, 5);
  }, [salesQuery.data]);

  const upcoming = useMemo(() => {
    const list = (debtorsQuery.data ?? []).filter((d) => d.nearestDueDate);
    return list
      .sort((a, b) => (a.nearestDueDate! < b.nearestDueDate! ? -1 : 1))
      .slice(0, 4);
  }, [debtorsQuery.data]);

  const margin =
    todaySales && todaySales.totalAmount > 0 && profitQuery.data
      ? profitQuery.data.todayProfit / todaySales.totalAmount
      : null;

  return (
    <>
      <PageHeader
        title={t('dashboard.title')}
        subtitle={formatFullDate(new Date(), i18n.language)}
        actions={
          <>
            {canShift && <ShiftPill isOpen={shift?.isOpen ?? false} openedAt={shift?.openedAt} />}
            {canCreateSale && (
              <Button onClick={() => navigate(`/${subdomain}/pos`)}>
                <Plus size={15} strokeWidth={2.4} />
                {t('dashboard.newSale')}
              </Button>
            )}
          </>
        }
      />

      <div className="flex flex-1 flex-col gap-[18px] p-8">
        {/* Stat cards */}
        <div className="grid grid-cols-4 gap-4">
          {canCash && (
            <StatCard
              label={t('dashboard.stats.salesToday')}
              value={formatSum(todaySales?.totalAmount ?? 0)}
              suffix={t('common.currency')}
              hint={
                <span className="flex items-center gap-1.5">
                  <GrowthTag value={salesGrowth} />
                  <span>
                    {t('dashboard.stats.vsYesterday')} · {t('dashboard.stats.checksCount', { count: todaySales?.totalSales ?? 0 })}
                  </span>
                </span>
              }
            />
          )}

          {canProfit && (
            <StatCard
              label={t('dashboard.stats.profitToday')}
              value={formatSum(profitQuery.data?.todayProfit ?? 0)}
              suffix={t('common.currency')}
              hint={margin !== null ? t('dashboard.stats.margin', { value: pct(margin) }) : undefined}
            />
          )}

          {canCash && (
            <StatCard
              label={t('dashboard.stats.cashInRegister')}
              value={formatSum(cash?.currentBalance ?? 0)}
              suffix={t('common.currency')}
              hint={
                todayWithdrawals.count > 0
                  ? t('dashboard.stats.withdrawalsToday', {
                      count: todayWithdrawals.count,
                      amount: formatSum(todayWithdrawals.amount),
                    })
                  : t('dashboard.stats.noWithdrawals')
              }
            />
          )}

          {canReports && (
            <StatCard
              label={t('dashboard.stats.customerDebts')}
              value={formatSum(summary?.pendingDebtsTotal ?? 0)}
              suffix={t('common.currency')}
              tone={summary && summary.pendingDebtsTotal > 0 ? 'warn' : 'default'}
              hint={
                <span className="flex items-center gap-1.5">
                  {summary && summary.overdueDebtsCount > 0 && (
                    <span className="font-semibold text-danger">
                      {t('dashboard.stats.overdueCount', { count: summary.overdueDebtsCount })}
                    </span>
                  )}
                  <span>· {t('dashboard.stats.totalCustomers', { count: summary?.customerCount ?? 0 })}</span>
                </span>
              }
            />
          )}
        </div>

        <div className="grid grid-cols-[2fr_1fr] items-start gap-[18px]">
          {/* Left column */}
          <div className="flex min-w-0 flex-col gap-[18px]">
            {canReports && <WeeklyChartCard points={weekly?.points ?? []} total={weekly?.currentTotal ?? 0} loading={weeklyQuery.isLoading} />}
            {canSales && <RecentSalesCard sales={recentSales} loading={salesQuery.isLoading} />}
          </div>

          {/* Right column */}
          <div className="flex min-w-0 flex-col gap-[18px]">
            {canZakup && <LowStockCard items={lowStockQuery.data ?? []} loading={lowStockQuery.isLoading} canOrder={hasPermission(PERMISSIONS.zakup.create)} />}
            {canDebts && <UpcomingPaymentsCard debtors={upcoming} loading={debtorsQuery.isLoading} />}
            {canShift && <ShiftCard shift={shift} loading={shiftQuery.isLoading} />}
          </div>
        </div>
      </div>
    </>
  );
}

function ShiftPill({ isOpen, openedAt }: { isOpen: boolean; openedAt?: string }) {
  const { t } = useTranslation();
  if (isOpen) {
    return (
      <span className="inline-flex items-center gap-2 rounded-pill border border-success-border bg-success-soft px-[15px] py-[7px] text-[12.5px] font-semibold text-success-text">
        <span className="h-[7px] w-[7px] rounded-pill bg-success" />
        {t('dashboard.shiftOpenAt', { time: openedAt ? formatTime(openedAt) : '—' })}
      </span>
    );
  }
  return (
    <span className="inline-flex items-center gap-2 rounded-pill border border-border bg-hairline px-[15px] py-[7px] text-[12.5px] font-semibold text-muted">
      <span className="h-[7px] w-[7px] rounded-pill bg-muted-2" />
      {t('dashboard.shiftClosed')}
    </span>
  );
}

function GrowthTag({ value }: { value: number | null }) {
  if (value === null) return null;
  const up = value >= 0;
  return (
    <span className={cn('font-semibold', up ? 'text-success' : 'text-danger')}>
      {up ? '▲' : '▼'} {Math.abs(Math.round(value * 100))}%
    </span>
  );
}

function ChartTooltip({ active, payload, label }: TooltipProps<number, string>) {
  const { t } = useTranslation();
  if (!active || !payload?.length) return null;
  return (
    <div className="rounded-input border border-border bg-surface px-3 py-2 text-[12px] shadow-card">
      <div className="mb-0.5 text-muted-2">{label}</div>
      <div className="font-semibold nums">
        {formatSum(payload[0]!.value ?? 0)} {t('common.currency')}
      </div>
    </div>
  );
}

function WeeklyChartCard({ points, total, loading }: { points: WeeklyPoint[]; total: number; loading: boolean }) {
  const { t, i18n } = useTranslation();
  const data = points.map((p) => ({ label: formatShortDate(p.date, i18n.language), revenue: p.revenue }));
  const lastIdx = data.length - 1;

  return (
    <Card className="p-6">
      <div className="mb-[18px] flex items-baseline justify-between">
        <h3 className="text-[15px] font-semibold">{t('dashboard.chart.title')}</h3>
        <div className="text-[12.5px] text-muted-2">
          {t('dashboard.chart.total')}:{' '}
          <span className="font-semibold text-text nums">
            {formatSum(total)} {t('common.currency')}
          </span>
        </div>
      </div>
      {loading ? (
        <div className="flex h-[170px] items-center justify-center text-primary">
          <Spinner size={22} />
        </div>
      ) : (
        <div className="h-[170px] w-full">
          <ResponsiveContainer width="100%" height="100%">
            <BarChart data={data} margin={{ top: 6, right: 0, left: 0, bottom: 0 }}>
              <XAxis
                dataKey="label"
                tickLine={false}
                axisLine={false}
                tick={{ fontSize: 11, fill: '#94a3b8' }}
                interval={0}
              />
              <Tooltip cursor={{ fill: 'rgba(37,99,235,.06)' }} content={<ChartTooltip />} />
              <Bar dataKey="revenue" radius={[6, 6, 0, 0]} maxBarSize={46}>
                {data.map((_, i) => (
                  <Cell key={i} fill={i === lastIdx ? '#2563eb' : '#bfdbfe'} />
                ))}
              </Bar>
            </BarChart>
          </ResponsiveContainer>
        </div>
      )}
    </Card>
  );
}

function RecentSalesCard({ sales, loading }: { sales: DailySale[]; loading: boolean }) {
  const { t } = useTranslation();
  return (
    <Card className="overflow-hidden">
      <div className="flex items-center justify-between px-6 pb-3.5 pt-[18px]">
        <h3 className="text-[15px] font-semibold">{t('dashboard.recent.title')}</h3>
        <span className="text-[12.5px] font-semibold text-primary">{t('dashboard.recent.all')} →</span>
      </div>
      <div className="grid grid-cols-dashboard-sales items-center gap-[14px] border-t border-hairline bg-bg/40 px-6 py-2.5 text-[11.5px] font-semibold tracking-[0.4px] text-muted-2">
        <span>{t('dashboard.recent.cols.time')}</span>
        <span>{t('dashboard.recent.cols.seller')}</span>
        <span>{t('dashboard.recent.cols.items')}</span>
        <span>{t('dashboard.recent.cols.pay')}</span>
        <span className="text-right">{t('dashboard.recent.cols.sum')}</span>
      </div>
      {loading ? (
        <div className="flex items-center justify-center py-14 text-primary">
          <Spinner size={22} />
        </div>
      ) : sales.length === 0 ? (
        <div className="py-14 text-center text-[14px] text-muted-2">{t('dashboard.recent.empty')}</div>
      ) : (
        sales.map((s) => <RecentSaleRow key={s.id} sale={s} />)
      )}
    </Card>
  );
}

function RecentSaleRow({ sale }: { sale: DailySale }) {
  const { t } = useTranslation();
  const badge = PAY_BADGE[sale.paymentType] ?? { key: sale.paymentType, tone: 'neutral' as const };
  return (
    <div className="grid grid-cols-dashboard-sales items-center gap-[14px] border-t border-hairline px-6 py-3 text-[13px]">
      <span className="text-muted-2 nums">{formatTime(sale.createdAt)}</span>
      <span className="truncate font-medium">{sale.sellerName}</span>
      <span className="truncate text-muted">{sale.customerName ?? t('sales.walkIn')}</span>
      <span>
        <Badge tone={badge.tone}>{t(`sales.payment.${badge.key}` as never)}</Badge>
      </span>
      <span className="text-right font-semibold nums">{formatSum(sale.totalAmount)}</span>
    </div>
  );
}

function LowStockCard({ items, loading, canOrder }: { items: ReorderSuggestion[]; loading: boolean; canOrder: boolean }) {
  const { t } = useTranslation();
  return (
    <Card className="p-[22px]">
      <h3 className="mb-3.5 text-[15px] font-semibold">{t('dashboard.lowStock.title')}</h3>
      {loading ? (
        <div className="flex justify-center py-8 text-primary">
          <Spinner size={20} />
        </div>
      ) : items.length === 0 ? (
        <p className="py-6 text-center text-[13px] text-muted-2">{t('dashboard.lowStock.empty')}</p>
      ) : (
        <>
          <div className="flex flex-col gap-[13px]">
            {items.map((s) => {
              const critical = s.currentQty <= 0 || (s.daysOfCover ?? 99) <= 2;
              const ratio = s.minThreshold > 0 ? s.currentQty / s.minThreshold : 0.5;
              const width = Math.max(4, Math.min(100, ratio * 100));
              return (
                <div key={s.productId}>
                  <div className="mb-1.5 flex justify-between text-[13px]">
                    <span className="truncate pr-2 font-medium">{s.name}</span>
                    <span className={cn('whitespace-nowrap font-semibold', critical ? 'text-danger' : 'text-warn-strong')}>
                      {formatQty(s.currentQty)} {s.unitName}
                    </span>
                  </div>
                  <div className="h-1.5 rounded-pill bg-hairline">
                    <div
                      className={cn('h-1.5 rounded-pill', critical ? 'bg-danger' : 'bg-warn-amber')}
                      style={{ width: `${width}%` }}
                    />
                  </div>
                </div>
              );
            })}
          </div>
          {canOrder && (
            <Button variant="secondary" fullWidth className="mt-4">
              {t('dashboard.lowStock.order')}
            </Button>
          )}
        </>
      )}
    </Card>
  );
}

function dueMeta(due: string, t: TFunction): { label: string; cls: string } {
  const days = differenceInCalendarDays(parseISO(due), startOfDay(new Date()));
  if (days < 0) return { label: t('dashboard.payments.overdueDays', { count: -days }), cls: 'text-danger font-semibold' };
  if (days === 0) return { label: t('dashboard.payments.today'), cls: 'text-warn-strong font-semibold' };
  return { label: t('dashboard.payments.inDays', { count: days }), cls: 'text-muted' };
}

function UpcomingPaymentsCard({ debtors, loading }: { debtors: DebtorSummary[]; loading: boolean }) {
  const { t, i18n } = useTranslation();
  return (
    <Card className="p-[22px]">
      <div className="mb-3.5 flex items-center justify-between">
        <h3 className="text-[15px] font-semibold">{t('dashboard.payments.title')}</h3>
        <span className="text-[12.5px] font-semibold text-primary">{t('dashboard.payments.all')} →</span>
      </div>
      {loading ? (
        <div className="flex justify-center py-8 text-primary">
          <Spinner size={20} />
        </div>
      ) : debtors.length === 0 ? (
        <p className="py-6 text-center text-[13px] text-muted-2">{t('dashboard.payments.empty')}</p>
      ) : (
        <div className="flex flex-col">
          {debtors.map((d) => {
            const meta = dueMeta(d.nearestDueDate!, t);
            return (
              <div
                key={d.customerId}
                className="flex items-center justify-between gap-2.5 border-b border-hairline py-2.5 last:border-0"
              >
                <div className="min-w-0">
                  <div className="truncate text-[13px] font-medium">{d.customerName ?? d.customerPhone}</div>
                  <div className="mt-0.5 text-[11.5px] text-muted-2">
                    {t('dashboard.payments.due', { date: formatShortDate(d.nearestDueDate!, i18n.language) })}
                  </div>
                </div>
                <div className="text-right">
                  <div className={cn('text-[13.5px] font-bold nums', d.isOverdue && 'text-danger')}>
                    {formatSum(d.remainingDebt)}
                  </div>
                  <div className={cn('mt-0.5 text-[11px]', meta.cls)}>{meta.label}</div>
                </div>
              </div>
            );
          })}
        </div>
      )}
    </Card>
  );
}

function ShiftCard({ shift, loading }: { shift: { cashierName: string; openedAt: string; checkCount: number; revenue: number } | null; loading: boolean }) {
  const { t } = useTranslation();
  if (loading) {
    return (
      <Card className="flex justify-center p-[22px] text-primary">
        <Spinner size={20} />
      </Card>
    );
  }
  const avg = shift && shift.checkCount > 0 ? Math.round(shift.revenue / shift.checkCount) : 0;
  return (
    <Card className="p-[22px]">
      <h3 className="mb-3.5 text-[15px] font-semibold">{t('dashboard.shift.title')}</h3>
      {!shift ? (
        <p className="py-4 text-center text-[13px] text-muted-2">{t('dashboard.shift.noShift')}</p>
      ) : (
        <div className="flex flex-col gap-2.5 text-[13px]">
          <Row label={t('dashboard.shift.cashier')} value={shift.cashierName} />
          <Row label={t('dashboard.shift.openedAt')} value={formatTime(shift.openedAt)} />
          <Row label={t('dashboard.shift.checks')} value={<span className="nums">{shift.checkCount}</span>} />
          <Row
            label={t('dashboard.shift.avgCheck')}
            value={
              <span className="nums">
                {formatSum(avg)} {t('common.currency')}
              </span>
            }
          />
        </div>
      )}
    </Card>
  );
}

function Row({ label, value }: { label: string; value: ReactNode }) {
  return (
    <div className="flex justify-between">
      <span className="text-muted">{label}</span>
      <span className="font-medium">{value}</span>
    </div>
  );
}
