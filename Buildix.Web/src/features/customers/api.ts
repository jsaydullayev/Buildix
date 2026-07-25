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
  /** Seller Клиенты — «Покупок за месяц» + «Последняя» (list'da hisoblanadi). */
  monthPurchaseCount: number;
  monthPurchaseSum: number;
  lastPurchaseAt: string | null;
}

export interface CreateCustomerBody {
  phone: string;
  fullName?: string | null;
  comment?: string | null;
  initialDebt?: number | null;
  customerType?: string | null;
  isRegular?: boolean;
  debtLimit?: number | null;
}

export interface UpdateCustomerBody {
  id: string;
  phone?: string | null;
  fullName?: string | null;
  customerType?: string | null;
  isRegular?: boolean;
  debtLimit?: number | null;
}

/** Blocker check before deleting a customer. */
export interface CustomerDeleteInfo {
  canDelete: boolean;
  salesCount: number;
  draftSalesCount: number;
  debtsCount: number;
  totalDebt: number;
  warningMessage: string | null;
}

export interface CustomerQuery {
  page?: number;
  size?: number;
  search?: string;
  /** «С долгом» chip — only customers with an open debt. */
  withDebt?: boolean;
  /** «Организации» chip — "Legal" | "Individual". */
  customerType?: string;
  /** «Постоянные» chip — only regular customers. */
  isRegular?: boolean;
}

export const customersApi = {
  listPaged: async (q: CustomerQuery): Promise<PagedResult<Customer>> => {
    const { data } = await apiClient.get<PagedResult<Customer>>('/Customers/GetCustomersPaged', {
      params: {
        page: q.page ?? 1,
        size: q.size ?? 50,
        search: q.search || undefined,
        withDebt: q.withDebt || undefined,
        customerType: q.customerType || undefined,
        isRegular: q.isRegular || undefined,
      },
    });
    return data;
  },

  create: async (body: CreateCustomerBody): Promise<Customer> => {
    const { data } = await apiClient.post<Customer>('/Customers/CreateCustomer', body);
    return data;
  },

  update: async (body: UpdateCustomerBody): Promise<Customer> => {
    const { data } = await apiClient.put<Customer>('/Customers/UpdateCustomer', body);
    return data;
  },

  /** Preflight before delete — reports sales / open debts that may block it. */
  deleteInfo: async (id: string): Promise<CustomerDeleteInfo> => {
    const { data } = await apiClient.get<CustomerDeleteInfo>(`/Customers/GetCustomerDeleteInfo/${id}/delete-info`);
    return data;
  },

  /** Soft-delete keeps the row (sales history stays intact) but hides it. */
  remove: async (id: string): Promise<void> => {
    await apiClient.post(`/Customers/SoftDeleteCustomer/${id}/soft-delete`);
  },

  exportExcel: async (lang: string): Promise<Blob> => {
    const { data } = await apiClient.get('/Customers/export', { params: { lang }, responseType: 'blob' });
    return data as Blob;
  },
};
