import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { useTranslation } from 'react-i18next';
import { X, ShieldAlert } from 'lucide-react';
import { Button, Badge, Spinner } from '@/shared/ui';
import { formatRelative, formatShortDate, formatSum } from '@/shared/lib/format';
import type { ApiError } from '@/shared/api/types';
import { superAdminApi } from './api';

/**
 * Do'kon kartochkasi — o'ngdan chiqadigan panel (dizaynda qator bosilganda).
 *
 * <p>Blok tugmasi ataylab shu yerda, ro'yxatdagidan boshqacha: bu yerda
 * to'liq kontekst (egasi, muddat, oxirgi faollik) ko'rinib turadi va tugma
 * ostida oqibat yozilgan. Ro'yxatdagi tugma tez amal uchun, bu esa qaror
 * uchun.</p>
 */
export function StoreDetailDrawer({
  segment,
  marketId,
  onClose,
}: {
  segment: string;
  marketId: number | null;
  onClose: () => void;
}) {
  const { t, i18n } = useTranslation();
  const qc = useQueryClient();

  const query = useQuery({
    queryKey: ['sa-store', segment, marketId],
    queryFn: () => superAdminApi.store(segment, marketId!),
    enabled: marketId !== null,
  });

  const toggleBlock = useMutation({
    mutationFn: (blocked: boolean) =>
      blocked
        ? superAdminApi.unblockMarket(segment, marketId!)
        : superAdminApi.blockMarket(segment, marketId!, t('sa.store.blockedByOperator')),
    onSuccess: () => {
      void qc.invalidateQueries({ queryKey: ['sa-store', segment, marketId] });
      void qc.invalidateQueries({ queryKey: ['sa-stores', segment] });
      void qc.invalidateQueries({ queryKey: ['sa-dashboard', segment] });
    },
  });

  if (marketId === null) return null;
  const d = query.data;
  const err = toggleBlock.error
    ? ((toggleBlock.error as unknown as ApiError).message ?? t('common.somethingWrong'))
    : null;

  return (
    <div
      className="fixed inset-0 z-50 flex justify-end bg-text/40"
      onMouseDown={(e) => e.target === e.currentTarget && onClose()}
    >
      <aside className="flex h-full w-[460px] flex-col bg-surface shadow-xl">
        <div className="flex items-start justify-between border-b border-hairline px-6 py-4">
          <div className="min-w-0">
            <h2 className="truncate text-[17px] font-semibold">{d?.store.name ?? '…'}</h2>
            {d && (
              <p className="mt-0.5 text-[12.5px] text-muted-2">
                {[d.store.city, d.store.subdomain ? `/${d.store.subdomain}` : null]
                  .filter(Boolean)
                  .join(' · ')}
              </p>
            )}
          </div>
          <button
            type="button"
            onClick={onClose}
            className="-mr-1 flex h-8 w-8 items-center justify-center rounded-md text-muted-2 hover:bg-hairline hover:text-text"
            aria-label={t('common.close')}
          >
            <X size={18} />
          </button>
        </div>

        {!d ? (
          <div className="flex flex-1 items-center justify-center text-primary">
            <Spinner size={24} />
          </div>
        ) : (
          <div className="flex-1 overflow-y-auto px-6 py-5">
            <div className="mb-5 flex items-center gap-2">
              {d.store.status === 'Blocked' ? (
                <Badge tone="neutral">{t('sa.store.status.blocked')}</Badge>
              ) : d.store.status === 'Overdue' ? (
                <Badge tone="danger">{t('sa.store.status.overdue')}</Badge>
              ) : (
                <Badge tone="success">{t('sa.store.status.active')}</Badge>
              )}
              <span className="text-[12.5px] text-muted">
                {t('sa.store.since', { date: formatShortDate(d.store.createdAt, i18n.language) })}
              </span>
            </div>

            <Section title={t('sa.store.ownerTitle')}>
              <Row label={t('sa.store.owner')} value={d.store.ownerName} />
              <Row label={t('sa.store.phone')} value={d.store.ownerPhone ?? '—'} />
            </Section>

            <Section title={t('sa.store.subscriptionTitle')}>
              <Row label={t('sa.store.plan')} value={d.store.plan ?? t('sa.store.planSoon')} />
              <Row
                label={t('sa.store.paidUntilLabel')}
                value={
                  d.store.expiresAt
                    ? formatShortDate(d.store.expiresAt, i18n.language)
                    : t('sa.store.noExpiry')
                }
                tone={d.store.status === 'Overdue' ? 'danger' : undefined}
              />
            </Section>

            <Section title={t('sa.store.statsTitle')}>
              <div className="grid grid-cols-3 gap-3">
                <Metric label={t('sa.store.usersLabel')} value={d.stats.users} />
                <Metric label={t('sa.store.checksThisMonth')} value={d.stats.checksThisMonth} />
                <Metric
                  label={t('sa.store.lastActivity')}
                  value={
                    d.stats.lastActivityUtc
                      ? formatRelative(d.stats.lastActivityUtc, i18n.language)
                      : '—'
                  }
                  small
                />
              </div>
              {d.stats.outstandingDebt > 0 && (
                <Row
                  label={t('sa.store.outstandingDebt')}
                  value={`${formatSum(d.stats.outstandingDebt)} ${t('common.currency')}`}
                />
              )}
            </Section>

            <Section title={t('sa.store.paymentsTitle')}>
              {d.payments.length === 0 ? (
                // S3 gacha ataylab bo'sh — to'lov jurnali BE-S2 bilan keladi.
                <p className="text-[12.5px] text-muted-2">{t('sa.store.paymentsSoon')}</p>
              ) : (
                d.payments.map((p) => (
                  <div key={p.paidAtUtc} className="flex justify-between py-1 text-[13px]">
                    <span className="text-muted">
                      {formatShortDate(p.paidAtUtc, i18n.language)} · {p.method}
                    </span>
                    <span className="font-semibold">+{formatSum(p.amountUzs)}</span>
                  </div>
                ))
              )}
            </Section>

            {d.blockedReason && (
              <div className="mb-5 rounded-card bg-hairline px-4 py-3 text-[12.5px] text-muted">
                <b>{t('sa.store.blockedReason')}:</b> {d.blockedReason}
                {d.blockedAt && ` · ${formatShortDate(d.blockedAt, i18n.language)}`}
              </div>
            )}
          </div>
        )}

        {d && (
          <div className="border-t border-hairline px-6 py-4">
            {err && <p className="mb-2 text-[12.5px] text-danger">{err}</p>}
            <p className="mb-2.5 flex items-start gap-2 text-[12px] leading-relaxed text-muted">
              <ShieldAlert size={14} className="mt-0.5 flex-none text-muted-2" />
              {t('sa.store.blockWarning')}
            </p>
            <Button
              variant={d.store.isBlocked ? 'secondary' : 'danger'}
              fullWidth
              onClick={() => toggleBlock.mutate(d.store.isBlocked)}
              loading={toggleBlock.isPending}
            >
              {d.store.isBlocked ? t('sa.store.unblockFull') : t('sa.store.blockFull')}
            </Button>
          </div>
        )}
      </aside>
    </div>
  );
}

function Section({ title, children }: { title: string; children: React.ReactNode }) {
  return (
    <section className="mb-5">
      <h3 className="mb-2 text-[11px] font-semibold uppercase tracking-[0.6px] text-muted-2">
        {title}
      </h3>
      {children}
    </section>
  );
}

function Row({ label, value, tone }: { label: string; value: string; tone?: 'danger' }) {
  return (
    <div className="flex justify-between border-b border-hairline py-2 last:border-b-0">
      <span className="text-[13px] text-muted">{label}</span>
      <span className={tone === 'danger' ? 'text-[13px] font-semibold text-danger' : 'text-[13px] font-semibold'}>
        {value}
      </span>
    </div>
  );
}

function Metric({
  label,
  value,
  small,
}: {
  label: string;
  value: string | number;
  small?: boolean;
}) {
  return (
    <div className="rounded-card border border-border px-3 py-2.5">
      <div className={small ? 'text-[12.5px] font-semibold' : 'text-[18px] font-semibold'}>
        {value}
      </div>
      <div className="mt-0.5 text-[11px] text-muted-2">{label}</div>
    </div>
  );
}
