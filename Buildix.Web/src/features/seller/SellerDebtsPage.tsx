import { useState } from 'react';
import { useQuery } from '@tanstack/react-query';
import { useTranslation } from 'react-i18next';
import { Search } from 'lucide-react';
import { PageHeader, Card, StatCard, Badge, Spinner, Button } from '@/shared/ui';
import { cn } from '@/shared/lib/cn';
import { formatSum, formatShortDate, formatTime } from '@/shared/lib/format';
import { useDebounce } from '@/shared/hooks/useDebounce';
import { useAuth } from '@/shared/auth/useAuth';
import { PERMISSIONS } from '@/shared/config/permissions';
import { debtsApi, type DebtorSummary, type DebtPaymentToday } from '@/features/debts/api';
import { PayDebtModal } from '@/features/debts/PayDebtModal';

const DUE_FILTERS = ['all', 'overdue', 'today', 'upcoming'] as const;
type DueFilter = (typeof DUE_FILTERS)[number];
const METHOD_KEY: Record<string, string> = { Cash: 'cash', Terminal: 'card', Transfer: 'transfer', Click: 'click' };

function initials(name: string): string {
  const p = name.trim().split(/\s+/);
  return ((p[0]?.[0] ?? '') + (p[1]?.[0] ?? '')).toUpperCase();
}

