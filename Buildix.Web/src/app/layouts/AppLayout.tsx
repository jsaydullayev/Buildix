import { Suspense } from 'react';
import { Outlet } from 'react-router-dom';
import { Sidebar } from './Sidebar';
import { RequireSubscription } from '@/shared/auth/guards';
import { Spinner } from '@/shared/ui';

/** Authenticated shell: navy sidebar + scrollable content area. */
export function AppLayout() {
  return (
    <RequireSubscription>
      <div className="flex min-h-screen min-w-[1360px] bg-bg text-text">
        <Sidebar />
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
