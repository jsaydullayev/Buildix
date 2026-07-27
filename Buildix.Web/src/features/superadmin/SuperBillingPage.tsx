import { useMemo, useState } from 'react';
import { useParams } from 'react-router-dom';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { useTranslation } from 'react-i18next';
import { Search, ArrowUpRight, Send, BellRing } from 'lucide-react';
import { PageHeader, Card, Badge, Button, Spinner } from '@/shared/ui';
import { cn } from '@/shared/lib/cn';
import { formatShortDate, formatSum } from '@/shared/lib/format';
import { useDebounce } from '@/shared/hooks/useDebounce';
import { superAdminApi, type SaBillingRow } from './api';
import { PaymentModal } from './PaymentModal';

type Tab = 'all' | 'soon' | 'overdue';

const MATCH: Record<Tab, SaBillingRow['status'][]> = {
  all: ['Active', 'Soon', 'Overdue', 'Blocked'],
  soon: ['Soon'],
  overdue: ['Overdue'],
};

const GRID = 'grid-cols-[minmax(0,1.6fr)_130px_150px_140px_130px_170px]';

export default function SuperBillingPage() {
  const { segment = '' } = useParams();
  const { t, i18n } = useTranslation();
  const qc = useQueryClient();

  const [tab, setTab] = useState<Tab>('all');
  const [search, setSearch] = useState('');
  const [paying, setPaying] = useState<SaBillingRow | null>(null);
  const debounced = useDebounce(search, 250);

  const plans = useQuery({ queryKey: ['sa-plans', segment], queryFn: () => superAdminApi.plans(segment) });
  const billing = useQuery({ queryKey: ['sa-billing', segment], queryFn: () => superAdminApi.billing(segment) });
  const payments = useQuery({
    queryKey: ['sa-payments', segment],
    queryFn: () => superAdminApi.recentPayments(segment, 8),
  });

  const rows = useMemo(() => billing.data ?? [], [billing.data]);

  // «Напомнить всем должникам» — Telegram orqali (SMS ishlatilmaydi).
  // Natijada nechtasiga yetib borgani KO'RSATILADI: bog'lamagan egalarni
  // operator qo'ng'iroq bilan xabardor qiladi.
  const remind = useMutation({
    mutationFn: () => superAdminApi.remindOverdue(segment),
  });

  const visible = useMemo(() => {
    const q = debounced.trim().toLowerCase();
    return rows.filter((r) => MATCH[tab].includes(r.status) && (!q || r.name.toLowerCase().includes(q)));
  }, [rows, tab, debounced]);

  // Oylik kutilayotgan daromad — Панель'dagi «Доход по подпискам» KPI bilan
  // AYNAN bir xil qoida: xizmat ko'rsatilayotgan do'konlar (Active + Soon).
  // Muddati o'tganlar va bloklanganlar KIRMAYDI — ular to'lamayapti, ularni
  // qo'shish kutilayotgan pulni oshirib ko'rsatardi (ular alohida
  // `overdueSum` da).
  const expected = useMemo(
    () =>
      rows
        .filter((r) => r.status === 'Active' || r.status === 'Soon')
        .reduce((sum, r) => sum + r.priceUzs, 0),
    [rows],
  );
  const overdueSum = useMemo(
    () => rows.filter((r) => r.status === 'Overdue').reduce((sum, r) => sum + r.priceUzs, 0),
    [rows],
  );

  const statusBadge = (s: SaBillingRow['status']) =>
    s === 'Blocked' ? (
      <Badge tone="neutral">{t('sa.store.status.blocked')}</Badge>
    ) : s === 'Overdue' ? (
      <Badge tone="danger">{t('sa.billing.status.overdue')}</Badge>
    ) : s === 'Soon' ? (
      <Badge tone="warn">{t('sa.billing.status.soon')}</Badge>
    ) : (
      <Badge tone="success">{t('sa.billing.status.paid')}</Badge>
    );

  return (
    <>
      <PageHeader
        title={t('sa.billing.title')}
        subtitle={t('sa.billing.summary', {
          expected: formatSum(expected),
          overdue: formatSum(overdueSum),
        })}
        actions={
          <div className="flex items-center gap-3">
            {remind.data && (
              <span className="text-[12.5px] text-muted">
                {t('sa.billing.remindResult', {
                  sent: remind.data.sent,
                  unreachable: remind.data.unreachable,
                })}
              </span>
            )}
            <Button
              variant="secondary"
              onClick={() => remind.mutate()}
              loading={remind.isPending}
              disabled={rows.every((r) => r.status !== 'Overdue')}
            >
              <BellRing size={15} /> {t('sa.billing.remindAll')}
            </Button>
          </div>
        }
      />

      <div className="flex flex-col gap-5 p-8">
        {/* Tarif kartochkalari */}
        <div className="grid grid-cols-3 gap-4">
          {(plans.data ?? []).map((p) => (
            <Card key={p.code} className="p-5">
              <div className="mb-3 flex items-center justify-between">
                <Badge tone="info">{t(`sa.billing.plans.${p.code}` as never)}</Badge>
                <span className="text-[12px] text-muted-2">
                  {t('sa.billing.planStores', { count: p.stores })}
                </span>
              </div>
              <div className="text-[24px] font-semibold">
                {formatSum(p.priceUzs)}{' '}
                <span className="text-[13px] font-normal text-muted">
                  {t('sa.dashboard.kpi.perMonth')}
                </span>
              </div>
              <div className="mt-2 text-[12.5px] leading-relaxed text-muted">
                {t('sa.billing.planLimits', {
                  points: p.maxPoints,
                  users: p.maxUsers === 0 ? t('sa.billing.unlimited') : p.maxUsers,
                })}
              </div>
            </Card>
          ))}
        </div>

        <div className="flex items-center gap-3">
          <div className="flex items-center gap-1 rounded-pill bg-hairline p-1">
            {(['all', 'soon', 'overdue'] as Tab[]).map((key) => (
              <button
                key={key}
                type="button"
                onClick={() => setTab(key)}
                className={cn(
                  'rounded-pill px-4 py-1.5 text-[13px] transition-colors',
                  tab === key
                    ? 'bg-surface font-semibold text-text shadow-card'
                    : 'font-medium text-muted hover:text-text',
                )}
              >
                {t(`sa.billing.tabs.${key}` as never)}
              </button>
            ))}
          </div>
          <div className="relative w-[280px]">
            <Search size={15} className="absolute left-3.5 top-1/2 -translate-y-1/2 text-muted-2" />
            <input
              value={search}
              onChange={(e) => setSearch(e.target.value)}
              placeholder={t('sa.billing.searchPlaceholder')}
              className="h-11 w-full rounded-input border border-border bg-surface pl-9 pr-3 text-[13.5px] outline-none focus:border-primary focus:shadow-focus-ring"
            />
          </div>
        </div>

        <Card className="overflow-hidden">
          <div
            className={cn(
              'grid items-center gap-3 border-b border-hairline px-6 py-3 text-[11px] font-semibold uppercase tracking-[0.5px] text-muted-2',
              GRID,
            )}
          >
            <span>{t('sa.stores.col.store')}</span>
            <span>{t('sa.stores.col.plan')}</span>
            <span>{t('sa.billing.col.amount')}</span>
            <span>{t('sa.stores.col.paidUntil')}</span>
            <span>{t('sa.stores.col.status')}</span>
            <span />
          </div>

          {billing.isLoading ? (
            <div className="flex justify-center py-14 text-primary">
              <Spinner size={22} />
            </div>
          ) : visible.length === 0 ? (
            <div className="py-14 text-center text-[13.5px] text-muted">{t('sa.stores.empty')}</div>
          ) : (
            visible.map((r) => (
              <div
                key={r.marketId}
                className={cn(
                  'grid items-center gap-3 border-b border-hairline px-6 py-3 last:border-b-0',
                  GRID,
                  r.status === 'Overdue' && 'bg-danger-soft/40',
                )}
              >
                <div className="min-w-0">
                  <div className="truncate text-[14px] font-semibold">{r.name}</div>
                  <div className="flex items-center gap-1.5 truncate text-[11.5px] text-muted-2">
                    {!r.ownerTelegramLinked && (
                      <span
                        className="flex items-center gap-1 text-warn-strong"
                        title={t('sa.billing.noTelegramHint')}
                      >
                        <Send size={11} /> {t('sa.billing.noTelegram')} ·
                      </span>
                    )}
                    {r.lastPaymentAtUtc
                      ? t('sa.billing.lastPayment', {
                          date: formatShortDate(r.lastPaymentAtUtc, i18n.language),
                          channel: t(`sa.billing.channels.${r.lastPaymentChannel}` as never),
                        })
                      : t('sa.billing.noPayments')}
                  </div>
                </div>

                <span>
                  <Badge tone="info">{t(`sa.billing.plans.${r.plan}` as never)}</Badge>
                </span>

                <span className="nums text-[13.5px] font-semibold">{formatSum(r.priceUzs)}</span>

                <span
                  className={cn(
                    'text-[13px]',
                    r.status === 'Overdue' && 'font-semibold text-danger',
                    r.status === 'Soon' && 'font-semibold text-warn-strong',
                  )}
                >
                  {r.expiresAt ? formatShortDate(r.expiresAt, i18n.language) : t('sa.store.noExpiry')}
                </span>

                <span>{statusBadge(r.status)}</span>

                <div className="flex justify-end">
                  <Button onClick={() => setPaying(r)}>{t('sa.billing.payAction')}</Button>
                </div>
              </div>
            ))
          )}
        </Card>

        {/* Последние платежи */}
        <Card className="overflow-hidden">
          <div className="border-b border-hairline px-6 py-4">
            <h2 className="text-[15px] font-semibold">{t('sa.billing.recentTitle')}</h2>
          </div>
          {(payments.data ?? []).length === 0 ? (
            <div className="py-10 text-center text-[13px] text-muted">
              {t('sa.billing.noPaymentsYet')}
            </div>
          ) : (
            (payments.data ?? []).map((p) => (
              <div
                key={p.id}
                className="flex items-center gap-3 border-b border-hairline px-6 py-2.5 last:border-b-0"
              >
                <span className="flex h-8 w-8 flex-none items-center justify-center rounded-pill bg-success-soft text-success-text">
                  <ArrowUpRight size={15} />
                </span>
                <div className="min-w-0 flex-1">
                  <div className="truncate text-[13.5px] font-semibold">{p.storeName}</div>
                  <div className="truncate text-[11.5px] text-muted-2">
                    {formatShortDate(p.paidAtUtc, i18n.language)} ·{' '}
                    {t(`sa.billing.channels.${p.channel}` as never)} ·{' '}
                    {t(`sa.billing.plans.${p.plan}` as never)}
                  </div>
                </div>
                <span className="nums text-[13.5px] font-semibold text-success-text">
                  +{formatSum(p.amountUzs)} {t('common.currency')}
                </span>
              </div>
            ))
          )}
        </Card>
      </div>

      <PaymentModal
        segment={segment}
        row={paying}
        onClose={() => setPaying(null)}
        onPaid={() => {
          void qc.invalidateQueries({ queryKey: ['sa-billing', segment] });
          void qc.invalidateQueries({ queryKey: ['sa-payments', segment] });
          void qc.invalidateQueries({ queryKey: ['sa-dashboard', segment] });
          void qc.invalidateQueries({ queryKey: ['sa-stores', segment] });
        }}
      />
    </>
  );
}
