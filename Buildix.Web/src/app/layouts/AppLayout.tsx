import { Suspense, useState } from 'react';
import { Outlet } from 'react-router-dom';
import { Menu } from 'lucide-react';
import { useTranslation } from 'react-i18next';
import { Sidebar } from './Sidebar';
import { RequireSubscription } from '@/shared/auth/guards';
import { BrandLogo, Spinner } from '@/shared/ui';
import { SyncFreshnessBanner } from '@/shared/sync/SyncFreshnessBanner';

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
      {/*
        Qobiq balandligi EKRANGA bog'langan, sahifaning o'ziga emas.

        Ilgari `min-h-screen` edi: sahifa kontent qancha bo'lsa shuncha
        cho'zilar va butun oyna surilardi. Jadval sarlavhalari yuqoriga
        chiqib ketardi — omborchi o'ntadan keyingi qatorda qaysi ustun
        nimaligini eslab qolishga majbur bo'lardi.

        Endi surish MAIN ichida bo'ladi: yon menyu joyida qoladi, jadvalli
        ekranlarda esa kartaning o'z aylanish sohasi ishlaydi va ustun
        nomlari ko'rinib turadi. Kontenti uzun oddiy sahifalar (Panel,
        Hisobotlar) odatdagidek main ichida suriladi — hech narsa
        kesilmaydi.
      */}
      <div className="flex h-screen overflow-hidden bg-bg text-text">
        <Sidebar open={navOpen} onClose={() => setNavOpen(false)} />
        <main className="flex min-h-0 min-w-0 flex-1 flex-col overflow-y-auto">
          {/* Mobil sarlavha — faqat yon menyu yashiringanda. Sahifaning o'z
              sarlavhasi (PageHeader) ostida qoladi, shuning uchun ingichka.
              sticky: sahifa pastga surilganda ham tepada qoladi — aks holda
              boshqa bo'limga o'tish uchun har safar butun sahifani tepaga
              surish kerak bo'lardi. z-30: kontentdan yuqori, lekin ochilgan
              yon menyu (z-50) va uning foni (z-40) ostida. */}
          <div className="sticky top-0 z-30 flex h-[52px] flex-none items-center gap-3 border-b border-hairline bg-sidebar px-3 text-white lg:hidden">
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
