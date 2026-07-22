import { useMemo, useState } from 'react';
import { useQuery, useMutation, useQueryClient } from '@tanstack/react-query';
import { useTranslation } from 'react-i18next';
import { Plus, Search } from 'lucide-react';
import { PageHeader, Button, Card, Badge, Spinner } from '@/shared/ui';
import { cn } from '@/shared/lib/cn';
import { formatSum, formatRelative } from '@/shared/lib/format';
import { useDebounce } from '@/shared/hooks/useDebounce';
import { useAuth } from '@/shared/auth/useAuth';
import { PERMISSIONS } from '@/shared/config/permissions';
import { employeesApi, type Employee, type StaffRow } from './api';
import { AddEmployeeModal } from './AddEmployeeModal';

const ROLE_FILTERS = ['all', 'Admin', 'Seller'] as const;
const ROLE_TONE: Record<string, 'info' | 'warn' | 'neutral'> = {
  Owner: 'warn',
  Admin: 'info',
  Seller: 'neutral',
};

function isOnline(lastActiveAt: string | null): boolean {
  if (!lastActiveAt) return false;
  return Date.now() - new Date(lastActiveAt).getTime() < 5 * 60 * 1000;
}

export default function EmployeesPage() {
  const { t, i18n } = useTranslation();
  const { hasPermission } = useAuth();
  const canManage = hasPermission(PERMISSIONS.users.manage);
  const qc = useQueryClient();

  const [search, setSearch] = useState('');
  const [role, setRole] = useState<string>('all');
  const [addOpen, setAddOpen] = useState(false);
  const debouncedSearch = useDebounce(search);

  const listQuery = useQuery({
    queryKey: ['employees', { search: debouncedSearch, role }],
    queryFn: () => employeesApi.list(debouncedSearch, role === 'all' ? null : role),
  });
  // L-4: staff-performance is reports.access-gated on the backend; only fetch it
  // when the caller actually has that permission (else every render 403-spams).
  const perfQuery = useQuery({
    queryKey: ['staff-perf'],
    queryFn: employeesApi.staffPerformance,
    enabled: hasPermission(PERMISSIONS.reports.access),
  });

  const perfById = useMemo(() => {
    const map = new Map<string, StaffRow>();
    (perfQuery.data ?? []).forEach((r) => map.set(r.userId, r));
    return map;
  }, [perfQuery.data]);

  const activeToday = useMemo(
    () =>
      (listQuery.data ?? []).filter(
        (u) => u.lastActiveAt && new Date(u.lastActiveAt).toDateString() === new Date().toDateString(),
      ).length,
    [listQuery.data],
  );

  const toggle = useMutation({
    mutationFn: (u: Employee) => (u.isActive ? employeesApi.deactivate(u.id) : employeesApi.activate(u.id)),
    onSuccess: () => void qc.invalidateQueries({ queryKey: ['employees'] }),
  });

  return (
    <>
      <PageHeader
        title={t('employees.title')}
        subtitle={t('employees.subtitle', { count: listQuery.data?.length ?? 0, active: activeToday })}
        actions={
          canManage && (
            <Button onClick={() => setAddOpen(true)}>
              <Plus size={15} strokeWidth={2.4} />
              {t('employees.add')}
            </Button>
          )
        }
      />

      <div className="flex flex-1 flex-col gap-[18px] p-8">
        <div className="flex flex-wrap items-center gap-3">
          <div className="relative min-w-[280px] flex-1">
            <Search size={17} className="absolute left-3.5 top-1/2 -translate-y-1/2 text-muted-2" />
            <input
              value={search}
              onChange={(e) => setSearch(e.target.value)}
              placeholder={t('employees.searchPlaceholder')}
              className="h-11 w-full rounded-input border border-input-border bg-surface pl-11 pr-4 text-[14px] outline-none focus:border-primary focus:shadow-focus-ring"
            />
          </div>
          <div className="flex items-center gap-1.5">
            {ROLE_FILTERS.map((r) => (
              <button
                key={r}
                type="button"
                onClick={() => setRole(r)}
                className={cn(
                  'rounded-input px-3.5 py-2 text-[13px] font-medium transition-colors',
                  role === r ? 'bg-primary text-white' : 'border border-input-border bg-surface text-muted hover:text-text',
                )}
              >
                {t(`employees.roles.${r === 'all' ? 'all' : r.toLowerCase()}` as never)}
              </button>
            ))}
          </div>
        </div>

        {listQuery.isLoading ? (
          <div className="flex items-center justify-center py-20 text-primary">
            <Spinner size={24} />
          </div>
        ) : listQuery.data && listQuery.data.length > 0 ? (
          <div className="grid grid-cols-3 gap-4">
            {listQuery.data.map((u) => (
              <EmployeeCard
                key={u.id}
                user={u}
                perf={perfById.get(u.id)}
                canManage={canManage}
                lang={i18n.language}
                onToggle={() => toggle.mutate(u)}
                busy={toggle.isPending}
              />
            ))}
          </div>
        ) : (
          <div className="py-20 text-center text-[14px] text-muted-2">{t('employees.empty')}</div>
        )}
      </div>

      <AddEmployeeModal open={addOpen} onClose={() => setAddOpen(false)} />
    </>
  );
}