/** Seller debt collection: view debtors and accept payments into the shift's till. */
export default function SellerDebtsPage() {
  const { t, i18n } = useTranslation();
  const { hasPermission } = useAuth();
  const canManage = hasPermission(PERMISSIONS.debts.manage);

  const [search, setSearch] = useState('');
  const [due, setDue] = useState<DueFilter>('all');
  const [paying, setPaying] = useState<DebtorSummary | null>(null);
  const debouncedSearch = useDebounce(search);

  const summaryQuery = useQuery({ queryKey: ['debt-summary'], queryFn: debtsApi.summary });
  const debtorsQuery = useQuery({
    queryKey: ['debtors', { search: debouncedSearch, due }],
    queryFn: () => debtsApi.debtors(debouncedSearch, due === 'all' ? null : due),
  });
  const todayQuery = useQuery({ queryKey: ['debt-payments-today'], queryFn: debtsApi.todayPayments });

  const s = summaryQuery.data;

  return (
    <>
      <PageHeader title={t('seller.debts.title')} subtitle={t('seller.debts.subtitle')} />

      <div className="mx-auto flex w-full max-w-[1240px] flex-1 flex-col gap-[18px] p-8">
        <div className="grid grid-cols-3 gap-4">
          <StatCard label={t('debts.stats.total')} value={formatSum(s?.totalDebt ?? 0)} suffix={t('common.currency')} />
          <StatCard
            label={t('debts.stats.overdue')}
            value={formatSum(s?.overdueTotal ?? 0)}
            suffix={t('common.currency')}
            tone={s && s.overdueTotal > 0 ? 'danger' : 'default'}
            hint={s && s.overdueCount > 0 ? `${s.overdueCount}` : undefined}
          />
          <StatCard label={t('debts.stats.paidToday')} value={formatSum(s?.paidToday ?? 0)} suffix={t('common.currency')} />
        </div>

        <div className="flex flex-wrap items-center gap-3">
          <div className="relative min-w-[280px] flex-1">
            <Search size={17} className="absolute left-3.5 top-1/2 -translate-y-1/2 text-muted-2" />
            <input
              value={search}
              onChange={(e) => setSearch(e.target.value)}
              placeholder={t('debts.searchPlaceholder')}
              className="h-11 w-full rounded-input border border-input-border bg-surface pl-11 pr-4 text-[14px] outline-none focus:border-primary focus:shadow-focus-ring"
            />
          </div>
          <div className="flex items-center gap-1.5">
            {DUE_FILTERS.map((f) => (
              <button
                key={f}
                type="button"
                onClick={() => setDue(f)}
                className={cn(
                  'rounded-input px-3.5 py-2 text-[13px] font-medium transition-colors',
                  due === f ? 'bg-primary text-white' : 'border border-input-border bg-surface text-muted hover:text-text',
                )}
              >
                {t(`debts.filters.${f}`)}
              </button>
            ))}
          </div>
        </div>

        <Card className="overflow-hidden">
          <div className="grid grid-cols-debts items-center gap-4 border-b border-hairline bg-bg/40 px-6 py-3 text-[11.5px] font-semibold tracking-[0.4px] text-muted-2">
            <span>{t('debts.cols.customer')}</span>
            <span>{t('debts.cols.phone')}</span>
            <span className="text-center">{t('debts.cols.debts')}</span>
            <span>{t('debts.cols.due')}</span>
            <span className="text-right">{t('debts.cols.sum')}</span>
            <span />
          </div>

          {debtorsQuery.isLoading ? (
            <div className="flex items-center justify-center py-20 text-primary">
              <Spinner size={24} />
            </div>
          ) : debtorsQuery.data && debtorsQuery.data.length > 0 ? (
            debtorsQuery.data.map((d) => (
              <div
                key={d.customerId}
                className="grid grid-cols-debts items-center gap-4 border-b border-hairline px-6 py-3.5 text-[13px] last:border-0 hover:bg-bg/40"
              >
                <div className="flex min-w-0 items-center gap-2.5">
                  <span className="flex h-8 w-8 flex-none items-center justify-center rounded-pill bg-primary-soft text-[11px] font-semibold text-primary">
                    {initials(d.customerName ?? d.customerPhone)}
                  </span>
                  <span className="truncate font-medium">{d.customerName ?? d.customerPhone}</span>
                  <Badge tone="neutral">
                    {t(`debts.type.${d.customerType === 'Legal' ? 'legal' : 'individual'}`)}
                  </Badge>
                </div>
                <span className="text-muted-2 nums">{d.customerPhone}</span>
                <span className="text-center text-muted nums">{d.debtCount}</span>
                <span className="nums">
                  {d.nearestDueDate ? (
                    <span className={cn(d.isOverdue && 'font-semibold text-danger')}>
                      {formatShortDate(d.nearestDueDate, i18n.language)}
                      {d.isOverdue && <span className="ml-1.5 text-[11px]">· {t('debts.overdueTag')}</span>}
                    </span>
                  ) : (
                    <span className="text-muted-2">{t('debts.noDue')}</span>
                  )}
                </span>
                <span className={cn('text-right font-semibold nums', d.isOverdue && 'text-danger')}>
                  {formatSum(d.remainingDebt)}
                </span>
                <span className="text-right">
                  {canManage && (
                    <Button size="sm" onClick={() => setPaying(d)}>
                      {t('debts.pay')}
                    </Button>
                  )}
                </span>
              </div>
            ))
          ) : (
            <div className="py-20 text-center text-[14px] text-muted-2">{t('debts.empty')}</div>
          )}
        </Card>

        {/* «Принятые сегодня» — bugungi qarz-to'lovlari */}
        {(todayQuery.data ?? []).length > 0 && (
          <Card className="overflow-hidden">
            <div className="border-b border-hairline px-6 py-4 text-[15px] font-semibold">
              {t('seller.debts.acceptedToday')}
            </div>
            <div className="grid grid-cols-[80px_1.6fr_1fr_1fr_1fr] items-center gap-4 border-b border-hairline bg-bg/40 px-6 py-2.5 text-[11.5px] font-semibold tracking-[0.4px] text-muted-2">
              <span>{t('seller.debts.acceptedCols.time')}</span>
              <span>{t('seller.debts.acceptedCols.customer')}</span>
              <span>{t('seller.debts.acceptedCols.method')}</span>
              <span className="text-right">{t('seller.debts.acceptedCols.remaining')}</span>
              <span className="text-right">{t('seller.debts.acceptedCols.amount')}</span>
            </div>
            {(todayQuery.data ?? []).map((p, i) => (
              <AcceptedRow key={i} p={p} />
            ))}
          </Card>
        )}
      </div>

      <PayDebtModal debtor={paying} onClose={() => setPaying(null)} />
    </>
  );
}

function AcceptedRow({ p }: { p: DebtPaymentToday }) {
  const { t } = useTranslation();
  return (
    <div className="grid grid-cols-[80px_1.6fr_1fr_1fr_1fr] items-center gap-4 border-b border-hairline px-6 py-3 text-[13px] last:border-0">
      <span className="text-muted-2 nums">{formatTime(p.at)}</span>
      <span className="truncate font-medium">{p.customerName ?? p.customerPhone ?? '—'}</span>
      <span>
        <Badge tone={p.method === 'Cash' ? 'success' : 'info'}>{t(`sales.payment.${METHOD_KEY[p.method] ?? 'cash'}` as never)}</Badge>
      </span>
      <span className="text-right text-muted-2 nums">{p.remainingDebt > 0 ? formatSum(p.remainingDebt) : '—'}</span>
      <span className="text-right font-semibold text-success nums">+ {formatSum(p.amount)}</span>
    </div>
  );
}
