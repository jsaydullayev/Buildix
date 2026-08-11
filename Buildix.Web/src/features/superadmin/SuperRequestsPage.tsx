import { useMemo, useState } from 'react';
import { useParams } from 'react-router-dom';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { useTranslation } from 'react-i18next';
import { Phone, Search, Info } from 'lucide-react';
import { PageHeader, Card, Badge, Button, Spinner } from '@/shared/ui';
import { cn } from '@/shared/lib/cn';
import { formatRelative } from '@/shared/lib/format';
import { useDebounce } from '@/shared/hooks/useDebounce';
import type { ApiError } from '@/shared/api/types';
import { superAdminApi, type SaRequestRow, type SaRequestStatus } from './api';
import { CreateStoreModal } from './CreateStoreModal';

type Tab = 'new' | 'accepted' | 'rejected' | 'all';

/**
 * Tab → qaysi backend statuslari kiradi. «Принятые» ikkalasini ham ko'rsatadi:
 * qabul qilingan (do'kon hali yaratilmagan) va ulangan (yaratilgan) — operator
 * uchun bu bitta "ha, ishlaymiz" to'plami.
 */
const TAB_MATCH: Record<Tab, SaRequestStatus[]> = {
  new: ['Pending'],
  accepted: ['Accepted', 'Approved'],
  rejected: ['Rejected'],
  all: ['Pending', 'Accepted', 'Approved', 'Rejected'],
};

const GRID = 'min-w-[880px] grid-cols-[minmax(0,1.6fr)_170px_150px_130px_minmax(0,300px)]';

