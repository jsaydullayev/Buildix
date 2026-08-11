import { useState } from 'react';
import { useQuery, keepPreviousData } from '@tanstack/react-query';
import { useTranslation } from 'react-i18next';
import { AlertTriangle, ShieldAlert, ChevronDown } from 'lucide-react';
import { PageHeader, Card, Badge, Spinner } from '@/shared/ui';
import { cn } from '@/shared/lib/cn';
import { formatShortDate, formatTime } from '@/shared/lib/format';
import { useAuth } from '@/shared/auth/useAuth';
import { PERMISSIONS } from '@/shared/config/permissions';
import { employeesApi } from '@/features/employees/api';
import { auditApi, type AuditLog } from './api';

const PAGE_SIZE = 30;

// Money/stock-reversing or security-sensitive actions get a louder tone.
const ACTION_TONE: Record<string, 'danger' | 'warn' | 'success' | 'info' | 'neutral'> = {
  Delete: 'danger',
  Cancel: 'danger',
  LoginFailed: 'danger',
  Block: 'warn',
  Discount: 'warn',
  MarkDebt: 'warn',
  Return: 'warn',
  PriceOverride: 'warn',
  Withdraw: 'warn',
  PermissionChange: 'warn',
  Create: 'success',
  Login: 'info',
};

// Actions offered in the filter dropdown (the common, review-worthy ones).
const FILTER_ACTIONS = [
  'Create', 'Update', 'Delete', 'Cancel', 'Return', 'Discount', 'MarkDebt',
  'Payment', 'Withdraw', 'PermissionChange', 'Login', 'LoginFailed',
];

export default function AuditPage() {
  const { t, i18n } = useTranslation();
  const { hasPermission } = useAuth();
  const [tab, setTab] = useState<'log' | 'suspicious'>('log');
  const [action, setAction] = useState('');
  const [userId, setUserId] = useState('');
  const [page, setPage] = useState(1);

  const logQuery = useQuery({
    queryKey: ['audit', { action, userId, page }],
    queryFn: () => auditApi.query({ action: action || null, userId: userId || null, page, size: PAGE_SIZE }),
    placeholderData: keepPreviousData,
    enabled: tab === 'log',
  });
  const suspiciousQuery = useQuery({
    queryKey: ['audit-suspicious'],
    queryFn: auditApi.suspicious,
    enabled: tab === 'suspicious',
  });
  const usersQuery = useQuery({
    queryKey: ['sellers-filter'],
    queryFn: () => employeesApi.list(),
    enabled: hasPermission(PERMISSIONS.users.access),
  });

  const susp = suspiciousQuery.data;
  const suspCount =
    (susp?.failedLoginBursts.length ?? 0) + (susp?.bulkDeleteBursts.length ?? 0) + (susp?.recentErrors.length ?? 0);

  return (
    <>
      <PageHeader title={t('audit.title')} subtitle={t('audit.subtitle')} />

      <div className="flex flex-1 flex-col gap-[18px] p-4 sm:p-6 lg:p-8">
        {/* Tabs */}
        <div className="flex items-center gap-1.5">
          <Tab label={t('audit.tabs.log')} active={tab === 'log'} onClick={() => setTab('log')} />
          <Tab
            label={t('audit.tabs.suspicious')}
            active={tab === 'suspicious'}
            onClick={() => setTab('suspicious')}
            badge={suspCount > 0 ? suspCount : undefined}
          />
        </div>

        {tab === 'log' ? (
          <>
            {/* Filters */}
            <div className="flex flex-wrap items-center gap-2">
              <select
                value={action}
                onChange={(e) => {
                  setAction(e.target.value);
                  setPage(1);
                }}
                className="h-10 rounded-input border border-input-border bg-surface px-3 text-[13.5px] outline-none focus:border-primary"
              >
                <option value="">{t('audit.allActions')}</option>
                {FILTER_ACTIONS.map((a) => (
                  <option key={a} value={a}>
                    {t(`audit.actions.${a}` as never, { defaultValue: a })}
                  </option>
                ))}
              </select>
              {hasPermission(PERMISSIONS.users.access) && (
                <select
                  value={userId}
                  onChange={(e) => {
                    setUserId(e.target.value);
                    setPage(1);
                  }}
                  className="h-10 rounded-input border border-input-border bg-surface px-3 text-[13.5px] outline-none focus:border-primary"
                >
                  <option value="">{t('audit.allUsers')}</option>
                  {(usersQuery.data ?? []).map((u) => (
                    <option key={u.id} value={u.id}>
                      {u.fullName}
                    </option>
                  ))}
                </select>
              )}
            </div>

            <Card className="overflow-hidden">
              <div className="grid grid-cols-[130px_1fr_150px_130px_28px] items-center gap-3 border-b border-hairline bg-bg/40 px-5 py-3 text-[11.5px] font-semibold tracking-[0.4px] text-muted-2">
                <span>{t('audit.cols.time')}</span>
                <span>{t('audit.cols.action')}</span>
                <span>{t('audit.cols.user')}</span>
                <span>{t('audit.cols.entity')}</span>
                <span />
              </div>
              {logQuery.isLoading ? (
                <div className="flex items-center justify-center py-20 text-primary">
                  <Spinner size={24} />
                </div>
              ) : logQuery.data && logQuery.data.items.length > 0 ? (
                logQuery.data.items.map((row) => <LogRow key={row.id} row={row} lang={i18n.language} />)
              ) : (
                <div className="py-20 text-center text-[14px] text-muted-2">{t('audit.empty')}</div>
              )}
            </Card>

            {logQuery.data && logQuery.data.totalPages > 1 && (
              <div className="flex items-center justify-end gap-1.5">
                <PagerBtn dir="‹" disabled={page <= 1} onClick={() => setPage((p) => p - 1)} />
                <span className="px-2 text-[13px] text-muted nums">
                  {page} / {logQuery.data.totalPages}
                </span>
                <PagerBtn
                  dir="›"
                  disabled={page >= logQuery.data.totalPages}
                  onClick={() => setPage((p) => p + 1)}
                />
              </div>
            )}
          </>
        ) : (
          <SuspiciousTab query={suspiciousQuery} lang={i18n.language} />
        )}
      </div>
    </>
  );
}

