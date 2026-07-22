import { useMemo, useState } from 'react';
import { useQuery, keepPreviousData } from '@tanstack/react-query';
import { useTranslation } from 'react-i18next';
import { Plus, Move3d, Package, Search } from 'lucide-react';
import { PageHeader, Button, Card, StatCard, Badge, Spinner } from '@/shared/ui';
import { cn } from '@/shared/lib/cn';
import { formatSum, formatQty } from '@/shared/lib/format';
import { useDebounce } from '@/shared/hooks/useDebounce';
import { useAuth } from '@/shared/auth/useAuth';
import { PERMISSIONS, ROLES } from '@/shared/config/permissions';
import { productsApi, categoriesApi, type Product } from './api';
import { ProductFormModal } from './ProductFormModal';
import { StocktakeModal } from './StocktakeModal';

const PAGE_SIZE = 20;

export default function WarehousePage() {
  const { t } = useTranslation();
  const { hasPermission, hasRole } = useAuth();
  const canViewCost = hasPermission(PERMISSIONS.data.costPrice);
  const canCreate = hasPermission(PERMISSIONS.products.create);
  const canEdit = hasPermission(PERMISSIONS.products.edit);
  const canStocktake = hasRole(ROLES.Owner, ROLES.SuperAdmin) && hasPermission(PERMISSIONS.products.edit);

  const [search, setSearch] = useState('');
  const [categoryId, setCategoryId] = useState<number | null>(null);
  const [lowOnly, setLowOnly] = useState(false);
  const [page, setPage] = useState(1);
  const [formOpen, setFormOpen] = useState(false);
  const [stocktakeOpen, setStocktakeOpen] = useState(false);
  const [editing, setEditing] = useState<Product | null>(null);
  const debouncedSearch = useDebounce(search);

  const openCreate = () => {
    setEditing(null);
    setFormOpen(true);
  };
  const openEdit = (p: Product) => {
    setEditing(p);
    setFormOpen(true);
  };

  const categoriesQuery = useQuery({ queryKey: ['categories'], queryFn: categoriesApi.list });
  const statsQuery = useQuery({ queryKey: ['products-all'], queryFn: productsApi.listAll });

  const listQuery = useQuery({
    queryKey: ['products', { page, search: debouncedSearch, categoryId, lowOnly }],
    queryFn: () =>
      productsApi.listPaged({
        page,
        size: PAGE_SIZE,
        search: debouncedSearch,
        categoryId,
        lowStockOnly: lowOnly,
      }),
    placeholderData: keepPreviousData,
  });

  const stats = useMemo(() => {
    const all = statsQuery.data ?? [];
    return {
      positions: all.length,
      value: all.reduce((s, p) => s + p.costPrice * p.quantity, 0),
      low: all.filter((p) => p.isLowStock && p.quantity > 0).length,
      out: all.filter((p) => p.quantity <= 0).length,
    };
  }, [statsQuery.data]);

  const resetPage = () => setPage(1);

  return (
    <>
      <PageHeader
        title={t('warehouse.title')}
        subtitle={t('warehouse.subtitle', {
          count: stats.positions,
          value: formatSum(stats.value),
        })}
        actions={
          <>
            {canStocktake && (
              <Button variant="secondary" onClick={() => setStocktakeOpen(true)}>
                <Move3d size={15} />
                {t('warehouse.stocktake')}
              </Button>
            )}
            {canCreate && (
              <Button onClick={openCreate}>
                <Plus size={15} strokeWidth={2.4} />
                {t('warehouse.addProduct')}
              </Button>
            )}
          </>
        }
      />

      <div className="flex flex-1 flex-col gap-[18px] p-8">
        {/* Stat cards */}
        <div className={cn('grid gap-4', canViewCost ? 'grid-cols-4' : 'grid-cols-3')}>
          <StatCard label={t('warehouse.stats.positions')} value={stats.positions} />
          {canViewCost && (
            <StatCard
              label={t('warehouse.stats.value')}
              value={formatSum(stats.value)}
              suffix={t('common.currency')}
            />
          )}
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

          <div className="flex items-center gap-1.5">
            <CategoryChip
              label={t('warehouse.allCategories')}
              active={categoryId === null}
              onClick={() => {
                setCategoryId(null);
                resetPage();
              }}
            />
            {(categoriesQuery.data ?? []).map((c) => (
              <CategoryChip
                key={c.id}
                label={c.name}
                active={categoryId === c.id}
                onClick={() => {
                  setCategoryId(c.id);
                  resetPage();
                }}
              />
            ))}
          </div>

          <label className="flex cursor-pointer select-none items-center gap-2 rounded-input border border-input-border bg-surface px-3.5 py-2.5 text-[13px]">
            <input
              type="checkbox"
              checked={lowOnly}
              onChange={(e) => {
                setLowOnly(e.target.checked);
                resetPage();
              }}
              className="h-4 w-4 accent-primary"
            />
            {t('warehouse.onlyLow')}
          </label>
        </div>

        {/* Table */}
        <Card className="overflow-hidden">
          <div
            className={cn(
              'grid items-center gap-4 border-b border-hairline bg-bg/40 px-6 py-3 text-[11.5px] font-semibold tracking-[0.4px] text-muted-2',
              canViewCost ? 'grid-cols-warehouse' : 'grid-cols-warehouse-nocost',
            )}
          >
            <span>{t('warehouse.cols.product')}</span>
            <span>{t('warehouse.cols.sku')}</span>
            <span>{t('warehouse.cols.category')}</span>
            <span className="text-right">{t('warehouse.cols.stock')}</span>
            {canViewCost && <span className="text-right">{t('warehouse.cols.cost')}</span>}
            <span className="text-right">{t('warehouse.cols.price')}</span>
            <span>{t('warehouse.cols.status')}</span>
          </div>

          {listQuery.isLoading ? (
            <div className="flex items-center justify-center py-20 text-primary">
              <Spinner size={24} />
            </div>
          ) : listQuery.data && listQuery.data.items.length > 0 ? (
            listQuery.data.items.map((p) => (
              <ProductRow
                key={p.id}
                product={p}
                canViewCost={canViewCost}
                onClick={canEdit ? () => openEdit(p) : undefined}
              />
            ))
          ) : (
            <div className="py-20 text-center text-[14px] text-muted-2">{t('warehouse.empty')}</div>
          )}
        </Card>

        {/* Footer / pagination */}
        {listQuery.data && listQuery.data.total > 0 && (
          <div className="flex items-center justify-between">
            <span className="text-[12.5px] text-muted-2">
              {t('warehouse.showing', {
                shown: listQuery.data.items.length,
                total: listQuery.data.total,
              })}
            </span>
            <Pager
              page={page}
              totalPages={listQuery.data.totalPages}
              onChange={setPage}
              busy={listQuery.isFetching}
            />
          </div>
        )}
      </div>

      <ProductFormModal
        open={formOpen}
        onClose={() => setFormOpen(false)}
        product={editing}
        categories={categoriesQuery.data ?? []}
      />
      <StocktakeModal open={stocktakeOpen} onClose={() => setStocktakeOpen(false)} />
    </>
  );
}

