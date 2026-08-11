import { useState } from 'react';
import { useParams, Link } from 'react-router-dom';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { useTranslation } from 'react-i18next';
import { Store, ShieldCheck, CreditCard, AlertTriangle, Clock } from 'lucide-react';
import { PageHeader, Card, Badge, Button, StatCard, Spinner } from '@/shared/ui';
import { cn } from '@/shared/lib/cn';
import { formatRelative, formatShortDate } from '@/shared/lib/format';
import type { ApiError } from '@/shared/api/types';
import { superAdminApi, type SaDashboardStore } from './api';

/** Muddat tugashiga (yoki tugaganiga) necha kun qolgani. */
function daysFrom(iso: string | null): number | null {
  if (!iso) return null;
  return Math.round((new Date(iso).getTime() - Date.now()) / 86_400_000);
}

function StatusBadge({ status }: { status: SaDashboardStore['status'] }) {
  const { t } = useTranslation();
  if (status === 'Blocked') return <Badge tone="neutral">{t('sa.store.status.blocked')}</Badge>;
  if (status === 'Overdue') return <Badge tone="danger">{t('sa.store.status.overdue')}</Badge>;
  return <Badge tone="success">{t('sa.store.status.active')}</Badge>;
}

export default function SuperDashboardPage() {
  const { segment = '' } = useParams();
  const { t, i18n } = useTranslation();
  const qc = useQueryClient();

  const query = useQuery({
    queryKey: ['sa-dashboard', segment],
    queryFn: () => superAdminApi.dashboard(segment),
    refetchInterval: 60_000,
  });

  // Amaldan keyin qator ro'yxatdan yo'qoladi (server faqat «yangi»larni
  // qaytaradi) — operator esa nima bo'lganini ko'rishi kerak. Shuning uchun
  // shu sessiyada tegilgan arizalar eslab qolinadi va tasdiq matni bilan
  // ko'rsatiladi (dizayn: «принята — свяжитесь с клиентом»).
  const [acted, setActed] = useState<{ id: string; fullName: string; phone: string; ok: boolean }[]>([]);

  const act = useMutation({
    mutationFn: (v: { id: string; action: 'accept' | 'reject'; row: { fullName: string; phone: string } }) =>
      v.action === 'accept'
        ? superAdminApi.acceptRequest(segment, v.id)
        : superAdminApi.rejectRequest(segment, v.id, t('sa.requests.rejectedByOperator')),
    onSuccess: (_data, v) => {
      setActed((prev) => [
        { id: v.id, fullName: v.row.fullName, phone: v.row.phone, ok: v.action === 'accept' },
        ...prev.filter((x) => x.id !== v.id),
      ]);
      void qc.invalidateQueries({ queryKey: ['sa-dashboard', segment] });
      void qc.invalidateQueries({ queryKey: ['sa-requests', segment] });
      void qc.invalidateQueries({ queryKey: ['sa-pending-count', segment] });
    },
  });

  const toggleBlock = useMutation({
    mutationFn: (s: SaDashboardStore) =>
      s.isBlocked
        ? superAdminApi.unblockMarket(segment, s.marketId)
        : superAdminApi.blockMarket(segment, s.marketId, t('sa.store.blockedByOperator')),
    onSuccess: () => void qc.invalidateQueries({ queryKey: ['sa-dashboard', segment] }),
  });

  const d = query.data;
  // Xato mutatsiyaning o'z holatidan o'qiladi — alohida state saqlash keyingi
  // muvaffaqiyatli urinishdan keyin eski xabarni ekranda qoldirib ketardi.
  const blockError = toggleBlock.error
    ? ((toggleBlock.error as unknown as ApiError).message ?? t('common.somethingWrong'))
    : null;

  return (
    <>
      <PageHeader
        title={t('sa.dashboard.title')}
        subtitle={`${formatShortDate(new Date(), i18n.language)} · ${t('sa.dashboard.subtitle')}`}
        actions={
          // Holat ko'rsatkichi panelning O'Z so'roviga tayanadi: u qaytgan
          // bo'lsa — API ham, DB ham tirik. Alohida health-endpoint chaqirmaymiz;
          // bu yerda ko'rsatiladigan yagona haqiqat — konsol ma'lumot ola
          // olayotgani. Xato bo'lsa yashil chiroq yoqilmaydi.
          <span
            className={cn(
              'flex items-center gap-2 rounded-pill border px-3.5 py-1.5 text-[12.5px] font-medium',
              query.isError
                ? 'border-danger/30 bg-danger-soft text-danger'
                : 'border-success/25 bg-success-soft text-success-text',
            )}
          >
            <span
              className={cn(
                'h-2 w-2 rounded-pill',
                query.isError ? 'bg-danger' : 'bg-success',
              )}
            />
            {query.isError ? t('sa.dashboard.systemsDown') : t('sa.dashboard.systemsOk')}
          </span>
        }
      />

      {query.isLoading || !d ? (
        <div className="flex flex-1 items-center justify-center text-primary">
          <Spinner size={26} />
        </div>
      ) : (
        <div className="flex flex-col gap-5 p-8">
          <div className="grid grid-cols-2 lg:grid-cols-4 gap-4">
            <StatCard
              label={t('sa.dashboard.kpi.activeStores')}
              value={d.kpis.activeStores}
              hint={
                d.kpis.newStoresThisMonth > 0
                  ? t('sa.dashboard.kpi.newThisMonth', { count: d.kpis.newStoresThisMonth })
                  : undefined
              }
              icon={<Store size={16} />}
            />
            <StatCard
              label={t('sa.dashboard.kpi.newRequests')}
              value={d.kpis.newRequests}
              hint={t('sa.dashboard.kpi.fromWelcome')}
              icon={<ShieldCheck size={16} />}
              tone={d.kpis.newRequests > 0 ? 'primary' : 'default'}
            />
            <StatCard
              label={t('sa.dashboard.kpi.revenue')}
              // Tarif modeli hali yo'q — «—», nol EMAS: nol «daromad yo'q»
              // degan yolg'on ma'lumot bo'lardi (S3 da jonlanadi).
              value={d.kpis.monthlyRevenueUzs === null ? '—' : d.kpis.monthlyRevenueUzs}
              hint={
                d.kpis.monthlyRevenueUzs === null
                  ? t('sa.dashboard.kpi.revenueSoon')
                  : t('sa.dashboard.kpi.perMonth')
              }
              icon={<CreditCard size={16} />}
            />
            <StatCard
              label={t('sa.dashboard.kpi.overdue')}
              value={d.kpis.overdueStores}
              hint={d.kpis.overdueStores > 0 ? t('sa.dashboard.kpi.overdueHint') : undefined}
              icon={<AlertTriangle size={16} />}
              tone={d.kpis.overdueStores > 0 ? 'danger' : 'default'}
            />
          </div>

          {blockError && (
            <div className="rounded-card bg-danger-soft px-4 py-2.5 text-[13px] text-danger">
              {blockError}
            </div>
          )}

          <div className="grid grid-cols-2 gap-5">
            {/* Заявки на подключение */}
            <Card className="overflow-hidden">
              <div className="flex items-center justify-between border-b border-hairline px-6 py-4">
                <h2 className="text-[15px] font-semibold">{t('sa.dashboard.requestsTitle')}</h2>
                <Link
                  to={`/_sa/${segment}/requests`}
                  className="text-[12.5px] text-primary hover:text-primary-hover"
                >
                  {t('sa.dashboard.viewAll')}
                </Link>
              </div>
              {acted.map((a) => (
                <div
                  key={a.id}
                  className="flex items-center gap-3 border-b border-hairline px-6 py-3 last:border-b-0"
                >
                  <div className="min-w-0 flex-1">
                    <div className="truncate text-[14px] font-semibold">{a.fullName}</div>
                    <div className="truncate text-[12px] text-muted-2">{a.phone}</div>
                  </div>
                  <span
                    className={cn(
                      'text-[12.5px] font-medium',
                      a.ok ? 'text-success-text' : 'text-muted',
                    )}
                  >
                    {t(a.ok ? 'sa.dashboard.acceptedHint' : 'sa.dashboard.rejectedHint')}
                  </span>
                </div>
              ))}
              {d.newRequests.length === 0 && acted.length === 0 ? (
                <div className="py-10 text-center text-[13px] text-muted">
                  {t('sa.dashboard.noRequests')}
                </div>
              ) : (
                d.newRequests.map((r) => (
                  <div
                    key={r.id}
                    className="flex items-center gap-3 border-b border-hairline px-6 py-3 last:border-b-0"
                  >
                    <div className="min-w-0 flex-1">
                      <div className="truncate text-[14px] font-semibold">{r.fullName}</div>
                      <div className="truncate text-[12px] text-muted-2">
                        {r.phone} · {formatRelative(r.createdAt, i18n.language)}
                      </div>
                    </div>
                    <Button
                      onClick={() => act.mutate({ id: r.id, action: 'accept', row: r })}
                      disabled={act.isPending}
                    >
                      {t('sa.requests.accept')}
                    </Button>
                    <Button
                      variant="danger"
                      onClick={() => act.mutate({ id: r.id, action: 'reject', row: r })}
                      disabled={act.isPending}
                    >
                      {t('sa.requests.reject')}
                    </Button>
                  </div>
                ))
              )}
            </Card>

            <div className="flex flex-col gap-5">
              {/* Требует внимания */}
              <Card className="overflow-hidden">
                <div className="border-b border-hairline px-6 py-4">
                  <h2 className="text-[15px] font-semibold">{t('sa.dashboard.attentionTitle')}</h2>
                </div>
                <div className="flex flex-col gap-2.5 p-4">
                  {d.overdue.length === 0 && d.expiringSoon.length === 0 && (
                    <div className="py-6 text-center text-[13px] text-muted">
                      {t('sa.dashboard.allGood')}
                    </div>
                  )}
                  {d.overdue.map((s) => (
                    <div
                      key={s.marketId}
                      className="flex items-start gap-2.5 rounded-card bg-danger-soft px-4 py-3"
                    >
                      <AlertTriangle size={15} className="mt-0.5 flex-none text-danger" />
                      <div className="min-w-0">
                        <div className="text-[13px] font-semibold text-danger">
                          {t('sa.dashboard.overdueLine', {
                            name: s.name,
                            days: Math.abs(daysFrom(s.expiresAt) ?? 0),
                          })}
                        </div>
                        <div className="text-[12px] text-muted">{t('sa.dashboard.overdueHelp')}</div>
                      </div>
                    </div>
                  ))}
                  {d.expiringSoon.length > 0 && (
                    <div className="flex items-start gap-2.5 rounded-card bg-warn-soft px-4 py-3">
                      <Clock size={15} className="mt-0.5 flex-none text-warn-strong" />
                      <div className="min-w-0">
                        <div className="text-[13px] font-semibold text-warn-text">
                          {t('sa.dashboard.expiringLine', { count: d.expiringSoon.length })}
                        </div>
                        <div className="truncate text-[12px] text-muted">
                          {d.expiringSoon.map((s) => s.name).join(', ')}
                        </div>
                      </div>
                    </div>
                  )}
                </div>
              </Card>

              {/* Магазины */}
              <Card className="overflow-hidden">
                <div className="flex items-center justify-between border-b border-hairline px-6 py-4">
                  <h2 className="text-[15px] font-semibold">{t('sa.dashboard.storesTitle')}</h2>
                  <span className="text-[11.5px] text-muted-2">
                    {t('sa.dashboard.byLastActivity')}
                  </span>
                </div>
                {d.stores.map((s) => (
                  <div
                    key={s.marketId}
                    className={cn(
                      'flex items-center gap-3 border-b border-hairline px-6 py-2.5 last:border-b-0',
                      s.status === 'Overdue' && 'bg-danger-soft/40',
                    )}
                  >
                    <div className="min-w-0 flex-1">
                      <div className="truncate text-[13.5px] font-semibold">{s.name}</div>
                      <div className="truncate text-[11.5px] text-muted-2">
                        {s.expiresAt
                          ? t('sa.store.paidUntil', {
                              date: formatShortDate(s.expiresAt, i18n.language),
                            })
                          : t('sa.store.noExpiry')}{' '}
                        · {t('sa.store.users', { count: s.users })}
                      </div>
                    </div>
                    <StatusBadge status={s.status} />
                    <Button
                      variant={s.isBlocked ? 'secondary' : 'danger'}
                      onClick={() => toggleBlock.mutate(s)}
                      disabled={toggleBlock.isPending}
                    >
                      {s.isBlocked ? t('sa.store.unblock') : t('sa.store.block')}
                    </Button>
                  </div>
                ))}
              </Card>
            </div>
          </div>
        </div>
      )}
    </>
  );
}
