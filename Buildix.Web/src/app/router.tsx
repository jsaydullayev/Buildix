import { lazy, Suspense, type ReactNode } from 'react';
import { createBrowserRouter } from 'react-router-dom';
import { AppLayout } from './layouts/AppLayout';
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
const ShiftsPage = lazy(() => import('@/features/shifts/ShiftsPage'));
const ReportsPage = lazy(() => import('@/features/reports/ReportsPage'));
const EmployeesPage = lazy(() => import('@/features/employees/EmployeesPage'));
const SettingsPage = lazy(() => import('@/features/settings/SettingsPage'));
const AccountPage = lazy(() => import('@/features/account/AccountPage'));
const PosPage = lazy(() => import('@/features/pos/PosPage'));

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
      { path: 'warehouse', element: perm(PERMISSIONS.products.access, <WarehousePage />) },
      { path: 'debts', element: perm(PERMISSIONS.debts.access, <DebtsPage />) },
      { path: 'purchases', element: perm(PERMISSIONS.zakup.access, <PurchasesPage />) },
      { path: 'shifts', element: perm(PERMISSIONS.cashregister.access, <ShiftsPage />) },
      { path: 'reports', element: perm(PERMISSIONS.reports.access, <ReportsPage />) },
      { path: 'employees', element: perm(PERMISSIONS.users.access, <EmployeesPage />) },
      {
        path: 'settings',
        element: <RequireRole roles={[ROLES.Owner, ROLES.SuperAdmin]}>{<SettingsPage />}</RequireRole>,
      },
      { path: 'account', element: <AccountPage /> },
    ],
  },
  { path: '*', element: publicElement(<NotFoundPage />) },
]);
