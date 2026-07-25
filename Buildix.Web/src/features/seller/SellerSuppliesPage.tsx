import { useMemo, useState } from 'react';
import { useQuery, useMutation, useQueryClient, keepPreviousData } from '@tanstack/react-query';
import { useTranslation } from 'react-i18next';
import { PackageCheck, Truck, Clock, Phone } from 'lucide-react';
import { PageHeader, Card, Badge, Spinner, Button } from '@/shared/ui';
import { cn } from '@/shared/lib/cn';
import { formatShortDate, formatQty } from '@/shared/lib/format';
import { useAuth } from '@/shared/auth/useAuth';
import { PERMISSIONS } from '@/shared/config/permissions';
import { purchasesApi, type ZakupReceipt } from '@/features/purchases/api';

const PAGE_SIZE = 30;
const GRID = 'grid-cols-[80px_1.4fr_1.7fr_90px_130px_130px]';
const FILTERS = ['all', 'awaiting', 'accepted'] as const;
type Filter = (typeof FILTERS)[number];

type StateKey = 'accepted' | 'awaiting' | 'inTransit' | 'delayed';
const TONE: Record<StateKey, 'success' | 'info' | 'warn' | 'neutral'> = {
  accepted: 'neutral',
  awaiting: 'success',
  inTransit: 'info',
  delayed: 'warn',
};

const TASHKENT_TZ = 'Asia/Tashkent';
const dayKey = (d: string | Date) => new Intl.DateTimeFormat('en-CA', { timeZone: TASHKENT_TZ }).format(new Date(d));

/** Derived delivery state — model has InTransit/Accepted; ETA splits in-transit into en-route / arrived / delayed. */
function stateOf(r: ZakupReceipt): { key: StateKey; canAccept: boolean } {
  if (r.deliveryStatus === 'Accepted') return { key: 'accepted', canAccept: false };
  const todayK = dayKey(new Date());
  const etaK = r.expectedDate ? dayKey(r.expectedDate) : null;
  if (etaK && etaK > todayK) return { key: 'inTransit', canAccept: false }; // ETA in the future — en route
  if (etaK && etaK < todayK) return { key: 'delayed', canAccept: true }; // ETA passed — overdue
  return { key: 'awaiting', canAccept: true }; // arrived today / no ETA
}

/**
 * Seller Поставки — pipeline ko'rinishi. Yetkazishlar admin tomonidan
 * yaratiladi; «В пути» → «Начать приёмку» (zakup.accept bo'lsa). Summalar
 * ko'rinmaydi (ZakupRoleShaper).
 */
export default function SellerSuppliesPage() {
  const { t, i18n } = useTranslation();
  const { hasPermission } = useAuth();
  const qc = useQueryClient();
  const canAccept = hasPermission(PERMISSIONS.zakup.accept);
  const [page, setPage] = useState(1);
  const [filter, setFilter] = useState<Filter>('all');

  const listQuery = useQuery({
    queryKey: ['seller-supplies', page],
    queryFn: () => purchasesApi.receiptsPaged(page, PAGE_SIZE),
    placeholderData: keepPreviousData,
  });

  const accept = useMutation({
    mutationFn: (id: string) => purchasesApi.accept(id),
    onSuccess: () => {
      void qc.invalidateQueries({ queryKey: ['seller-supplies'] });
      void qc.invalidateQueries({ queryKey: ['seller-products'] });
    },
  });

  const rows = useMemo(() => listQuery.data?.items ?? [], [listQuery.data]);

  // Ikki featured karta: qabul kutayotgan (awaiting/delayed) + yo'ldagi (inTransit).
  const { arrived, enRoute, filtered } = useMemo(() => {
    const withState = rows.map((r) => ({ r, s: stateOf(r) }));
    return {
      arrived: withState.find((x) => x.s.key === 'awaiting' || x.s.key === 'delayed'),
      enRoute: withState.find((x) => x.s.key === 'inTransit'),
      filtered: withState.filter((x) =>
        filter === 'awaiting'
          ? x.s.key !== 'accepted'
          : filter === 'accepted'
            ? x.s.key === 'accepted'
            : true,
      ),
    };
  }, [rows, filter]);

  return (
    <>
      <PageHeader title={t('seller.supplies.title')} subtitle={t('seller.supplies.subtitle')} />

      <div className="mx-auto flex w-full max-w-[1240px] flex-1 flex-col gap-[18px] p-8">
        {/* Pipeline featured cards */}
        {(arrived || enRoute) && (
          <div className="grid grid-cols-2 gap-4">
            {arrived && (
              <FeatureCard
                x={arrived}
                lang={i18n.language}
                onAccept={canAccept ? () => accept.mutate(arrived.r.id) : undefined}
                accepting={accept.isPending && accept.variables === arrived.r.id}
              />
            )}
            {enRoute && <FeatureCard x={enRoute} lang={i18n.language} />}
          </div>
        )}

        {/* Все поставки + filter */}
        <div className="flex items-center justify-between">
          <span className="text-[16px] font-semibold">{t('seller.supplies.allTitle')}</span>
          <div className="flex items-center gap-1.5">
            {FILTERS.map((f) => (
              <button
                key={f}
                type="button"
                onClick={() => setFilter(f)}
                className={cn(
                  'rounded-input px-3.5 py-2 text-[12.5px] font-medium transition-colors',
                  filter === f ? 'bg-primary text-white' : 'border border-input-border bg-surface text-muted hover:text-text',
                )}
              >
                {t(`seller.supplies.filters.${f}`)}
              </button>
            ))}
          </div>
        </div>

        <Card className="overflow-hidden">
          <div className={cn('grid items-center gap-4 border-b border-hairline bg-bg/40 px-6 py-3 text-[11.5px] font-semibold tracking-[0.4px] text-muted-2', GRID)}>
            <span>{t('purchases.cols.number')}</span>
            <span>{t('purchases.cols.supplier')}</span>
            <span>{t('purchases.cols.items')}</span>
            <span className="text-center">{t('seller.supplies.cols.positions')}</span>
            <span>{t('purchases.cols.date')}</span>
            <span className="text-right">{t('purchases.cols.status')}</span>
          </div>

          {listQuery.isLoading ? (
            <div className="flex items-center justify-center py-20 text-primary">
              <Spinner size={24} />
            </div>
          ) : filtered.length > 0 ? (
            filtered.map(({ r, s }) => <SupplyRow key={r.id} r={r} state={s.key} lang={i18n.language} />)
          ) : (
            <div className="py-20 text-center text-[14px] text-muted-2">{t('purchases.empty')}</div>
          )}
        </Card>

        {listQuery.data && listQuery.data.totalPages > 1 && (
          <div className="flex items-center justify-end gap-1.5">
            {listQuery.isFetching && <Spinner size={14} className="mr-1 text-primary" />}
            <button type="button" aria-label={t('common.prev')} disabled={page <= 1} onClick={() => setPage((p) => p - 1)} className="h-8 rounded-md border border-input-border bg-surface px-3 text-[13px] disabled:opacity-40">‹</button>
            <span className="px-2 text-[13px] text-muted nums">{page} / {listQuery.data.totalPages}</span>
            <button type="button" aria-label={t('common.next')} disabled={page >= listQuery.data.totalPages} onClick={() => setPage((p) => p + 1)} className="h-8 rounded-md border border-input-border bg-surface px-3 text-[13px] disabled:opacity-40">›</button>
          </div>
        )}
      </div>
    </>
  );
}

