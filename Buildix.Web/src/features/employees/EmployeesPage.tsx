import { useEffect, useMemo, useState } from 'react';
import { useQuery, useMutation, useQueryClient } from '@tanstack/react-query';
import { useTranslation } from 'react-i18next';
import { Plus, Search, Pencil, Trash2 } from 'lucide-react';
import { PageHeader, Button, Card, Badge, Spinner } from '@/shared/ui';
import { cn } from '@/shared/lib/cn';
import { formatSum, formatRelative } from '@/shared/lib/format';
import { useDebounce } from '@/shared/hooks/useDebounce';
import { useAuth } from '@/shared/auth/useAuth';
import { PERMISSIONS, ROLES } from '@/shared/config/permissions';
import { employeesApi, type Employee, type StaffRow } from './api';
import { AddEmployeeModal } from './AddEmployeeModal';
import { EditEmployeeModal } from './EditEmployeeModal';
import { PermissionEditor } from './PermissionEditor';

const ROLE_FILTERS = ['all', 'Admin', 'Seller'] as const;
const ROLE_TONE: Record<string, 'info' | 'warn' | 'neutral'> = { Owner: 'warn', Admin: 'info', Seller: 'neutral' };

function isOnline(lastActiveAt: string | null): boolean {
  if (!lastActiveAt) return false;
  return Date.now() - new Date(lastActiveAt).getTime() < 5 * 60 * 1000;
}
function initials(name: string) {
  const p = name.trim().split(/\s+/);
  return (p[0]?.[0] ?? '') + (p[1]?.[0] ?? '');
}
const TASHKENT_TZ = 'Asia/Tashkent';
const tashkentDay = (d: string | Date) => new Intl.DateTimeFormat('en-CA', { timeZone: TASHKENT_TZ }).format(new Date(d));

/**
 * Сотрудники и доступы — master-detail. Left: searchable employee cards. Right
 * (sticky): the selected employee's profile, actions, and inline permission
 * editor (access preset + limits + grouped toggles).
 */
