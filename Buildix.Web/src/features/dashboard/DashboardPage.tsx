import { useEffect, useMemo, useState, type ReactNode } from 'react';
import { Link, useNavigate, useParams } from 'react-router-dom';
import { useQuery, useMutation, useQueryClient } from '@tanstack/react-query';
import { useTranslation } from 'react-i18next';
import type { TFunction } from 'i18next';
import { format, startOfDay, parseISO, differenceInCalendarDays } from 'date-fns';
import { Plus, Receipt, TrendingUp, CreditCard, Boxes, Clock, Truck, ChevronRight, Users, AlertTriangle } from 'lucide-react';
import type { LucideIcon } from 'lucide-react';
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
import { formatSum, formatQty, formatFullDate, formatShortDate, formatWeekday, formatTime, initials } from '@/shared/lib/format';
import { unitLabel } from '@/shared/lib/units';
import { useAuth } from '@/shared/auth/useAuth';
import { PERMISSIONS } from '@/shared/config/permissions';
import { purchasesApi, type ReorderSuggestion, type ZakupReceipt } from '@/features/purchases/api';
import { debtsApi, type DebtorSummary } from '@/features/debts/api';
import { shiftsApi, type Shift } from '@/features/shifts/api';
import { productsApi } from '@/features/warehouse/api';
import { employeesApi, type Employee } from '@/features/employees/api';
import { notificationsApi, type NotificationItem } from '@/features/notifications/api';
import { SaleDetailModal } from '@/features/sales/SaleDetailModal';
import { type Sale } from '@/features/sales/api';
import { dashboardApi, type DailySale, type WeeklyPoint } from './api';

/**
 * Server `paymentType` → badge. The keys are the PaymentType enum names
 * lowercased, exactly as SalesListService emits them, plus the literal
 * "qaytarilgan" it uses for a sale carrying refund payments.
 *
 * `terminal` and `credit` were missing, so a card sale fell through to the
 * fallback branch and rendered the raw i18n key ("sales.payment.terminal") in
 * the badge instead of a label.
 */
const PAY_BADGE: Record<string, { key: string; tone: 'success' | 'info' | 'warn' | 'neutral' }> = {
  cash: { key: 'cash', tone: 'success' },
  terminal: { key: 'card', tone: 'info' },
  card: { key: 'card', tone: 'info' },
  click: { key: 'click', tone: 'info' },
  transfer: { key: 'transfer', tone: 'info' },
  credit: { key: 'credit', tone: 'neutral' },
  debt: { key: 'debt', tone: 'warn' },
  qaytarilgan: { key: 'returned', tone: 'neutral' },
};

const PAY_FALLBACK = { key: 'cash', tone: 'success' } as const;

function payBadge(sale: DailySale): { key: string; tone: 'success' | 'info' | 'warn' | 'neutral' } {
  // Qarzga sotuvda to'lov yozuvi bo'lmasligi mumkin va server "Cash" ga
  // tushib qoladi — status bu yerda ishonchliroq manba (Sotuvlar ro'yxatidagi
  // paymentBadge ham xuddi shunday qaraydi).
  if (sale.status === 'Debt') return { key: 'debt', tone: 'warn' };
  return PAY_BADGE[sale.paymentType?.toLowerCase() ?? ''] ?? PAY_FALLBACK;
}

function pct(value: number): string {
  return `${(value * 100).toFixed(1).replace('.', ',')}%`;
}

