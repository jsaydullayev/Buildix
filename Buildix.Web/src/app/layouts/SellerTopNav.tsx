import { NavLink, useParams } from 'react-router-dom';
import { useTranslation } from 'react-i18next';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { Clock, LogOut } from 'lucide-react';
import { cn } from '@/shared/lib/cn';
import { SELLER_NAV_ITEMS } from '@/shared/config/navigation';
import { NotificationBell } from '@/features/notifications/NotificationBell';
import { shiftsApi } from '@/features/shifts/api';
import { useAuth, useLogout } from '@/shared/auth/useAuth';

function initials(fullName: string): string {
  const parts = fullName.trim().split(/\s+/);
  return (parts[0]?.[0] ?? '') + (parts[1]?.[0] ?? '');
}

/**
 * "6 s 32 d" — the shift pill is narrow, so hours/minutes stay abbreviated.
 * Takes the already-resolved unit labels rather than `t` itself: i18next's `t`
 * is heavily overloaded and passing it through a plain function signature makes
 * the compiler give up on the call.
 */
function duration(minutes: number, hLabel: string, mLabel: string): string {
  const h = Math.floor(minutes / 60);
  const m = minutes % 60;
  return h > 0 ? `${h}${hLabel} ${m}${mLabel}` : `${m}${mLabel}`;
}

/**
 * Horizontal navy top-bar for the seller (cashier) shell — the counterpart to
 * the admin app's left Sidebar. Left: the 6 primary tabs. Right: shift entry,
 * notifications bell, the seller chip (→ account), and logout.
 */
