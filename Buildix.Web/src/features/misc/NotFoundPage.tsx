import { Link } from 'react-router-dom';
import { useTranslation } from 'react-i18next';
import { BrandLogo, Button } from '@/shared/ui';

export default function NotFoundPage() {
  const { t } = useTranslation();
  return (
    <div className="flex min-h-screen flex-col items-center justify-center gap-6 bg-bg px-6 text-center">
      <BrandLogo />
      <div className="font-brand text-[64px] font-bold text-primary">404</div>
      <p className="text-[15px] text-muted">{t('common.notFound')}</p>
      <Link to="/">
        <Button variant="secondary">{t('common.back')}</Button>
      </Link>
    </div>
  );
}
