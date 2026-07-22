import { NavLink, useParams } from 'react-router-dom';
import { useTranslation } from 'react-i18next';
import { Clock, Bell, LogOut } from 'lucide-react';
import { cn } from '@/shared/lib/cn';
import { SELLER_NAV_ITEMS } from '@/shared/config/navigation';
import { useAuth, useLogout } from '@/shared/auth/useAuth';

function initials(fullName: string): string {
  const parts = fullName.trim().split(/\s+/);
  return (parts[0]?.[0] ?? '') + (parts[1]?.[0] ?? '');
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

  const pillClass = ({ isActive }: { isActive: boolean }) =>
    cn(
      'flex items-center gap-2 rounded-lg px-3.5 py-2 text-[13.5px] transition-colors',
      isActive
        ? 'bg-primary font-semibold text-white'
        : 'font-medium text-white/60 hover:bg-white/[0.08] hover:text-white',
    );

  const iconLinkClass = ({ isActive }: { isActive: boolean }) =>
    cn(
      'relative flex h-9 w-9 items-center justify-center rounded-lg transition-colors',
      isActive ? 'bg-white/[0.14] text-white' : 'text-white/60 hover:bg-white/[0.08] hover:text-white',
    );

  return (
    <header className="flex h-[62px] flex-none items-center justify-between bg-sidebar px-6 text-white">
      {/* Left — primary nav tabs */}
      <nav className="flex items-center gap-1 rounded-xl bg-white/[0.07] p-1">
        {items.map((item) => (
          <NavLink key={item.path} to={`${base}/${item.path}`} className={pillClass}>
            <item.icon size={16} />
            {t(item.labelKey as never)}
          </NavLink>
        ))}
      </nav>

      {/* Right — shift, notifications, user, logout */}
      <div className="flex items-center gap-2.5">
        <NavLink
          to={`${base}/shifts`}
          className={({ isActive }) =>
            cn(
              'flex items-center gap-2 rounded-pill px-3 py-1.5 text-[12.5px] font-medium transition-colors',
              isActive
                ? 'bg-white/[0.14] text-white'
                : 'text-white/70 hover:bg-white/[0.08] hover:text-white',
            )
          }
        >
          <Clock size={14} />
          {t('seller.nav.shifts')}
        </NavLink>

        <NavLink to={`${base}/notifications`} className={iconLinkClass} aria-label={t('seller.nav.notifications')}>
          <Bell size={17} />
        </NavLink>

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
