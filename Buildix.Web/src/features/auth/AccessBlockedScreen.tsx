import { useTranslation } from 'react-i18next';
import { AlertTriangle, Clock } from 'lucide-react';
import { ACCESS_BLOCK, type AccessBlock } from '@/shared/api/types';
import { BrandLogo, Button } from '@/shared/ui';
import { useLogout } from '@/shared/auth/useAuth';

export function AccessBlockedScreen({ block }: { block: AccessBlock }) {
  const { t } = useTranslation();
  const logout = useLogout();
  const expired = block === ACCESS_BLOCK.expired;

  return (
    <div className="flex min-h-screen flex-col items-center justify-center bg-bg px-6 text-center">
      <BrandLogo className="mb-10" />
      <div
        className={`mb-5 flex h-16 w-16 items-center justify-center rounded-2xl ${
          expired ? 'bg-warn-soft text-warn' : 'bg-danger-soft text-danger'
        }`}
      >
        {expired ? <Clock size={28} /> : <AlertTriangle size={28} />}
      </div>
      <h1 className="mb-2 text-[22px] font-semibold text-text">
        {expired ? t('subscription.expiredTitle') : t('subscription.blockedTitle')}
      </h1>
      <p className="max-w-md text-[15px] leading-relaxed text-muted">
        {expired ? t('subscription.expiredDesc') : t('subscription.blockedDesc')}
      </p>
      <Button variant="secondary" className="mt-8" onClick={() => void logout()}>
        {t('nav.logout')}
      </Button>
    </div>
  );
}
