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

export const employeesApi = {
  list: async (search?: string, role?: string | null): Promise<Employee[]> => {
    const { data } = await apiClient.get<Employee[]>('/Users/GetAllUsers', {
      params: { search: search || undefined, role: role || undefined },
    });
    return data;
  },

  staffPerformance: async (): Promise<StaffRow[]> => {
    const { data } = await apiClient.get<{ staff: StaffRow[] }>('/Reports/staff-performance', {
      params: { period: 'month' },
    });
    return data.staff ?? [];
  },

  create: async (body: CreateEmployeeBody): Promise<Employee> => {
    const { data } = await apiClient.post<Employee>('/Users/CreateUser', body);
    return data;
  },

  activate: async (id: string): Promise<void> => {
    await apiClient.post(`/Users/ActivateUser/${id}/activate`);
  },
  deactivate: async (id: string): Promise<void> => {
    await apiClient.post(`/Users/DeactivateUser/${id}/deactivate`);
  },
};
