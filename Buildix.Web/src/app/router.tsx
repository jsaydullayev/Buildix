import { lazy, Suspense, type ReactNode } from 'react';
import { createBrowserRouter, Navigate } from 'react-router-dom';
import { AppLayout } from './layouts/AppLayout';
import { SellerLayout } from './layouts/SellerLayout';
import { SuperAdminLayout } from './layouts/SuperAdminLayout';
import { LoginPage } from '@/features/auth/LoginPage';
import {
  RequireAuth,
  RequirePermission,
  RequireRole,
  RequireSubscription,
  RequireTenant,
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
const DevicesPage = lazy(() => import('@/features/devices/DevicesPage'));
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

// SuperAdmin konsoli — S0: qobiq, marshrutlar va tema; ekranlar S1..S5 da
// to'ldiriladi (docs/SUPERADMIN-DESIGN-INTEGRATION-TZ.md).
const SuperLoginPage = lazy(() =>
  import('@/features/superadmin/SuperLoginPage').then((m) => ({ default: m.SuperLoginPage })),
);
const SuperDashboardPage = lazy(() => import('@/features/superadmin/SuperDashboardPage'));
const SuperRequestsPage = lazy(() => import('@/features/superadmin/SuperRequestsPage'));
const SuperStoresPage = lazy(() => import('@/features/superadmin/SuperStoresPage'));
const SuperBillingPage = lazy(() => import('@/features/superadmin/SuperBillingPage'));
const SuperUsersPage = lazy(() => import('@/features/superadmin/SuperUsersPage'));
const SuperSettingsPage = lazy(() => import('@/features/superadmin/SuperSettingsPage'));

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
  // Ildizdagi login — SuperAdmin shu yerdan kiradi (u hech qaysi do'konga
  // tegishli emas, ya'ni `/:subdomain/login` unga yaramaydi). Muvaffaqiyatdan
  // keyin sahifa uni konsolga yoki o'z do'koniga yo'naltiradi.
  { path: '/login', element: <LoginPage /> },
  { path: '/:subdomain/login', element: <LoginPage /> },
  // ── SuperAdmin konsoli ────────────────────────────────────────────────────
  // `:segment` — backenddagi yashirin segmentning AYNAN o'zi
  // (SuperAdmin:ConsoleSegment). U bundle ichida emas, URL'da yashaydi:
  // operator to'liq havolani o'zi biladi. Noto'g'ri segment bilan har bir API
  // chaqiruvi 404 qaytaradi, ya'ni konsol borligi oshkor bo'lmaydi.
  { path: '/_sa/:segment/login', element: publicElement(<SuperLoginPage />) },
  {
    path: '/_sa/:segment',
    element: (
      <RequireAuth>
        <RequireRole roles={[ROLES.SuperAdmin]}>
          <SuperAdminLayout />
        </RequireRole>
      </RequireAuth>
    ),
    children: [
      { index: true, element: <Navigate to="dashboard" replace /> },
      { path: 'dashboard', element: <SuperDashboardPage /> },
      { path: 'requests', element: <SuperRequestsPage /> },
      { path: 'stores', element: <SuperStoresPage /> },
      { path: 'billing', element: <SuperBillingPage /> },
      { path: 'users', element: <SuperUsersPage /> },
      { path: 'settings', element: <SuperSettingsPage /> },
    ],
  },
  {
    // Full-screen POS checkout — outside AppLayout (no sidebar).
    path: '/:subdomain/pos',
    element: (
      <RequireAuth>
        <RequireTenant>
          <RequireSubscription>
            <RequirePermission permission={PERMISSIONS.sales.create}>
              {publicElement(<PosPage />)}
            </RequirePermission>
          </RequireSubscription>
        </RequireTenant>
      </RequireAuth>
    ),
  },
  {
    path: '/:subdomain',
    element: (
      <RequireAuth>
        <RequireTenant>
          <AppLayout />
        </RequireTenant>
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
      // Qurilmalar: printer va skanerni sinash. products.access — kassir ham
      // o'z skanerini tekshira olsin.
      { path: 'devices', element: perm(PERMISSIONS.products.access, <DevicesPage />) },
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
        <RequireTenant>
          <SellerLayout />
        </RequireTenant>
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
