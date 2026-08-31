import { useMemo, useState } from 'react';
import { useQuery, keepPreviousData } from '@tanstack/react-query';
import { useTranslation } from 'react-i18next';
import {
  format,
  startOfWeek,
  startOfMonth,
  startOfQuarter,
} from 'date-fns';
import {
  Bar,
  BarChart,
  CartesianGrid,
  Cell,
  ResponsiveContainer,
  Tooltip,
  XAxis,
  type TooltipProps,
} from 'recharts';
import { Download, AlertTriangle } from 'lucide-react';
import { PageHeader, Button, Card, StatCard, Badge, Spinner } from '@/shared/ui';
import { cn } from '@/shared/lib/cn';
import { formatSum, formatQty, formatShortDate, initials } from '@/shared/lib/format';
import { useAuth } from '@/shared/auth/useAuth';
import { PERMISSIONS } from '@/shared/config/permissions';
import {
  reportsApi,
  type ReportPeriod,
  type StaffRow,
  type TopProductRow,
  type WeeklySeriesPoint,
} from './api';
import { returnsApi } from '@/features/returns/api';

const PERIODS: ReportPeriod[] = ['week', 'month', 'quarter'];

// weekly-series is capped at 30 days server-side; quarter falls back to 30.
const SERIES_DAYS: Record<ReportPeriod, number> = { week: 7, month: 30, quarter: 30 };
// top-products / staff-performance accept only today|week|month(|year) — quarter → month.
const STRING_PERIOD: Record<ReportPeriod, string> = { week: 'week', month: 'month', quarter: 'month' };

const CHART_REVENUE = '#2563eb';
const CHART_PROFIT = '#16a34a';

/** Descending blue shades for the category bars (token-based). */
const CATEGORY_BARS = ['bg-primary', 'bg-primary/75', 'bg-primary/55', 'bg-primary/40', 'bg-primary/30'];

function periodRange(period: ReportPeriod): { start: string; end: string } {
  const now = new Date();
  const start =
    period === 'week'
      ? startOfWeek(now, { weekStartsOn: 1 })
      : period === 'month'
        ? startOfMonth(now)
        : startOfQuarter(now);
  return { start: start.toISOString(), end: now.toISOString() };
}

/** Equal-length window immediately preceding the current one — for growth %. */
function prevRange(range: { start: string; end: string }): { start: string; end: string } {
  const startMs = new Date(range.start).getTime();
  const endMs = new Date(range.end).getTime();
  const span = endMs - startMs;
  return { start: new Date(startMs - span).toISOString(), end: range.start };
}

function formatPercent(ratio: number, digits = 0): string {
  if (!Number.isFinite(ratio)) return '—';
  return `${(ratio * 100).toFixed(digits).replace('.', ',')}%`;
}


/** Map a backend paymentType to the shared sales.payment.* label + badge tone. */
function paymentMeta(paymentType: string): { key: string; tone: 'success' | 'info' | 'warn' } {
  switch (paymentType) {
    case 'Cash':
      return { key: 'cash', tone: 'success' };
    case 'Terminal':
    case 'Card':
      return { key: 'card', tone: 'info' };
    case 'Click':
      return { key: 'click', tone: 'info' };
    case 'Transfer':
      return { key: 'transfer', tone: 'info' };
    case 'Debt':
      return { key: 'debt', tone: 'warn' };
    default:
      return { key: 'mixed', tone: 'info' };
  }
}

