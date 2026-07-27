import { useEffect, useRef, useState } from 'react';
import { useNavigate, useParams } from 'react-router-dom';
import { useQuery, useMutation, useQueryClient, keepPreviousData } from '@tanstack/react-query';
import { useTranslation } from 'react-i18next';
import { Plus, Move3d, Search, FileDown, ChevronRight } from 'lucide-react';
import { PageHeader, Button, Card, StatCard, Badge, Spinner } from '@/shared/ui';
import { cn } from '@/shared/lib/cn';
import { formatQty, formatShortDate } from '@/shared/lib/format';
import { unitLabel } from '@/shared/lib/units';
import { useDebounce } from '@/shared/hooks/useDebounce';
import { useExport } from '@/shared/hooks/useExport';
import { useAuth } from '@/shared/auth/useAuth';
import { PERMISSIONS, ROLES } from '@/shared/config/permissions';
import { productsApi, type Product } from './api';
import { StocktakeModal } from './StocktakeModal';
import { StockMovementModal } from './StockMovementModal';

const PAGE_SIZE = 30;
type StatusFilter = 'all' | 'ok' | 'low' | 'out';

/**
 * Склад — stock & movement only. Prices/margin moved to Товары (A2); this screen
 * is about what's on hand and how it got there: a status filter, an inline
 * min-threshold, and a per-row movement ledger (Приход / Продажа / Корректировка).
 */