export default function DashboardPage() {
  const { t, i18n } = useTranslation();
  const { subdomain } = useParams();
  const navigate = useNavigate();
  const { hasPermission } = useAuth();
  // «Все продажи» qatori bosilganda ochiladigan chek detali (Изменить/Аннулировать/Вернуть).
  const [openSale, setOpenSale] = useState<Sale | null>(null);

  const canCash = hasPermission(PERMISSIONS.cashregister.access);
  const canReports = hasPermission(PERMISSIONS.reports.access);
  const canProfit = hasPermission(PERMISSIONS.data.profit);
  const canSales = hasPermission(PERMISSIONS.sales.access);
  const canShift = hasPermission(PERMISSIONS.users.shift);
  const canZakup = hasPermission(PERMISSIONS.zakup.access);
  const canDebts = hasPermission(PERMISSIONS.debts.access);
  const canProducts = hasPermission(PERMISSIONS.products.access);
  const canNotifications = hasPermission(PERMISSIONS.notifications.access);
  const canUsers = hasPermission(PERMISSIONS.users.access);
  const canCreateSale = hasPermission(PERMISSIONS.sales.create);

  // H-13: send the Tashkent calendar date (no time/offset) so the server's
  // LocalDayToUtcRange resolves the correct business day. A UTC instant would
  // shift Tashkent 00:00–05:00 to the previous UTC day on a UTC server.
  //
  // Recomputed on a timer rather than frozen at mount: a dashboard left open on
  // the shop's wall screen used to keep asking for yesterday after midnight,
  // and the header kept printing yesterday's date.
  const today = useTodayDate();

  // Jonli ko'rsatkichlar — dashboard doim ochiq turadi, shuning uchun o'zi
  // yangilanadi. Tez o'zgaradiganlari (sotuv, kassa, smena, so'nggi cheklar)
  // 60 soniyada, sekinlari (haftalik grafik, qarz/mijoz yig'indisi, ombor)
  // 5 daqiqada.
  const live = { refetchInterval: 60_000, refetchIntervalInBackground: false } as const;
  const slow = { refetchInterval: 300_000, refetchIntervalInBackground: false } as const;

  const todayQuery = useQuery({ queryKey: ['dash-today'], queryFn: dashboardApi.todaySales, enabled: canCash, ...live });
  const profitQuery = useQuery({ queryKey: ['dash-profit'], queryFn: dashboardApi.profitSummary, enabled: canProfit, ...live });
  const weeklyQuery = useQuery({
    queryKey: ['dash-weekly'],
    queryFn: () => dashboardApi.weeklySeries(7, true),
    enabled: canReports,
    ...slow,
  });
  const salesQuery = useQuery({
    queryKey: ['dash-daily-sales', today],
    queryFn: () => dashboardApi.dailySales(today),
    enabled: canSales,
    ...live,
  });
  const lowStockQuery = useQuery({
    queryKey: ['dash-lowstock'],
    queryFn: () => purchasesApi.reorderSuggestions(4),
    enabled: canZakup,
    ...slow,
  });
  const debtorsQuery = useQuery({
    queryKey: ['dash-debtors'],
    queryFn: () => debtsApi.debtors(),
    enabled: canDebts,
    ...slow,
  });
  // «Требует внимания» — the server notification feed, narrowed to items that
  // still need an owner decision (Warning/Danger). Same source as the sidebar
  // bell, so a resolved alert drops off both places at once.
  const attentionQuery = useQuery({
    queryKey: ['dash-attention'],
    queryFn: () => notificationsApi.feed(null),
    enabled: canNotifications,
    ...slow,
  });
  // Market smenalari — «Продавцы на смене» KPI (ochiq smenalar soni) va o'ng
  // paneldagi «Смена · сегодня» kartasi shu bir manbadan.
  const openShiftsQuery = useQuery({
    queryKey: ['dash-open-shifts'],
    queryFn: () => shiftsApi.history(50),
    enabled: canShift,
    ...live,
  });
  // «Сигналы склада» — low-stock + out-of-stock positions.
  const stockQuery = useQuery({
    queryKey: ['dash-stock-summary'],
    queryFn: () => productsApi.summary(),
    enabled: canProducts,
    ...slow,
  });
  // «Закупы» — so'nggi xaridlar (status bilan; в пути → qabul qilish mumkin).
  const purchasesQuery = useQuery({
    queryKey: ['dash-purchases'],
    queryFn: () => purchasesApi.receiptsPaged(1, 5),
    enabled: canZakup,
    ...slow,
  });
  // «Доступы продавцов» — kassirlar ro'yxati (tez ko'rinish + Сотрудники link).
  const sellersQuery = useQuery({
    queryKey: ['dash-sellers'],
    queryFn: () => employeesApi.list(),
    enabled: canUsers,
    ...slow,
  });

  const todaySales = todayQuery.data;
  const weekly = weeklyQuery.data;
  // Eng so'nggi ochiq smena — o'ng paneldagi «Смена · сегодня» kartasi uchun.
  const shift = useMemo(
    () => (openShiftsQuery.data ?? []).find((s) => s.closedAt === null) ?? null,
    [openShiftsQuery.data],
  );

  // Sales growth vs yesterday from the weekly series (last two points).
  const salesGrowth = useMemo(() => {
    const pts = weekly?.points ?? [];
    if (pts.length < 2) return null;
    const prev = pts[pts.length - 2]!.revenue;
    const last = pts[pts.length - 1]!.revenue;
    if (prev <= 0) return null;
    return (last - prev) / prev;
  }, [weekly]);

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

  // Only actionable alerts on the dashboard; Info/Success events stay in the
  // full feed. Danger first, then Warning, capped so the panel stays glanceable.
  const attention = useMemo(() => {
    const items = attentionQuery.data?.items ?? [];
    const rank = (s: string) => (s === 'Danger' ? 0 : s === 'Warning' ? 1 : 2);
    return items
      .filter((n) => n.severity === 'Danger' || n.severity === 'Warning')
      .sort((a, b) => rank(a.severity) - rank(b.severity))
      .slice(0, 4);
  }, [attentionQuery.data]);

  // Distinct cashiers currently on an open shift.
  const openSellers = useMemo(() => {
    const ids = new Set((openShiftsQuery.data ?? []).filter((s) => s.isOpen).map((s) => s.userId));
    return ids.size;
  }, [openShiftsQuery.data]);

  const stockLow = stockQuery.data?.lowStock ?? 0;
  const stockOut = stockQuery.data?.outOfStock ?? 0;
  const stockSignals = stockLow + stockOut;

  const margin =
    todaySales && todaySales.totalAmount > 0 && profitQuery.data
      ? profitQuery.data.todayProfit / todaySales.totalAmount
      : null;

  return (
    <>
      <PageHeader
        title={t('dashboard.title')}
        subtitle={formatFullDate(today, i18n.language)}
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

      <div className="flex flex-1 flex-col gap-[18px] p-4 sm:p-6 lg:p-8">
        {/* Stat cards — auto-fit so a role that lacks a permission (e.g. Admin
            without data.profit) gets an evenly filled row instead of a hole
            where the hidden card used to sit. */}
        <div className="grid grid-cols-[repeat(auto-fit,minmax(220px,1fr))] gap-4">
          {canCash && (
            <StatCard
              label={t('dashboard.stats.salesToday')}
              icon={<Receipt size={16} strokeWidth={1.9} />}
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
              icon={<TrendingUp size={16} strokeWidth={1.9} />}
              value={formatSum(profitQuery.data?.todayProfit ?? 0)}
              suffix={t('common.currency')}
              hint={margin !== null ? t('dashboard.stats.margin', { value: pct(margin) }) : undefined}
            />
          )}

          {canShift && (
            <StatCard
              label={t('dashboard.stats.sellersOnShift')}
              icon={<Users size={16} strokeWidth={1.9} />}
              value={String(openSellers)}
              hint={
                openSellers > 0
                  ? t('dashboard.stats.onShiftNow', { count: openSellers })
                  : t('dashboard.stats.noOpenShift')
              }
            />
          )}

          {canProducts && (
            <StatCard
              label={t('dashboard.stats.stockSignals')}
              icon={<Boxes size={16} strokeWidth={1.9} />}
              value={String(stockSignals)}
              tone={stockOut > 0 ? 'warn' : 'default'}
              hint={
                stockSignals > 0 ? (
                  <span className="flex items-center gap-1.5">
                    {stockOut > 0 && (
                      <span className="font-semibold text-danger">
                        {t('dashboard.stats.outCount', { count: stockOut })}
                      </span>
                    )}
                    <span>{stockOut > 0 ? '· ' : ''}{t('dashboard.stats.lowCount', { count: stockLow })}</span>
                  </span>
                ) : (
                  t('dashboard.stats.stockOk')
                )
              }
            />
          )}
        </div>

        {/* Telefonda ikki ustun sig'maydi — kartalar ustma-ust joylashadi.
            minmax(0,...): ustun kontentdan kichik bo'la olsin, aks holda
            ichkaridagi jadval butun sahifani cho'zadi. */}
        <div className="grid grid-cols-1 items-start gap-[18px] lg:grid-cols-[minmax(0,2fr)_minmax(0,1fr)]">
          {/* Left column */}
          <div className="flex min-w-0 flex-col gap-[18px]">
            {canReports && <WeeklyChartCard points={weekly?.points ?? []} total={weekly?.currentTotal ?? 0} loading={weeklyQuery.isLoading} />}
            {canSales && (
              <RecentSalesCard
                sales={recentSales}
                loading={salesQuery.isLoading}
                to={`/${subdomain}/sales`}
                onOpen={canSales ? (s) => setOpenSale(dailyToSaleRow(s)) : undefined}
              />
            )}
            {canZakup && (
              <PurchasesCard
                receipts={purchasesQuery.data?.items ?? []}
                loading={purchasesQuery.isLoading}
                to={`/${subdomain}/purchases`}
              />
            )}
          </div>

          {/* Right column */}
          <div className="flex min-w-0 flex-col gap-[18px]">
            {canNotifications && attention.length > 0 && (
              <AttentionCard
                items={attention}
                onOpen={(n) => n.actionTarget && navigate(`/${subdomain}/${n.actionTarget}`)}
              />
            )}
            {canUsers && (
              <SellerAccessCard
                sellers={(sellersQuery.data ?? []).filter((e) => e.role === 'Seller')}
                loading={sellersQuery.isLoading}
                to={`/${subdomain}/employees`}
              />
            )}
            {canZakup && (
              <LowStockCard
                items={lowStockQuery.data ?? []}
                loading={lowStockQuery.isLoading}
                onOrder={hasPermission(PERMISSIONS.zakup.create) ? () => navigate(`/${subdomain}/purchases`) : undefined}
              />
            )}
            {canDebts && <UpcomingPaymentsCard debtors={upcoming} loading={debtorsQuery.isLoading} to={`/${subdomain}/debts`} />}
            {canShift && <ShiftCard shift={shift} loading={openShiftsQuery.isLoading} />}
          </div>
        </div>
      </div>

      <SaleDetailModal sale={openSale} onClose={() => setOpenSale(null)} />
    </>
  );
}