export default function ReportsPage() {
  const { t, i18n } = useTranslation();
  const { hasPermission } = useAuth();
  const canProfit = hasPermission(PERMISSIONS.data.profit);
  const canExport = hasPermission(PERMISSIONS.reports.export);

  const [period, setPeriod] = useState<ReportPeriod>('month');
  const [downloading, setDownloading] = useState(false);

  const range = useMemo(() => periodRange(period), [period]);
  const previous = useMemo(() => prevRange(range), [range]);
  const stringPeriod = STRING_PERIOD[period];

  const periodQuery = useQuery({
    queryKey: ['reports-period', range.start, range.end],
    queryFn: () => reportsApi.period(range.start, range.end),
    placeholderData: keepPreviousData,
  });
  const prevQuery = useQuery({
    queryKey: ['reports-period-prev', previous.start, previous.end],
    queryFn: () => reportsApi.period(previous.start, previous.end),
    placeholderData: keepPreviousData,
  });
  const seriesQuery = useQuery({
    queryKey: ['reports-series', period],
    queryFn: () => reportsApi.weeklySeries(SERIES_DAYS[period]),
    placeholderData: keepPreviousData,
  });
  // H-13: monthly-category-sales resolves the month from a calendar date via the
  // Tashkent clock — send yyyy-MM-dd, not a UTC instant (which would shift the
  // month at Tashkent 00:00–05:00 on a UTC server).
  const categoryDate = format(new Date(range.end), 'yyyy-MM-dd');
  const categoryQuery = useQuery({
    queryKey: ['reports-categories', categoryDate],
    queryFn: () => reportsApi.categorySales(categoryDate),
    placeholderData: keepPreviousData,
  });
  const topQuery = useQuery({
    queryKey: ['reports-top', stringPeriod],
    queryFn: () => reportsApi.topProducts(stringPeriod, 'revenue', 5),
    placeholderData: keepPreviousData,
  });
  const staffQuery = useQuery({
    queryKey: ['reports-staff', stringPeriod],
    queryFn: () => reportsApi.staffPerformance(stringPeriod),
    placeholderData: keepPreviousData,
  });
  const returnsQuery = useQuery({
    queryKey: ['reports-returns', range.start],
    queryFn: () => returnsApi.summary(range.start),
    placeholderData: keepPreviousData,
  });

  const report = periodQuery.data;
  const growth = useMemo(() => {
    const cur = report?.totalSales ?? 0;
    const prev = prevQuery.data?.totalSales ?? 0;
    if (prev <= 0) return null;
    return (cur - prev) / prev;
  }, [report?.totalSales, prevQuery.data?.totalSales]);

  const margin = report && report.profit != null && report.totalSales > 0 ? report.profit / report.totalSales : null;

  async function handleDownload() {
    if (!canExport || downloading) return;
    setDownloading(true);
    try {
      const blob = await reportsApi.exportPeriodPdf(range.start, range.end, i18n.language);
      const url = URL.createObjectURL(blob);
      const a = document.createElement('a');
      a.href = url;
      a.download = `report-${period}.pdf`;
      document.body.appendChild(a);
      a.click();
      a.remove();
      URL.revokeObjectURL(url);
    } catch {
      // Export is best-effort; a missing endpoint should not break the page.
    } finally {
      setDownloading(false);
    }
  }

  return (
    <>
      <PageHeader
        title={t('reports.title')}
        subtitle={t('reports.subtitle')}
        actions={
          <>
            <div className="inline-flex rounded-input bg-hairline p-1">
              {PERIODS.map((p) => (
                <button
                  key={p}
                  type="button"
                  onClick={() => setPeriod(p)}
                  className={cn(
                    'rounded-md px-4 py-1.5 text-[13px] font-medium transition-colors',
                    period === p ? 'bg-surface text-text shadow-card' : 'text-muted hover:text-text',
                  )}
                >
                  {t(`reports.period.${p}`)}
                </button>
              ))}
            </div>
            {canExport && (
              <Button variant="secondary" onClick={handleDownload} loading={downloading}>
                {!downloading && <Download size={15} strokeWidth={2.2} />}
                {t('reports.download')}
              </Button>
            )}
          </>
        }
      />

      <div className="flex flex-1 flex-col gap-4 p-8">
        {/* 4 stat cards */}
        <div className="grid grid-cols-2 lg:grid-cols-4 gap-4">
          <StatCard
            label={t('reports.stats.revenue')}
            value={formatSum(report?.totalSales ?? 0)}
            suffix={t('common.currency')}
            hint={
              growth != null ? (
                <span className={cn('font-semibold', growth >= 0 ? 'text-success-text' : 'text-danger')}>
                  {growth >= 0 ? '▲' : '▼'} {formatPercent(Math.abs(growth))} {t('reports.stats.vsPrevPeriod')}
                </span>
              ) : undefined
            }
          />
          <StatCard
            label={t('reports.stats.profit')}
            value={canProfit ? formatSum(report?.profit ?? 0) : '—'}
            suffix={canProfit ? t('common.currency') : undefined}
            hint={
              canProfit && margin != null
                ? t('reports.stats.margin', { value: formatPercent(margin, 1) })
                : undefined
            }
          />
          <StatCard
            label={t('reports.stats.expenses')}
            value={formatSum(report?.totalZakup ?? 0)}
            suffix={t('common.currency')}
          />
          <StatCard
            label={t('reports.stats.avgCheck')}
            value={formatSum(report?.averageSale ?? 0)}
            suffix={t('common.currency')}
            hint={t('reports.stats.checksReturns', {
              count: report?.totalTransactions ?? 0,
              returns: returnsQuery.data?.count ?? 0,
            })}
          />
        </div>

        {/* Chart + category/payment sidebar */}
        <div className="grid grid-cols-[minmax(0,1.7fr)_minmax(0,1fr)] items-start gap-4">
          <RevenueProfitChart
            points={seriesQuery.data?.points ?? []}
            loading={seriesQuery.isLoading}
            showProfit={canProfit}
            lang={i18n.language}
          />

          <div className="flex flex-col gap-4">
            <CategoriesCard
              rows={(categoryQuery.data?.categories ?? [])
                .slice()
                .sort((a, b) => b.totalSales - a.totalSales)
                .slice(0, 5)}
              // Bo'luvchi — BARCHA kategoriyalar yig'indisi, serverning
              // `totalSales` i emas.
              //
              // Server qatorlarni chegirmasiz (gross) beradi, jamini esa
              // chegirma ayirilgan holda (net) — bu ataylab shunday, chunki
              // chegirma butun chekka tegishli va uni bitta kategoriyaga
              // bo'lib bo'lmaydi. Lekin ulushni net'ga bo'lish 100% dan
              // oshib ketardi: ekranda bitta kategoriya «111%» deb turgan
              // va chizig'i kartadan chiqib ketgan edi.
              //
              // Bu karta «qaysi kategoriya ko'p sotildi» degan savolga javob
              // beradi, ya'ni ulushlar yig'indisi aynan 100% bo'lishi kerak.
              // Kesilgan beshta emas, hammasi qo'shiladi — aks holda ulush
              // «eng yaxshi beshtaning ichida» degan boshqa ma'noni olardi.
              total={(categoryQuery.data?.categories ?? []).reduce((sum, c) => sum + c.totalSales, 0)}
              loading={categoryQuery.isLoading}
            />
            <PaymentsCard
              rows={report?.paymentBreakdown ?? []}
              total={report?.totalSales ?? 0}
              loading={periodQuery.isLoading}
            />
          </div>
        </div>

        {/*
          Oxirgi qator QOLGAN balandlikni to'ldiradi — ilgari uning ostida
          yarim ekran bo'sh kulrang maydon qolardi. `items-start` olib
          tashlandi: kartalar bir xil bo'yga cho'ziladi va yonma-yon
          notekis turmaydi.
        */}
        <div className="grid min-h-0 flex-1 grid-cols-2 gap-4">
          <TopProductsCard items={topQuery.data?.items ?? []} loading={topQuery.isLoading} />
          <StaffCard staff={staffQuery.data?.staff ?? []} loading={staffQuery.isLoading} />
        </div>
      </div>
    </>
  );
}

