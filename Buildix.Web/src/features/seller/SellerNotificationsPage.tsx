import { useMemo, useState } from 'react';
import { useNavigate, useParams } from 'react-router-dom';
import { useQuery, useMutation, useQueryClient } from '@tanstack/react-query';
import { useTranslation } from 'react-i18next';
import { CheckCheck, Boxes, CreditCard, Clock, Truck, Bell } from 'lucide-react';
import type { LucideIcon } from 'lucide-react';
import { PageHeader, Button, Card, Spinner } from '@/shared/ui';
import { cn } from '@/shared/lib/cn';
import { formatTime } from '@/shared/lib/format';
import { notificationsApi, type NotificationItem } from '@/features/notifications/api';

const CAT_ICON: Record<string, LucideIcon> = { Warehouse: Boxes, Debt: CreditCard, Shift: Clock, Supply: Truck };
const SEV_TONE: Record<string, { dot: string; icon: string; bg: string }> = {
  Danger: { dot: 'bg-danger', icon: 'text-danger', bg: 'bg-danger-soft' },
  Warning: { dot: 'bg-warn-strong', icon: 'text-warn-strong', bg: 'bg-warn-soft' },
  Success: { dot: 'bg-success', icon: 'text-success', bg: 'bg-success-soft' },
  Info: { dot: 'bg-primary', icon: 'text-primary', bg: 'bg-primary-soft' },
};

// Server actionTarget (admin marshrutlari) → seller marshruti.
const SELLER_TARGET: Record<string, string> = {
  warehouse: 'products',
  products: 'products',
  debts: 'debts',
  shifts: 'shifts',
  purchases: 'supplies',
  suppliers: 'supplies',
  supply: 'supplies',
};

const TASHKENT_TZ = 'Asia/Tashkent';
const dayKey = (d: string) => new Intl.DateTimeFormat('en-CA', { timeZone: TASHKENT_TZ }).format(new Date(d));

/**
 * Kassir bildirishnomalari — server feed'idan (BE-4). Admin bilan bir manba;
 * dizayn Все/Непрочитанные filtri + kun-guruh + o'qilgan holati.
 */
export default function SellerNotificationsPage() {
  const { t, i18n } = useTranslation();
  const { subdomain } = useParams();
  const navigate = useNavigate();
  const qc = useQueryClient();
  const [onlyUnread, setOnlyUnread] = useState(false);

  const feedQuery = useQuery({
    queryKey: ['notifications', 'seller'],
    queryFn: () => notificationsApi.feed(null),
  });
  const data = feedQuery.data;

  const invalidate = () => {
    void qc.invalidateQueries({ queryKey: ['notifications'] });
    void qc.invalidateQueries({ queryKey: ['notifications-unread'] });
  };
  const markRead = useMutation({ mutationFn: (id: string) => notificationsApi.markRead(id), onSuccess: invalidate });
  const markAll = useMutation({ mutationFn: () => notificationsApi.markAllRead(), onSuccess: invalidate });

  const groups = useMemo(() => {
    const items = (data?.items ?? []).filter((n) => !onlyUnread || !n.isRead);
    const todayK = dayKey(new Date().toISOString());
    const yK = dayKey(new Date(Date.now() - 86_400_000).toISOString());
    const map = new Map<string, NotificationItem[]>();
    for (const n of items) {
      const k = dayKey(n.createdAt);
      if (!map.has(k)) map.set(k, []);
      map.get(k)!.push(n);
    }
    return [...map.entries()].map(([k, list]) => ({
      key: k,
      label:
        k === todayK
          ? t('notifications.today')
          : k === yK
            ? t('notifications.yesterday')
            : new Intl.DateTimeFormat(i18n.language, { day: 'numeric', month: 'long' }).format(new Date(list[0]!.createdAt)),
      list,
    }));
  }, [data, onlyUnread, t, i18n.language]);

  function open(n: NotificationItem) {
    if (!n.isRead) markRead.mutate(n.id);
    const target = n.actionTarget ? SELLER_TARGET[n.actionTarget] : undefined;
    if (target) navigate(`/${subdomain}/seller/${target}`);
  }

  const unread = data?.unreadCount ?? 0;

  return (
    <>
      <PageHeader
        title={t('seller.nav.notifications')}
        subtitle={t('notifications.subtitle', { count: unread })}
        actions={
          unread > 0 ? (
            <Button variant="secondary" size="sm" loading={markAll.isPending} onClick={() => markAll.mutate()}>
              <CheckCheck size={15} />
              {t('notifications.markAll')}
            </Button>
          ) : undefined
        }
      />

      <div className="mx-auto flex w-full max-w-[820px] flex-1 flex-col gap-[18px] p-8">
        {/* Все / Непрочитанные */}
        <div className="flex items-center gap-1.5">
          {[
            { k: false, label: t('common.all') },
            { k: true, label: t('seller.notifications.unread') },
          ].map((o) => (
            <button
              key={String(o.k)}
              type="button"
              onClick={() => setOnlyUnread(o.k)}
              className={cn(
                'rounded-input px-3.5 py-2 text-[13px] font-medium transition-colors',
                onlyUnread === o.k
                  ? 'bg-primary text-white'
                  : 'border border-input-border bg-surface text-muted hover:text-text',
              )}
            >
              {o.label}
            </button>
          ))}
        </div>

        {feedQuery.isLoading ? (
          <div className="flex items-center justify-center py-20 text-primary">
            <Spinner size={24} />
          </div>
        ) : groups.length === 0 ? (
          <Card className="flex flex-col items-center gap-3 py-16 text-muted-2">
            <Bell size={26} />
            <p className="text-[14px]">{t('seller.notifications.empty')}</p>
          </Card>
        ) : (
          groups.map((g) => (
            <div key={g.key}>
              <h3 className="mb-2 px-1 text-[11.5px] font-semibold uppercase tracking-[0.5px] text-muted-2">{g.label}</h3>
              <Card className="overflow-hidden">
                {g.list.map((n) => (
                  <NotificationRow key={n.id} n={n} onClick={() => open(n)} />
                ))}
              </Card>
            </div>
          ))
        )}
      </div>
    </>
  );
}

function NotificationRow({ n, onClick }: { n: NotificationItem; onClick: () => void }) {
  const tone = SEV_TONE[n.severity] ?? SEV_TONE.Info!;
  const Icon = CAT_ICON[n.category] ?? Bell;
  return (
    <button
      type="button"
      onClick={onClick}
      className={cn(
        'flex w-full items-start gap-3.5 border-b border-hairline px-5 py-3.5 text-left transition-colors last:border-0 hover:bg-bg/50',
        !n.isRead && 'bg-primary-soft/25',
      )}
    >
      <span className={cn('mt-0.5 flex h-9 w-9 flex-none items-center justify-center rounded-lg', tone.bg, tone.icon)}>
        <Icon size={17} />
      </span>
      <span className="min-w-0 flex-1">
        <span className="flex items-center gap-2">
          <span className="truncate text-[13.5px] font-medium">{n.title}</span>
          {!n.isRead && <span className={cn('h-2 w-2 flex-none rounded-full', tone.dot)} />}
        </span>
        <span className="mt-0.5 block truncate text-[12.5px] text-muted-2">{n.text}</span>
      </span>
      <span className="flex-none text-[11.5px] text-muted-2 nums">{formatTime(n.createdAt)}</span>
    </button>
  );
}
