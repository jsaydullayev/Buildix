import { useQuery } from '@tanstack/react-query';
import { useTranslation } from 'react-i18next';
import { Download } from 'lucide-react';
import { Spinner } from '@/shared/ui';
import { settingsApi } from '@/features/settings/api';

/**
 * Do'kon dasturini yuklab olish — QO'LLANMA va tugma.
 *
 * <p>Ikki joyda ko'rsatiladi: sozlamalardagi karta ichida va do'konning
 * o'z sahifasida (<code>/{'{'}do'kon{'}'}/desktop</code>). Ko'rinish bitta
 * joyda turadi — ikki nusxa bo'lsa, biri o'zgarib ikkinchisi eskirib
 * qolardi.</p>
 *
 * <p>So'rov ham shu yerda va kesh kaliti umumiy: egasi sozlamalardan
 * yuklab olish sahifasiga o'tsa, manzil qayta so'ralmaydi.</p>
 */
function useDesktopApp() {
  return useQuery({ queryKey: ['desktop-app'], queryFn: settingsApi.desktopApp });
}

/**
 * Do'kon manzili — desktop birinchi ochilganda AYNAN shu yoziladi.
 *
 * <p>Har do'konning o'z manzili bor va desktop shu manzil orqali qaysi
 * do'konga tegishli ekanini biladi. Manzilsiz bulut loginni barcha
 * do'konlar ichidan qidiradi: ikki do'konda bir xil login uchrasa, kassa
 * begona do'konga bog'lanib ketishi mumkin.</p>
 */
function marketUrl(subdomain: string): string {
  return `${window.location.origin}/${subdomain}`;
}

export function DesktopDownload({ subdomain }: { subdomain: string }) {
  const { t } = useTranslation();
  const query = useDesktopApp();

  return (
    <div className="flex flex-col gap-4">
      <ol className="flex flex-col gap-2 text-[13px] text-muted">
        <li>1. {t('desktop.step1')}</li>
        <li>2. {t('desktop.step2')}</li>
        <li>3. {t('desktop.step3')}</li>
      </ol>

      {/* Kiritiladigan manzil — ALOHIDA va ko'rinarli. Egasi uni brauzer
          satridan ko'chirishga urinsa, `/desktop` bilan birga oladi va
          qaysi qismi kerakligini bilmaydi. */}
      <div className="rounded-input border border-input-border bg-bg px-3.5 py-3">
        <div className="text-[12.5px] text-muted-2">{t('desktop.urlLabel')}</div>
        <div className="mt-1 break-all font-mono text-[14px] font-semibold">{marketUrl(subdomain)}</div>
      </div>

      <div className="border-t border-hairline pt-4">
        {query.isLoading ? (
          <Spinner size={18} />
        ) : query.data?.url ? (
          <div className="flex flex-wrap items-center gap-3">
            {/* Oddiy havola, `fetch` emas: fayl 170 MB dan katta va uni
                brauzer xotirasiga yuklashning ma'nosi yo'q — yuklab olishni
                brauzerning o'zi boshqarsin (to'xtatish, davom ettirish). */}
            <a
              href={query.data.url}
              className="inline-flex h-9 items-center gap-2 rounded-btn bg-primary px-4 text-[13.5px] font-medium text-white transition-opacity hover:opacity-90"
            >
              <Download size={16} /> {t('desktop.download')}
            </a>
            {query.data.version && (
              <span className="text-[12.5px] text-muted-2">
                {t('desktop.version', { version: query.data.version })}
              </span>
            )}
          </div>
        ) : (
          <p className="text-[12.5px] text-muted-2">{t('desktop.notReady')}</p>
        )}
      </div>
    </div>
  );
}
