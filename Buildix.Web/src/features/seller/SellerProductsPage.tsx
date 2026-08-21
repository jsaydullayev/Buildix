import { useState } from 'react';
import { useQuery, keepPreviousData } from '@tanstack/react-query';
import { useTranslation } from 'react-i18next';
import { useNavigate, useParams } from 'react-router-dom';
import { Search, X, ShoppingCart } from 'lucide-react';
import { PageHeader, Card, Badge, Spinner, Button } from '@/shared/ui';
import { cn } from '@/shared/lib/cn';
import { formatSum, formatQty, formatShortDate } from '@/shared/lib/format';
import { unitLabel } from '@/shared/lib/units';
import { useDebounce } from '@/shared/hooks/useDebounce';
import { productsApi, categoriesApi, type Product } from '@/features/warehouse/api';

const PAGE_SIZE = 50;
const GRID = 'min-w-[760px] grid-cols-[1.7fr_120px_100px_1fr_1.1fr_96px]';

/** Seller read-only catalog: browse products + stock, no cost/margin, no editing. */
export default function SellerProductsPage() {
  const { t } = useTranslation();
  const [search, setSearch] = useState('');
  const [categoryId, setCategoryId] = useState<number | null>(null);
  const [lowOnly, setLowOnly] = useState(false);
  const [selected, setSelected] = useState<Product | null>(null);
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

      <div className="flex flex-1 overflow-hidden">
        <div className="min-w-0 flex-1 overflow-y-auto">
          <div className="mx-auto flex w-full max-w-[1240px] flex-col gap-[18px] p-4 sm:p-6 lg:p-8">
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

            <Card className="min-w-0 overflow-x-auto">
              <div className={cn('grid items-center gap-4 border-b border-hairline bg-bg/40 px-6 py-3 text-[11.5px] font-semibold tracking-[0.4px] text-muted-2', GRID)}>
                <span>{t('warehouse.cols.product')}</span>
                <span>{t('warehouse.cols.sku')}</span>
                <span className="text-right">{t('warehouse.cols.stock')}</span>
                <span className="text-right">{t('warehouse.cols.price')}</span>
                <span>{t('seller.products.cols.location')}</span>
                <span className="text-center">{t('warehouse.cols.status')}</span>
              </div>

              {listQuery.isLoading ? (
                <div className="flex items-center justify-center py-20 text-primary">
                  <Spinner size={24} />
                </div>
              ) : rows.length > 0 ? (
                rows.map((p) => (
                  <Row key={p.id} product={p} active={selected?.id === p.id} onOpen={() => setSelected(p)} />
                ))
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
        </div>

        {selected && <ProductDrawer product={selected} onClose={() => setSelected(null)} />}
      </div>
    </>
  );
}

function statusOf(p: Product): { key: 'inStock' | 'low' | 'out'; tone: 'success' | 'warn' | 'danger' } {
  if (p.quantity <= 0) return { key: 'out', tone: 'danger' };
  if (p.isLowStock) return { key: 'low', tone: 'warn' };
  return { key: 'inStock', tone: 'success' };
}

function Row({ product: p, active, onOpen }: { product: Product; active: boolean; onOpen: () => void }) {
  const { t } = useTranslation();
  const st = statusOf(p);
  return (
    <button
      type="button"
      onClick={onOpen}
      className={cn(
        'grid w-full items-center gap-4 border-b border-hairline px-6 py-3.5 text-left text-[13px] last:border-0 hover:bg-bg/40',
        GRID,
        active && 'bg-primary-soft/30',
      )}
    >
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
      <span className="truncate text-muted-2">{p.warehouseLocation ?? '—'}</span>
      <span className="text-center">
        <Badge tone={st.tone}>{t(`warehouse.status.${st.key}`)}</Badge>
      </span>
    </button>
  );
}

/** «Место на складе», поставщик, последний приход, продано за месяц — kassir kartochkasi. */
function ProductDrawer({ product: p, onClose }: { product: Product; onClose: () => void }) {
  const { t, i18n } = useTranslation();
  const { subdomain } = useParams();
  const navigate = useNavigate();
  const st = statusOf(p);
  const statsQuery = useQuery({ queryKey: ['product-stats', p.id], queryFn: () => productsApi.stats(p.id) });
  const s = statsQuery.data;

  const lastIn = s?.lastReceiptAt
    ? `${formatShortDate(s.lastReceiptAt, i18n.language)}${s.lastReceiptNumber ? ` · З-${s.lastReceiptNumber}` : ''}`
    : '—';

  return (
    <aside className="flex w-[400px] flex-none flex-col gap-[18px] overflow-y-auto border-l border-hairline bg-surface p-6">
      <div className="flex items-start justify-between">
        <div className="min-w-0">
          <div className="text-[17px] font-semibold leading-tight">{p.name}</div>
          <div className="mt-2 flex items-center gap-2">
            {p.categoryName && <Badge tone="info">{p.categoryName}</Badge>}
            <Badge tone={st.tone}>{t(`warehouse.status.${st.key}`)}</Badge>
          </div>
        </div>
        <button
          type="button"
          onClick={onClose}
          className="-mr-1 flex h-8 w-8 flex-none items-center justify-center rounded-md text-muted-2 hover:bg-hairline hover:text-text"
          aria-label={t('common.close')}
        >
          <X size={18} />
        </button>
      </div>

      <div className="flex items-baseline justify-between rounded-card border border-border bg-bg/40 px-4 py-3.5">
        <span className="text-[12.5px] text-muted">{t('seller.products.card.price')}</span>
        <span className="text-[20px] font-bold text-primary nums">
          {formatSum(p.salePrice)} <span className="text-[12px] font-normal text-muted-2">{t('common.currency')}/{unitLabel(t, p.unit, p.unitName)}</span>
        </span>
      </div>

      <div className="flex flex-col text-[13px]">
        <DrawerRow label={t('seller.products.card.stock')} value={`${formatQty(p.quantity)} ${unitLabel(t, p.unit, p.unitName)}`} valueClass={st.key === 'out' ? 'text-danger' : st.key === 'low' ? 'text-warn-strong' : ''} />
        <DrawerRow label={t('seller.products.card.minStock')} value={`${formatQty(p.minThreshold)} ${unitLabel(t, p.unit, p.unitName)}`} />
        <DrawerRow label={t('seller.products.card.sku')} value={p.sku ?? '—'} />
        {/* Shtrix-kod — maketda ham bor. Kassir uni skanerlash o'rniga ko'z
            bilan solishtirishi kerak bo'lgan holatlar uchun. */}
        <DrawerRow label={t('seller.products.card.barcode')} value={p.barcode ?? '—'} />
        <DrawerRow label={t('seller.products.card.location')} value={p.warehouseLocation ?? '—'} />
        <DrawerRow label={t('seller.products.card.supplier')} value={statsQuery.isLoading ? '…' : s?.supplierName ?? '—'} />
        <DrawerRow label={t('seller.products.card.lastIn')} value={statsQuery.isLoading ? '…' : lastIn} />
        <DrawerRow
          label={t('seller.products.card.soldMonth')}
          value={statsQuery.isLoading ? '…' : `${formatQty(s?.soldThisMonth ?? 0)} ${unitLabel(t, p.unit, p.unitName)}`}
          last
        />
      </div>

      <div className="mt-auto flex flex-col gap-2.5">
        <Button fullWidth size="lg" onClick={() => navigate(`/${subdomain}/seller/pos`)}>
          <ShoppingCart size={16} />
          {t('seller.products.card.sell')}
        </Button>
        <p className="text-center text-[11.5px] text-muted-2">{t('seller.products.card.costHidden')}</p>
      </div>
    </aside>
  );
}

function DrawerRow({ label, value, valueClass, last }: { label: string; value: string; valueClass?: string; last?: boolean }) {
  return (
    <div className={cn('flex items-center justify-between py-2.5', !last && 'border-b border-hairline')}>
      <span className="text-muted">{label}</span>
      <span className={cn('font-semibold nums', valueClass)}>{value}</span>
    </div>
  );
}

function Chip({ label, active, onClick }: { label: string; active: boolean; onClick: () => void }) {
  return (
    <button
      type="button"
      onClick={onClick}
      className={cn(
        'flex-none whitespace-nowrap rounded-input px-3.5 py-2 text-[13px] font-medium transition-colors',
        active ? 'bg-primary text-white' : 'border border-input-border bg-surface text-muted hover:text-text',
      )}
    >
      {label}
    </button>
  );
}
