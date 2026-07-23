import { apiClient } from '@/shared/api/client';
import type { PagedResult } from '@/shared/api/paged';

/** A customer (CustomerDto) — shared by the seller Clients page and POS. */
export interface Customer {
  id: string;
  phone: string;
  fullName: string | null;
  comment: string | null;
  totalDebt: number;
  customerType: string; // "Individual" | "Legal"
  isRegular: boolean;
  debtLimit: number | null;
}

export interface CreateCustomerBody {
  phone: string;
  fullName?: string | null;
  customerType?: string | null;
  isRegular?: boolean;
}

export interface CustomerQuery {
  page?: number;
  size?: number;
  search?: string;
}

export const customersApi = {
  listPaged: async (q: CustomerQuery): Promise<PagedResult<Customer>> => {
    const { data } = await apiClient.get<PagedResult<Customer>>('/Customers/GetCustomersPaged', {
      params: { page: q.page ?? 1, size: q.size ?? 50, search: q.search || undefined },
    });
    return data;
  },

  create: async (body: CreateCustomerBody): Promise<Customer> => {
    const { data } = await apiClient.post<Customer>('/Customers/CreateCustomer', body);
    return data;
  },
};
