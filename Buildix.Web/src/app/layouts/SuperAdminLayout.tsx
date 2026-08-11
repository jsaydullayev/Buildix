import { Suspense, useState } from 'react';
import { NavLink, Outlet, useParams } from 'react-router-dom';
import { useQuery } from '@tanstack/react-query';
import { useTranslation } from 'react-i18next';
import {
  LayoutGrid,
  ShieldCheck,
  Store,
  CreditCard,
  Users,
  Settings,
  LogOut,
  Menu,
} from 'lucide-react';
import { cn } from '@/shared/lib/cn';
import { BrandLogo, Spinner } from '@/shared/ui';
import { useAuth, useLogout } from '@/shared/auth/useAuth';
import { superAdminApi } from '@/features/superadmin/api';

const NAV = [
  { path: 'dashboard', labelKey: 'sa.nav.dashboard', icon: LayoutGrid },
  { path: 'requests', labelKey: 'sa.nav.requests', icon: ShieldCheck },
  { path: 'stores', labelKey: 'sa.nav.stores', icon: Store },
  { path: 'billing', labelKey: 'sa.nav.billing', icon: CreditCard },
  { path: 'users', labelKey: 'sa.nav.users', icon: Users },
  { path: 'settings', labelKey: 'sa.nav.settings', icon: Settings },
] as const;

/**
 * Platforma konsolining qobig'i (docs/Web design superadmin).
 *
 * <p>Ildizdagi <code>data-theme="super"</code> — butun akcentni binafshaga
 * o'tkazadigan yagona nuqta (index.css). Shu tufayli ichkaridagi Button, Badge,
 * Card lar admin panelidagi bilan AYNAN bir xil komponent bo'lib qolaveradi.</p>
 *
 * <p>Admin qobig'idan farqi: bu yerda <code>RequireSubscription</code> YO'Q —
 * SuperAdmin hech qaysi marketga tegishli emas va obuna eshigidan o'tmaydi
 * (barcha do'konlar yopilib qolsa ham konsol ochilishi shart).</p>
 */
export function SuperAdminLayout() {
  const { segment = '' } = useParams();
  const { t } = useTranslation();
  const { session } = useAuth();
  const logout = useLogout();
  const base = `/_sa/${segment}`;
  const [navOpen, setNavOpen] = useState(false);

  // «Заявки» yonidagi son — yangi arizalar. Operator konsolning istalgan
  // ekranida turib, ish paydo bo'lganini ko'rib turadi (dizayndagi badge).
  const pending = useQuery({
    queryKey: ['sa-pending-count', segment],
    queryFn: () => superAdminApi.requests(segment, 'Pending'),
    refetchInterval: 60_000,
    select: (rows) => rows.length,
  });

  const linkClass = ({ isActive }: { isActive: boolean }) =>
    cn(
      'flex items-center gap-[11px] rounded-lg px-3 py-2.5 text-[13.5px] transition-colors',
      isActive
        ? 'bg-white/[0.14] font-semibold text-white'
        : 'font-medium text-white/60 hover:bg-white/[0.07] hover:text-white',
    );

  return (
    <div data-theme="super" className="flex min-h-screen bg-bg text-text">
      {/* Fon — panel ochiq bo'lganda (kichik ekran). */}
      {navOpen && (
        <div
          className="fixed inset-0 z-40 bg-black/50 lg:hidden"
          onClick={() => setNavOpen(false)}
          aria-hidden="true"
        />
      )}
      <aside
        className={cn(
          'flex w-sidebar flex-none flex-col overflow-y-auto bg-sidebar px-3.5 pb-[18px] pt-[22px] text-white',
          'fixed inset-y-0 left-0 z-50 transition-transform duration-200 lg:static lg:z-auto lg:translate-x-0',
          navOpen ? 'translate-x-0' : '-translate-x-full',
        )}
      >
        <div className="mb-[26px] flex items-center gap-2 px-2.5">
          <BrandLogo size="sm" onDark />
          <span className="ml-auto rounded-pill border border-white/25 bg-white/10 px-2.5 py-[3px] text-[10px] font-bold tracking-[0.5px] text-white/90">
            SUPER
          </span>
        </div>

        <nav className="flex flex-col gap-0.5">
          {NAV.map((item) => (
            <NavLink key={item.path} to={`${base}/${item.path}`} className={linkClass} onClick={() => setNavOpen(false)}>
              <item.icon size={17} />
              {t(item.labelKey as never)}
              {item.path === 'requests' && (pending.data ?? 0) > 0 && (
                <span className="ml-auto flex h-5 min-w-5 items-center justify-center rounded-pill bg-primary px-1.5 text-[11px] font-semibold text-white">
                  {pending.data}
                </span>
              )}
            </NavLink>
          ))}
        </nav>

        <div className="mt-auto flex items-center gap-2.5 border-t border-white/[0.12] p-3">
          <span className="flex h-[34px] w-[34px] flex-none items-center justify-center rounded-pill bg-primary text-[13px] font-semibold uppercase">
            SA
          </span>
          <div className="min-w-0 flex-1">
            <div className="truncate text-[13px] font-semibold">
              {session?.fullName ?? 'Superadmin'}
            </div>
            <div className="truncate text-[11px] text-white/50">Buildix HQ</div>
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
      </aside>

      <main className="flex min-w-0 flex-1 flex-col">
        {/* Mobil sarlavha — yon menyu yashiringanda. */}
        <div className="flex h-[52px] flex-none items-center gap-3 border-b border-hairline bg-sidebar px-3 text-white lg:hidden">
          <button
            type="button"
            onClick={() => setNavOpen(true)}
            aria-label={t('nav.menu')}
            className="flex h-9 w-9 flex-none items-center justify-center rounded-lg text-white/80 transition-colors hover:bg-white/[0.1] hover:text-white"
          >
            <Menu size={20} />
          </button>
          <BrandLogo size="sm" onDark />
        </div>

        <Suspense
          fallback={
            <div className="flex flex-1 items-center justify-center text-primary">
              <Spinner size={26} />
            </div>
          }
        >
          <Outlet />
        </Suspense>
      </main>
    </div>
  );
}
