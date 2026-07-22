import type { ComponentType } from 'react';
import {
  LayoutGrid,
  ScrollText,
  Boxes,
  CreditCard,
  Truck,
  Clock,
  BarChart3,
  Users,
} from 'lucide-react';
import { PERMISSIONS } from './permissions';

export interface NavItem {
  /** Route segment under /:subdomain */
  path: string;
  /** i18n key for the label (nav.*) */
  labelKey: string;
  icon: ComponentType<{ size?: number | string; className?: string }>;
  /** Permission required to see this item (undefined = always). */
  permission?: string;
}

/** Main sidebar navigation — order matches the design. */
export const NAV_ITEMS: NavItem[] = [
  { path: 'dashboard', labelKey: 'nav.dashboard', icon: LayoutGrid, permission: PERMISSIONS.dashboard.access },
  { path: 'sales', labelKey: 'nav.sales', icon: ScrollText, permission: PERMISSIONS.sales.access },
  { path: 'warehouse', labelKey: 'nav.warehouse', icon: Boxes, permission: PERMISSIONS.products.access },
  { path: 'debts', labelKey: 'nav.debts', icon: CreditCard, permission: PERMISSIONS.debts.access },
  { path: 'purchases', labelKey: 'nav.purchases', icon: Truck, permission: PERMISSIONS.zakup.access },
  { path: 'shifts', labelKey: 'nav.shifts', icon: Clock, permission: PERMISSIONS.cashregister.access },
  { path: 'reports', labelKey: 'nav.reports', icon: BarChart3, permission: PERMISSIONS.reports.access },
  { path: 'employees', labelKey: 'nav.employees', icon: Users, permission: PERMISSIONS.users.access },
];