function initials(name: string) {
  const p = name.trim().split(/\s+/);
  return (p[0]?.[0] ?? '') + (p[1]?.[0] ?? '');
}

function EmployeeCard({
  user: u,
  perf,
  canManage,
  lang,
  onToggle,
  busy,
}: {
  user: Employee;
  perf: StaffRow | undefined;
  canManage: boolean;
  lang: string;
  onToggle: () => void;
  busy: boolean;
}) {
  const { t } = useTranslation();
  const online = isOnline(u.lastActiveAt);

  return (
    <Card className={cn('p-5', !u.isActive && 'opacity-60')}>
      <div className="flex items-start gap-3">
        <div className="relative flex h-12 w-12 flex-none items-center justify-center overflow-hidden rounded-pill bg-primary/10 text-[15px] font-semibold uppercase text-primary">
          {u.profileImage ? <img src={u.profileImage} alt="" className="h-full w-full object-cover" /> : initials(u.fullName)}
          {online && (
            <span className="absolute bottom-0 right-0 h-3 w-3 rounded-full border-2 border-surface bg-success" />
          )}
        </div>
        <div className="min-w-0 flex-1">
          <div className="flex items-center gap-2">
            <span className="truncate font-semibold">{u.fullName}</span>
            <Badge tone={ROLE_TONE[u.role] ?? 'neutral'}>
              {t(`employees.roles.${u.role.toLowerCase()}` as never)}
            </Badge>
          </div>
          <div className="truncate text-[12.5px] text-muted-2">@{u.username}</div>
          <div className="mt-0.5 text-[12px] text-muted-2">
            {!u.isActive ? (
              <span className="text-danger">{t('employees.blocked')}</span>
            ) : online ? (
              <span className="text-success">{t('employees.online')}</span>
            ) : u.lastActiveAt ? (
              t('employees.lastSeen', { time: formatRelative(u.lastActiveAt, lang) })
            ) : (
              t('employees.never')
            )}
          </div>
        </div>
      </div>

      {u.phone && <div className="mt-3 text-[13px] text-muted nums">{u.phone}</div>}

      <div className="mt-4 grid grid-cols-3 gap-2 border-t border-hairline pt-3 text-center">
        <Stat label={t('employees.stats.checks')} value={String(perf?.saleCount ?? 0)} />
        <Stat label={t('employees.stats.revenue')} value={formatSum(perf?.revenue ?? 0)} />
        <Stat label={t('employees.stats.shifts')} value={String(perf?.shiftCount ?? 0)} />
      </div>

      {canManage && u.role !== 'Owner' && (
        <Button
          variant={u.isActive ? 'danger' : 'secondary'}
          fullWidth
          size="sm"
          className="mt-4"
          loading={busy}
          onClick={onToggle}
        >
          {u.isActive ? t('employees.block') : t('employees.unblock')}
        </Button>
      )}
    </Card>
  );
}

function Stat({ label, value }: { label: string; value: string }) {
  return (
    <div>
      <div className="text-[14px] font-semibold nums">{value}</div>
      <div className="mt-0.5 text-[11px] text-muted-2">{label}</div>
    </div>
  );
}