/**
 * Dashboard `DailySale` → Sales `Sale` placeholder for SaleDetailModal. The
 * modal re-fetches full detail by id immediately; this only fills the header
 * for the first paint, so the unknown fields get safe defaults.
 */
function dailyToSaleRow(d: DailySale): Sale {
  return {
    id: d.id,
    saleNumber: 0,
    sellerId: '',
    sellerName: d.sellerName,
    customerId: null,
    customerName: d.customerName,
    customerPhone: null,
    status: d.status,
    totalAmount: d.totalAmount,
    paidAmount: 0,
    remainingAmount: 0,
    discountAmount: 0,
    createdAt: d.createdAt,
    items: [],
    payments: [],
  };
}

/**
 * Today's calendar date as "yyyy-MM-dd", re-evaluated every minute so a
 * dashboard left open on the shop's screen rolls over to the new business day
 * instead of pinning the date it was mounted on.
 */
function useTodayDate(): string {
  const [date, setDate] = useState(() => format(new Date(), 'yyyy-MM-dd'));
  useEffect(() => {
    const id = setInterval(
      () => setDate((prev) => {
        const next = format(new Date(), 'yyyy-MM-dd');
        return next === prev ? prev : next; // keep the reference stable within a day
      }),
      60_000,
    );
    return () => clearInterval(id);
  }, []);
  return date;
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
  // Bar yorlig'i qisqa ("Пн") — tooltip'da to'liq sanani ko'rsatamiz.
  const fullDate = (payload[0]?.payload as { fullDate?: string } | undefined)?.fullDate;
  return (
    <div className="rounded-input border border-border bg-surface px-3 py-2 text-[12px] shadow-card">
      <div className="mb-0.5 text-muted-2">{fullDate ?? label}</div>
      <div className="font-semibold nums">
        {formatSum(payload[0]!.value ?? 0)} {t('common.currency')}
      </div>
    </div>
  );
}