function LogRow({ row, lang }: { row: AuditLog; lang: string }) {
  const { t } = useTranslation();
  const [open, setOpen] = useState(false);
  const pretty = usePrettyPayload(row.payload);

  return (
    <div className="border-b border-hairline last:border-0">
      <button
        type="button"
        onClick={() => setOpen((o) => !o)}
        className="grid w-full grid-cols-[130px_1fr_150px_130px_28px] items-center gap-3 px-5 py-2.5 text-left text-[13px] hover:bg-bg/40"
      >
        <span className="text-muted-2 nums">
          {formatShortDate(row.createdAt, lang)} {formatTime(row.createdAt)}
        </span>
        <span>
          <Badge tone={ACTION_TONE[row.action] ?? 'neutral'}>
            {t(`audit.actions.${row.action}` as never, { defaultValue: row.action })}
          </Badge>
        </span>
        <span className="truncate text-muted">{row.userName ?? '—'}</span>
        <span className="truncate text-muted-2">
          {t(`audit.entities.${row.entityType}` as never, { defaultValue: row.entityType })}
        </span>
        <ChevronDown size={15} className={cn('text-muted-2 transition-transform', open && 'rotate-180')} />
      </button>
      {open && pretty && (
        <pre className="overflow-x-auto whitespace-pre-wrap break-words bg-bg/60 px-5 py-3 text-[11.5px] leading-relaxed text-muted">
          {pretty}
        </pre>
      )}
    </div>
  );
}

/** Pretty-print the JSON payload; fall back to the raw string if it won't parse. */
function usePrettyPayload(payload: string): string {
  if (!payload) return '';
  try {
    return JSON.stringify(JSON.parse(payload), null, 2);
  } catch {
    return payload;
  }
}

