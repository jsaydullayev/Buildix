import { useEffect, useRef, useState } from 'react';
import { Link, useLocation, useNavigate, useParams } from 'react-router-dom';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { useTranslation } from 'react-i18next';
import { Bell, Boxes, CheckCheck, ChevronRight, Clock, CreditCard, Truck } from 'lucide-react';
import { cn } from '@/shared/lib/cn';
import { formatTime } from '@/shared/lib/format';
import { useAuth } from '@/shared/auth/useAuth';
import { PERMISSIONS } from '@/shared/config/permissions';
import { notificationsApi, type NotificationItem } from './api';
import { SELLER_TARGET } from '@/features/seller/notificationTargets';

/** NotificationsPage bilan bir xil ikonkalar/ranglar — panel tanish ko'rinsin. */
const CAT_ICON: Record<string, typeof Bell> = {
  Warehouse: Boxes,
  Debt: CreditCard,
  Shift: Clock,
  Supply: Truck,
};
const INFO_TONE = { icon: 'text-primary', bg: 'bg-primary-soft' };
const SEV_TONE: Record<string, { icon: string; bg: string }> = {
  Danger: { icon: 'text-danger', bg: 'bg-danger-soft' },
  Warning: { icon: 'text-warn-strong', bg: 'bg-warn-soft' },
  Success: { icon: 'text-success', bg: 'bg-success-soft' },
  Info: INFO_TONE,
};

/**
 * Yuqori paneldagi qo'ng'iroq — bosilganda KICHIK PANEL ochadi, butun ekranli
 * sahifaga olib ketmaydi: foydalanuvchi qilayotgan ishidan uzilmaydi.
 *
 * <ul>
 *   <li>Bildirishnoma ustiga bosilsa — o'qilgan deb belgilanadi va tegishli
 *       bo'limga (ombor, qarzlar, smenalar…) o'tkazadi;</li>
 *   <li>«Hammasini o'qilgan» tugmasi — panelni yopmasdan sonni nolga tushiradi;</li>
 *   <li>to'liq sahifa saqlanib qoladi — paneldagi «Hammasini ko'rish» havolasi.</li>
 * </ul>
 *
 * <p>O'zini o'zi yashiradi: SuperAdmin konsolida (do'kon yo'q) va ruxsatsiz
 * foydalanuvchida.</p>
 *
 * <p><b>Kassir qobig'i.</b> <code>shell="seller"</code> bilan u navy panelga
 * mos ko'rinishga o'tadi va havolalar <code>/seller/…</code> ostiga boradi.
 * Ilgari kassirda qo'ng'iroq butun sahifaga olib ketardi — kassir yarim
 * qolgan chekni yo'qotardi; endi u ham admin kabi kichik panel ochadi.</p>
 */