export default function EmployeesPage() {
  const { t, i18n } = useTranslation();
  const { hasPermission, hasRole } = useAuth();
  const canManage = hasPermission(PERMISSIONS.users.manage);
  const canEditPermissions = hasRole(ROLES.Owner, ROLES.SuperAdmin);
  const canSeePerf = hasPermission(PERMISSIONS.reports.access);
  const qc = useQueryClient();

  const [search, setSearch] = useState('');
  const [role, setRole] = useState<string>('all');
  // Card figures show this-month performance (the design has no period switch here).
  const period = 'month';
  const [addOpen, setAddOpen] = useState(false);
  const [editing, setEditing] = useState<Employee | null>(null);
  const [selectedId, setSelectedId] = useState<string | null>(null);
  const debouncedSearch = useDebounce(search);

  const listQuery = useQuery({
    queryKey: ['employees', { search: debouncedSearch, role }],
    queryFn: () => employeesApi.list(debouncedSearch, role === 'all' ? null : role),
  });
  const users = useMemo(() => listQuery.data ?? [], [listQuery.data]);
  const perfQuery = useQuery({
    queryKey: ['staff-perf', period],
    queryFn: () => employeesApi.staffPerformance(period),
    enabled: canSeePerf,
  });
  const perfById = useMemo(() => {
    const map = new Map<string, StaffRow>();
    (perfQuery.data ?? []).forEach((r) => map.set(r.userId, r));
    return map;
  }, [perfQuery.data]);

  useEffect(() => {
    if (users.length === 0) setSelectedId(null);
    else if (!selectedId || !users.some((u) => u.id === selectedId)) setSelectedId(users[0]!.id);
  }, [users, selectedId]);
  const selected = useMemo(() => users.find((u) => u.id === selectedId) ?? null, [users, selectedId]);

  const activeToday = useMemo(
    () => users.filter((u) => u.lastActiveAt && tashkentDay(u.lastActiveAt) === tashkentDay(new Date())).length,
    [users],
  );

  const toggle = useMutation({
    mutationFn: (u: Employee) => (u.isActive ? employeesApi.deactivate(u.id) : employeesApi.activate(u.id)),
    onSuccess: () => void qc.invalidateQueries({ queryKey: ['employees'] }),
  });
  const del = useMutation({
    mutationFn: (u: Employee) => employeesApi.remove(u.id),
    onSuccess: () => void qc.invalidateQueries({ queryKey: ['employees'] }),
  });
  const askDelete = (u: Employee) => {
    if (window.confirm(t('employees.deleteConfirm', { name: u.fullName }))) del.mutate(u);
  };

  const isProtected = (u: Employee) => u.role === 'Owner' || u.role === 'SuperAdmin';

  return (
    <>
      <PageHeader
        title={t('employees.title')}
        subtitle={t('employees.subtitle', { count: users.length, active: activeToday })}
        actions={
          canManage && (
            <Button onClick={() => setAddOpen(true)}>
              <Plus size={15} strokeWidth={2.4} />
              {t('employees.add')}
            </Button>
          )
        }
      />

      <div className="flex flex-1 gap-[18px] p-8">
        {/* Master */}
        <div className="flex min-w-0 flex-1 flex-col gap-3">
          <div className="flex flex-wrap items-center gap-3">
            <div className="relative min-w-[240px] flex-1">
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
                    'rounded-input px-3 py-2 text-[12.5px] font-medium transition-colors',
                    role === r ? 'bg-primary text-white' : 'border border-input-border bg-surface text-muted hover:text-text',
                  )}
                >
                  {t(`employees.roles.${r === 'all' ? 'all' : r.toLowerCase()}` as never)}
                </button>
              ))}
            </div>
          </div>

          {listQuery.isLoading ? (
            <div className="flex items-center justify-center py-20 text-primary"><Spinner size={24} /></div>
          ) : users.length === 0 ? (
            <Card className="py-20 text-center text-[14px] text-muted-2">{t('employees.empty')}</Card>
          ) : (
            <div className="flex flex-col gap-2.5">
              {users.map((u) => (
                <EmployeeCard
                  key={u.id}
                  user={u}
                  perf={perfById.get(u.id)}
                  active={u.id === selectedId}
                  onClick={() => setSelectedId(u.id)}
                />
              ))}
            </div>
          )}
        </div>

        {/* Detail */}
        <div className="w-[420px] flex-none">
          {selected && (
            <Card className="sticky top-8 flex max-h-[calc(100vh-64px)] flex-col overflow-hidden">
              <div className="flex items-start gap-3 border-b border-hairline p-5">
                <div className="flex h-12 w-12 flex-none items-center justify-center overflow-hidden rounded-pill bg-primary/10 text-[15px] font-semibold uppercase text-primary">
                  {selected.profileImage ? <img src={selected.profileImage} alt="" className="h-full w-full object-cover" /> : initials(selected.fullName)}
                </div>
                <div className="min-w-0 flex-1">
                  <div className="flex items-center gap-2">
                    <span className="truncate font-semibold">{selected.fullName}</span>
                    <Badge tone={selected.isActive ? 'success' : 'danger'}>
                      {selected.isActive ? t('employees.active') : t('employees.blocked')}
                    </Badge>
                  </div>
                  <div className="truncate text-[12.5px] text-muted-2">
                    {selected.phone ? `${selected.phone} · ` : ''}@{selected.username}
                  </div>
                  {selected.lastActiveAt && (
                    <div className="mt-0.5 text-[11.5px] text-muted-2">
                      {isOnline(selected.lastActiveAt)
                        ? t('employees.online')
                        : t('employees.lastSeen', { time: formatRelative(selected.lastActiveAt, i18n.language) })}
                    </div>
                  )}
                </div>
                <Badge tone={ROLE_TONE[selected.role] ?? 'neutral'}>{t(`employees.roles.${selected.role.toLowerCase()}` as never)}</Badge>
              </div>

              {canSeePerf && (
                <div className="grid grid-cols-3 gap-2 border-b border-hairline px-5 py-3 text-center">
                  <Stat label={t('employees.stats.checks')} value={String(perfById.get(selected.id)?.saleCount ?? 0)} />
                  <Stat label={t('employees.stats.revenue')} value={formatSum(perfById.get(selected.id)?.revenue ?? 0)} />
                  <Stat label={t('employees.stats.shifts')} value={String(perfById.get(selected.id)?.shiftCount ?? 0)} />
                </div>
              )}

              <div className="min-h-0 flex-1 overflow-y-auto px-5 py-4">
                {isProtected(selected) ? (
                  <p className="py-10 text-center text-[13px] text-muted-2">{t('employees.protected')}</p>
                ) : (
                  <>
                    {canManage && (
                      <div className="mb-4 flex items-center gap-2">
                        <Button
                          variant={selected.isActive ? 'danger' : 'secondary'}
                          size="sm"
                          className="flex-1"
                          loading={toggle.isPending}
                          onClick={() => toggle.mutate(selected)}
                        >
                          {selected.isActive ? t('employees.block') : t('employees.unblock')}
                        </Button>
                        <Button variant="secondary" size="sm" onClick={() => setEditing(selected)}>
                          <Pencil size={13} />
                          {t('employees.editTitle')}
                        </Button>
                        <Button variant="ghost" size="sm" className="text-danger" onClick={() => askDelete(selected)}>
                          <Trash2 size={13} />
                        </Button>
                      </div>
                    )}
                    {canEditPermissions && <PermissionEditor employee={selected} />}
                  </>
                )}
              </div>
            </Card>
          )}
        </div>
      </div>

      <AddEmployeeModal open={addOpen} onClose={() => setAddOpen(false)} />
      <EditEmployeeModal employee={editing} onClose={() => setEditing(null)} />
    </>
  );
}

