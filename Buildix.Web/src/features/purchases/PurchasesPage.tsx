import { useMemo, useState } from 'react';
import { Link, useParams } from 'react-router-dom';
import { useQuery, keepPreviousData } from '@tanstack/react-query';
import { useTranslation } from 'react-i18next';
import { startOfMonth } from 'date-fns';
import { Plus, FileDown, Truck } from 'lucide-react';
import { PageHeader, Button, Card, StatCard, Badge, Spinner } from '@/shared/ui';
import { cn } from '@/shared/lib/cn';
import { formatSum, formatQty, formatShortDate } from '@/shared/lib/format';
import { unitLabel } from '@/shared/lib/units';
import { useExport } from '@/shared/hooks/useExport';
import { useAuth } from '@/shared/auth/useAuth';
import { PERMISSIONS } from '@/shared/config/permissions';
import { purchasesApi, type ZakupReceipt, type ReorderSuggestion, type Supplier } from './api';
import { NewPurchaseModal } from './NewPurchaseModal';
import { ReceiptDetailModal } from './ReceiptDetailModal';

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
  const exporter = useExport(() => purchasesApi.exportReceipts(), 'purchases.xlsx');

  const [page, setPage] = useState(1);
  const [delivery, setDelivery] = useState<'all' | 'InTransit' | 'Accepted'>('all');
  const [newOpen, setNewOpen] = useState(false);
  const [openReceiptId, setOpenReceiptId] = useState<string | null>(null);

  // Month-start (Tashkent) as a UTC instant for the server-side KPI aggregate.
  const monthStart = useMemo(() => startOfMonth(new Date()).toISOString(), []);
  const summaryQuery = useQuery({
    queryKey: ['receipts', 'summary', monthStart],
    queryFn: () => purchasesApi.summary(monthStart),
  });
  const suppliersQuery = useQuery({ queryKey: ['suppliers'], queryFn: purchasesApi.suppliers });
  const reorderQuery = useQuery({ queryKey: ['reorder'], queryFn: () => purchasesApi.reorderSuggestions(6) });
  const listQuery = useQuery({
    queryKey: ['receipts', page],
    queryFn: () => purchasesApi.receiptsPaged(page, 8),
    placeholderData: keepPreviousData,
  });

  const stats = useMemo(() => {
    const suppliers = suppliersQuery.data ?? [];
    return {
      count: summaryQuery.data?.count ?? 0,
      sum: summaryQuery.data?.total ?? 0,
      supplierDebt: suppliers.reduce((s, x) => s + x.outstandingDebt, 0),
      suppliers: suppliers.length,
    };
  }, [summaryQuery.data, suppliersQuery.data]);

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
          <div className="flex items-center gap-3">
            {hasPermission(PERMISSIONS.zakup.access) && (
              <Button variant="secondary" loading={exporter.downloading} onClick={exporter.download}>
                <FileDown size={15} />
                {t('common.export')}
              </Button>
            )}
            {canCreate && (
              <Button onClick={() => setNewOpen(true)}>
                <Plus size={15} strokeWidth={2.4} />
                {t('purchases.newPurchase')}
              </Button>
            )}
          </div>
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
            {/* Delivery-status tabs (filter the loaded page). */}
            <div className="flex items-center gap-1.5 border-b border-hairline px-5 py-2.5">
              {(['all', 'InTransit', 'Accepted'] as const).map((d) => (
                <button
                  key={d}
                  type="button"
                  onClick={() => setDelivery(d)}
                  className={cn(
                    'rounded-input px-3.5 py-1.5 text-[12.5px] font-medium transition-colors',
                    delivery === d
                      ? 'bg-primary text-white'
                      : 'border border-input-border bg-surface text-muted hover:text-text',
                  )}
                >
                  {t(`purchases.delivery.${d === 'all' ? 'all' : d === 'InTransit' ? 'inTransit' : 'accepted'}`)}
                </button>
              ))}
            </div>
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
            ) : (() => {
              const items = (listQuery.data?.items ?? []).filter(
                (r) => delivery === 'all' || r.deliveryStatus === delivery,
              );
              return items.length > 0 ? (
                items.map((r) => <ReceiptRow key={r.id} receipt={r} onOpen={() => setOpenReceiptId(r.id)} />)
              ) : (
                <div className="py-16 text-center text-[14px] text-muted-2">{t('purchases.empty')}</div>
              );
            })()}
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

      <NewPurchaseModal open={newOpen} onClose={() => setNewOpen(false)} />
      <ReceiptDetailModal receiptId={openReceiptId} onClose={() => setOpenReceiptId(null)} />
    </>
  );
}

function ReceiptRow({ receipt: r, onOpen }: { receipt: ZakupReceipt; onOpen: () => void }) {
  const { t, i18n } = useTranslation();
  return (
    <button
      type="button"
      onClick={onOpen}
      className="grid w-full grid-cols-purchases items-center gap-3 border-b border-hairline px-5 py-3.5 text-left text-[13px] last:border-0 hover:bg-bg/40"
    >
      <span className="font-semibold text-primary nums">№{r.receiptNumber}</span>
      <span className="truncate font-medium">{r.supplierName ?? t('purchases.noSupplier')}</span>
      <span className="text-muted-2">{t('purchases.itemsCount', { count: r.itemCount })}</span>
      <span className="text-muted-2 nums">{formatShortDate(r.createdAt, i18n.language)}</span>
      <span className="text-right font-semibold nums">{formatSum(r.totalAmount)}</span>
      <span className="flex items-center gap-1.5">
        {r.deliveryStatus === 'InTransit' ? (
          <Badge tone="info" className="gap-1">
            <Truck size={11} />
            {t('purchases.delivery.inTransit')}
          </Badge>
        ) : (
          <Badge tone={STATUS_TONE[r.paymentStatus] ?? 'neutral'}>
            {t(`purchases.status.${STATUS_KEY[r.paymentStatus] ?? 'unpaid'}`)}
          </Badge>
        )}
      </span>
    </button>
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
                        unit: unitLabel(t, s.unit, s.unitName),
                        days: s.daysOfCover ?? 0,
                      })}
                </div>
              </div>
              <div className="whitespace-nowrap text-right text-[11.5px] text-muted-2">
                {t('purchases.reorder.perDay', { qty: formatQty(s.avgDailySales), unit: unitLabel(t, s.unit, s.unitName) })}
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
  const { subdomain } = useParams();
  const { hasPermission } = useAuth();
  const canSeeSuppliers = hasPermission(PERMISSIONS.suppliers.access);
  // Bo'sh bo'lsa ham kartani ko'rsatamiz, agar yetkazib beruvchilar sahifasiga
  // kira olsa — "boshqarish" havolasi bu yagona kirish nuqtasi (sidebar'da yo'q).
  if (suppliers.length === 0 && !canSeeSuppliers) return null;
  return (
    <Card className="p-5">
      <div className="mb-4 flex items-center justify-between">
        <h3 className="text-[15px] font-semibold">{t('purchases.topSuppliers')}</h3>
        {canSeeSuppliers && (
          <Link
            to={`/${subdomain}/suppliers`}
            className="text-[12.5px] font-semibold text-primary hover:text-primary-hover"
          >
            {t('purchases.manageSuppliers')} →
          </Link>
        )}
      </div>
      {suppliers.length === 0 ? (
        <p className="py-4 text-center text-[13px] text-muted-2">{t('suppliers.empty')}</p>
      ) : (
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
      )}
    </Card>
  );
}
