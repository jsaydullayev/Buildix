import { useState } from 'react';
import { useQuery } from '@tanstack/react-query';
import { useTranslation } from 'react-i18next';
import { Search, Download } from 'lucide-react';
import { PageHeader, Card, StatCard, Spinner, Button } from '@/shared/ui';
import { cn } from '@/shared/lib/cn';
import { formatSum, formatShortDate } from '@/shared/lib/format';
import { useDebounce } from '@/shared/hooks/useDebounce';
import { useAuth } from '@/shared/auth/useAuth';
import { PERMISSIONS } from '@/shared/config/permissions';
import { debtsApi, type DebtCheck } from './api';
import { DebtCheckModal } from './DebtCheckModal';

const DUE_FILTERS = ['all', 'overdue', 'today', 'upcoming'] as const;
type DueFilter = (typeof DUE_FILTERS)[number];

/** avatar initials from a customer name / phone. */
function initials(s: string): string {
  return s
    .trim()
    .split(/\s+/)
    .slice(0, 2)
    .map((w) => w[0]?.toUpperCase() ?? '')
    .join('') || '#';
}

const GRID = 'grid-cols-[minmax(0,1.7fr)_100px_minmax(0,1fr)_120px_180px_140px]';

export default function DebtsPage() {
  const { t, i18n } = useTranslation();
  const { hasPermission } = useAuth();
  const canManage = hasPermission(PERMISSIONS.debts.manage);

  const [search, setSearch] = useState('');
  const [due, setDue] = useState<DueFilter>('all');
  const [openCheck, setOpenCheck] = useState<DebtCheck | null>(null);
  const [downloading, setDownloading] = useState(false);
  const debouncedSearch = useDebounce(search);

  async function handleExport() {
    if (downloading) return;
    setDownloading(true);
    try {
      const blob = await debtsApi.exportExcel(i18n.language);
      const url = URL.createObjectURL(blob);
      const a = document.createElement('a');
      a.href = url;
      a.download = 'debts.xlsx';
      document.body.appendChild(a);
      a.click();
      a.remove();
      URL.revokeObjectURL(url);
    } catch {
      // best-effort; ignore
    } finally {
      setDownloading(false);
    }
  }

  const summaryQuery = useQuery({ queryKey: ['debt-summary'], queryFn: debtsApi.summary });
  const checksQuery = useQuery({
    queryKey: ['debt-checks', { search: debouncedSearch, due }],
    queryFn: () => debtsApi.checks(debouncedSearch, due === 'all' ? null : due),
  });

  const s = summaryQuery.data;
  const checks = checksQuery.data ?? [];

  return (
    <>
      <PageHeader
        title={t('debts.title')}
        subtitle={t('debts.subtitleChecks', {
          debts: checks.length,
          clients: s?.customerCount ?? 0,
          value: formatSum(s?.totalDebt ?? 0),
          overdue: formatSum(s?.overdueTotal ?? 0),
        })}
        actions={
          <Button variant="secondary" onClick={handleExport} loading={downloading}>
            {!downloading && <Download size={15} strokeWidth={2.2} />}
            {t('common.export')}
          </Button>
        }
      />

      <div className="flex flex-1 flex-col gap-[18px] p-8">
        <div className="grid grid-cols-4 gap-4">
          <StatCard label={t('debts.stats.total')} value={formatSum(s?.totalDebt ?? 0)} suffix={t('common.currency')} />
          <StatCard
            label={t('debts.stats.overdue')}
            value={formatSum(s?.overdueTotal ?? 0)}
            suffix={t('common.currency')}
            tone={s && s.overdueTotal > 0 ? 'danger' : 'default'}
            hint={s && s.overdueCount > 0 ? `${s.overdueCount}` : undefined}
          />
          <StatCard label={t('debts.stats.paidToday')} value={formatSum(s?.paidToday ?? 0)} suffix={t('common.currency')} />
          <StatCard label={t('debts.stats.paidMonth')} value={formatSum(s?.paidThisMonth ?? 0)} suffix={t('common.currency')} />
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
          <div className={cn('grid items-center gap-3 border-b border-hairline bg-bg/40 px-6 py-3 text-[11.5px] font-semibold tracking-[0.4px] text-muted-2', GRID)}>
            <span>{t('debts.cols.customer')}</span>
            <span>{t('debts.cols.check')}</span>
            <span>{t('debts.cols.items')}</span>
            <span>{t('debts.cols.due')}</span>
            <span className="text-right">{t('debts.cols.remaining')}</span>
            <span />
          </div>

          {checksQuery.isLoading ? (
            <div className="flex items-center justify-center py-20 text-primary">
              <Spinner size={24} />
            </div>
          ) : checks.length > 0 ? (
            checks.map((c) => {
              const paid = Math.max(0, c.totalDebt - c.remainingDebt);
              return (
                <button
                  key={c.debtId}
                  type="button"
                  onClick={() => setOpenCheck(c)}
                  className={cn(
                    'grid w-full items-center gap-3 border-b border-hairline px-6 py-3.5 text-left text-[13px] last:border-0 hover:bg-primary/[0.03]',
                    GRID,
                    c.isOverdue && 'bg-danger-soft/40',
                  )}
                >
                  {/* Клиент */}
                  <div className="flex min-w-0 items-center gap-3">
                    <span className="flex h-9 w-9 flex-none items-center justify-center rounded-pill bg-primary-soft text-[12px] font-semibold text-primary-hover">
                      {initials(c.customerName ?? c.customerPhone)}
                    </span>
                    <div className="min-w-0">
                      <div className="flex items-center gap-1.5">
                        <span className="truncate font-semibold">{c.customerName ?? c.customerPhone}</span>
                        {c.customerDebtCount > 1 && (
                          <span
                            title={t('debts.multiTitle')}
                            className="flex-none rounded-pill bg-primary/10 px-2 py-[1px] text-[10px] font-bold text-primary"
                          >
                            {t('debts.multi', { count: c.customerDebtCount })}
                          </span>
                        )}
                      </div>
                      <div className="truncate text-[11.5px] text-muted-2 nums">{c.customerPhone}</div>
                    </div>
                  </div>

                  {/* Чек */}
                  <div className="min-w-0">
                    <div className="font-semibold text-primary nums">{t('cash.desc.receipt', { n: c.saleNumber })}</div>
                    <div className="text-[11.5px] text-muted-2 nums">{formatShortDate(c.createdAt, i18n.language)}</div>
                  </div>

                  {/* Товары */}
                  <span className="truncate text-muted">
                    {c.itemsSummary || t('purchases.itemsCount', { count: c.itemCount })}
                  </span>

                  {/* Срок */}
                  <span className="nums">
                    {c.dueDate ? (
                      c.isOverdue ? (
                        <span className="font-semibold text-danger">{t('debts.overdueTag')}</span>
                      ) : (
                        <span className="text-muted">{formatShortDate(c.dueDate, i18n.language)}</span>
                      )
                    ) : (
                      <span className="text-muted-2">{t('debts.noDue')}</span>
                    )}
                  </span>

                  {/* Остаток долга */}
                  <div className="text-right">
                    <div className={cn('font-bold nums', c.isOverdue ? 'text-danger' : 'text-text')}>
                      {formatSum(c.remainingDebt)}
                    </div>
                    {paid > 0 ? (
                      <div className="text-[11px] font-semibold text-success nums">
                        {t('debts.paidOf', { paid: formatSum(paid), total: formatSum(c.totalDebt) })}
                      </div>
                    ) : (
                      <div className="text-[11px] text-muted-2 nums">
                        {t('debts.checkFor', { total: formatSum(c.totalDebt) })}
                      </div>
                    )}
                  </div>

                  {/* Action */}
                  <span className="flex justify-end">
                    {canManage && (
                      <span className="rounded-input bg-success px-3 py-1.5 text-[12px] font-semibold text-white">
                        {t('debts.pay')}
                      </span>
                    )}
                  </span>
                </button>
              );
            })
          ) : (
            <div className="py-20 text-center text-[14px] text-muted-2">{t('debts.empty')}</div>
          )}
        </Card>

        <p className="flex items-start gap-2 px-1 text-[12.5px] text-muted-2">
          {t('debts.perCheckHint')}
        </p>
      </div>

      <DebtCheckModal check={openCheck} canManage={canManage} onClose={() => setOpenCheck(null)} />
    </>
  );
}
