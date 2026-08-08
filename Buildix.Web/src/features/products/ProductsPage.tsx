import { useEffect, useRef, useState } from 'react';
import { useQuery, useMutation, useQueryClient, keepPreviousData } from '@tanstack/react-query';
import { useTranslation } from 'react-i18next';
import { Plus, Search, Eye, EyeOff, Printer } from 'lucide-react';
import { PageHeader, Button, Card, Spinner } from '@/shared/ui';
import { cn } from '@/shared/lib/cn';
import { formatSum, formatQty } from '@/shared/lib/format';
import { unitLabel } from '@/shared/lib/units';
import { useDebounce } from '@/shared/hooks/useDebounce';
import { useAuth } from '@/shared/auth/useAuth';
import { PERMISSIONS } from '@/shared/config/permissions';
import { productsApi, categoriesApi, type Product } from '@/features/warehouse/api';
import { ProductFormModal } from '@/features/warehouse/ProductFormModal';
import { PrintLabelsModal } from '@/features/warehouse/PrintLabelsModal';

const PAGE_SIZE = 30;

/**
 * Товары и цены — the catalog + pricing screen, split from Склад (which keeps
 * stock/movement). Here the sale price is editable inline, the margin is shown
 * live, and a product can be hidden from the cashier's POS without deleting it.
 * Cost price and margin are only shown to cost-viewers (Owner/Admin).
 */