function initials(name: string) {
  return name
    .replace(/[«»№"]/g, '')
    .trim()
    .split(/\s+/)
    .slice(0, 2)
    .map((w) => w[0] ?? '')
    .join('')
    .toUpperCase();
}

export default function SuperRequestsPage() {
  const { segment = '' } = useParams();
  const { t, i18n } = useTranslation();
  const qc = useQueryClient();

  const [tab, setTab] = useState<Tab>('new');
  const [search, setSearch] = useState('');
  const [creating, setCreating] = useState<SaRequestRow | null>(null);
  const [error, setError] = useState<string | null>(null);
  const debounced = useDebounce(search, 250);

  // Bitta so'rov — barcha arizalar. Tab va qidiruv klient tomonda: arizalar
  // soni tabiiy ravishda kichik (kuniga bir nechta), va tab hisoblagichlari
  // baribir to'liq ro'yxatni talab qiladi.
  const query = useQuery({
    queryKey: ['sa-requests', segment],
    queryFn: () => superAdminApi.requests(segment),
    refetchInterval: 60_000,
  });

  const rows = useMemo(() => query.data ?? [], [query.data]);

  const counts = useMemo(
    () => ({
      new: rows.filter((r) => r.status === 'Pending').length,
      accepted: rows.filter((r) => r.status === 'Accepted' || r.status === 'Approved').length,
      rejected: rows.filter((r) => r.status === 'Rejected').length,
      all: rows.length,
    }),
    [rows],
  );

  const visible = useMemo(() => {
    const q = debounced.trim().toLowerCase();
    const digits = q.replace(/\D/g, '');
    return rows.filter(
      (r) =>
        TAB_MATCH[tab].includes(r.status) &&
        (!q ||
          r.fullName.toLowerCase().includes(q) ||
          (digits.length > 0 && r.phone.replace(/\D/g, '').includes(digits))),
    );
  }, [rows, tab, debounced]);

  const act = useMutation({
    mutationFn: (v: { id: string; action: 'accept' | 'reopen' | 'reject' }) => {
      if (v.action === 'accept') return superAdminApi.acceptRequest(segment, v.id);
      if (v.action === 'reopen') return superAdminApi.reopenRequest(segment, v.id);
      return superAdminApi.rejectRequest(segment, v.id, t('sa.requests.rejectedByOperator'));
    },
    onSuccess: () => {
      setError(null);
      void qc.invalidateQueries({ queryKey: ['sa-requests', segment] });
    },
    onError: (e) => setError((e as unknown as ApiError).message ?? t('common.somethingWrong')),
  });

  const statusBadge = (r: SaRequestRow) => {
    if (r.status === 'Approved') {
      return <Badge tone="info">{t('sa.requests.status.connected')}</Badge>;
    }
    if (r.status === 'Accepted') {
      return <Badge tone="success">{t('sa.requests.status.accepted')}</Badge>;
    }
    if (r.status === 'Rejected') {
      return <Badge tone="neutral">{t('sa.requests.status.rejected')}</Badge>;
    }
    return <Badge tone="info">{t('sa.requests.status.new')}</Badge>;
  };

  return (
    <>
      <PageHeader
        title={t('sa.requests.title')}
        subtitle={t('sa.requests.subtitle')}
        actions={
          <div className="flex max-w-full items-center gap-1 overflow-x-auto no-scrollbar rounded-pill bg-hairline p-1">
            {(['new', 'accepted', 'rejected', 'all'] as Tab[]).map((key) => (
              <button
                key={key}
                type="button"
                onClick={() => setTab(key)}
                className={cn(
                  'flex-none whitespace-nowrap rounded-pill px-4 py-1.5 text-[13px] transition-colors',
                  tab === key
                    ? 'bg-surface font-semibold text-text shadow-card'
                    : 'font-medium text-muted hover:text-text',
                )}
              >
                {t(`sa.requests.tabs.${key}` as never)}
                {counts[key] > 0 && <span className="ml-1.5 text-muted-2">{counts[key]}</span>}
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
            placeholder={t('sa.requests.searchPlaceholder')}
            className="h-11 w-full rounded-input border border-border bg-surface pl-9 pr-3 text-[13.5px] outline-none focus:border-primary focus:shadow-focus-ring"
          />
        </div>

        {error && (
          <div className="rounded-card bg-danger-soft px-4 py-2.5 text-[13px] text-danger">
            {error}
          </div>
        )}

        <Card className="min-w-0 overflow-x-auto">
          <div
            className={cn(
              'grid items-center gap-3 border-b border-hairline px-6 py-3 text-[11px] font-semibold uppercase tracking-[0.5px] text-muted-2',
              GRID,
            )}
          >
            <span>{t('sa.requests.col.applicant')}</span>
            <span>{t('sa.requests.col.phone')}</span>
            <span>{t('sa.requests.col.received')}</span>
            <span>{t('sa.requests.col.status')}</span>
            <span />
          </div>

          {query.isLoading ? (
            <div className="flex justify-center py-14 text-primary">
              <Spinner size={22} />
            </div>
          ) : visible.length === 0 ? (
            <div className="py-14 text-center text-[13.5px] text-muted">{t('sa.requests.empty')}</div>
          ) : (
            visible.map((r) => (
              <div
                key={r.id}
                className={cn(
                  'grid items-center gap-3 border-b border-hairline px-6 py-3 last:border-b-0',
                  GRID,
                  r.status === 'Rejected' && 'bg-bg/60',
                )}
              >
                <div className="flex min-w-0 items-center gap-3">
                  <span className="flex h-9 w-9 flex-none items-center justify-center rounded-pill bg-primary-soft text-[11.5px] font-semibold text-primary">
                    {initials(r.fullName)}
                  </span>
                  <div className="min-w-0">
                    <div className="truncate text-[14px] font-semibold">{r.fullName}</div>
                    {r.note && <div className="truncate text-[12px] text-muted-2">{r.note}</div>}
                  </div>
                </div>

                <span className="nums text-[13.5px]">{r.phone}</span>
                <span className="text-[12.5px] text-muted">
                  {formatRelative(r.createdAt, i18n.language)}
                </span>
                <span>{statusBadge(r)}</span>

                <div className="flex items-center justify-end gap-2">
                  {/* Qo'ng'iroq — oqimning birinchi qadami (dizayndagi telefon
                      tugmasi), shuning uchun har qatorda turadi. */}
                  <a
                    href={`tel:${r.phone}`}
                    className="flex h-9 w-9 items-center justify-center rounded-btn border border-border text-muted transition-colors hover:border-primary hover:text-primary"
                    aria-label={t('sa.requests.call')}
                  >
                    <Phone size={15} />
                  </a>

                  {r.status === 'Pending' && (
                    <>
                      <Button
                        onClick={() => act.mutate({ id: r.id, action: 'accept' })}
                        disabled={act.isPending}
                      >
                        {t('sa.requests.accept')}
                      </Button>
                      <Button
                        variant="danger"
                        onClick={() => act.mutate({ id: r.id, action: 'reject' })}
                        disabled={act.isPending}
                      >
                        {t('sa.requests.reject')}
                      </Button>
                    </>
                  )}

                  {r.status === 'Accepted' && (
                    <>
                      <Button onClick={() => setCreating(r)}>{t('sa.requests.createStore')}</Button>
                      <Button
                        variant="ghost"
                        onClick={() => act.mutate({ id: r.id, action: 'reopen' })}
                        disabled={act.isPending}
                      >
                        {t('sa.requests.reopen')}
                      </Button>
                    </>
                  )}

                  {r.status === 'Rejected' && (
                    <Button
                      variant="ghost"
                      onClick={() => act.mutate({ id: r.id, action: 'reopen' })}
                      disabled={act.isPending}
                    >
                      {t('sa.requests.reopen')}
                    </Button>
                  )}

                  {r.status === 'Approved' && (
                    <span className="text-[12.5px] text-muted-2">{t('sa.requests.storeCreated')}</span>
                  )}
                </div>
              </div>
            ))
          )}
        </Card>

        <p className="flex items-start gap-2 px-1 text-[12.5px] leading-relaxed text-muted">
          <Info size={14} className="mt-0.5 flex-none text-muted-2" />
          {t('sa.requests.workflowHint')}
        </p>
      </div>

      <CreateStoreModal
        segment={segment}
        request={creating}
        onClose={() => setCreating(null)}
        onCreated={() => void qc.invalidateQueries({ queryKey: ['sa-requests', segment] })}
      />
    </>
  );
}
