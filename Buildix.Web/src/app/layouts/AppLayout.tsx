import { Suspense, useState } from 'react';
import { Outlet } from 'react-router-dom';
import { Menu } from 'lucide-react';
import { useTranslation } from 'react-i18next';
import { Sidebar } from './Sidebar';
import { RequireSubscription } from '@/shared/auth/guards';
import { BrandLogo, Spinner } from '@/shared/ui';

/**
 * Authenticated shell: navy sidebar + scrollable content area.
 *
 * <p>Ilgari bu yerda `min-w-[1360px]` turardi — ya'ni admin paneli 1360px dan
 * tor har qanday ekranda gorizontal surilardi (telefon, planshet, kichik
 * noutbuk). Endi yon menyu lg dan pastda suriladigan panelga aylanadi va
 * uning o'rniga ingichka mobil sarlavha chiqadi.</p>
 */
export function AppLayout() {
  const { t } = useTranslation();
  const [navOpen, setNavOpen] = useState(false);

  return (
    <RequireSubscription>
      <div className="flex min-h-screen bg-bg text-text">
        <Sidebar open={navOpen} onClose={() => setNavOpen(false)} />
        <main className="flex min-w-0 flex-1 flex-col">
          {/* Mobil sarlavha — faqat yon menyu yashiringanda. Sahifaning o'z
              sarlavhasi (PageHeader) ostida qoladi, shuning uchun ingichka. */}
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
    </RequireSubscription>
  );
}
