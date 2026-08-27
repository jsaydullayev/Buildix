import type { ComponentType } from 'react';
import {
  LayoutGrid,
  ScrollText,
  Boxes,
  Package,
  Wallet,
  Undo2,
  CreditCard,
  Truck,
  Warehouse,
  Clock,
  BarChart3,
  Users,
  Contact,
  ShieldAlert,
  Printer,
  ShoppingCart,
  Tags,
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

/** A titled group of nav items — the design's sectioned sidebar. */
export interface NavSection {
  /** i18n key for the section heading (nav.sections.*) */
  titleKey: string;
  items: NavItem[];
}

/**
 * Main sidebar, grouped into sections to match the admin design. Item order
 * within each section follows the maket. Three items are new screens still
 * being built out (cash, products, returns) — their routes exist as
 * placeholders (A1) and fill in over later phases.
 */
export const NAV_SECTIONS: NavSection[] = [
  {
    titleKey: 'nav.sections.operations',
    items: [
      { path: 'dashboard', labelKey: 'nav.dashboard', icon: LayoutGrid, permission: PERMISSIONS.dashboard.access },
      { path: 'sales', labelKey: 'nav.sales', icon: ScrollText, permission: PERMISSIONS.sales.access },
      { path: 'cash', labelKey: 'nav.cash', icon: Wallet, permission: PERMISSIONS.cashregister.access },
      { path: 'warehouse', labelKey: 'nav.warehouse', icon: Warehouse, permission: PERMISSIONS.products.access },
      { path: 'products', labelKey: 'nav.products', icon: Package, permission: PERMISSIONS.products.access },
      { path: 'categories', labelKey: 'nav.categories', icon: Tags, permission: PERMISSIONS.categories.access },
      { path: 'returns', labelKey: 'nav.returns', icon: Undo2, permission: PERMISSIONS.sales.access },
    ],
  },
  {
    titleKey: 'nav.sections.clients',
    items: [
      { path: 'customers', labelKey: 'nav.customers', icon: Contact, permission: PERMISSIONS.customers.access },
      { path: 'debts', labelKey: 'nav.debts', icon: CreditCard, permission: PERMISSIONS.debts.access },
    ],
  },
  {
    titleKey: 'nav.sections.supply',
    items: [
      { path: 'purchases', labelKey: 'nav.purchases', icon: Truck, permission: PERMISSIONS.zakup.access },
      { path: 'suppliers', labelKey: 'nav.suppliers', icon: Boxes, permission: PERMISSIONS.suppliers.access },
    ],
  },
  {
    titleKey: 'nav.sections.management',
    items: [
      { path: 'reports', labelKey: 'nav.reports', icon: BarChart3, permission: PERMISSIONS.reports.access },
      { path: 'shifts', labelKey: 'nav.shifts', icon: Clock, permission: PERMISSIONS.cashregister.access },
      { path: 'employees', labelKey: 'nav.employees', icon: Users, permission: PERMISSIONS.users.access },
      { path: 'audit', labelKey: 'nav.audit', icon: ShieldAlert, permission: PERMISSIONS.data.auditLog },
      { path: 'devices', labelKey: 'nav.devices', icon: Printer, permission: PERMISSIONS.products.access },
    ],
  },
];

/**
 * Flat list of every nav item, in sidebar order. Kept for consumers that don't
 * care about grouping — notably useFirstAccessiblePath, which lands the user on
 * their first permitted screen.
 */
export const NAV_ITEMS: NavItem[] = NAV_SECTIONS.flatMap((s) => s.items);

/**
 * Seller (cashier) top-nav — the 6 primary tabs from the seller design, in order:
 * Касса · Мои продажи · Товары · Клиенты · Долги · Поставки. Shifts, Account and
 * Notifications are reached from the top-bar (shift pill / user chip / bell),
 * not this row. Routes live under /:subdomain/seller/*.
 */
export const SELLER_NAV_ITEMS: NavItem[] = [
  { path: 'pos', labelKey: 'seller.nav.pos', icon: ShoppingCart, permission: PERMISSIONS.sales.create },
  { path: 'sales', labelKey: 'seller.nav.sales', icon: ScrollText, permission: PERMISSIONS.sales.access },
  { path: 'products', labelKey: 'seller.nav.products', icon: Boxes, permission: PERMISSIONS.products.access },
  { path: 'clients', labelKey: 'seller.nav.clients', icon: Users, permission: PERMISSIONS.customers.access },
  { path: 'debts', labelKey: 'seller.nav.debts', icon: CreditCard, permission: PERMISSIONS.debts.access },
  { path: 'supplies', labelKey: 'seller.nav.supplies', icon: Truck, permission: PERMISSIONS.zakup.access },
];