function WeeklyChartCard({ points, total, loading }: { points: WeeklyPoint[]; total: number; loading: boolean }) {
  const { t, i18n } = useTranslation();
  // Yorliq — hafta kuni ("Пн"), dizayndagidek. To'liq sana ("19 июля") 7 marta
  // bu kenglikka sig'maydi va ustma-ust tushib ketardi. To'liq sana tooltip'da.
  const data = points.map((p) => ({
    label: formatWeekday(p.date, i18n.language),
    fullDate: formatShortDate(p.date, i18n.language),
    revenue: p.revenue,
  }));
  const lastIdx = data.length - 1;

  return (
    <Card className="p-4 sm:p-6">
      <div className="mb-[18px] flex items-baseline justify-between">
        <h3 className="min-w-0 text-[15px] font-semibold">{t('dashboard.chart.title')}</h3>
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

function RecentSalesCard({
  sales,
  loading,
  to,
  onOpen,
}: {
  sales: DailySale[];
  loading: boolean;
  to: string;
  onOpen?: (s: DailySale) => void;
}) {
  const { t } = useTranslation();
  return (
    <Card className="overflow-hidden">
      <div className="flex items-center justify-between gap-3 px-6 pb-3.5 pt-[18px]">
        <h3 className="min-w-0 text-[15px] font-semibold">{t('dashboard.recent.title')}</h3>
        <Link to={to} className="flex-none whitespace-nowrap text-[12.5px] font-semibold text-primary hover:text-primary-hover">
          {t('dashboard.recent.all')} →
        </Link>
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
        sales.map((s) => <RecentSaleRow key={s.id} sale={s} onOpen={onOpen ? () => onOpen(s) : undefined} />)
      )}
    </Card>
  );
}