function EmployeeCard({
  user: u,
  perf,
  active,
  onClick,
}: {
  user: Employee;
  perf: StaffRow | undefined;
  active: boolean;
  onClick: () => void;
}) {
  const { t } = useTranslation();
  const online = isOnline(u.lastActiveAt);
  return (
    <button
      type="button"
      onClick={onClick}
      className={cn(
        'flex items-center gap-3 rounded-card border bg-surface px-4 py-3 text-left transition-colors',
        active ? 'border-primary shadow-focus-ring' : 'border-border hover:border-input-border',
        !u.isActive && 'opacity-60',
      )}
    >
      <div className="relative flex h-11 w-11 flex-none items-center justify-center overflow-hidden rounded-pill bg-primary/10 text-[14px] font-semibold uppercase text-primary">
        {u.profileImage ? <img src={u.profileImage} alt="" className="h-full w-full object-cover" /> : initials(u.fullName)}
        {online && <span className="absolute bottom-0 right-0 h-3 w-3 rounded-full border-2 border-surface bg-success" />}
      </div>
      <div className="min-w-0 flex-1">
        <div className="flex items-center gap-2">
          <span className="truncate text-[13.5px] font-semibold">{u.fullName}</span>
          {!u.isActive && <Badge tone="danger">{t('employees.blocked')}</Badge>}
        </div>
        <div className="truncate text-[11.5px] text-muted-2">
          {t(`employees.roles.${u.role.toLowerCase()}` as never)} · @{u.username}
        </div>
      </div>
      <div className="flex-none text-right">
        <div className="text-[13px] font-semibold nums">{formatSum(perf?.revenue ?? 0)}</div>
        <div className="text-[10.5px] text-muted-2">{t('employees.stats.revenue')}</div>
      </div>
    </button>
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