export default function WarehousePage() {
  const { t, i18n } = useTranslation();
  const { subdomain } = useParams();
  const navigate = useNavigate();
  const { hasPermission, hasRole } = useAuth();
  const canEdit = hasPermission(PERMISSIONS.products.edit);
  const canExport = hasPermission(PERMISSIONS.products.export);
  const canStocktake = hasRole(ROLES.Owner, ROLES.SuperAdmin) && hasPermission(PERMISSIONS.products.edit);
  const canPurchase = hasPermission(PERMISSIONS.zakup.access);
  const exporter = useExport(() => productsApi.exportExcel(i18n.language), 'products.xlsx');

  const [search, setSearch] = useState('');
  const [status, setStatus] = useState<StatusFilter>('all');
  const [page, setPage] = useState(1);
  const [stocktakeOpen, setStocktakeOpen] = useState(false);
  const [moving, setMoving] = useState<Product | null>(null);
  const debouncedSearch = useDebounce(search);
  const resetPage = () => setPage(1);

  const summaryQuery = useQuery({ queryKey: ['products', 'summary'], queryFn: productsApi.summary });
  const listQuery = useQuery({
    queryKey: ['products', 'warehouse', { page, search: debouncedSearch, status }],
    queryFn: () =>
      productsApi.listPaged({
        page,
        size: PAGE_SIZE,
        search: debouncedSearch,
        // The "low" filter is server-side; ok/out are refined client-side below
        // (a dedicated status param isn't worth a backend round unless paging hurts).
        lowStockOnly: status === 'low',
        includeHidden: true,
      }),
    placeholderData: keepPreviousData,
  });

  const rows = (listQuery.data?.items ?? []).filter((p) => {
    if (status === 'out') return p.quantity <= 0;
    if (status === 'ok') return p.quantity > p.minThreshold;
    return true;
  });

  const stats = {
    positions: summaryQuery.data?.positions ?? 0,
    low: summaryQuery.data?.lowStock ?? 0,
    out: summaryQuery.data?.outOfStock ?? 0,
  };

  const FILTERS: StatusFilter[] = ['all', 'ok', 'low', 'out'];

  return (
    <>
      <PageHeader
        title={t('warehouse.title')}
        subtitle={t('warehouse.subtitleShort', { count: stats.positions, low: stats.low, out: stats.out })}
        actions={
          <>
            {canExport && (
              <Button variant="secondary" loading={exporter.downloading} onClick={exporter.download}>
                <FileDown size={15} />
                {t('common.export')}
              </Button>
            )}
            {canStocktake && (
              <Button variant="secondary" onClick={() => setStocktakeOpen(true)}>
                <Move3d size={15} />
                {t('warehouse.stocktake')}
              </Button>
            )}
            {canPurchase && (
              <Button onClick={() => navigate(`/${subdomain}/purchases`)}>
                <Plus size={15} strokeWidth={2.4} />
                {t('warehouse.newPurchase')}
              </Button>
            )}
          </>
        }
      />

      <div className="flex flex-1 flex-col gap-[18px] p-8">
        <div className="grid grid-cols-3 gap-4">
          <StatCard label={t('warehouse.stats.positions')} value={stats.positions} />
          <StatCard label={t('warehouse.stats.low')} value={stats.low} tone="warn" />
          <StatCard label={t('warehouse.stats.out')} value={stats.out} tone="danger" />
        </div>

        {/* Toolbar */}
        <div className="flex flex-wrap items-center gap-3">
          <div className="relative min-w-[280px] flex-1">
            <Search size={17} className="absolute left-3.5 top-1/2 -translate-y-1/2 text-muted-2" />
            <input
              value={search}
              onChange={(e) => {
                setSearch(e.target.value);
                resetPage();
              }}
              placeholder={t('warehouse.searchPlaceholder')}
              className="h-11 w-full rounded-input border border-input-border bg-surface pl-11 pr-4 text-[14px] outline-none focus:border-primary focus:shadow-focus-ring"
            />
          </div>
          <div className="inline-flex rounded-input bg-hairline p-1">
            {FILTERS.map((f) => (
              <button
                key={f}
                type="button"
                onClick={() => {
                  setStatus(f);
                  resetPage();
                }}
                className={cn(
                  'rounded-md px-4 py-1.5 text-[13px] font-medium transition-colors',
                  status === f ? 'bg-surface text-text shadow-card' : 'text-muted hover:text-text',
                )}
              >
                {t(`warehouse.filter.${f}`)}
              </button>
            ))}
          </div>
        </div>

        {/* Table */}
        <Card className="overflow-hidden">
          <div className="grid grid-cols-[2.2fr_0.9fr_1.1fr_0.9fr_1fr_0.3fr] items-center gap-4 border-b border-hairline bg-bg/40 px-6 py-3 text-[11.5px] font-semibold tracking-[0.4px] text-muted-2">
            <span>{t('warehouse.cols.product')}</span>
            <span className="text-right">{t('warehouse.cols.stock')}</span>
            <span className="text-right">{t('warehouse.cols.minStock')}</span>
            <span>{t('warehouse.cols.status')}</span>
            <span>{t('warehouse.cols.lastReceipt')}</span>
            <span />
          </div>

          {listQuery.isLoading ? (
            <div className="flex items-center justify-center py-20 text-primary">
              <Spinner size={24} />
            </div>
          ) : rows.length > 0 ? (
            rows.map((p) => (
              <WarehouseRow key={p.id} product={p} canEdit={canEdit} onOpen={() => setMoving(p)} />
            ))
          ) : (
            <div className="py-20 text-center text-[14px] text-muted-2">{t('warehouse.empty')}</div>
          )}
        </Card>

        <p className="text-[12px] text-muted-2">{t('warehouse.movementHint')}</p>

        {listQuery.data && listQuery.data.totalPages > 1 && (
          <div className="flex items-center justify-end gap-1.5">
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
        )}
      </div>

      <StocktakeModal open={stocktakeOpen} onClose={() => setStocktakeOpen(false)} />
      <StockMovementModal product={moving} open={!!moving} onClose={() => setMoving(null)} />
    </>
  );
}