function itemsPreview(r: ZakupReceipt): { name: string; qty: number }[] {
  return (r.items ?? []).slice(0, 3).map((i) => ({ name: i.productName, qty: i.quantity }));
}

function FeatureCard({
  x,
  lang,
  onAccept,
  accepting,
}: {
  x: { r: ZakupReceipt; s: { key: StateKey; canAccept: boolean } };
  lang: string;
  onAccept?: () => void;
  accepting?: boolean;
}) {
  const { t } = useTranslation();
  const { r, s } = x;
  const items = itemsPreview(r);
  return (
    <Card className={cn('overflow-hidden border', s.key === 'inTransit' ? 'border-primary/25' : s.key === 'delayed' ? 'border-warn-amber/40' : 'border-success/30')}>
      <div className="flex items-center gap-3 border-b border-hairline px-5 py-4">
        <span className={cn('flex h-10 w-10 flex-none items-center justify-center rounded-lg', s.key === 'inTransit' ? 'bg-primary-soft text-primary' : s.key === 'delayed' ? 'bg-warn-soft text-warn-strong' : 'bg-success-soft text-success')}>
          {s.key === 'inTransit' ? <Clock size={19} /> : <Truck size={19} />}
        </span>
        <div className="min-w-0 flex-1">
          <div className="flex items-center gap-2">
            <span className="truncate text-[14px] font-semibold">З-{r.receiptNumber}</span>
            <Badge tone={TONE[s.key]}>{t(`seller.supplies.state.${s.key}`)}</Badge>
          </div>
          <div className="truncate text-[12px] text-muted-2">
            {r.supplierName ?? t('purchases.noSupplier')}
            {r.driverPhone && (
              <span className="inline-flex items-center gap-1"> · <Phone size={11} /> {r.driverPhone}</span>
            )}
          </div>
        </div>
      </div>
      <div className="flex flex-col gap-2 px-5 py-3.5">
        {items.map((it, i) => (
          <div key={i} className="flex justify-between text-[13px]">
            <span className="truncate text-muted">{it.name}</span>
            <span className="font-semibold nums">{formatQty(it.qty)}</span>
          </div>
        ))}
        <div className="mt-1 flex items-center justify-between border-t border-hairline pt-3">
          <span className="text-[12px] text-muted-2">
            {t('seller.supplies.positionsAmounts', { count: r.itemCount })}
          </span>
          {onAccept ? (
            <Button size="sm" loading={accepting} onClick={onAccept}>
              <PackageCheck size={14} />
              {t('seller.supplies.accept')}
            </Button>
          ) : s.key === 'inTransit' ? (
            <span className="text-[12px] font-medium text-muted">
              {r.expectedDate ? t('seller.supplies.eta', { date: formatShortDate(r.expectedDate, lang) }) : t('seller.supplies.acceptOnArrival')}
            </span>
          ) : null}
        </div>
      </div>
    </Card>
  );
}

function SupplyRow({ r, state, lang }: { r: ZakupReceipt; state: StateKey; lang: string }) {
  const { t } = useTranslation();
  const items = (r.items ?? []).slice(0, 2).map((i) => i.productName).join(', ');
  return (
    <div className={cn('grid items-center gap-4 border-b border-hairline px-6 py-3.5 text-[13px] last:border-0 hover:bg-bg/40', GRID)}>
      <span className="font-semibold text-primary nums">З-{r.receiptNumber}</span>
      <span className="truncate font-medium">{r.supplierName ?? t('purchases.noSupplier')}</span>
      <span className="truncate text-muted-2">{items || '—'}</span>
      <span className="text-center text-muted nums">{r.itemCount}</span>
      <span className="text-muted-2 nums">{formatShortDate(r.createdAt, lang)}</span>
      <span className="text-right">
        <Badge tone={TONE[state]}>{t(`seller.supplies.state.${state}`)}</Badge>
      </span>
    </div>
  );
}