// --- Chart -------------------------------------------------------------------

interface ChartDatum {
  label: string;
  revenue: number;
  profit: number;
}

function RevenueProfitChart({
  points,
  loading,
  showProfit,
  lang,
}: {
  points: WeeklySeriesPoint[];
  loading: boolean;
  showProfit: boolean;
  lang: string;
}) {
  const { t } = useTranslation();
  const data: ChartDatum[] = points.map((p) => ({
    label: formatShortDate(p.date, lang),
    revenue: p.revenue,
    profit: p.profit,
  }));
  const hasData = data.some((d) => d.revenue > 0 || d.profit > 0);
  // Best revenue day — highlighted bar + header caption.
  const bestIdx = hasData
    ? data.reduce((best, d, i) => (d.revenue > data[best]!.revenue ? i : best), 0)
    : -1;
  const best = bestIdx >= 0 ? data[bestIdx]! : null;

  return (
    <Card className="p-4 sm:p-6">
      <div className="mb-4 flex items-center justify-between gap-3">
        <h3 className="text-[15px] font-semibold">{t('reports.chart.title')}</h3>
        {best && (
          <span className="text-[12.5px] text-muted-2">
            {t('reports.chart.best')}:{' '}
            <span className="font-semibold text-text">
              {best.label} · {formatSum(best.revenue)}
            </span>
          </span>
        )}
      </div>
      <div className="mb-4 flex gap-4 text-[12px] text-muted">
        <LegendDot color={CHART_REVENUE} label={t('reports.chart.revenue')} />
        {showProfit && <LegendDot color={CHART_PROFIT} label={t('reports.chart.profit')} />}
      </div>

      {loading ? (
        <div className="flex h-[240px] items-center justify-center text-primary">
          <Spinner size={22} />
        </div>
      ) : !hasData ? (
        <div className="flex h-[240px] items-center justify-center text-[13px] text-muted-2">
          {t('reports.categories.empty')}
        </div>
      ) : (
        <ResponsiveContainer width="100%" height={240}>
          <BarChart data={data} barGap={2} margin={{ top: 8, right: 4, bottom: 0, left: 4 }}>
            <CartesianGrid vertical={false} stroke="var(--color-hairline, #eef2f7)" />
            <XAxis
              dataKey="label"
              tickLine={false}
              axisLine={false}
              tick={{ fontSize: 11, fill: '#94a3b8' }}
              interval="preserveStartEnd"
              minTickGap={16}
            />
            <Tooltip cursor={{ fill: 'rgba(37,99,235,0.06)' }} content={<ChartTooltip showProfit={showProfit} />} />
            <Bar dataKey="revenue" name={t('reports.chart.revenue')} radius={[4, 4, 0, 0]} maxBarSize={26}>
              {data.map((d, i) => (
                <Cell key={d.label + i} fill={i === bestIdx ? CHART_REVENUE : '#93c5fd'} />
              ))}
            </Bar>
            {showProfit && (
              <Bar dataKey="profit" name={t('reports.chart.profit')} fill={CHART_PROFIT} radius={[4, 4, 0, 0]} maxBarSize={26} />
            )}
          </BarChart>
        </ResponsiveContainer>
      )}
    </Card>
  );
}

