import { useMemo, useState } from 'react';
import { useParams } from 'react-router-dom';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { useTranslation } from 'react-i18next';
import { Plus, Search } from 'lucide-react';
import { PageHeader, Card, Badge, Button, Spinner } from '@/shared/ui';
import { cn } from '@/shared/lib/cn';
import { formatShortDate, initials } from '@/shared/lib/format';
import { useDebounce } from '@/shared/hooks/useDebounce';
import type { ApiError } from '@/shared/api/types';
import { superAdminApi, type SaStoreRow } from './api';
import { StoreDetailDrawer } from './StoreDetailDrawer';
import { CreateStoreModal } from './CreateStoreModal';

type Filter = 'all' | 'active' | 'overdue' | 'blocked';

const MATCH: Record<Filter, SaStoreRow['status'][]> = {
  all: ['Active', 'Overdue', 'Blocked'],
  active: ['Active'],
  overdue: ['Overdue'],
  blocked: ['Blocked'],
};

const GRID = 'min-w-[940px] grid-cols-[minmax(0,1.5fr)_minmax(0,1.2fr)_110px_130px_80px_120px_130px]';


export default function SuperStoresPage() {
  const { segment = '' } = useParams();
  const { t, i18n } = useTranslation();
  const qc = useQueryClient();
  // Arizasiz do'kon ochish — arizalar sahifasiga borish shart emas.
  const [createOpen, setCreateOpen] = useState(false);

  const [filter, setFilter] = useState<Filter>('all');
  const [search, setSearch] = useState('');
  const [openId, setOpenId] = useState<number | null>(null);
  const debounced = useDebounce(search, 250);

  const query = useQuery({
    queryKey: ['sa-stores', segment],
    queryFn: () => superAdminApi.stores(segment),
  });

  const rows = useMemo(() => query.data ?? [], [query.data]);

  const counts = useMemo(
    () => ({
      all: rows.length,
      active: rows.filter((s) => s.status === 'Active').length,
      overdue: rows.filter((s) => s.status === 'Overdue').length,
      blocked: rows.filter((s) => s.status === 'Blocked').length,
    }),
    [rows],
  );

  const visible = useMemo(() => {
    const q = debounced.trim().toLowerCase();
    return rows.filter(
      (s) =>
        MATCH[filter].includes(s.status) &&
        (!q ||
          s.name.toLowerCase().includes(q) ||
          s.ownerName.toLowerCase().includes(q) ||
          (s.city ?? '').toLowerCase().includes(q)),
    );
  }, [rows, filter, debounced]);

  const toggleBlock = useMutation({
    mutationFn: (s: SaStoreRow) =>
      s.isBlocked
        ? superAdminApi.unblockMarket(segment, s.marketId)
        : superAdminApi.blockMarket(segment, s.marketId, t('sa.store.blockedByOperator')),
    onSuccess: () => {
      void qc.invalidateQueries({ queryKey: ['sa-stores', segment] });
      void qc.invalidateQueries({ queryKey: ['sa-dashboard', segment] });
    },
  });

  const err = toggleBlock.error
    ? ((toggleBlock.error as unknown as ApiError).message ?? t('common.somethingWrong'))
    : null;

  return (
    <>
      <PageHeader
        title={t('sa.stores.title')}
        subtitle={t('sa.stores.summary', {
          total: counts.all,
          active: counts.active,
          overdue: counts.overdue,
          blocked: counts.blocked,
        })}
        actions={
          <div className="flex flex-wrap items-center gap-2">
            <Button onClick={() => setCreateOpen(true)}>
              <Plus size={15} />
              {t('sa.create.newStore')}
            </Button>
            <span className="mx-1 h-6 w-px bg-border" />
            {(['all', 'active', 'overdue', 'blocked'] as Filter[]).map((key) => (
              <button
                key={key}
                type="button"
                onClick={() => setFilter(key)}
                className={cn(
                  'rounded-pill border px-4 py-1.5 text-[13px] transition-colors',
                  filter === key
                    ? 'border-primary bg-primary font-semibold text-white'
                    : 'border-border bg-surface text-muted hover:border-primary hover:text-primary',
                )}
              >
                {t(`sa.stores.filters.${key}` as never)}
              </button>
            ))}
          </div>
        }
      />

      <div className="flex flex-col gap-4 p-4 sm:p-6 lg:p-8">
        <div className="relative w-full sm:w-[320px]">
          <Search size={15} className="absolute left-3.5 top-1/2 -translate-y-1/2 text-muted-2" />
          <input
            value={search}
            onChange={(e) => setSearch(e.target.value)}
            placeholder={t('sa.stores.searchPlaceholder')}
            className="h-11 w-full rounded-input border border-border bg-surface pl-9 pr-3 text-[13.5px] outline-none focus:border-primary focus:shadow-focus-ring"
          />
        </div>

        {err && (
          <div className="rounded-card bg-danger-soft px-4 py-2.5 text-[13px] text-danger">{err}</div>
        )}

        <Card className="min-w-0 overflow-x-auto">
          <div
            className={cn(
              'grid items-center gap-3 border-b border-hairline px-6 py-3 text-[11px] font-semibold uppercase tracking-[0.5px] text-muted-2',
              GRID,
            )}
          >
            <span>{t('sa.stores.col.store')}</span>
            <span>{t('sa.stores.col.owner')}</span>
            <span>{t('sa.stores.col.plan')}</span>
            <span>{t('sa.stores.col.paidUntil')}</span>
            <span>{t('sa.stores.col.users')}</span>
            <span>{t('sa.stores.col.status')}</span>
            <span />
          </div>

          {query.isLoading ? (
            <div className="flex justify-center py-14 text-primary">
              <Spinner size={22} />
            </div>
          ) : visible.length === 0 ? (
            <div className="py-14 text-center text-[13.5px] text-muted">{t('sa.stores.empty')}</div>
          ) : (
            visible.map((s) => (
              <div
                key={s.marketId}
                role="button"
                tabIndex={0}
                onClick={() => setOpenId(s.marketId)}
                onKeyDown={(e) => e.key === 'Enter' && setOpenId(s.marketId)}
                className={cn(
                  'grid cursor-pointer items-center gap-3 border-b border-hairline px-6 py-3 text-left last:border-b-0 hover:bg-bg',
                  GRID,
                  s.status === 'Overdue' && 'bg-danger-soft/40',
                  s.status === 'Blocked' && 'bg-bg/70',
                )}
              >
                <div className="flex min-w-0 items-center gap-3">
                  <span className="flex h-9 w-9 flex-none items-center justify-center rounded-pill bg-primary-soft text-[11.5px] font-semibold text-primary">
                    {initials(s.name)}
                  </span>
                  <div className="min-w-0">
                    <div className="truncate text-[14px] font-semibold">{s.name}</div>
                    <div className="truncate text-[11.5px] text-muted-2">
                      {[s.city, t('sa.store.since', { date: formatShortDate(s.createdAt, i18n.language) })]
                        .filter(Boolean)
                        .join(' · ')}
                    </div>
                  </div>
                </div>

                <div className="min-w-0">
                  <div className="truncate text-[13.5px]">{s.ownerName}</div>
                  <div className="nums truncate text-[11.5px] text-muted-2">{s.ownerPhone ?? '—'}</div>
                </div>

                {/* Tarif modeli S3 da — hozircha «—», o'ylab topilgan qiymat emas. */}
                <span className="text-[13px] text-muted-2">{s.plan ?? '—'}</span>

                <span
                  className={cn(
                    'text-[13px]',
                    s.status === 'Overdue' ? 'font-semibold text-danger' : 'text-text',
                  )}
                >
                  {s.expiresAt ? formatShortDate(s.expiresAt, i18n.language) : t('sa.store.noExpiry')}
                </span>

                <span className="nums text-[13px]">{s.users}</span>

                <span>
                  {s.status === 'Blocked' ? (
                    <Badge tone="neutral">{t('sa.store.status.blocked')}</Badge>
                  ) : s.status === 'Overdue' ? (
                    <Badge tone="danger">{t('sa.store.status.overdue')}</Badge>
                  ) : (
                    <Badge tone="success">{t('sa.store.status.active')}</Badge>
                  )}
                </span>

                <div className="flex justify-end">
                  <Button
                    variant={s.isBlocked ? 'secondary' : 'danger'}
                    onClick={(e) => {
                      // Qator bosilishi drawer ochadi — tugma uni ochmasin.
                      e.stopPropagation();
                      toggleBlock.mutate(s);
                    }}
                    disabled={toggleBlock.isPending}
                  >
                    {s.isBlocked ? t('sa.store.unblock') : t('sa.store.block')}
                  </Button>
                </div>
              </div>
            ))
          )}
        </Card>
      </div>

      <CreateStoreModal
        segment={segment}
        request={null}
        standalone={createOpen}
        onClose={() => setCreateOpen(false)}
        onCreated={() => {
          void qc.invalidateQueries({ queryKey: ['sa-stores'] });
          void qc.invalidateQueries({ queryKey: ['sa-dashboard'] });
        }}
      />

      <StoreDetailDrawer segment={segment} marketId={openId} onClose={() => setOpenId(null)} />
    </>
  );
}
