import { useMemo, useState } from 'react';
import { useQuery, keepPreviousData } from '@tanstack/react-query';
import { useTranslation } from 'react-i18next';
import { startOfMonth } from 'date-fns';
import { Plus } from 'lucide-react';
import { PageHeader, Button, Card, StatCard, Badge, Spinner } from '@/shared/ui';
import { formatSum, formatQty, formatShortDate } from '@/shared/lib/format';
import { useAuth } from '@/shared/auth/useAuth';
import { PERMISSIONS } from '@/shared/config/permissions';
import { purchasesApi, type ZakupReceipt, type ReorderSuggestion, type Supplier } from './api';

const STATUS_TONE: Record<string, 'success' | 'warn' | 'danger'> = {
  Paid: 'success',
  Partial: 'warn',
  Unpaid: 'danger',
};
const STATUS_KEY: Record<string, 'paid' | 'partial' | 'unpaid'> = {
  Paid: 'paid',
  Partial: 'partial',
  Unpaid: 'unpaid',
};

export default function PurchasesPage() {
  const { t } = useTranslation();
  const { hasPermission } = useAuth();
  const canCreate = hasPermission(PERMISSIONS.zakup.create);

  const [page, setPage] = useState(1);

  const allReceiptsQuery = useQuery({ queryKey: ['receipts-all'], queryFn: purchasesApi.allReceipts });
  const suppliersQuery = useQuery({ queryKey: ['suppliers'], queryFn: purchasesApi.suppliers });
  const reorderQuery = useQuery({ queryKey: ['reorder'], queryFn: () => purchasesApi.reorderSuggestions(6) });
  const listQuery = useQuery({
    queryKey: ['receipts', page],
    queryFn: () => purchasesApi.receiptsPaged(page, 8),
    placeholderData: keepPreviousData,
  });

  const stats = useMemo(() => {
    const all = allReceiptsQuery.data ?? [];
    const monthStart = startOfMonth(new Date()).toISOString();
    const month = all.filter((r) => r.createdAt >= monthStart);
    const suppliers = suppliersQuery.data ?? [];
    return {
      count: month.length,
      sum: month.reduce((s, r) => s + r.totalAmount, 0),
      supplierDebt: suppliers.reduce((s, x) => s + x.outstandingDebt, 0),
      suppliers: suppliers.length,
    };
  }, [allReceiptsQuery.data, suppliersQuery.data]);

  const topSuppliers = useMemo(
    () =>
      (suppliersQuery.data ?? [])
        .slice()
        .sort((a, b) => b.outstandingDebt - a.outstandingDebt || b.receiptCount - a.receiptCount)
        .slice(0, 4),
    [suppliersQuery.data],
  );

  return (
    <>
      <PageHeader
        title={t('purchases.title')}
        subtitle={t('purchases.subtitle')}
        actions={
          canCreate && (
            <Button>
              <Plus size={15} strokeWidth={2.4} />
              {t('purchases.newPurchase')}
            </Button>
          )
        }
      />

      <div className="flex flex-1 flex-col gap-[18px] p-8">
        <div className="grid grid-cols-4 gap-4">
          <StatCard label={t('purchases.stats.count')} value={stats.count} />
          <StatCard label={t('purchases.stats.sum')} value={formatSum(stats.sum)} suffix={t('common.currency')} />
          <StatCard label={t('purchases.stats.suppliers')} value={stats.suppliers} />
          <StatCard
            label={t('purchases.stats.supplierDebt')}
            value={formatSum(stats.supplierDebt)}
            suffix={t('common.currency')}
            tone={stats.supplierDebt > 0 ? 'warn' : 'default'}
          />
        </div>

        <div className="grid grid-cols-[2fr_1fr] items-start gap-[18px]">
          {/* Receipts */}
          <Card className="overflow-hidden">
            <div className="grid grid-cols-purchases items-center gap-3 border-b border-hairline bg-bg/40 px-5 py-3 text-[11.5px] font-semibold tracking-[0.4px] text-muted-2">
              <span>{t('purchases.cols.number')}</span>
              <span>{t('purchases.cols.supplier')}</span>
              <span>{t('purchases.cols.items')}</span>
              <span>{t('purchases.cols.date')}</span>
              <span className="text-right">{t('purchases.cols.sum')}</span>
              <span>{t('purchases.cols.status')}</span>
            </div>
            {listQuery.isLoading ? (
              <div className="flex items-center justify-center py-20 text-primary">
                <Spinner size={24} />
              </div>
            ) : listQuery.data && listQuery.data.items.length > 0 ? (
              listQuery.data.items.map((r) => <ReceiptRow key={r.id} receipt={r} />)
            ) : (
              <div className="py-16 text-center text-[14px] text-muted-2">{t('purchases.empty')}</div>
            )}
            {listQuery.data && listQuery.data.totalPages > 1 && (
              <div className="flex items-center justify-end gap-1.5 border-t border-hairline px-5 py-3">
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
            )}
          </Card>

          {/* Sidebar */}
          <div className="flex flex-col gap-[18px]">
            <ReorderCard suggestions={reorderQuery.data ?? []} loading={reorderQuery.isLoading} />
            <TopSuppliersCard suppliers={topSuppliers} />
          </div>
        </div>
      </div>
    </>
  );
}

