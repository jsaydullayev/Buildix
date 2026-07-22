import { Suspense } from 'react';
import { Outlet } from 'react-router-dom';
import { SellerTopNav } from './SellerTopNav';
import { RequireSubscription } from '@/shared/auth/guards';
import { Spinner } from '@/shared/ui';

/** Authenticated cashier shell: navy top-nav + scrollable content area. The
 *  horizontal counterpart to AppLayout (which uses a left sidebar). */
export function SellerLayout() {
  return (
    <RequireSubscription>
      <div className="flex min-h-screen flex-col bg-bg text-text">
        <SellerTopNav />
        <main className="flex min-w-0 flex-1 flex-col">
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