function LegendDot({ color, label }: { color: string; label: string }) {
  return (
    <span className="flex items-center gap-1.5">
      <span className="h-2.5 w-2.5 rounded-[3px]" style={{ backgroundColor: color }} />
      {label}
    </span>
  );
}

function ChartTooltip({ active, payload, label, showProfit }: TooltipProps<number, string> & { showProfit: boolean }) {
  const { t } = useTranslation();
  if (!active || !payload || payload.length === 0) return null;
  const revenue = payload.find((p) => p.dataKey === 'revenue')?.value ?? 0;
  const profit = payload.find((p) => p.dataKey === 'profit')?.value ?? 0;
  return (
    <div className="rounded-input border border-border bg-surface px-3 py-2 text-[12px] shadow-card">
      <div className="mb-1 font-semibold">{label}</div>
      <div className="flex items-center gap-2">
        <span className="h-2 w-2 rounded-[2px]" style={{ backgroundColor: CHART_REVENUE }} />
        <span className="text-muted">{t('reports.chart.revenue')}</span>
        <span className="ml-auto font-semibold nums">{formatSum(revenue)}</span>
      </div>
      {showProfit && (
        <div className="mt-1 flex items-center gap-2">
          <span className="h-2 w-2 rounded-[2px]" style={{ backgroundColor: CHART_PROFIT }} />
          <span className="text-muted">{t('reports.chart.profit')}</span>
          <span className="ml-auto font-semibold nums">{formatSum(profit)}</span>
        </div>
      )}
    </div>
  );
}

// --- Categories --------------------------------------------------------------