function ReceiptRow({ receipt: r }: { receipt: ZakupReceipt }) {
  const { t, i18n } = useTranslation();
  return (
    <div className="grid grid-cols-purchases items-center gap-3 border-b border-hairline px-5 py-3.5 text-[13px] last:border-0 hover:bg-bg/40">
      <span className="font-semibold text-primary nums">№{r.receiptNumber}</span>
      <span className="truncate font-medium">{r.supplierName ?? t('purchases.noSupplier')}</span>
      <span className="text-muted-2">{t('purchases.itemsCount', { count: r.itemCount })}</span>
      <span className="text-muted-2 nums">{formatShortDate(r.createdAt, i18n.language)}</span>
      <span className="text-right font-semibold nums">{formatSum(r.totalAmount)}</span>
      <span>
        <Badge tone={STATUS_TONE[r.paymentStatus] ?? 'neutral'}>
          {t(`purchases.status.${STATUS_KEY[r.paymentStatus] ?? 'unpaid'}`)}
        </Badge>
      </span>
    </div>
  );
}

function ReorderCard({ suggestions, loading }: { suggestions: ReorderSuggestion[]; loading: boolean }) {
  const { t } = useTranslation();
  return (
    <Card className="p-5">
      <div className="mb-4 flex items-center justify-between">
        <h3 className="text-[15px] font-semibold">{t('purchases.reorder.title')}</h3>
        {suggestions.length > 0 && (
          <Badge tone="danger">{t('purchases.reorder.positions', { count: suggestions.length })}</Badge>
        )}
      </div>
      {loading ? (
        <div className="flex justify-center py-8 text-primary">
          <Spinner size={20} />
        </div>
      ) : suggestions.length === 0 ? (
        <p className="py-6 text-center text-[13px] text-muted-2">{t('purchases.reorder.empty')}</p>
      ) : (
        <div className="flex flex-col gap-3.5">
          {suggestions.map((s) => (
            <div key={s.productId} className="flex items-start justify-between gap-3">
              <div className="min-w-0">
                <div className="truncate text-[13.5px] font-medium">{s.name}</div>
                <div className="mt-0.5 text-[11.5px] font-medium text-warn-strong">
                  {s.currentQty <= 0
                    ? t('purchases.reorder.outOfStock')
                    : t('purchases.reorder.daysLeft', {
                        qty: formatQty(s.currentQty),
                        unit: s.unitName,
                        days: s.daysOfCover ?? 0,
                      })}
                </div>
              </div>
              <div className="whitespace-nowrap text-right text-[11.5px] text-muted-2">
                {t('purchases.reorder.perDay', { qty: formatQty(s.avgDailySales), unit: s.unitName })}
              </div>
            </div>
          ))}
        </div>
      )}
    </Card>
  );
}

function TopSuppliersCard({ suppliers }: { suppliers: Supplier[] }) {
  const { t } = useTranslation();
  if (suppliers.length === 0) return null;
  return (
    <Card className="p-5">
      <h3 className="mb-4 text-[15px] font-semibold">{t('purchases.topSuppliers')}</h3>
      <div className="flex flex-col">
        {suppliers.map((s) => (
          <div key={s.id} className="flex items-center justify-between gap-3 border-b border-hairline py-2.5 last:border-0">
            <div className="flex items-center gap-2.5">
              <div className="flex h-8 w-8 items-center justify-center rounded-pill bg-primary/10 text-[11px] font-semibold uppercase text-primary">
                {s.name.slice(0, 2)}
              </div>
              <span className="text-[13px] font-medium">{s.name}</span>
            </div>
            <span className="text-[13px] font-semibold nums">{formatSum(s.outstandingDebt)}</span>
          </div>
        ))}
      </div>
    </Card>
  );
}