function CategoryChip({
  label,
  active,
  onClick,
}: {
  label: string;
  active: boolean;
  onClick: () => void;
}) {
  return (
    <button
      type="button"
      onClick={onClick}
      className={cn(
        'rounded-input px-3.5 py-2 text-[13px] font-medium transition-colors',
        active ? 'bg-primary text-white' : 'bg-surface text-muted border border-input-border hover:text-text',
      )}
    >
      {label}
    </button>
  );
}

function ProductRow({
  product: p,
  canViewCost,
  onClick,
}: {
  product: Product;
  canViewCost: boolean;
  onClick?: () => void;
}) {
  const { t } = useTranslation();
  const imgSrc = p.imageUrl ? `/api${p.imageUrl}` : null;
  const stockTone = p.quantity <= 0 ? 'text-danger' : p.isLowStock ? 'text-warn-strong' : 'text-text';

  return (
    <div
      onClick={onClick}
      className={cn(
        'grid items-center gap-4 border-b border-hairline px-6 py-3.5 text-[13px] last:border-0 hover:bg-bg/40',
        onClick && 'cursor-pointer',
        canViewCost ? 'grid-cols-warehouse' : 'grid-cols-warehouse-nocost',
      )}
    >
      <div className="flex min-w-0 items-center gap-3">
        <div className="flex h-9 w-9 flex-none items-center justify-center overflow-hidden rounded-lg bg-hairline text-muted-2">
          {imgSrc ? (
            <img src={imgSrc} alt="" className="h-full w-full object-cover" />
          ) : (
            <Package size={16} />
          )}
        </div>
        <span className="truncate font-medium">{p.name}</span>
      </div>
      <span className="truncate text-muted-2">{p.sku ?? '—'}</span>
      <span className="truncate text-muted">{p.categoryName ?? '—'}</span>
      <span className={cn('text-right font-semibold nums', stockTone)}>
        {formatQty(p.quantity)} {p.unitName}
      </span>
      {canViewCost && <span className="text-right text-muted nums">{formatSum(p.costPrice)}</span>}
      <span className="text-right font-semibold nums">{formatSum(p.salePrice)}</span>
      <span>
        {p.quantity <= 0 ? (
          <Badge tone="danger">{t('warehouse.status.out')}</Badge>
        ) : p.isLowStock ? (
          <Badge tone="warn">{t('warehouse.status.low')}</Badge>
        ) : (
          <Badge tone="success">{t('warehouse.status.inStock')}</Badge>
        )}
      </span>
    </div>
  );
}

function Pager({
  page,
  totalPages,
  onChange,
  busy,
}: {
  page: number;
  totalPages: number;
  onChange: (p: number) => void;
  busy: boolean;
}) {
  if (totalPages <= 1) return null;
  return (
    <div className="flex items-center gap-1.5">
      {busy && <Spinner size={14} className="mr-1 text-primary" />}
      <button
        type="button"
        disabled={page <= 1}
        onClick={() => onChange(page - 1)}
        className="h-8 rounded-md border border-input-border bg-surface px-3 text-[13px] disabled:opacity-40"
      >
        ‹
      </button>
      <span className="px-2 text-[13px] text-muted nums">
        {page} / {totalPages}
      </span>
      <button
        type="button"
        disabled={page >= totalPages}
        onClick={() => onChange(page + 1)}
        className="h-8 rounded-md border border-input-border bg-surface px-3 text-[13px] disabled:opacity-40"
      >
        ›
      </button>
    </div>
  );
}