function CategoriesCard({
  rows,
  total,
  loading,
}: {
  rows: { categoryId: number; categoryName: string; totalSales: number }[];
  total: number;
  loading: boolean;
}) {
  const { t } = useTranslation();
  return (
    <Card className="p-4 sm:p-5">
      <h3 className="mb-4 text-[15px] font-semibold">{t('reports.categories.title')}</h3>
      {loading ? (
        <div className="flex justify-center py-8 text-primary">
          <Spinner size={20} />
        </div>
      ) : rows.length === 0 || total <= 0 ? (
        <p className="py-6 text-center text-[13px] text-muted-2">{t('reports.categories.empty')}</p>
      ) : (
        <div className="flex flex-col gap-3">
          {rows.map((c, i) => {
            const ratio = c.totalSales / total;
            return (
              <div key={c.categoryId}>
                <div className="mb-[5px] flex items-center justify-between text-[13px]">
                  <span className="truncate font-medium">{c.categoryName}</span>
                  <span className="text-muted nums">{formatPercent(ratio)}</span>
                </div>
                <div className="h-2 rounded-pill bg-hairline">
                  <div
                    className={cn('h-2 rounded-pill', CATEGORY_BARS[i] ?? 'bg-primary/30')}
                    style={{ width: barWidth(ratio) }}
                  />
                </div>
              </div>
            );
          })}
        </div>
      )}
    </Card>
  );
}

/**
 * Chiziq kengligi — foizda, o'z yo'lakchasidan CHIQMAYDIGAN qilib.
 *
 * <p>Ma'lumot har doim ham 0–100 oralig'ida bo'lavermaydi: chegirma,
 * qaytarish yoki manfiy summa ulushni 100 dan oshirib yoki noldan pastga
 * tushirib yuborishi mumkin. Bunday paytda chiziq kartadan chiqib ketardi
 * — ekranda aynan shu ko'rindi. Raqam esa o'z holicha ko'rsatilaveradi:
 * uni yashirish muammoni ko'rinmas qilardi, tuzatmasdi.</p>
 */
function barWidth(ratio: number, min = 2): string {
  if (!Number.isFinite(ratio)) return `${min}%`;
  return `${Math.min(100, Math.max(min, ratio * 100))}%`;
}

// --- Payment methods ---------------------------------------------------------

function PaymentsCard({
  rows,
  total,
  loading,
}: {
  rows: { paymentType: string; amount: number }[];
  total: number;
  loading: boolean;
}) {
  const { t } = useTranslation();
  const sorted = rows.slice().sort((a, b) => b.amount - a.amount);
  // Qarz ulushi — 20% dan oshsa, kassa oqimi uchun ogohlantirish (dizayn).
  const debtShare = total > 0 ? sorted.filter((r) => r.paymentType === 'Debt').reduce((s, r) => s + r.amount, 0) / total : 0;
  return (
    <Card className="p-4 sm:p-5">
      <h3 className="mb-4 text-[15px] font-semibold">{t('reports.payments.title')}</h3>
      {loading ? (
        <div className="flex justify-center py-8 text-primary">
          <Spinner size={20} />
        </div>
      ) : sorted.length === 0 || total <= 0 ? (
        <p className="py-6 text-center text-[13px] text-muted-2">{t('reports.payments.empty')}</p>
      ) : (
        <>
          <div className="flex flex-col gap-3 text-[13px]">
            {sorted.map((row) => {
              const meta = paymentMeta(row.paymentType);
              return (
                <div key={row.paymentType} className="flex items-center justify-between gap-2">
                  <Badge tone={meta.tone}>{t(`sales.payment.${meta.key}` as never)}</Badge>
                  <span className="flex items-baseline gap-2">
                    <span className="text-[12px] text-muted-2 nums">{formatSum(row.amount)}</span>
                    <span className="w-11 text-right font-semibold nums">{formatPercent(row.amount / total)}</span>
                  </span>
                </div>
              );
            })}
          </div>
          {debtShare > 0.2 && (
            <div className="mt-4 flex items-start gap-2 rounded-input border border-warn-amber/30 bg-warn-soft px-3 py-2.5 text-[12px] text-warn-strong">
              <AlertTriangle size={14} className="mt-0.5 flex-none" />
              <span>{t('reports.payments.debtRisk', { value: formatPercent(debtShare) })}</span>
            </div>
          )}
        </>
      )}
    </Card>
  );
}