function SuspiciousTab({
  query,
  lang,
}: {
  query: { isLoading: boolean; data?: import('./api').SuspiciousReport };
  lang: string;
}) {
  const { t } = useTranslation();
  if (query.isLoading) {
    return (
      <div className="flex items-center justify-center py-20 text-primary">
        <Spinner size={24} />
      </div>
    );
  }
  const s = query.data;
  const empty = s && s.failedLoginBursts.length === 0 && s.bulkDeleteBursts.length === 0 && s.recentErrors.length === 0;
  if (!s || empty) {
    return (
      <Card className="flex flex-col items-center gap-2 py-16 text-center">
        <ShieldAlert size={28} className="text-success" />
        <p className="text-[14px] text-muted-2">{t('audit.suspicious.clean')}</p>
      </Card>
    );
  }
  return (
    <div className="flex flex-col gap-[18px]">
      {s.failedLoginBursts.length > 0 && (
        <Card className="p-4 sm:p-5">
          <h3 className="mb-3 flex items-center gap-2 text-[15px] font-semibold">
            <AlertTriangle size={16} className="text-danger" />
            {t('audit.suspicious.failedLogins')}
          </h3>
          <div className="flex flex-col divide-y divide-hairline">
            {s.failedLoginBursts.map((b, i) => (
              <div key={i} className="flex items-center justify-between gap-3 py-2.5 text-[13px]">
                <span className="font-medium">{b.username}</span>
                <span className="text-muted-2">
                  {t('audit.suspicious.attempts', { count: b.count })} · {b.ipAddresses.join(', ')}
                </span>
              </div>
            ))}
          </div>
        </Card>
      )}
      {s.bulkDeleteBursts.length > 0 && (
        <Card className="p-4 sm:p-5">
          <h3 className="mb-3 flex items-center gap-2 text-[15px] font-semibold">
            <AlertTriangle size={16} className="text-danger" />
            {t('audit.suspicious.bulkDelete')}
          </h3>
          <div className="flex flex-col divide-y divide-hairline">
            {s.bulkDeleteBursts.map((b, i) => (
              <div key={i} className="flex items-center justify-between gap-3 py-2.5 text-[13px]">
                <span className="font-medium">{b.userName ?? '—'}</span>
                <span className="text-muted-2">
                  {t('audit.suspicious.deletes', { count: b.count })} · {b.entityTypes.join(', ')}
                </span>
              </div>
            ))}
          </div>
        </Card>
      )}
      {s.recentErrors.length > 0 && (
        <Card className="p-4 sm:p-5">
          <h3 className="mb-3 flex items-center gap-2 text-[15px] font-semibold">
            <AlertTriangle size={16} className="text-warn-strong" />
            {t('audit.suspicious.errors')}
          </h3>
          <div className="flex flex-col divide-y divide-hairline">
            {s.recentErrors.map((e, i) => (
              <div key={i} className="flex items-center justify-between gap-3 py-2.5 text-[12.5px]">
                <span className="min-w-0">
                  <span className="mr-2 font-semibold text-danger nums">{e.statusCode}</span>
                  <span className="text-muted">{e.method} {e.path}</span>
                </span>
                <span className="whitespace-nowrap text-muted-2 nums">
                  {formatShortDate(e.createdAt, lang)} {formatTime(e.createdAt)}
                </span>
              </div>
            ))}
          </div>
        </Card>
      )}
    </div>
  );
}

function Tab({ label, active, onClick, badge }: { label: string; active: boolean; onClick: () => void; badge?: number }) {
  return (
    <button
      type="button"
      onClick={onClick}
      className={cn(
        'flex items-center gap-2 rounded-input px-4 py-2 text-[13.5px] font-medium transition-colors',
        active ? 'bg-primary text-white' : 'border border-input-border bg-surface text-muted hover:text-text',
      )}
    >
      {label}
      {badge !== undefined && (
        <span className={cn('rounded-pill px-1.5 text-[11px] font-semibold', active ? 'bg-white/25' : 'bg-danger text-white')}>
          {badge}
        </span>
      )}
    </button>
  );
}

function PagerBtn({ dir, disabled, onClick }: { dir: string; disabled: boolean; onClick: () => void }) {
  return (
    <button
      type="button"
      disabled={disabled}
      onClick={onClick}
      className="h-8 rounded-md border border-input-border bg-surface px-3 text-[13px] disabled:opacity-40"
    >
      {dir}
    </button>
  );
}