/**
 * "Цемент М500 ×10, Арматура Ø12" — the design's ТОВАРЫ cell. Two names, then
 * a "+N" tail, so the line stays readable inside a 1.4fr column.
 */
function itemsText(sale: DailySale): string {
  const lines = sale.items ?? [];
  if (lines.length === 0) return '—';
  const head = lines
    .slice(0, 2)
    .map((i) => `${i.productName} ×${formatQty(i.quantity)}`)
    .join(', ');
  return lines.length > 2 ? `${head} +${lines.length - 2}` : head;
}

function RecentSaleRow({ sale, onOpen }: { sale: DailySale; onOpen?: () => void }) {
  const { t } = useTranslation();
  const badge = payBadge(sale);
  const clickable = !!onOpen;
  return (
    <div
      role={clickable ? 'button' : undefined}
      tabIndex={clickable ? 0 : undefined}
      onClick={onOpen}
      onKeyDown={clickable ? (e) => (e.key === 'Enter' || e.key === ' ') && (e.preventDefault(), onOpen!()) : undefined}
      className={cn(
        'grid grid-cols-dashboard-sales items-center gap-[14px] border-t border-hairline px-6 py-3 text-[13px]',
        clickable && 'cursor-pointer hover:bg-bg/50',
      )}
      title={clickable ? t('dashboard.recent.openHint') : undefined}
    >
      <span className="text-muted-2 nums">{formatTime(sale.createdAt)}</span>
      <span className="truncate font-medium">{sale.sellerName}</span>
      {/* Design's 5 columns have no customer cell — the buyer is surfaced in
          the hover title so the row keeps that context without a 6th column. */}
      <span className="truncate text-muted">{itemsText(sale)}</span>
      <span>
        <Badge tone={badge.tone}>{t(`sales.payment.${badge.key}` as never)}</Badge>
      </span>
      <span className="text-right font-semibold nums">{formatSum(sale.totalAmount)}</span>
    </div>
  );
}

const ATTN_CAT_ICON: Record<string, LucideIcon> = { Warehouse: Boxes, Debt: CreditCard, Shift: Clock, Supply: Truck };
/** Qator qaysi ekranga olib borishi — chaqiruv matni uchun. */
const ATTN_CTA: Record<string, string> = {
  Shift: 'shift', Warehouse: 'warehouse', Debt: 'debt', Supply: 'supply',
};
const ATTN_TONE: Record<string, { row: string; icon: string; title: string; text: string }> = {
  Danger: { row: 'bg-danger-soft border-danger/25', icon: 'text-danger', title: 'text-danger', text: 'text-danger/80' },
  Warning: { row: 'bg-warn-soft border-warn-amber/30', icon: 'text-warn-strong', title: 'text-warn-strong', text: 'text-warn-strong/85' },
};

/**
 * «Требует внимания» — the actionable slice of the notification feed, shown as
 * severity-tinted rows. Clicking a row deep-links to where it gets resolved
 * (склад / долги / смены), matching the sidebar bell's target.
 */