// --- Top products ------------------------------------------------------------

function TopProductsCard({ items, loading }: { items: TopProductRow[]; loading: boolean }) {
  const { t } = useTranslation();
  return (
    <Card className="overflow-hidden">
      <div className="px-[22px] pb-3 pt-4 text-[15px] font-semibold">{t('reports.topProducts.title')}</div>
      <div className="grid grid-cols-reports-topproducts gap-3 border-t border-hairline bg-bg/40 px-[22px] py-2 text-[11.5px] font-semibold tracking-[0.4px] text-muted-2">
        <span>{t('reports.topProducts.cols.product')}</span>
        <span className="text-right">{t('reports.topProducts.cols.sold')}</span>
        <span className="text-right">{t('reports.topProducts.cols.revenue')}</span>
      </div>
      {loading ? (
        <div className="flex justify-center py-14 text-primary">
          <Spinner size={20} />
        </div>
      ) : items.length === 0 ? (
        <p className="py-14 text-center text-[13px] text-muted-2">{t('reports.topProducts.empty')}</p>
      ) : (
        items.map((p) => (
          <div
            key={p.productId}
            className="grid grid-cols-reports-topproducts items-center gap-3 border-t border-hairline px-[22px] py-2.5 text-[13px]"
          >
            <div className="flex min-w-0 items-center gap-2.5">
              <span className="flex h-[22px] w-[22px] flex-none items-center justify-center rounded-md bg-primary-soft text-[11px] font-bold text-primary-hover nums">
                {p.rank}
              </span>
              <span className="truncate font-medium">{p.name}</span>
            </div>
            <span className="text-right text-muted nums">{formatQty(p.quantity)}</span>
            <span className="text-right font-semibold nums">{formatSum(p.revenue)}</span>
          </div>
        ))
      )}
    </Card>
  );
}

// --- Staff -------------------------------------------------------------------

function StaffCard({ staff, loading }: { staff: StaffRow[]; loading: boolean }) {
  const { t } = useTranslation();
  const totalRevenue = staff.reduce((s, x) => s + x.revenue, 0);
  // Bars are scaled to the top performer so the leader fills the track.
  const maxRevenue = staff.reduce((m, x) => Math.max(m, x.revenue), 0);
  return (
    <Card className="p-4 sm:p-5">
      <h3 className="mb-2 text-[15px] font-semibold">{t('reports.staff.title')}</h3>
      {loading ? (
        <div className="flex justify-center py-14 text-primary">
          <Spinner size={20} />
        </div>
      ) : staff.length === 0 ? (
        <p className="py-14 text-center text-[13px] text-muted-2">{t('reports.staff.empty')}</p>
      ) : (
        <>
          <div className="flex flex-col">
            {staff.map((s) => (
              <div
                key={s.userId}
                className="flex items-center gap-3 border-b border-hairline py-2.5 last:border-0"
              >
                <span className="flex h-9 w-9 flex-none items-center justify-center rounded-pill bg-primary-soft text-[12px] font-semibold text-primary-hover">
                  {initials(s.fullName)}
                </span>
                <div className="min-w-0 flex-1">
                  <div className="truncate text-[13.5px] font-semibold">{s.fullName}</div>
                  <div className="mt-0.5 text-[11.5px] text-muted-2 nums">
                    {t('reports.staff.checksAvg', { count: s.saleCount, avg: formatSum(s.averageCheck) })}
                  </div>
                </div>
                <div className="flex-none text-right">
                  <div className="text-[14px] font-bold nums">{formatSum(s.revenue)}</div>
                  <div className="mt-1.5 h-[5px] w-[90px] rounded-pill bg-hairline">
                    <div
                      className="h-[5px] rounded-pill bg-primary"
                      style={{ width: maxRevenue > 0 ? barWidth(s.revenue / maxRevenue, 3) : '0%' }}
                    />
                  </div>
                </div>
              </div>
            ))}
          </div>
          {totalRevenue > 0 && (
            <p className="mt-3 text-[11.5px] text-muted-2">{t('reports.staff.shareNote')}</p>
          )}
        </>
      )}
    </Card>
  );
}
