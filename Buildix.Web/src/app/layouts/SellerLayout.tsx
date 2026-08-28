import { Suspense } from 'react';
import { useInactivityLogout } from '@/shared/auth/useInactivityLogout';
import { Outlet } from 'react-router-dom';
import { SellerTopNav } from './SellerTopNav';
import { RequireSubscription } from '@/shared/auth/guards';
import { Spinner } from '@/shared/ui';
import { SyncFreshnessBanner } from '@/shared/sync/SyncFreshnessBanner';

/** Authenticated cashier shell: navy top-nav + scrollable content area. The
 *  horizontal counterpart to AppLayout (which uses a left sidebar). */
export function SellerLayout() {
  // Harakatsizlikda avto-chiqish — do'kon sozlamasidan. Nol bo'lsa (sukut)
  // hech narsa qilmaydi.
  useInactivityLogout();

  return (
    <RequireSubscription>
      <div className="flex min-h-screen flex-col bg-bg text-text">
        <SellerTopNav />
        <main className="flex min-w-0 flex-1 flex-col">
          <SyncFreshnessBanner />

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
    </RequireSubscription>
  );
}
