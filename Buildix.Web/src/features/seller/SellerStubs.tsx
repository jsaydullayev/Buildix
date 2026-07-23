import { useTranslation } from 'react-i18next';
import { PagePlaceholder } from '@/features/_shared/PagePlaceholder';

/**
 * Scaffolding for seller screens not yet built. Replaced by real feature pages
 * per phase: POS in B2, Supplies + Notifications in B4 (v1-lite). The B1 pages
 * (Products / Debts / Sales / Shifts / Clients) are now real and live in their
 * own files under features/seller/.
 */
function Stub({ titleKey }: { titleKey: string }) {
  const { t } = useTranslation();
  return <PagePlaceholder title={t(titleKey as never)} />;
}

export function SellerSuppliesPage() {
  return <Stub titleKey="seller.nav.supplies" />;
}
export function SellerNotificationsPage() {
  return <Stub titleKey="seller.nav.notifications" />;
}
