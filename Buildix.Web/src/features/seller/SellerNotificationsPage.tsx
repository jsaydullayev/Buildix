import { useMemo } from 'react';
import { useQuery } from '@tanstack/react-query';
import { useNavigate, useParams } from 'react-router-dom';
import { useTranslation } from 'react-i18next';
import { AlertTriangle, PackageX, CalendarClock, ChevronRight, BellOff } from 'lucide-react';
import { PageHeader, Card, Badge, Spinner } from '@/shared/ui';
import { cn } from '@/shared/lib/cn';
import { formatSum, formatQty, formatShortDate } from '@/shared/lib/format';
import { unitLabel } from '@/shared/lib/units';
import { useAuth } from '@/shared/auth/useAuth';
import { PERMISSIONS } from '@/shared/config/permissions';
import { productsApi } from '@/features/warehouse/api';
import { debtsApi } from '@/features/debts/api';

type Alert = {
  id: string;
  tone: 'danger' | 'warn' | 'neutral';
  icon: typeof AlertTriangle;
  title: string;
  body: string;
  to: string;
};

/**
 * Cashier alert feed, assembled on the client from data the seller can already
 * read: low stock and debts that are due today or overdue.
 *
 * There is no Notification entity server-side yet (no per-user delivery or
 * read/unread state — see docs/SELLER-INTEGRATION-PLAN.md §3.4 N1), so this is
 * deliberately a live view of current conditions rather than a fake inbox: it
 * never claims something is "unread", and it disappears once the condition is
 * resolved.
 */
export default function SellerNotificationsPage() {
  const { t, i18n } = useTranslation();
  const { subdomain } = useParams();
  const navigate = useNavigate();
  const { hasPermission } = useAuth();
  const canProducts = hasPermission(PERMISSIONS.products.access);
  const canDebts = hasPermission(PERMISSIONS.debts.access);
  const base = `/${subdomain}/seller`;

  const lowStockQuery = useQuery({
    queryKey: ['seller-alerts-lowstock'],
    queryFn: () => productsApi.listPaged({ page: 1, size: 20, lowStockOnly: true }),
    enabled: canProducts,
  });
  const overdueQuery = useQuery({
    queryKey: ['seller-alerts-debts', 'overdue'],
    queryFn: () => debtsApi.debtors(undefined, 'overdue'),
    enabled: canDebts,
  });
  const dueTodayQuery = useQuery({
    queryKey: ['seller-alerts-debts', 'today'],
    queryFn: () => debtsApi.debtors(undefined, 'today'),
    enabled: canDebts,
  });

  const alerts = useMemo<Alert[]>(() => {
    const out: Alert[] = [];

    for (const d of overdueQuery.data ?? []) {
      out.push({
        id: `overdue-${d.customerId}`,
        tone: 'danger',
        icon: AlertTriangle,
        title: t('seller.notifications.debtOverdue'),
        body: `${d.customerName ?? d.customerPhone} · ${formatSum(d.remainingDebt)} ${t('common.currency')}${
          d.nearestDueDate ? ` · ${formatShortDate(d.nearestDueDate, i18n.language)}` : ''
        }`,
        to: `${base}/debts`,
      });
    }

    for (const d of dueTodayQuery.data ?? []) {
      out.push({
        id: `today-${d.customerId}`,
        tone: 'warn',
        icon: CalendarClock,
        title: t('seller.notifications.debtDueToday'),
        body: `${d.customerName ?? d.customerPhone} · ${formatSum(d.remainingDebt)} ${t('common.currency')}`,
        to: `${base}/debts`,
      });
    }

    for (const p of lowStockQuery.data?.items ?? []) {
      const out0 = p.quantity <= 0;
      out.push({
        id: `stock-${p.id}`,
        tone: out0 ? 'danger' : 'warn',
        icon: PackageX,
        title: out0 ? t('seller.notifications.outOfStock') : t('seller.notifications.lowStock'),
        body: `${p.name} · ${formatQty(p.quantity)} ${unitLabel(t, p.unit, p.unitName)}`,
        to: `${base}/products`,
      });
    }

    return out;
  }, [overdueQuery.data, dueTodayQuery.data, lowStockQuery.data, t, i18n.language, base]);

  const loading = lowStockQuery.isLoading || overdueQuery.isLoading || dueTodayQuery.isLoading;

  return (
    <>
      <PageHeader
        title={t('seller.nav.notifications')}
        subtitle={t('seller.notifications.subtitle', { count: alerts.length })}
      />

      <div className="mx-auto flex w-full max-w-[860px] flex-1 flex-col gap-[18px] p-8">
        {loading ? (
          <Card className="flex items-center justify-center py-20 text-primary">
            <Spinner size={24} />
          </Card>
        ) : alerts.length === 0 ? (
          <Card className="flex flex-col items-center gap-3 py-16 text-muted-2">
            <BellOff size={26} />
            <p className="text-[14px]">{t('seller.notifications.empty')}</p>
          </Card>
        ) : (
          <Card className="overflow-hidden">
            {alerts.map((a) => (
              <button
                key={a.id}
                type="button"
                onClick={() => navigate(a.to)}
                className="flex w-full items-center gap-3.5 border-b border-hairline px-5 py-3.5 text-left last:border-0 hover:bg-bg/50"
              >
                <span
                  className={cn(
                    'flex h-9 w-9 flex-none items-center justify-center rounded-lg',
                    a.tone === 'danger' ? 'bg-danger-soft text-danger' : 'bg-warn-soft text-warn-strong',
                  )}
                >
                  <a.icon size={17} />
                </span>
                <span className="min-w-0 flex-1">
                  <span className="flex items-center gap-2">
                    <span className="text-[13.5px] font-medium">{a.title}</span>
                    {a.tone === 'danger' && <Badge tone="danger">{t('debts.overdueTag')}</Badge>}
                  </span>
                  <span className="mt-0.5 block truncate text-[12.5px] text-muted-2">{a.body}</span>
                </span>
                <ChevronRight size={16} className="flex-none text-muted-2" />
              </button>
            ))}
          </Card>
        )}

        <p className="text-[12px] text-muted-2">{t('seller.notifications.liveHint')}</p>
      </div>
    </>
  );
}