export function SellerTopNav() {
  const { subdomain } = useParams();
  const { t } = useTranslation();
  const { session, hasPermission } = useAuth();
  const logout = useLogout();

  const base = `/${subdomain}/seller`;
  const items = SELLER_NAV_ITEMS.filter((i) => !i.permission || hasPermission(i.permission));

  // The unread count and the feed now live inside NotificationBell — this bar
  // only places it.

  // The pill used to read a bare "Smenalar", so the cashier could not tell from
  // the register whether a shift was even open — the one fact that decides
  // whether a sale can be rung up at all. Poll: the shift can also be closed
  // from the owner's panel (force-close), and this bar must not keep claiming
  // it is open.
  const currentShift = useQuery({
    queryKey: ['shift-current'],
    queryFn: shiftsApi.current,
    refetchInterval: 60_000,
  });
  const shift = currentShift.data;
  const shiftOpen = !!shift?.isOpen;

  /**
   * Smena YOPIQ bo'lganda belgi tugmaga aylanadi va uni shu yerning o'zida
   * ochadi.
   *
   * <p><b>Nega sahifaga olib bormaydi.</b> Kassir ish boshlashda birinchi
   * qiladigan ishi — smena ochish, va bu bitta harakat bo'lishi kerak.
   * Ilgari belgi «Smenalar» sahifasiga olib borardi: kassir savdo
   * ekranidan chiqib ketar, u yerda tugmani topib bosar, keyin qo'lda
   * kassaga qaytardi. Kuniga takrorlanadigan uch qadam — birining
   * o'rniga.</p>
   *
   * <p>Smena OCHIQ bo'lsa aksincha: belgi sahifaga olib boradi, chunki
   * yopishdan oldin kassir yakunni — tushum, cheklar soni, kutilayotgan
   * naqd — ko'rishi kerak. Uni bir bosishda yopish xavfli bo'lardi.</p>
   */
  const qc = useQueryClient();
  const openShift = useMutation({
    mutationFn: shiftsApi.open,
    onSuccess: () => {
      void qc.invalidateQueries({ queryKey: ['shift-current'] });
      // Kassa ekrani «smena yopiq» deb turgan bo'lishi mumkin.
      void qc.invalidateQueries({ queryKey: ['pos-drafts'] });
    },
  });
  const shiftFor = shiftOpen
    ? duration(shift!.durationMinutes, t('seller.nav.hoursShort'), t('seller.nav.minsShort'))
    : '';

  const pillClass = ({ isActive }: { isActive: boolean }) =>
    cn(
      // flex-none + whitespace-nowrap: tasma surilganda bo'limlar siqilib
      // o'qib bo'lmas holga kelmasin.
      'flex flex-none items-center gap-2 whitespace-nowrap rounded-lg px-3.5 py-2 text-[13.5px] transition-colors',
      isActive
        ? 'bg-primary font-semibold text-white'
        : 'font-medium text-white/60 hover:bg-white/[0.08] hover:text-white',
    );

  return (
    // sticky: kassir sahifani pastga surganda ham bo'limlar tasmasi tepada
    // qoladi — savdo o'rtasida boshqa bo'limga o'tish uchun butun sahifani
    // tepaga surish shart emas.
    <header className="sticky top-0 z-30 flex h-[62px] flex-none items-center justify-between gap-2 bg-sidebar px-3 text-white sm:gap-4 sm:px-6">
      {/* Chap — asosiy bo'limlar. Telefon va planshetda yettita bo'lim bir
          qatorga sig'maydi: siqish o'rniga tasmani suriladigan qilamiz
          (min-w-0 bo'lmasa flex bola qisqarmaydi va sahifa toshadi). */}
      <nav className="flex min-w-0 flex-1 items-center gap-1 overflow-x-auto rounded-xl bg-white/[0.07] p-1 no-scrollbar">
        {items.map((item) => (
          <NavLink key={item.path} to={`${base}/${item.path}`} className={pillClass}>
            <item.icon size={16} />
            {t(item.labelKey as never)}
          </NavLink>
        ))}
      </nav>

      {/* Right — shift, notifications, user, logout */}
      <div className="flex flex-none items-center gap-1.5 sm:gap-2.5">
        {shiftOpen ? (
          <NavLink
            to={`${base}/shifts`}
            title={`${t('seller.nav.shiftOpen')} · ${shiftFor}`}
            className={({ isActive }) =>
              cn(
                'flex items-center gap-2 rounded-pill px-3 py-1.5 text-[12.5px] font-medium transition-colors',
                isActive
                  ? 'bg-white/[0.14] text-white'
                  : 'text-white/70 hover:bg-white/[0.08] hover:text-white',
              )
            }
          >
            <span className="h-2 w-2 flex-none rounded-pill bg-success" />
            <span className="font-semibold text-white nums">№{shift!.shiftNumber}</span>
            {/* Davomiylik — foydali, lekin smena raqamidan kam muhim:
                tor ekranda birinchi bo'lib yashiriladi. */}
            <span className="hidden text-white/55 nums sm:inline">{shiftFor}</span>
          </NavLink>
        ) : (
          <button
            type="button"
            onClick={() => openShift.mutate()}
            disabled={openShift.isPending}
            title={t('seller.nav.openShift')}
            className={cn(
              'flex items-center gap-2 rounded-pill px-3 py-1.5 text-[12.5px] font-medium transition-colors',
              'text-white/70 hover:bg-white/[0.08] hover:text-white disabled:opacity-60',
            )}
          >
            <span className="h-2 w-2 flex-none rounded-pill bg-white/35" />
            <Clock size={14} />
            <span className="hidden sm:inline">{t('seller.nav.openShift')}</span>
          </button>
        )}

        {/* A DROPDOWN, not a link. The bell used to navigate to the full
            notifications page, which tore the cashier out of a half-rung sale.
            The component gates itself on notifications.access, so the Seller
            role's default (no access) still shows nothing. */}
        <NotificationBell shell="seller" />

        <div className="mx-1 h-7 w-px bg-white/[0.14]" />

        <NavLink to={`${base}/account`} className="flex items-center gap-2.5" title={t('seller.nav.account')}>
          <span className="flex h-[34px] w-[34px] flex-none items-center justify-center rounded-pill bg-primary text-[13px] font-semibold uppercase">
            {session ? initials(session.fullName) : '—'}
          </span>
          <span className="hidden min-w-0 text-left sm:block">
            <span className="block truncate text-[13px] font-semibold">{session?.fullName}</span>
            <span className="block truncate text-[11px] text-white/50">{session?.role}</span>
          </span>
        </NavLink>

        <button
          type="button"
          onClick={() => void logout()}
          className="flex text-white/55 transition-colors hover:text-white"
          aria-label={t('nav.logout')}
        >
          <LogOut size={17} />
        </button>
      </div>
    </header>
  );
}