function AttentionCard({ items, onOpen }: { items: NotificationItem[]; onOpen: (n: NotificationItem) => void }) {
  const { t } = useTranslation();
  return (
    <Card className="p-[22px]">
      <h3 className="mb-3.5 text-[15px] font-semibold">{t('dashboard.attention.title')}</h3>
      <div className="flex flex-col gap-2.5">
        {items.map((n) => {
          const tone = ATTN_TONE[n.severity] ?? ATTN_TONE.Warning!;
          const Icon = ATTN_CAT_ICON[n.category] ?? AlertTriangle;
          return (
            <button
              key={n.id}
              type="button"
              onClick={() => onOpen(n)}
              className={cn(
                'flex w-full items-start gap-2.5 rounded-input border px-3.5 py-3 text-left transition-opacity',
                tone.row,
                n.actionTarget ? 'hover:opacity-80' : 'cursor-default',
              )}
            >
              <Icon size={16} className={cn('mt-0.5 flex-none', tone.icon)} />
              <span className="min-w-0 flex-1">
                <span className={cn('block truncate text-[13px] font-semibold', tone.title)}>{n.title}</span>
                <span className={cn('mt-0.5 block truncate text-[12px]', tone.text)}>
                  {n.text}
                  {/* Ochiq chaqiruv: qator bosiladigani va QAYERGA olib borishi
                      yozib qo'yiladi. Ilgari faqat o'ng tomondagi «›» belgisi
                      bor edi — u nima ochilishini aytmasdi (dizayn: «открыть
                      сверку смены →»). */}
                  {n.actionTarget && (
                    <>
                      {n.text ? ' · ' : ''}
                      <span className="font-semibold underline underline-offset-2">
                        {t(`dashboard.attention.open.${ATTN_CTA[n.category] ?? 'default'}` as never)} →
                      </span>
                    </>
                  )}
                </span>
              </span>
              {n.actionTarget && <ChevronRight size={15} className={cn('mt-0.5 flex-none', tone.icon)} />}
            </button>
          );
        })}
      </div>
    </Card>
  );
}

/**
 * Postavka tarkibi — birinchi ikkita tovar nomi, qolgani «+N».
 * Nomlar kelmagan bo'lsa (ro'yxat proyeksiyasi ularni bermasa) chaqiruvchi
 * pozitsiyalar soniga tushadi.
 */
function composition(r: ZakupReceipt): string {
  const names = (r.items ?? []).map((i) => i.productName).filter(Boolean);
  if (names.length === 0) return '';
  const head = names.slice(0, 2).join(', ');
  return names.length > 2 ? `${head} +${names.length - 2}` : head;
}

/** «Закупы» — so'nggi xaridlar, status bilan; «в пути» qatorini qabul qilish. */
function PurchasesCard({ receipts, loading, to }: { receipts: ZakupReceipt[]; loading: boolean; to: string }) {
  const { t } = useTranslation();
  const { hasPermission } = useAuth();
  const qc = useQueryClient();
  const accept = useMutation({
    mutationFn: (id: string) => purchasesApi.accept(id),
    onSuccess: () => {
      void qc.invalidateQueries({ queryKey: ['dash-purchases'] });
      void qc.invalidateQueries({ queryKey: ['dash-stock-summary'] });
    },
  });
  const canAccept = hasPermission(PERMISSIONS.zakup.create);
  return (
    <Card className="overflow-hidden">
      <div className="flex items-center justify-between gap-3 px-6 pb-3.5 pt-[18px]">
        <h3 className="min-w-0 text-[15px] font-semibold">{t('dashboard.purchases.title')}</h3>
        <Link to={to} className="flex-none whitespace-nowrap text-[12.5px] font-semibold text-primary hover:text-primary-hover">
          {t('dashboard.purchases.all')} →
        </Link>
      </div>
      {loading ? (
        <div className="flex items-center justify-center py-12 text-primary">
          <Spinner size={22} />
        </div>
      ) : receipts.length === 0 ? (
        <div className="py-12 text-center text-[14px] text-muted-2">{t('dashboard.purchases.empty')}</div>
      ) : (
        receipts.map((r) => {
          const inTransit = r.deliveryStatus === 'InTransit';
          return (
            <div
              key={r.id}
              className="flex items-center gap-3 border-t border-hairline px-6 py-3 text-[13px]"
            >
              <span className="w-[64px] flex-none font-semibold nums">З-{r.receiptNumber}</span>
              <span className="min-w-0 flex-1 truncate">{r.supplierName ?? '—'}</span>
              {/* «СОСТАВ» — nima kelayotgani. Faqat pozitsiyalar soni emas,
                  tovar nomlari: operator ro'yxatni ochmasdan turib qaysi
                  postavka ekanini taniydi (dizayn: «Цемент, песок»). */}
              <span className="min-w-0 flex-1 truncate text-muted" title={composition(r)}>
                {composition(r) || t('dashboard.purchases.positions', { count: r.itemCount })}
              </span>
              <Badge tone={inTransit ? 'warn' : 'success'}>
                {t(inTransit ? 'dashboard.purchases.inTransit' : 'dashboard.purchases.accepted')}
              </Badge>
              <span className="w-[110px] flex-none text-right font-semibold nums">{formatSum(r.totalAmount)}</span>
              {inTransit && canAccept && (
                <Button
                  size="sm"
                  variant="secondary"
                  loading={accept.isPending && accept.variables === r.id}
                  onClick={() => accept.mutate(r.id)}
                >
                  {t('dashboard.purchases.accept')}
                </Button>
              )}
            </div>
          );
        })
      )}
    </Card>
  );
}

