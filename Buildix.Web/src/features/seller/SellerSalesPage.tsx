import { useMemo, useState } from 'react';
import { useNavigate, useParams } from 'react-router-dom';
import { useQuery, keepPreviousData } from '@tanstack/react-query';
import { useTranslation } from 'react-i18next';
import { startOfDay, startOfWeek, startOfMonth } from 'date-fns';
import { Plus, Search } from 'lucide-react';
import { PageHeader, Button, Card, StatCard, Badge, Spinner } from '@/shared/ui';
import { cn } from '@/shared/lib/cn';
import { formatSum, formatTime } from '@/shared/lib/format';
import { useDebounce } from '@/shared/hooks/useDebounce';
import { useAuth } from '@/shared/auth/useAuth';
import { PERMISSIONS } from '@/shared/config/permissions';
import { salesApi, type Sale } from '@/features/sales/api';
import { shiftsApi } from '@/features/shifts/api';

const PAGE_SIZE = 20;
const GRID = 'grid-cols-[0.7fr_0.7fr_1.5fr_1.1fr_0.9fr_1fr]';
// 'shift' scopes to the CURRENT open shift via Sale.ShiftId — the cashier's own
// question is "what have I rung up since I opened the till", which a calendar
// day cannot answer for a shift that started yesterday evening.
type Period = 'shift' | 'today' | 'week' | 'month';
const PAYMENT_FILTERS = ['all', 'cash', 'card', 'debt'] as const;
type PayFilter = (typeof PAYMENT_FILTERS)[number];
const PAY_PARAM: Record<PayFilter, string | null> = { all: null, cash: 'Cash', card: 'Terminal', debt: 'Debt' };

function periodRange(period: Period): { from: string; to: string } | null {
  if (period === 'shift') return null; // scoped by shiftId instead of a date range
  const now = new Date();
  const start =
    period === 'today' ? startOfDay(now) : period === 'week' ? startOfWeek(now, { weekStartsOn: 1 }) : startOfMonth(now);
  return { from: start.toISOString(), to: now.toISOString() };
}

