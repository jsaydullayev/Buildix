import { useState } from 'react';
import { useQuery, keepPreviousData } from '@tanstack/react-query';
import { useTranslation } from 'react-i18next';
import { Search } from 'lucide-react';
import { PageHeader, Card, Badge, Spinner } from '@/shared/ui';
import { cn } from '@/shared/lib/cn';
import { formatSum, formatQty } from '@/shared/lib/format';
import { unitLabel } from '@/shared/lib/units';
import { useDebounce } from '@/shared/hooks/useDebounce';
import { productsApi, categoriesApi, type Product } from '@/features/warehouse/api';

const PAGE_SIZE = 50;
const GRID = 'grid-cols-[1.7fr_130px_120px_1fr_110px]';

/** Seller read-only catalog: browse products + stock, no cost/margin, no editing. */
export default function SellerProductsPage() {
  const { t } = useTranslation();
  const [search, setSearch] = useState('');
  const [categoryId, setCategoryId] = useState<number | null>(null);
  const [lowOnly, setLowOnly] = useState(false);
  const debouncedSearch = useDebounce(search);

  const categoriesQuery = useQuery({ queryKey: ['categories'], queryFn: categoriesApi.list });
  const listQuery = useQuery({
    queryKey: ['seller-products', { search: debouncedSearch, categoryId, lowOnly }],
    queryFn: () =>
      productsApi.listPaged({ page: 1, size: PAGE_SIZE, search: debouncedSearch, categoryId, lowStockOnly: lowOnly }),
    placeholderData: keepPreviousData,
  });

  const rows = listQuery.data?.items ?? [];

  return (
    <>
      <PageHeader title={t('seller.products.title')} subtitle={t('seller.products.subtitle')} />

      <div className="mx-auto flex w-full max-w-[1240px] flex-1 flex-col gap-[18px] p-8">
        {/* Toolbar */}
        <div className="flex flex-wrap items-center gap-3">
          <div className="relative min-w-[280px] flex-1">
            <Search size={17} className="absolute left-3.5 top-1/2 -translate-y-1/2 text-muted-2" />
            <input
              value={search}
              onChange={(e) => setSearch(e.target.value)}
              placeholder={t('warehouse.searchPlaceholder')}
              className="h-11 w-full rounded-input border border-input-border bg-surface pl-11 pr-4 text-[14px] outline-none focus:border-primary focus:shadow-focus-ring"
            />
          </div>
          <div className="flex flex-wrap items-center gap-1.5">
            <Chip label={t('warehouse.allCategories')} active={categoryId === null} onClick={() => setCategoryId(null)} />
            {(categoriesQuery.data ?? []).map((c) => (
              <Chip key={c.id} label={c.name} active={categoryId === c.id} onClick={() => setCategoryId(c.id)} />
            ))}
            <Chip label={t('warehouse.onlyLow')} active={lowOnly} onClick={() => setLowOnly((v) => !v)} />
          </div>
        </div>

        <Card className="overflow-hidden">
          <div className={cn('grid items-center gap-4 border-b border-hairline bg-bg/40 px-6 py-3 text-[11.5px] font-semibold tracking-[0.4px] text-muted-2', GRID)}>
            <span>{t('warehouse.cols.product')}</span>
            <span>{t('warehouse.cols.sku')}</span>
            <span className="text-right">{t('warehouse.cols.stock')}</span>
            <span className="text-right">{t('warehouse.cols.price')}</span>
            <span className="text-center">{t('warehouse.cols.status')}</span>
          </div>

          {listQuery.isLoading ? (
            <div className="flex items-center justify-center py-20 text-primary">
              <Spinner size={24} />
            </div>
          ) : rows.length > 0 ? (
            rows.map((p) => <Row key={p.id} product={p} />)
          ) : (
            <div className="py-20 text-center text-[14px] text-muted-2">{t('warehouse.empty')}</div>
          )}
        </Card>

        {listQuery.data && listQuery.data.total > rows.length && (
          <span className="text-[12.5px] text-muted-2">
            {t('warehouse.showing', { shown: rows.length, total: listQuery.data.total })}
          </span>
        )}
      </div>
    </>
  );
}

function statusOf(p: Product): { key: 'inStock' | 'low' | 'out'; tone: 'success' | 'warn' | 'danger' } {
  if (p.quantity <= 0) return { key: 'out', tone: 'danger' };
  if (p.isLowStock) return { key: 'low', tone: 'warn' };
  return { key: 'inStock', tone: 'success' };
}

function Row({ product: p }: { product: Product }) {
  const { t } = useTranslation();
  const st = statusOf(p);
  return (
    <div className={cn('grid items-center gap-4 border-b border-hairline px-6 py-3.5 text-[13px] last:border-0 hover:bg-bg/40', GRID)}>
      <div className="min-w-0">
        <div className="truncate font-medium">{p.name}</div>
        <div className="truncate text-[11.5px] text-muted-2">
          {p.categoryName ?? '—'} · {unitLabel(t, p.unit, p.unitName)}
        </div>
      </div>
      <span className="truncate text-muted-2 nums">{p.sku ?? '—'}</span>
      <span className={cn('text-right nums', st.key === 'out' ? 'text-danger' : st.key === 'low' ? 'text-warn' : 'text-text')}>
        {formatQty(p.quantity)}
      </span>
      <span className="text-right font-semibold text-primary nums">{formatSum(p.salePrice)}</span>
      <span className="text-center">
        <Badge tone={st.tone}>{t(`warehouse.status.${st.key}`)}</Badge>
      </span>
    </div>
  );
}

function Chip({ label, active, onClick }: { label: string; active: boolean; onClick: () => void }) {
  return (
    <button
      type="button"
      onClick={onClick}
      className={cn(
        'rounded-input px-3.5 py-2 text-[13px] font-medium transition-colors',
        active ? 'bg-primary text-white' : 'border border-input-border bg-surface text-muted hover:text-text',
      )}
    >
      {label}
    </button>
  );
}
