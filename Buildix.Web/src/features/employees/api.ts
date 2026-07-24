import { apiClient } from '@/shared/api/client';

export interface Employee {
  id: string;
  fullName: string;
  username: string;
  profileImage: string | null;
  role: string;
  isActive: boolean;
  phone: string | null;
  lastActiveAt: string | null;
}

export interface StaffRow {
  userId: string;
  saleCount: number;
  revenue: number;
  shiftCount: number;
}

export interface CreateEmployeeBody {
  fullName: string;
  username: string;
  password: string;
  role: string;
}

export interface UpdateEmployeeBody {
  id: string;
  fullName: string;
  /** Empty/omitted = keep current password. */
  password?: string | null;
  role: string;
  isActive: boolean;
}

/** GET /Users/GetUserPermissions/{id} — RBAC state for one user. */
export interface UserPermissions {
  userId: string;
  role: string;
  /** True once the Owner saved an explicit set; false = still on role default. */
  isCustomized: boolean;
  effectivePermissions: string[];
  roleDefaults: string[];
  /** Every permission key that exists — the full catalogue to render. */
  catalog: string[];
  /** Per-user discount cap (%). null = unlimited. */
  maxDiscountPercent: number | null;
  /** Per-user debt cap per receipt. null = unlimited. */
  maxDebtPerCheck: number | null;
}

export const employeesApi = {
  list: async (search?: string, role?: string | null): Promise<Employee[]> => {
    const { data } = await apiClient.get<Employee[]>('/Users/GetAllUsers', {
      params: { search: search || undefined, role: role || undefined },
    });
    return data;
  },

  staffPerformance: async (period = 'month'): Promise<StaffRow[]> => {
    const { data } = await apiClient.get<{ staff: StaffRow[] }>('/Reports/staff-performance', {
      params: { period },
    });
    return data.staff ?? [];
  },

  create: async (body: CreateEmployeeBody): Promise<Employee> => {
    const { data } = await apiClient.post<Employee>('/Users/CreateUser', body);
    return data;
  },

  // Controller uses [Route("api/[controller]/[action]")] + [HttpPut("{id}")],
  // so the action segment stays in the path.
  update: async (body: UpdateEmployeeBody): Promise<Employee> => {
    const { data } = await apiClient.put<Employee>(`/Users/UpdateUser/${body.id}`, body);
    return data;
  },

  remove: async (id: string): Promise<void> => {
    await apiClient.delete(`/Users/DeleteUser/${id}`);
  },

  activate: async (id: string): Promise<void> => {
    await apiClient.post(`/Users/ActivateUser/${id}/activate`);
  },
  deactivate: async (id: string): Promise<void> => {
    await apiClient.post(`/Users/DeactivateUser/${id}/deactivate`);
  },

  // ── RBAC (Owner-only) ─────────────────────────────────────────────────────

  permissions: async (id: string): Promise<UserPermissions> => {
    const { data } = await apiClient.get<UserPermissions>(`/Users/GetUserPermissions/${id}`);
    return data;
  },

  /** Overwrite a user's explicit set + limits; empty list resets to the role default. */
  updatePermissions: async (
    id: string,
    permissions: string[],
    limits?: { maxDiscountPercent: number | null; maxDebtPerCheck: number | null },
  ): Promise<UserPermissions> => {
    const { data } = await apiClient.put<UserPermissions>(`/Users/UpdateUserPermissions/${id}`, {
      permissions,
      maxDiscountPercent: limits?.maxDiscountPercent ?? null,
      maxDebtPerCheck: limits?.maxDebtPerCheck ?? null,
    });
    return data;
  },
};
