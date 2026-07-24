import { lazy, Suspense, type ReactNode } from 'react';
import { createBrowserRouter, Navigate } from 'react-router-dom';
import { AppLayout } from './layouts/AppLayout';
import { SellerLayout } from './layouts/SellerLayout';
import { LoginPage } from '@/features/auth/LoginPage';
import {
  RequireAuth,
  RequirePermission,
  RequireRole,
  RequireSubscription,
  IndexRedirect,
} from '@/shared/auth/guards';
import { FullscreenLoader } from '@/shared/ui';
import { PERMISSIONS, ROLES } from '@/shared/config/permissions';

// Feature pages are code-split — the shell + login load eagerly, modules on demand.
const LandingPage = lazy(() => import('@/features/landing/LandingPage'));
const NotFoundPage = lazy(() => import('@/features/misc/NotFoundPage'));
const DashboardPage = lazy(() => import('@/features/dashboard/DashboardPage'));
const SalesPage = lazy(() => import('@/features/sales/SalesPage'));
const WarehousePage = lazy(() => import('@/features/warehouse/WarehousePage'));
const DebtsPage = lazy(() => import('@/features/debts/DebtsPage'));
const PurchasesPage = lazy(() => import('@/features/purchases/PurchasesPage'));
const SuppliersPage = lazy(() => import('@/features/suppliers/SuppliersPage'));
const CustomersPage = lazy(() => import('@/features/customers/CustomersPage'));
const AuditPage = lazy(() => import('@/features/audit/AuditPage'));
const NotificationsPage = lazy(() => import('@/features/notifications/NotificationsPage'));
const ShiftsPage = lazy(() => import('@/features/shifts/ShiftsPage'));
const ReportsPage = lazy(() => import('@/features/reports/ReportsPage'));
const EmployeesPage = lazy(() => import('@/features/employees/EmployeesPage'));
const SettingsPage = lazy(() => import('@/features/settings/SettingsPage'));
const AccountPage = lazy(() => import('@/features/account/AccountPage'));
const PosPage = lazy(() => import('@/features/pos/PosPage'));

// Admin design integration — A1: new routes stood up as placeholders,
// filled in over later phases (cash=A3, products=A2, returns=A5).
const CashPage = lazy(() => import('@/features/cash/CashPage'));
const ProductsPage = lazy(() => import('@/features/products/ProductsPage'));
const ReturnsPage = lazy(() => import('@/features/returns/ReturnsPage'));

// Seller (cashier) shell pages — Bosqich 1.
const SellerProductsPage = lazy(() => import('@/features/seller/SellerProductsPage'));
const SellerDebtsPage = lazy(() => import('@/features/seller/SellerDebtsPage'));
const SellerSalesPage = lazy(() => import('@/features/seller/SellerSalesPage'));
const SellerShiftsPage = lazy(() => import('@/features/seller/SellerShiftsPage'));
const SellerClientsPage = lazy(() => import('@/features/seller/SellerClientsPage'));
const SellerPosPage = lazy(() => import('@/features/seller/SellerPosPage'));
const SellerSuppliesPage = lazy(() => import('@/features/seller/SellerSuppliesPage'));
const SellerNotificationsPage = lazy(() => import('@/features/seller/SellerNotificationsPage'));

const publicElement = (node: ReactNode) => <Suspense fallback={<FullscreenLoader />}>{node}</Suspense>;

const perm = (permission: string, node: ReactNode) => (
  <RequirePermission permission={permission}>{node}</RequirePermission>
);

export const router = createBrowserRouter([
  { path: '/', element: publicElement(<LandingPage />) },
  { path: '/:subdomain/login', element: <LoginPage /> },
  {
    // Full-screen POS checkout — outside AppLayout (no sidebar).
    path: '/:subdomain/pos',
    element: (
      <RequireAuth>
        <RequireSubscription>
          <RequirePermission permission={PERMISSIONS.sales.create}>
            {publicElement(<PosPage />)}
          </RequirePermission>
        </RequireSubscription>
      </RequireAuth>
    ),
  },
  {
    path: '/:subdomain',
    element: (
      <RequireAuth>
        <AppLayout />
      </RequireAuth>
    ),
    children: [
      { index: true, element: <IndexRedirect /> },
      { path: 'dashboard', element: perm(PERMISSIONS.dashboard.access, <DashboardPage />) },
      { path: 'sales', element: perm(PERMISSIONS.sales.access, <SalesPage />) },
      { path: 'cash', element: perm(PERMISSIONS.cashregister.access, <CashPage />) },
      { path: 'warehouse', element: perm(PERMISSIONS.products.access, <WarehousePage />) },
      { path: 'products', element: perm(PERMISSIONS.products.access, <ProductsPage />) },
      { path: 'returns', element: perm(PERMISSIONS.sales.access, <ReturnsPage />) },
      { path: 'debts', element: perm(PERMISSIONS.debts.access, <DebtsPage />) },
      { path: 'customers', element: perm(PERMISSIONS.customers.access, <CustomersPage />) },
      { path: 'purchases', element: perm(PERMISSIONS.zakup.access, <PurchasesPage />) },
      { path: 'suppliers', element: perm(PERMISSIONS.suppliers.access, <SuppliersPage />) },
      { path: 'shifts', element: perm(PERMISSIONS.cashregister.access, <ShiftsPage />) },
      { path: 'reports', element: perm(PERMISSIONS.reports.access, <ReportsPage />) },
      { path: 'employees', element: perm(PERMISSIONS.users.access, <EmployeesPage />) },
      { path: 'audit', element: perm(PERMISSIONS.data.auditLog, <AuditPage />) },
      { path: 'notifications', element: perm(PERMISSIONS.notifications.access, <NotificationsPage />) },
      {
        path: 'settings',
        element: <RequireRole roles={[ROLES.Owner, ROLES.SuperAdmin]}>{<SettingsPage />}</RequireRole>,
      },
      { path: 'account', element: <AccountPage /> },
    ],
  },
  {
    // Seller (cashier) shell — horizontal top-nav, outside AppLayout. Sellers
    // land here after login (useFirstAccessiblePath); Owner/Admin can preview.
    path: '/:subdomain/seller',
    element: (
      <RequireAuth>
        <SellerLayout />
      </RequireAuth>
    ),
    children: [
      { index: true, element: <Navigate to="pos" replace /> },
      { path: 'pos', element: perm(PERMISSIONS.sales.create, <SellerPosPage />) },
      { path: 'sales', element: perm(PERMISSIONS.sales.access, <SellerSalesPage />) },
      // Shift open/close is self-service (no permission gate) — matches backend.
      { path: 'shifts', element: <SellerShiftsPage /> },
      { path: 'products', element: perm(PERMISSIONS.products.access, <SellerProductsPage />) },
      { path: 'clients', element: perm(PERMISSIONS.customers.access, <SellerClientsPage />) },
      { path: 'debts', element: perm(PERMISSIONS.debts.access, <SellerDebtsPage />) },
      { path: 'supplies', element: perm(PERMISSIONS.zakup.access, <SellerSuppliesPage />) },
      { path: 'account', element: <AccountPage /> },
      { path: 'notifications', element: perm(PERMISSIONS.notifications.access, <SellerNotificationsPage />) },
    ],
  },
  { path: '*', element: publicElement(<NotFoundPage />) },
]);
