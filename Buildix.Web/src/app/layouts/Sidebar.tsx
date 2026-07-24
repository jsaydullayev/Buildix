import { NavLink, useParams } from 'react-router-dom';
import { useQuery } from '@tanstack/react-query';
import { useTranslation } from 'react-i18next';
import { ChevronsUpDown, Settings, LogOut, Bell } from 'lucide-react';
import { cn } from '@/shared/lib/cn';
import { BrandLogo } from '@/shared/ui';
import { NAV_SECTIONS } from '@/shared/config/navigation';
import { ROLES, PERMISSIONS } from '@/shared/config/permissions';
import { notificationsApi } from '@/features/notifications/api';
import { useAuth, useLogout } from '@/shared/auth/useAuth';

function initials(fullName: string): string {
  const parts = fullName.trim().split(/\s+/);
  return (parts[0]?.[0] ?? '') + (parts[1]?.[0] ?? '');
}

export function Sidebar() {
  const { subdomain } = useParams();
  const { t } = useTranslation();
  const { session, hasPermission, hasRole } = useAuth();
  const logout = useLogout();

  const base = `/${subdomain}`;
  // Keep only sections that still have a visible item after permission filtering,
  // so an empty section heading never floats above nothing.
  const sections = NAV_SECTIONS.map((s) => ({
    titleKey: s.titleKey,
    items: s.items.filter((i) => !i.permission || hasPermission(i.permission)),
  })).filter((s) => s.items.length > 0);

  const canNotifications = hasPermission(PERMISSIONS.notifications.access);
  // Badge = unread notifications from the server feed. Polls periodically so a
  // freshly reconciled stock/debt alert or a pushed shift/supply event shows up
  // without a manual refresh.
  const unreadQuery = useQuery({
    queryKey: ['notifications-unread'],
    queryFn: () => notificationsApi.unreadCount(),
    enabled: canNotifications,
    refetchInterval: 60_000,
  });
  const unreadCount = unreadQuery.data ?? 0;

  const linkClass = ({ isActive }: { isActive: boolean }) =>
    cn(
      'flex items-center gap-[11px] rounded-lg px-3 py-2.5 text-[13.5px] transition-colors',
      isActive
        ? 'bg-white/[0.12] font-semibold text-white'
        : 'font-medium text-white/60 hover:bg-white/[0.07] hover:text-white',
    );

  return (
    <aside className="flex w-sidebar flex-none flex-col bg-sidebar px-3.5 pb-[18px] pt-[22px] text-white">
      <div className="mb-[26px] flex items-center gap-2 px-2.5">
        <BrandLogo size="sm" onDark />
        {/* ADMIN badge — marks the owner/admin panel apart from the cashier shell. */}
        <span className="ml-auto rounded-pill border border-[#f5a623]/45 bg-[#f5a623]/20 px-2.5 py-[3px] text-[10px] font-bold tracking-[0.5px] text-[#fcd34d]">
          ADMIN
        </span>
      </div>

      {/* Market switcher */}
      <button
        type="button"
        className="mb-[22px] flex items-center justify-between rounded-[9px] border border-white/[0.12] bg-white/[0.07] px-3 py-2.5 text-left transition-colors hover:bg-white/[0.11]"
      >
        <span className="min-w-0">
          <span className="block truncate text-[13px] font-semibold">
            {session?.subdomain ?? '—'}
          </span>
          <span className="block truncate text-[11px] text-white/50">{session?.role}</span>
        </span>
        <ChevronsUpDown size={14} className="text-white/55" />
      </button>

      {/* Main nav — grouped into titled sections (design). */}
      <nav className="flex flex-col gap-[18px] overflow-y-auto">
        {sections.map((section) => (
          <div key={section.titleKey} className="flex flex-col gap-0.5">
            <div className="mb-1 px-3 text-[10.5px] font-semibold uppercase tracking-[0.6px] text-white/35">
              {t(section.titleKey as never)}
            </div>
            {section.items.map((item) => (
              <NavLink key={item.path} to={`${base}/${item.path}`} className={linkClass}>
                <item.icon size={17} />
                {t(item.labelKey as never)}
              </NavLink>
            ))}
          </div>
        ))}
      </nav>

      {/* Footer: settings (owner) + user */}
      <div className="mt-auto flex flex-col gap-0.5">
        {hasRole(ROLES.Owner, ROLES.SuperAdmin) && (
          <NavLink to={`${base}/settings`} className={linkClass}>
            <Settings size={17} />
            {t('nav.settings')}
          </NavLink>
        )}
        {canNotifications && (
          <NavLink to={`${base}/notifications`} className={linkClass}>
            <span className="relative flex">
              <Bell size={17} />
              {unreadCount > 0 && (
                <span className="absolute -right-1.5 -top-1.5 flex h-4 min-w-4 items-center justify-center rounded-pill bg-danger px-1 text-[10px] font-semibold text-white">
                  {unreadCount > 99 ? '99+' : unreadCount}
                </span>
              )}
            </span>
            {t('nav.notifications')}
          </NavLink>
        )}
        <div className="mt-2 flex items-center gap-2.5 border-t border-white/[0.12] p-3">
          <NavLink
            to={`${base}/account`}
            className="flex h-[34px] w-[34px] flex-none items-center justify-center rounded-pill bg-primary text-[13px] font-semibold uppercase"
          >
            {session ? initials(session.fullName) : '—'}
          </NavLink>
          <div className="min-w-0 flex-1">
            <div className="truncate text-[13px] font-semibold">{session?.fullName}</div>
            <div className="truncate text-[11px] text-white/50">{session?.role}</div>
          </div>
          <button
            type="button"
            onClick={() => void logout()}
            className="flex text-white/55 transition-colors hover:text-white"
            aria-label={t('nav.logout')}
          >
            <LogOut size={16} />
          </button>
        </div>
      </div>
    </aside>
  );
}