export function NotificationBell({ shell = 'admin' }: { shell?: 'admin' | 'seller' } = {}) {
  const { subdomain } = useParams();
  const { pathname } = useLocation();
  const { t } = useTranslation();
  const navigate = useNavigate();
  const qc = useQueryClient();
  const { hasPermission } = useAuth();
  const [open, setOpen] = useState(false);
  const rootRef = useRef<HTMLDivElement>(null);

  const seller = shell === 'seller';
  // The admin instance lives in PageHeader, which the seller shell also renders
  // on some pages — so the admin variant still has to stand down inside /seller.
  const inSellerShell = pathname.startsWith(`/${subdomain}/seller`);
  const visible =
    !!subdomain && (seller || !inSellerShell) && hasPermission(PERMISSIONS.notifications.access);
  const base = seller ? `/${subdomain}/seller` : `/${subdomain}`;

  // Davriy so'rov: qayta hisoblangan qoldiq/qarz ogohlantirishi yoki smena
  // hodisasi sahifani qo'lda yangilamasdan ko'rinsin.
  const unreadQuery = useQuery({
    queryKey: ['notifications-unread'],
    queryFn: () => notificationsApi.unreadCount(),
    enabled: visible,
    refetchInterval: 60_000,
  });

  // Lenta faqat panel ochiq bo'lganda tortiladi; kalit to'liq sahifaning
  // «hammasi» tabi bilan bir xil — kesh bo'lishiladi.
  const feedQuery = useQuery({
    queryKey: ['notifications', 'all'],
    queryFn: () => notificationsApi.feed(null),
    enabled: visible && open,
  });

  const invalidate = () => {
    void qc.invalidateQueries({ queryKey: ['notifications'] });
    void qc.invalidateQueries({ queryKey: ['notifications-unread'] });
  };
  const markRead = useMutation({ mutationFn: notificationsApi.markRead, onSuccess: invalidate });
  const markAll = useMutation({ mutationFn: notificationsApi.markAllRead, onSuccess: invalidate });

  // Tashqariga bosish yoki Escape — panelni yopadi.
  useEffect(() => {
    if (!open) return;
    const onDown = (e: MouseEvent) => {
      if (rootRef.current && !rootRef.current.contains(e.target as Node)) setOpen(false);
    };
    const onKey = (e: KeyboardEvent) => {
      if (e.key === 'Escape') setOpen(false);
    };
    document.addEventListener('mousedown', onDown);
    document.addEventListener('keydown', onKey);
    return () => {
      document.removeEventListener('mousedown', onDown);
      document.removeEventListener('keydown', onKey);
    };
  }, [open]);

  if (!visible) return null;

  // Panel ochiqligida lentaning o'z hisobi aniqroq — 60 soniyalik so'rov
  // oralig'ini kutmasdan nishoncha va «hammasini o'qilgan» tugmasi yangilanadi.
  const unread = (open ? feedQuery.data?.unreadCount : undefined) ?? unreadQuery.data ?? 0;
  const items = feedQuery.data?.items ?? [];

  function openItem(n: NotificationItem) {
    if (!n.isRead) markRead.mutate(n.id);
    if (!n.actionTarget) return;
    // The server speaks admin routes. In the cashier shell they are translated,
    // and anything with no counterpart simply doesn't navigate — better to stay
    // put than to land on a 404 mid-sale.
    const target = seller ? SELLER_TARGET[n.actionTarget] : n.actionTarget;
    if (!target) return;
    setOpen(false);
    navigate(`${base}/${target}`);
  }

  return (
    <div ref={rootRef} className="relative">
      <button
        type="button"
        aria-label={t('nav.notifications')}
        title={t('nav.notifications')}
        onClick={() => setOpen((v) => !v)}
        className={cn(
          'relative flex items-center justify-center transition-colors',
          seller
            ? cn(
                'h-9 w-9 rounded-lg',
                open ? 'bg-white/[0.14] text-white' : 'text-white/60 hover:bg-white/[0.08] hover:text-white',
              )
            : cn(
                'h-[42px] w-[42px] rounded-[9px] border',
                open
                  ? 'border-primary bg-primary/10 text-primary'
                  : 'border-border bg-surface text-label hover:border-primary hover:text-primary',
              ),
        )}
      >
        <Bell size={seller ? 17 : 18} />
        {unread > 0 && (
          <span
            className={cn(
              'absolute flex items-center justify-center rounded-pill bg-danger px-1 text-[10px] font-semibold text-white',
              seller ? '-right-1 -top-1 h-4 min-w-4' : '-right-1 -top-1 h-[18px] min-w-[18px]',
            )}
          >
            {unread > 99 ? '99+' : unread}
          </span>
        )}
      </button>

      {open && (
        <div
          className={cn(
            'absolute right-0 z-40 w-[380px] overflow-hidden rounded-card border border-border bg-surface shadow-pop',
            seller ? 'top-[46px]' : 'top-[50px]',
          )}
        >
          {/* Sarlavha + hammasini o'qilgan qilish */}
          <div className="flex items-center justify-between border-b border-hairline px-4 py-3">
            <span className="text-[13.5px] font-semibold">{t('nav.notifications')}</span>
            <button
              type="button"
              onClick={() => markAll.mutate()}
              disabled={unread === 0 || markAll.isPending}
              className="flex items-center gap-1.5 text-[12px] font-medium text-primary transition-colors hover:text-primary-hover disabled:cursor-default disabled:text-muted-2"
            >
              <CheckCheck size={14} />
              {t('notifications.markAll')}
            </button>
          </div>

          {/* Lenta */}
          <div className="max-h-[420px] overflow-y-auto">
            {feedQuery.isLoading ? (
              <p className="py-8 text-center text-[12.5px] text-muted-2">…</p>
            ) : items.length === 0 ? (
              <p className="py-8 text-center text-[12.5px] text-muted-2">{t('notifications.empty')}</p>
            ) : (
              items.slice(0, 20).map((n) => {
                const Icon = CAT_ICON[n.category] ?? Bell;
                const tone = SEV_TONE[n.severity] ?? INFO_TONE;
                return (
                  <button
                    key={n.id}
                    type="button"
                    onClick={() => openItem(n)}
                    className={cn(
                      'flex w-full items-start gap-3 border-b border-hairline px-4 py-2.5 text-left transition-colors last:border-0 hover:bg-bg/60',
                      !n.isRead && 'bg-primary-soft/25',
                    )}
                  >
                    <span className={cn('mt-0.5 flex h-8 w-8 flex-none items-center justify-center rounded-lg', tone.bg, tone.icon)}>
                      <Icon size={15} />
                    </span>
                    <span className="min-w-0 flex-1">
                      <span className="flex items-center gap-1.5">
                        <span className="truncate text-[13px] font-semibold">{n.title}</span>
                        {!n.isRead && <span className="h-1.5 w-1.5 flex-none rounded-pill bg-primary" />}
                      </span>
                      <span className="block truncate text-[12px] text-muted-2">{n.text}</span>
                    </span>
                    <span className="flex flex-none items-center gap-1 pt-0.5 text-[11px] text-muted-2 nums">
                      {formatTime(n.createdAt)}
                      {n.actionTarget && <ChevronRight size={13} />}
                    </span>
                  </button>
                );
              })
            )}
          </div>

          {/* To'liq sahifa — tarix va kategoriya filtrlari uchun */}
          <Link
            to={`${base}/notifications`}
            onClick={() => setOpen(false)}
            className="block border-t border-hairline px-4 py-2.5 text-center text-[12.5px] font-medium text-primary transition-colors hover:bg-bg/60"
          >
            {t('notifications.viewAll')}
          </Link>
        </div>
      )}
    </div>
  );
}