/** Seller's own sales (server scopes to the seller without data.allSalesView). */
export default function SellerSalesPage() {
  const { t } = useTranslation();
  const { subdomain } = useParams();
  const navigate = useNavigate();
  const { hasPermission } = useAuth();
  const canCreate = hasPermission(PERMISSIONS.sales.create);

  const [period, setPeriod] = useState<Period>('shift');
  const [pay, setPay] = useState<PayFilter>('all');
  const [search, setSearch] = useState('');
  const [page, setPage] = useState(1);
  const debouncedSearch = useDebounce(search);
  const range = useMemo(() => periodRange(period), [period]);

  // Stats come from the seller's OWN current shift, not /CashRegister/today-sales
  // — that endpoint needs cashregister.access, which the Seller role does not
  // have (it 403'd and left every card at 0). /Shifts/current is self-service
  // and is also what the design shows ("за смену").
  const shiftQuery = useQuery({ queryKey: ['shift-current'], queryFn: shiftsApi.current });
  const shiftId = shiftQuery.data?.id ?? null;

  const listQuery = useQuery({
    queryKey: ['seller-sales', { page, search: debouncedSearch, pay, period, shiftId }],
    queryFn: () =>
      salesApi.listPaged({
        page,
        size: PAGE_SIZE,
        search: debouncedSearch,
        paymentType: PAY_PARAM[pay],
        from: range?.from,
        to: range?.to,
        shiftId: period === 'shift' ? shiftId : null,
      }),
    // Без открытой смены нечего фильтровать — an unscoped request would quietly
    // fall back to "all sales ever", which reads as someone else's receipts.
    enabled: period !== 'shift' || !!shiftId,
    placeholderData: keepPreviousData,
  });

  const sh = shiftQuery.data;
  const avg = sh && sh.checkCount > 0 ? Math.round(sh.revenue / sh.checkCount) : 0;
  const resetPage = () => setPage(1);

  return (
    <>
      <PageHeader
        title={t('seller.nav.sales')}
        actions={
          canCreate && (
            <Button onClick={() => navigate(`/${subdomain}/seller/pos`)}>
              <Plus size={15} strokeWidth={2.4} />
              {t('sales.newSale')}
            </Button>
          )
        }
      />

      <div className="mx-auto flex w-full max-w-[1240px] flex-1 flex-col gap-[18px] p-8">
        <div className="grid grid-cols-4 gap-4">
          <StatCard label={t('sales.stats.sum')} value={formatSum(sh?.revenue ?? 0)} suffix={t('common.currency')} hint={t('shifts.currentShift')} />
          <StatCard label={t('sales.stats.checks')} value={sh?.checkCount ?? 0} hint={t('shifts.currentShift')} />
          <StatCard label={t('sales.stats.avg')} value={formatSum(avg)} suffix={t('common.currency')} />
          <StatCard label={t('shifts.metrics.cashIn')} value={formatSum(sh?.cashIn ?? 0)} suffix={t('common.currency')} />
        </div>

        <div className="flex flex-wrap items-center gap-3">
          <div className="relative min-w-[280px] flex-1">
            <Search size={17} className="absolute left-3.5 top-1/2 -translate-y-1/2 text-muted-2" />
            <input
              value={search}
              onChange={(e) => {
                setSearch(e.target.value);
                resetPage();
              }}
              placeholder={t('sales.searchPlaceholder')}
              className="h-11 w-full rounded-input border border-input-border bg-surface pl-11 pr-4 text-[14px] outline-none focus:border-primary focus:shadow-focus-ring"
            />
          </div>
          <div className="flex items-center gap-1.5">
            {PAYMENT_FILTERS.map((p) => (
              <button
                key={p}
                type="button"
                onClick={() => {
                  setPay(p);
                  resetPage();
                }}
                className={cn(
                  'rounded-input px-3.5 py-2 text-[13px] font-medium transition-colors',
                  pay === p ? 'bg-primary text-white' : 'border border-input-border bg-surface text-muted hover:text-text',
                )}
              >
                {t(`sales.payment.${p}`)}
              </button>
            ))}
          </div>
          <div className="ml-auto inline-flex rounded-input bg-hairline p-1">
            {(['shift', 'today', 'week', 'month'] as Period[]).map((p) => (
              <button
                key={p}
                type="button"
                onClick={() => {
                  setPeriod(p);
                  resetPage();
                }}
                className={cn(
                  'rounded-md px-4 py-1.5 text-[13px] font-medium transition-colors',
                  period === p ? 'bg-surface text-text shadow-card' : 'text-muted hover:text-text',
                )}
              >
                {t(`sales.period.${p}`)}
              </button>
            ))}
          </div>
        </div>

        <Card className="overflow-hidden">
          <div className={cn('grid items-center gap-4 border-b border-hairline bg-bg/40 px-6 py-3 text-[11.5px] font-semibold tracking-[0.4px] text-muted-2', GRID)}>
            <span>{t('sales.cols.number')}</span>
            <span>{t('sales.cols.time')}</span>
            <span>{t('sales.cols.items')}</span>
            <span>{t('sales.cols.customer')}</span>
            <span>{t('sales.cols.pay')}</span>
            <span className="text-right">{t('sales.cols.sum')}</span>
          </div>

          {listQuery.isLoading ? (
            <div className="flex items-center justify-center py-20 text-primary">
              <Spinner size={24} />
            </div>
          ) : period === 'shift' && !shiftId ? (
            <div className="py-20 text-center text-[14px] text-muted-2">{t('shifts.noOpenShift')}</div>
          ) : listQuery.data && listQuery.data.items.length > 0 ? (
            listQuery.data.items.map((sale) => <SaleRow key={sale.id} sale={sale} />)
          ) : (
            <div className="py-20 text-center text-[14px] text-muted-2">{t('sales.empty')}</div>
          )}
        </Card>

        {listQuery.data && listQuery.data.totalPages > 1 && (
          <div className="flex items-center justify-between">
            <span className="text-[12.5px] text-muted-2">
              {t('sales.showing', { shown: listQuery.data.items.length, total: listQuery.data.total })}
            </span>
            <div className="flex items-center gap-1.5">
              {listQuery.isFetching && <Spinner size={14} className="mr-1 text-primary" />}
              <button
                type="button"
                disabled={page <= 1}
                onClick={() => setPage((p) => p - 1)}
                className="h-8 rounded-md border border-input-border bg-surface px-3 text-[13px] disabled:opacity-40"
              >
                ‹
              </button>
              <span className="px-2 text-[13px] text-muted nums">
                {page} / {listQuery.data.totalPages}
              </span>
              <button
                type="button"
                disabled={page >= listQuery.data.totalPages}
                onClick={() => setPage((p) => p + 1)}
                className="h-8 rounded-md border border-input-border bg-surface px-3 text-[13px] disabled:opacity-40"
              >
                ›
              </button>
            </div>
          </div>
        )}
      </div>
    </>
  );
}

function paymentBadge(sale: Sale): { key: string; tone: 'success' | 'info' | 'warn' | 'neutral' } {
  if (sale.status === 'Debt' || sale.remainingAmount > 0) return { key: 'debt', tone: 'warn' };
  const types = new Set(sale.payments.map((p) => p.paymentType));
  if (types.size > 1) return { key: 'mixed', tone: 'neutral' };
  const type = sale.payments[0]?.paymentType;
  if (type === 'Terminal' || type === 'Click' || type === 'Transfer') return { key: 'card', tone: 'info' };
  return { key: 'cash', tone: 'success' };
}

function SaleRow({ sale }: { sale: Sale }) {
  const { t } = useTranslation();
  const badge = paymentBadge(sale);
  const itemsText =
    sale.items.length === 0
      ? '—'
      : sale.items.length === 1
        ? sale.items[0]!.productName
        : `${sale.items[0]!.productName} +${sale.items.length - 1}`;
  return (
    <div className={cn('grid items-center gap-4 border-b border-hairline px-6 py-3.5 text-[13px] last:border-0 hover:bg-bg/40', GRID)}>
      <span className="font-semibold text-primary nums">№{sale.saleNumber}</span>
      <span className="text-muted-2 nums">{formatTime(sale.createdAt)}</span>
      <span className="truncate text-muted">{itemsText}</span>
      <span className="truncate text-muted">{sale.customerName ?? t('sales.walkIn')}</span>
      <span>
        <Badge tone={badge.tone}>{t(`sales.payment.${badge.key}` as never)}</Badge>
      </span>
      <span className="text-right font-semibold nums">{formatSum(sale.totalAmount)}</span>
    </div>
  );
}
