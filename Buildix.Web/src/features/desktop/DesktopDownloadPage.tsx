import { useQuery } from '@tanstack/react-query';
import { Link, useParams } from 'react-router-dom';
import { useTranslation } from 'react-i18next';
import { ArrowLeft } from 'lucide-react';
import { BrandLogo, Card, LanguageSwitch } from '@/shared/ui';
import { publicMarketApi } from '@/shared/api/auth';
import { DesktopDownload } from './DesktopDownload';

/**
 * Do'konning O'Z yuklab olish sahifasi — <code>/{'{'}do'kon{'}'}/desktop</code>.
 *
 * <p><b>Nega har do'konning o'z manzili.</b> Desktop birinchi ochilganda
 * qaysi do'konga tegishli ekanini so'raydi va javob aynan shu manzil
 * bo'ladi. Manzilsiz bulut loginni BARCHA do'konlar ichidan qidiradi:
 * ikki do'konda bir xil login uchrasa (o'zbek do'konlarida «admin»,
 * «jamshid» kabi loginlar tez-tez takrorlanadi), kassa begona do'konga
 * bog'lanib ketishi mumkin edi.</p>
 *
 * <p><b>Nega layoutsiz.</b> Sahifani do'konning istagan xodimi ochadi —
 * admin ham, sotuvchi ham. Ikkalasining o'z maketi bor va ulardan birini
 * tanlash ikkinchisiga notanish ekran ko'rsatardi. Shuning uchun sahifa
 * kirish sahifasi kabi mustaqil turadi.</p>
 */
export default function DesktopDownloadPage() {
  const { t } = useTranslation();
  const { subdomain } = useParams();

  // Do'kon nomi — «to'g'ri do'kondamanmi?» degan savolga javob. Kesh
  // kaliti kirish sahifasinikidek, ya'ni u yerdan kelgan foydalanuvchida
  // qayta so'rov ketmaydi.
  const market = useQuery({
    queryKey: ['public-market', subdomain],
    queryFn: () => publicMarketApi.getState(subdomain!),
    enabled: !!subdomain,
    retry: false,
  });

  return (
    <div className="flex min-h-screen flex-col bg-surface px-6 py-8 text-text sm:px-[60px] sm:py-10">
      <div className="flex items-center justify-between">
        <BrandLogo />
        <LanguageSwitch />
      </div>

      <div className="mx-auto flex w-full max-w-[560px] flex-1 flex-col justify-center py-10">
        <h1 className="text-[24px] font-semibold">{t('desktop.title')}</h1>
        <p className="mt-1 text-[13.5px] text-muted">
          {market.data?.marketName ?? subdomain} · {t('desktop.subtitle')}
        </p>

        <Card className="mt-6 p-5 sm:p-6">
          <DesktopDownload subdomain={subdomain!} />
        </Card>

        <Link
          to={`/${subdomain}`}
          className="mt-6 inline-flex items-center gap-1.5 text-[13px] text-muted hover:text-text"
        >
          <ArrowLeft size={15} />
          {t('common.back')}
        </Link>
      </div>
    </div>
  );
}
