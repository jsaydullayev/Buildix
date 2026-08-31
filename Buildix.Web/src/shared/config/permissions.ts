/**
 * Fine-grained RBAC permission keys — mirror of
 * Buildix.Domain/Constants/PermissionKeys.cs.
 * The server returns the user's effective set in AuthResponse.permissions[];
 * the UI gates elements/routes against these keys.
 *
 * Keep in sync with the backend catalogue.
 */
export const PERMISSIONS = {
  dashboard: { access: 'dashboard.access' },
  notifications: { access: 'notifications.access' },
  products: {
    access: 'products.access',
    create: 'products.create',
    edit: 'products.edit',
    delete: 'products.delete',
    export: 'products.export',
    import: 'products.import',
  },
  categories: { access: 'categories.access', manage: 'categories.manage' },
  sales: {
    access: 'sales.access',
    create: 'sales.create',
    edit: 'sales.edit',
    delete: 'sales.delete',
    export: 'sales.export',
    invoice: 'sales.invoice',
    return: 'sales.return',
  },
  customers: {
    access: 'customers.access',
    manage: 'customers.manage',
    delete: 'customers.delete',
    export: 'customers.export',
  },
  zakup: { access: 'zakup.access', create: 'zakup.create', accept: 'zakup.accept', delete: 'zakup.delete' },
  suppliers: { access: 'suppliers.access', manage: 'suppliers.manage', delete: 'suppliers.delete' },
  cashregister: { access: 'cashregister.access', manage: 'cashregister.manage' },
  reports: { access: 'reports.access', export: 'reports.export' },
  users: { access: 'users.access', manage: 'users.manage', shift: 'users.shift' },
  debts: { access: 'debts.access', manage: 'debts.manage', dueDate: 'debts.dueDate' },
  data: {
    profit: 'data.profit',
    costPrice: 'data.costPrice',
    cashBalance: 'data.cashBalance',
    allSalesView: 'data.allSalesView',
    auditLog: 'data.auditLog',
  },
} as const;

export const ROLES = {
  SuperAdmin: 'SuperAdmin',
  Owner: 'Owner',
  Admin: 'Admin',
  Seller: 'Seller',
} as const;

export type Role = (typeof ROLES)[keyof typeof ROLES];