function WarehouseRow({
  product: p,
  canEdit,
  onOpen,
}: {
  product: Product;
  canEdit: boolean;
  onOpen: () => void;
}) {
  const { t, i18n } = useTranslation();
  const qc = useQueryClient();
  const unit = unitLabel(t, p.unit, p.unitName);
  const stockTone = p.quantity <= 0 ? 'text-danger' : p.isLowStock ? 'text-warn-strong' : 'text-text';

  // Inline min-threshold, committed on blur when changed. 0 bo'sh ko'rinadi —
  // foydalanuvchi son yozishdan oldin «0» ni o'chirib o'tirmasin.
  const [min, setMin] = useState(p.minThreshold === 0 ? '' : String(p.minThreshold));
  useEffect(() => setMin(p.minThreshold === 0 ? '' : String(p.minThreshold)), [p.minThreshold]);

  const patch = useMutation({
    mutationFn: (minThreshold: number) => productsApi.patch(p.id, { minThreshold }),
    onSuccess: () => void qc.invalidateQueries({ queryKey: ['products'] }),
  });
  // Escape bekor qiladi. `blur()` onBlur'ni SINXRON chaqiradi — commit hali
  // eski (yozilgan) qiymatni ko'rib turadi va uni saqlab yuborardi; bayroq
  // shu poygani uzadi.
  const cancelRef = useRef(false);
  const commitMin = () => {
    if (cancelRef.current) {
      cancelRef.current = false;
      setMin(p.minThreshold === 0 ? '' : String(p.minThreshold));
      return;
    }
    const next = Math.max(0, Number(min) || 0);
    if (next !== p.minThreshold) patch.mutate(next);
    else setMin(p.minThreshold === 0 ? '' : String(p.minThreshold));
  };

  return (
    <div className="grid grid-cols-[2.2fr_0.9fr_1.1fr_0.9fr_1fr_0.3fr] items-center gap-4 border-b border-hairline px-6 py-2.5 text-[13px] last:border-0 hover:bg-bg/40">
      <button type="button" onClick={onOpen} className="flex min-w-0 flex-col items-start text-left">
        <span className="truncate font-medium">{p.name}</span>
        <span className="truncate text-[11.5px] text-muted-2">
          {p.sku && <span className="font-medium text-muted">{p.sku} · </span>}
          {p.categoryName ?? '—'}
        </span>
      </button>

      <span className={cn('text-right font-semibold nums', stockTone)}>
        {formatQty(p.quantity)} {unit}
      </span>

      <div className="flex justify-end">
        {canEdit ? (
          <input
            type="number"
            step="any"
            min="0"
            placeholder="0"
            value={min}
            disabled={patch.isPending}
            onFocus={(e) => e.currentTarget.select()}
            onChange={(e) => setMin(e.target.value)}
            onBlur={commitMin}
            onKeyDown={(e) => {
              if (e.key === 'Enter') e.currentTarget.blur();
              if (e.key === 'Escape') {
                cancelRef.current = true;
                e.currentTarget.blur();
              }
            }}
            className="h-9 w-[112px] rounded-input border border-input-border bg-surface px-3 text-right text-[13px] outline-none focus:border-primary disabled:opacity-50 nums"
          />
        ) : (
          <span className="text-muted nums">
            {formatQty(p.minThreshold)} {unit}
          </span>
        )}
      </div>

      <span>
        {p.quantity <= 0 ? (
          <Badge tone="danger">{t('warehouse.status.out')}</Badge>
        ) : p.isLowStock ? (
          <Badge tone="warn">{t('warehouse.status.low')}</Badge>
        ) : (
          <Badge tone="success">{t('warehouse.status.inStock')}</Badge>
        )}
      </span>

      <span className="min-w-0">
        {p.lastReceiptAt ? (
          <span className="flex flex-col">
            <span className="text-muted nums">{formatShortDate(p.lastReceiptAt, i18n.language)}</span>
            {p.lastReceiptNumber != null && (
              <span className="text-[11px] text-muted-2 nums">З-{p.lastReceiptNumber}</span>
            )}
          </span>
        ) : (
          <span className="text-muted-2">—</span>
        )}
      </span>

      <button type="button" onClick={onOpen} className="flex justify-end text-muted-2 hover:text-text">
        <ChevronRight size={17} />
      </button>
    </div>
  );
}