/** «Доступы продавцов» — kassirlar tez ko'rinishi (read-only) + Сотрудники link. */
function SellerAccessCard({ sellers, loading, to }: { sellers: Employee[]; loading: boolean; to: string }) {
  const { t } = useTranslation();
  return (
    <Card className="p-[22px]">
      <div className="mb-3.5 flex items-center justify-between gap-3">
        <h3 className="min-w-0 text-[15px] font-semibold">{t('dashboard.sellerAccess.title')}</h3>
        <Link to={to} className="flex-none whitespace-nowrap text-[12.5px] font-semibold text-primary hover:text-primary-hover">
          {t('dashboard.sellerAccess.configure')} →
        </Link>
      </div>
      {loading ? (
        <div className="flex justify-center py-6 text-primary">
          <Spinner size={20} />
        </div>
      ) : sellers.length === 0 ? (
        <p className="py-4 text-center text-[13px] text-muted-2">{t('dashboard.sellerAccess.empty')}</p>
      ) : (
        <div className="flex flex-col gap-2.5">
          {sellers.slice(0, 5).map((s) => (
            <div key={s.id} className="flex items-center gap-2.5">
              <span className="flex h-8 w-8 flex-none items-center justify-center rounded-pill bg-primary-soft text-[11px] font-semibold text-primary">
                {initials(s.fullName)}
              </span>
              <span className="min-w-0 flex-1 truncate text-[13px] font-medium">{s.fullName}</span>
              <Badge tone={s.isActive ? 'success' : 'danger'}>
                {t(s.isActive ? 'dashboard.sellerAccess.active' : 'dashboard.sellerAccess.blocked')}
              </Badge>
            </div>
          ))}
        </div>
      )}
    </Card>
  );
}


function LowStockCard({ items, loading, onOrder }: { items: ReorderSuggestion[]; loading: boolean; onOrder?: () => void }) {
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
                      {formatQty(s.currentQty)} {unitLabel(t, s.unit, s.unitName)}
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
          {onOrder && (
            <Button variant="secondary" fullWidth className="mt-4" onClick={onOrder}>
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

function UpcomingPaymentsCard({ debtors, loading, to }: { debtors: DebtorSummary[]; loading: boolean; to: string }) {
  const { t, i18n } = useTranslation();
  return (
    <Card className="p-[22px]">
      <div className="mb-3.5 flex items-center justify-between gap-3">
        <h3 className="min-w-0 text-[15px] font-semibold">{t('dashboard.payments.title')}</h3>
        <Link to={to} className="flex-none whitespace-nowrap text-[12.5px] font-semibold text-primary hover:text-primary-hover">
          {t('dashboard.payments.all')} →
        </Link>
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

function ShiftCard({ shift, loading }: { shift: Shift | null; loading: boolean }) {
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
