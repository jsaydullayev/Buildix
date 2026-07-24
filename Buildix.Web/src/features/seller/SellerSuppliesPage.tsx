import { useQuery, keepPreviousData } from '@tanstack/react-query';
import { useTranslation } from 'react-i18next';
import { useState } from 'react';
import { Lock, PackageCheck, Truck } from 'lucide-react';
import { PageHeader, Card, Badge, Spinner } from '@/shared/ui';
import { cn } from '@/shared/lib/cn';
import { formatShortDate, formatTime } from '@/shared/lib/format';
import { purchasesApi } from '@/features/purchases/api';

const PAGE_SIZE = 20;
const GRID = 'grid-cols-[90px_1.6fr_1fr_1fr_120px]';

/**
 * Seller view of incoming goods — read-only.
 *
 * Deliveries are recorded by the administrator and, in the current model, a
 * receipt IS the goods arriving: creating it adds the stock. So every row here
 * is already received; the design's in-transit / "начать приёмку" pipeline needs
 * a delivery lifecycle the backend does not have yet (see
 * docs/SELLER-INTEGRATION-PLAN.md §3.4 PV1) and is deliberately not faked.
 *
 * Amounts are stripped server-side for anyone without data.costPrice
 * (ZakupRoleShaper), so this screen shows quantities only.
 */
export default function SellerSuppliesPage() {
  const { t, i18n } = useTranslation();
  const [page, setPage] = useState(1);

  const listQuery = useQuery({
    queryKey: ['seller-supplies', page],
    queryFn: () => purchasesApi.receiptsPaged(page, PAGE_SIZE),
    placeholderData: keepPreviousData,
  });

  const rows = listQuery.data?.items ?? [];

  return (
    <>
      <PageHeader title={t('seller.supplies.title')} subtitle={t('seller.supplies.subtitle')} />

      <div className="mx-auto flex w-full max-w-[1240px] flex-1 flex-col gap-[18px] p-8">
        <div className="flex items-center gap-2 rounded-input border border-border bg-surface px-4 py-2.5 text-[12.5px] text-muted">
          <Lock size={14} className="flex-none text-muted-2" />
          {t('seller.supplies.readOnly')}
        </div>

        <Card className="overflow-hidden">
          <div className={cn('grid items-center gap-4 border-b border-hairline bg-bg/40 px-6 py-3 text-[11.5px] font-semibold tracking-[0.4px] text-muted-2', GRID)}>
            <span>{t('purchases.cols.number')}</span>
            <span>{t('purchases.cols.supplier')}</span>
            <span className="text-center">{t('purchases.cols.items')}</span>
            <span>{t('purchases.cols.date')}</span>
            <span className="text-right">{t('purchases.cols.status')}</span>
          </div>

          {listQuery.isLoading ? (
            <div className="flex items-center justify-center py-20 text-primary">
              <Spinner size={24} />
            </div>
          ) : rows.length > 0 ? (
            rows.map((r) => (
              <div
                key={r.id}
                className={cn('grid items-center gap-4 border-b border-hairline px-6 py-3.5 text-[13px] last:border-0 hover:bg-bg/40', GRID)}
              >
                <span className="font-semibold text-primary nums">№{r.receiptNumber}</span>
                <span className="truncate font-medium">{r.supplierName ?? t('purchases.noSupplier')}</span>
                <span className="text-center text-muted nums">{r.itemCount}</span>
                <span className="text-muted-2 nums">
                  {formatShortDate(r.createdAt, i18n.language)} · {formatTime(r.createdAt)}
                </span>
                <span className="text-right">
                  {r.deliveryStatus === 'InTransit' ? (
                    <Badge tone="info" className="gap-1">
                      <Truck size={13} />
                      {t('purchases.delivery.inTransit')}
                    </Badge>
                  ) : (
                    <Badge tone="success" className="gap-1">
                      <PackageCheck size={13} />
                      {t('seller.supplies.received')}
                    </Badge>
                  )}
                </span>
              </div>
            ))
          ) : (
            <div className="py-20 text-center text-[14px] text-muted-2">{t('purchases.empty')}</div>
          )}
        </Card>

        {listQuery.data && listQuery.data.totalPages > 1 && (
          <div className="flex items-center justify-between">
            <span className="text-[12.5px] text-muted-2">
              {t('sales.showing', { shown: rows.length, total: listQuery.data.total })}
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