export default function ProductsPage() {
  const { t } = useTranslation();
  const { hasPermission } = useAuth();
  const canViewCost = hasPermission(PERMISSIONS.data.costPrice);
  const canCreate = hasPermission(PERMISSIONS.products.create);
  const canEdit = hasPermission(PERMISSIONS.products.edit);

  const [search, setSearch] = useState('');
  const [categoryId, setCategoryId] = useState<number | null>(null);
  const [page, setPage] = useState(1);
  const [formOpen, setFormOpen] = useState(false);
  const [editing, setEditing] = useState<Product | null>(null);
  const [selected, setSelected] = useState<Set<string>>(new Set());
  const [printing, setPrinting] = useState(false);
  const debouncedSearch = useDebounce(search);
  const resetPage = () => setPage(1);

  const categoriesQuery = useQuery({ queryKey: ['categories'], queryFn: categoriesApi.list });
  // Whole-catalog totals for the subtitle (unfiltered) — hidden = all − visible.
  const countsQuery = useQuery({
    queryKey: ['products', 'catalog-counts'],
    queryFn: async () => {
      const [all, visible] = await Promise.all([
        productsApi.listPaged({ page: 1, size: 1, includeHidden: true }),
        productsApi.listPaged({ page: 1, size: 1, includeHidden: false }),
      ]);
      return { total: all.total, hidden: Math.max(all.total - visible.total, 0) };
    },
  });
  const listQuery = useQuery({
    queryKey: ['products', 'catalog', { page, search: debouncedSearch, categoryId }],
    queryFn: () =>
      productsApi.listPaged({
        page,
        size: PAGE_SIZE,
        search: debouncedSearch,
        categoryId,
        // Admin catalog shows hidden products too (they carry a badge + Показать).
        includeHidden: true,
      }),
    placeholderData: keepPreviousData,
  });

  const openCreate = () => {
    setEditing(null);
    setFormOpen(true);
  };
  const openEdit = (p: Product) => {
    setEditing(p);
    setFormOpen(true);
  };

  // Yorliq chop etish huquqi bo'lgandagina tanlash ustuni chiqadi — aks holda
  // u hech narsa qilmaydigan ortiqcha element bo'lardi.
  const canPrintLabels = canEdit;
  const cols = cn(
    canPrintLabels && 'grid-cols-[34px_2.2fr_0.9fr_1fr_1.1fr_0.7fr_0.9fr]',
    canPrintLabels && !canViewCost && 'grid-cols-[34px_2.6fr_1fr_1.2fr_0.9fr]',
    !canPrintLabels && canViewCost && 'grid-cols-[2.2fr_0.9fr_1fr_1.1fr_0.7fr_0.9fr]',
    !canPrintLabels && !canViewCost && 'grid-cols-[2.6fr_1fr_1.2fr_0.9fr]',
  );

  const rows = listQuery.data?.items ?? [];
  const allSelected = rows.length > 0 && rows.every((p) => selected.has(p.id));
  const toggle = (id: string) =>
    setSelected((prev) => {
      const next = new Set(prev);
      if (next.has(id)) next.delete(id);
      else next.add(id);
      return next;
    });
  // Faqat joriy sahifa tanlanadi: ko'rinmayotgan tovarlarga yorliq bosib
  // yuborish kutilmagan natija bo'lardi.
  const toggleAll = () =>
    setSelected((prev) => {
      const next = new Set(prev);
      if (allSelected) rows.forEach((p) => next.delete(p.id));
      else rows.forEach((p) => next.add(p.id));
      return next;
    });

  const selectedProducts = rows.filter((p) => selected.has(p.id));

  return (
    <>
      <PageHeader
        title={t('products.title')}
        subtitle={
          countsQuery.data
            ? `${t('products.catalogCount', { count: countsQuery.data.total, hidden: countsQuery.data.hidden })} · ${t('products.subtitle')}`
            : t('products.subtitle')
        }
        actions={
          canCreate && (
            <Button onClick={openCreate}>
              <Plus size={15} strokeWidth={2.4} />
              {t('warehouse.addProduct')}
            </Button>
          )
        }
      />

      <div className="flex flex-1 flex-col gap-[18px] p-8">
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
          <div className="flex flex-wrap items-center gap-1.5">
            <Chip
              label={t('warehouse.allCategories')}
              active={categoryId === null}
              onClick={() => {
                setCategoryId(null);
                resetPage();
              }}
            />
            {(categoriesQuery.data ?? []).map((c) => (
              <Chip
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
        </div>

        {/* Table */}
        <Card className="overflow-hidden">
          <div
            className={cn(
              'grid items-center gap-4 border-b border-hairline bg-bg/40 px-6 py-3 text-[11.5px] font-semibold tracking-[0.4px] text-muted-2',
              cols,
            )}
          >
            {canPrintLabels && (
              <input
                type="checkbox"
                className="h-4 w-4 accent-primary"
                checked={allSelected}
                onChange={toggleAll}
                aria-label={t('labels.selectAll')}
              />
            )}
            <span>{t('warehouse.cols.product')}</span>
            <span className="text-right">{t('warehouse.cols.stock')}</span>
            {canViewCost && <span className="text-right">{t('warehouse.cols.cost')}</span>}
            <span className="text-right">{t('warehouse.cols.price')}</span>
            {canViewCost && <span className="text-right">{t('products.margin')}</span>}
            <span className="text-right" />
          </div>

          {listQuery.isLoading ? (
            <div className="flex items-center justify-center py-20 text-primary">
              <Spinner size={24} />
            </div>
          ) : rows.length > 0 ? (
            rows.map((p) => (
              <ProductRow
                key={p.id}
                product={p}
                gridCols={cols}
                canViewCost={canViewCost}
                canEdit={canEdit}
                onOpen={canEdit ? () => openEdit(p) : undefined}
                selectable={canPrintLabels}
                selected={selected.has(p.id)}
                onToggle={() => toggle(p.id)}
              />
            ))
          ) : (
            <div className="py-20 text-center text-[14px] text-muted-2">{t('warehouse.empty')}</div>
          )}
        </Card>

        <p className="text-[12px] text-muted-2">{t('products.hiddenNote')}</p>

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

      {/* Tanlov paneli — ekran pastida suzadi, ro'yxatni siljitmaydi.
          Sahifa almashsa tanlov saqlanadi (Set id lar bo'yicha), lekin
          «hammasini tanlash» faqat ko'rinayotgan sahifaga tegishli. */}
      {selectedProducts.length > 0 && (
        <div className="pointer-events-none sticky bottom-0 z-20 flex justify-center p-6">
          <div className="pointer-events-auto flex items-center gap-4 rounded-pill border border-border bg-surface px-5 py-3 shadow-pop">
            <span className="text-[13.5px] font-medium">
              {t('labels.selected', { count: selectedProducts.length })}
            </span>
            <Button size="sm" onClick={() => setPrinting(true)}>
              <Printer size={14} />
              {t('labels.fromProduct')}
            </Button>
            <button
              type="button"
              onClick={() => setSelected(new Set())}
              className="text-[13px] text-muted-2 transition-colors hover:text-text"
            >
              {t('labels.clearSelection')}
            </button>
          </div>
        </div>
      )}

      <ProductFormModal
        open={formOpen}
        onClose={() => setFormOpen(false)}
        product={editing}
        categories={categoriesQuery.data ?? []}
      />

      <PrintLabelsModal
        open={printing}
        onClose={() => setPrinting(false)}
        targets={selectedProducts.map((p) => ({
          id: p.id,
          name: p.name,
          sku: p.sku,
          barcode: p.barcode,
        }))}
      />
    </>
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

/** Margin % from cost→sale, for the colored МАРЖА cell. */
function marginPct(cost: number, sale: number): number | null {
  if (sale <= 0) return null;
  return Math.round(((sale - cost) / sale) * 100);
}

function ProductRow({
  product: p,
  gridCols,
  canViewCost,
  canEdit,
  onOpen,
  selectable,
  selected,
  onToggle,
}: {
  product: Product;
  gridCols: string;
  canViewCost: boolean;
  canEdit: boolean;
  onOpen?: () => void;
  selectable: boolean;
  selected: boolean;
  onToggle: () => void;
}) {
  const { t } = useTranslation();
  const qc = useQueryClient();

  // Local mirror of the editable sale price so typing feels immediate; committed
  // on blur/Enter and only when it actually changed.
  // 0 narx bo'sh maydon sifatida ko'rinadi — «0» ni o'chirish shart emas.
  const [price, setPrice] = useState(p.salePrice === 0 ? '' : String(p.salePrice));
  useEffect(() => setPrice(p.salePrice === 0 ? '' : String(p.salePrice)), [p.salePrice]);

  const patch = useMutation({
    mutationFn: (body: { salePrice?: number; isHidden?: boolean }) => productsApi.patch(p.id, body),
    onSuccess: () => {
      void qc.invalidateQueries({ queryKey: ['products'] });
    },
  });

  // Escape bekor qiladi (blur sinxron commit poygasi — WarehousePage'dagi kabi).
  const cancelRef = useRef(false);
  const commitPrice = () => {
    if (cancelRef.current) {
      cancelRef.current = false;
      setPrice(p.salePrice === 0 ? '' : String(p.salePrice));
      return;
    }
    const next = Math.max(0, Number(price) || 0);
    if (next !== p.salePrice) patch.mutate({ salePrice: next });
    else setPrice(p.salePrice === 0 ? '' : String(p.salePrice));
  };

  const margin = canViewCost ? marginPct(p.costPrice, p.salePrice) : null;
  const marginTone =
    margin === null ? 'text-muted-2' : margin <= 0 ? 'text-danger' : margin < 10 ? 'text-warn-strong' : 'text-success';
  const stockTone = p.quantity <= 0 ? 'text-danger' : p.isLowStock ? 'text-warn-strong' : 'text-text';

  return (
    <div
      className={cn(
        'grid items-center gap-4 border-b border-hairline px-6 py-2.5 text-[13px] last:border-0 hover:bg-bg/40',
        gridCols,
        p.isHidden && 'opacity-60',
        selected && 'bg-primary-soft/40',
      )}
    >
      {selectable && (
        <input
          type="checkbox"
          className="h-4 w-4 accent-primary"
          checked={selected}
          onChange={onToggle}
          aria-label={p.name}
        />
      )}
      {/* Name + category·unit (clickable to edit) */}
      <button type="button" onClick={onOpen} className="flex min-w-0 flex-col items-start text-left" disabled={!onOpen}>
        <span className="flex items-center gap-2 truncate font-medium">
          {p.name}
          {p.isHidden && (
            <span className="rounded-pill bg-hairline px-1.5 py-0.5 text-[10px] font-semibold text-muted-2">
              {t('products.hidden')}
            </span>
          )}
        </span>
        <span className="truncate text-[11.5px] text-muted-2">
          {/* Artikul qidiruv unga ishlasa-da ko'rinmasdi — endi birinchi turadi. */}
          {p.sku && <span className="font-medium text-muted">{p.sku} · </span>}
          {p.categoryName ?? '—'} · {unitLabel(t, p.unit, p.unitName)}
        </span>
      </button>

      <span className={cn('text-right font-semibold nums', stockTone)}>
        {formatQty(p.quantity)} {unitLabel(t, p.unit, p.unitName)}
      </span>

      {canViewCost && <span className="text-right text-muted-2 nums">{formatSum(p.costPrice)}</span>}

      {/* Inline sale price */}
      <div className="flex justify-end">
        {canEdit ? (
          <input
            type="number"
            step="any"
            min="0"
            placeholder="0"
            value={price}
            disabled={patch.isPending}
            onFocus={(e) => e.currentTarget.select()}
            onChange={(e) => setPrice(e.target.value)}
            onBlur={commitPrice}
            onKeyDown={(e) => {
              if (e.key === 'Enter') e.currentTarget.blur();
              if (e.key === 'Escape') {
                cancelRef.current = true;
                e.currentTarget.blur();
              }
            }}
            className="h-9 w-[112px] rounded-input border border-input-border bg-surface px-3 text-right text-[13px] font-semibold outline-none focus:border-primary disabled:opacity-50 nums"
          />
        ) : (
          <span className="font-semibold nums">{formatSum(p.salePrice)}</span>
        )}
      </div>

      {canViewCost && (
        <span className={cn('text-right font-semibold nums', marginTone)}>
          {margin === null ? '—' : `${margin}%`}
        </span>
      )}

      {/* Hide / show */}
      <div className="flex justify-end">
        {canEdit && (
          <button
            type="button"
            title={p.isHidden ? t('products.show') : t('products.hide')}
            disabled={patch.isPending}
            onClick={() => patch.mutate({ isHidden: !p.isHidden })}
            className={cn(
              'flex items-center gap-1.5 rounded-input border px-3 py-1.5 text-[12px] font-medium transition-colors disabled:opacity-50',
              p.isHidden
                ? 'border-success/40 bg-success-soft text-success'
                : 'border-input-border bg-surface text-muted hover:text-text',
            )}
          >
            {p.isHidden ? <Eye size={13} /> : <EyeOff size={13} />}
            {p.isHidden ? t('products.show') : t('products.hide')}
          </button>
        )}
      </div>
    </div>
  );
}
