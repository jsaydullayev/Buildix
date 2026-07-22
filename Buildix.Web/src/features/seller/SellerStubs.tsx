import { useTranslation } from 'react-i18next';
import { PagePlaceholder } from '@/features/_shared/PagePlaceholder';

/**
 * Bosqich 0 scaffolding — keeps the seller shell + nav coherent before each
 * real screen is built. Every stub is replaced by its own feature page in a
 * later phase (Products/Debts/Sales/Shifts/Clients in B1, POS in B2,
 * Supplies/Notifications in B4).
 */
function Stub({ titleKey }: { titleKey: string }) {
  const { t } = useTranslation();
  return <PagePlaceholder title={t(titleKey as never)} />;
}

export function SellerPosPage() {
  return <Stub titleKey="seller.nav.pos" />;
}
export function SellerSalesPage() {
  return <Stub titleKey="seller.nav.sales" />;
}
export function SellerShiftsPage() {
  return <Stub titleKey="seller.nav.shifts" />;
}
export function SellerProductsPage() {
  return <Stub titleKey="seller.nav.products" />;
}
export function SellerClientsPage() {
  return <Stub titleKey="seller.nav.clients" />;
}
export function SellerDebtsPage() {
  return <Stub titleKey="seller.nav.debts" />;
}
export function SellerSuppliesPage() {
  return <Stub titleKey="seller.nav.supplies" />;
}
export function SellerNotificationsPage() {
  return <Stub titleKey="seller.nav.notifications" />;
}
