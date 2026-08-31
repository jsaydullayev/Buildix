import { useMemo, useState } from 'react';
import { useParams } from 'react-router-dom';
import { keepPreviousData, useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { useTranslation } from 'react-i18next';
import { Search, Info } from 'lucide-react';
import { PageHeader, Card, Badge, Button, Spinner } from '@/shared/ui';
import { cn } from '@/shared/lib/cn';
import { formatRelative, initials } from '@/shared/lib/format';
import { useDebounce } from '@/shared/hooks/useDebounce';
import type { ApiError } from '@/shared/api/types';
import { superAdminApi, type SaRole, type SaUserRow } from './api';
import { ResetPasswordModal } from './ResetPasswordModal';

const ROLES: (SaRole | 'all')[] = ['all', 'Owner', 'Admin', 'Seller'];
const GRID = 'min-w-[900px] grid-cols-[minmax(0,1.5fr)_120px_minmax(0,1.2fr)_150px_120px_200px]';
const PAGE_SIZE = 20;

const ROLE_TONE: Record<SaRole, 'info' | 'warn' | 'neutral'> = {
  Owner: 'info',
  Admin: 'warn',
  Seller: 'neutral',
};


export default function SuperUsersPage() {
  const { segment = '' } = useParams();
  const { t, i18n } = useTranslation();
  const qc = useQueryClient();

  const [role, setRole] = useState<SaRole | 'all'>('all');
  const [storeId, setStoreId] = useState<number | 'all'>('all');
  const [search, setSearch] = useState('');
  const [page, setPage] = useState(1);
  const [resetting, setResetting] = useState<SaUserRow | null>(null);
  const debounced = useDebounce(search, 300);

  const stores = useQuery({ queryKey: ['sa-stores', segment], queryFn: () => superAdminApi.stores(segment) });

  const query = useQuery({
    // Filtr va sahifalash SERVER tomonda — foydalanuvchilar soni do'konlar
    // bilan birga o'sadi, klient filtri butun bazani tortib olishni talab
    // qilardi.
    queryKey: ['sa-users', segment, role, storeId, debounced, page],
    queryFn: () =>
      superAdminApi.users(segment, {
        role: role === 'all' ? undefined : role,
        marketId: storeId === 'all' ? undefined : storeId,
        search: debounced.trim() || undefined,
        page,
        size: PAGE_SIZE,
      }),
    placeholderData: keepPreviousData,
  });

  const toggleActive = useMutation({
    mutationFn: (u: SaUserRow) => superAdminApi.setUserActive(segment, u.id, !u.isActive),
    onSuccess: () => void qc.invalidateQueries({ queryKey: ['sa-users', segment] }),
  });

  const err = toggleActive.error
    ? ((toggleActive.error as unknown as ApiError).message ?? t('common.somethingWrong'))
    : null;

  const data = query.data;
  const counts = useMemo(() => {
    const items = data?.items ?? [];
    return {
      owners: items.filter((u) => u.role === 'Owner').length,
      admins: items.filter((u) => u.role === 'Admin').length,
      sellers: items.filter((u) => u.role === 'Seller').length,
    };
  }, [data]);

  const reset = (next: () => void) => {
    setPage(1);
    next();
  };

  return (
    <>
      <PageHeader
        title={t('sa.users.title')}
        subtitle={t('sa.users.summary', {
          total: data?.total ?? 0,
          owners: counts.owners,
          admins: counts.admins,
          sellers: counts.sellers,
        })}
        actions={
          <div className="flex flex-wrap items-center gap-2">
            {ROLES.map((r) => (
              <button
                key={r}
                type="button"
                onClick={() => reset(() => setRole(r))}
                className={cn(
                  'rounded-pill border px-4 py-1.5 text-[13px] transition-colors',
                  role === r
                    ? 'border-primary bg-primary font-semibold text-white'
                    : 'border-border bg-surface text-muted hover:border-primary hover:text-primary',
                )}
              >
                {r === 'all' ? t('common.all') : t(`sa.users.roles.${r}` as never)}
              </button>
            ))}
          </div>
        }
      />

      <div className="flex flex-col gap-4 p-4 sm:p-6 lg:p-8">
        <div className="flex items-center gap-3">
          <div className="relative w-full sm:w-[320px]">
            <Search size={15} className="absolute left-3.5 top-1/2 -translate-y-1/2 text-muted-2" />
            <input
              value={search}
              onChange={(e) => reset(() => setSearch(e.target.value))}
              placeholder={t('sa.users.searchPlaceholder')}
              className="h-11 w-full rounded-input border border-border bg-surface pl-9 pr-3 text-[13.5px] outline-none focus:border-primary focus:shadow-focus-ring"
            />
          </div>
          <select
            value={storeId}
            onChange={(e) =>
              reset(() => setStoreId(e.target.value === 'all' ? 'all' : Number(e.target.value)))
            }
            className="h-11 rounded-input border border-border bg-surface px-3 text-[13.5px] outline-none focus:border-primary"
          >
            <option value="all">{t('sa.users.allStores')}</option>
            {(stores.data ?? []).map((s) => (
              <option key={s.marketId} value={s.marketId}>
                {s.name}
              </option>
            ))}
          </select>
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
            <span>{t('sa.users.col.user')}</span>
            <span>{t('sa.users.col.role')}</span>
            <span>{t('sa.users.col.store')}</span>
            <span>{t('sa.users.col.lastLogin')}</span>
            <span>{t('sa.stores.col.status')}</span>
            <span />
          </div>

          {query.isLoading ? (
            <div className="flex justify-center py-14 text-primary">
              <Spinner size={22} />
            </div>
          ) : (data?.items.length ?? 0) === 0 ? (
            <div className="py-14 text-center text-[13.5px] text-muted">{t('sa.stores.empty')}</div>
          ) : (
            data!.items.map((u) => (
              <div
                key={u.id}
                className={cn(
                  'grid items-center gap-3 border-b border-hairline px-6 py-3 last:border-b-0',
                  GRID,
                  !u.isActive && 'bg-bg/70',
                )}
              >
                <div className="flex min-w-0 items-center gap-3">
                  <span className="flex h-9 w-9 flex-none items-center justify-center rounded-pill bg-primary-soft text-[11.5px] font-semibold text-primary">
                    {initials(u.fullName)}
                  </span>
                  <div className="min-w-0">
                    <div className="truncate text-[14px] font-semibold">{u.fullName}</div>
                    <div className="truncate text-[11.5px] text-muted-2">
                      {u.username}
                      {u.phone ? ` · ${u.phone}` : ''}
                    </div>
                  </div>
                </div>

                <span>
                  <Badge tone={ROLE_TONE[u.role]}>{t(`sa.users.roles.${u.role}` as never)}</Badge>
                </span>

                <span className="truncate text-[13px]">{u.storeName ?? '—'}</span>

                <span className="text-[12.5px] text-muted">
                  {u.lastActiveAt ? formatRelative(u.lastActiveAt, i18n.language) : '—'}
                </span>

                <span>
                  {u.isActive ? (
                    <Badge tone="success">{t('sa.store.status.active')}</Badge>
                  ) : (
                    <Badge tone="neutral">{t('sa.store.status.blocked')}</Badge>
                  )}
                </span>

                <div className="flex items-center justify-end gap-2">
                  <Button variant="secondary" onClick={() => setResetting(u)}>
                    {t('sa.users.resetPassword')}
                  </Button>
                  <Button
                    variant={u.isActive ? 'danger' : 'secondary'}
                    onClick={() => toggleActive.mutate(u)}
                    disabled={toggleActive.isPending}
                  >
                    {u.isActive ? t('sa.store.block') : t('sa.store.unblock')}
                  </Button>
                </div>
              </div>
            ))
          )}
        </Card>

        {(data?.totalPages ?? 0) > 1 && (
          <div className="flex items-center justify-center gap-3">
            <Button variant="ghost" disabled={page <= 1} onClick={() => setPage((p) => p - 1)}>
              {t('common.prev')}
            </Button>
            <span className="text-[13px] text-muted">
              {data!.page} / {data!.totalPages}
            </span>
            <Button
              variant="ghost"
              disabled={page >= (data?.totalPages ?? 1)}
              onClick={() => setPage((p) => p + 1)}
            >
              {t('common.next')}
            </Button>
          </div>
        )}

        <p className="flex items-start gap-2 px-1 text-[12.5px] leading-relaxed text-muted">
          <Info size={14} className="mt-0.5 flex-none text-muted-2" />
          {t('sa.users.scopeHint')}
        </p>
      </div>

      <ResetPasswordModal
        segment={segment}
        user={resetting}
        onClose={() => setResetting(null)}
      />
    </>
  );
}
