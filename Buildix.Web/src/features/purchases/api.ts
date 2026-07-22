import { apiClient } from '@/shared/api/client';
import type { PagedResult } from '@/shared/api/paged';

export interface ZakupReceipt {
  id: string;
  receiptNumber: number;
  supplierId: string | null;
  supplierName: string | null;
  invoiceNumber: string | null;
  totalAmount: number;
  paidAmount: number;
  outstandingAmount: number;
  paymentStatus: string; // Unpaid | Partial | Paid
  itemCount: number;
  createdAt: string;
}

export interface Supplier {
  id: string;
  name: string;
  phone: string | null;
  outstandingDebt: number;
  receiptCount: number;
}

export interface ReorderSuggestion {
  productId: string;
  name: string;
  unitName: string;
  currentQty: number;
  minThreshold: number;
  avgDailySales: number;
  daysOfCover: number | null;
  suggestedQty: number;
}

export const purchasesApi = {
  receiptsPaged: async (page = 1, size = 20): Promise<PagedResult<ZakupReceipt>> => {
    const { data } = await apiClient.get<PagedResult<ZakupReceipt>>('/Zakups/GetReceiptsPaged', {
      params: { page, size },
    });
    return data;
  },

  allReceipts: async (): Promise<ZakupReceipt[]> => {
    const { data } = await apiClient.get<ZakupReceipt[]>('/Zakups/GetAllReceipts');
    return data;
  },

  suppliers: async (): Promise<Supplier[]> => {
    const { data } = await apiClient.get<Supplier[]>('/Suppliers/GetAllSuppliers');
    return data;
  },

  reorderSuggestions: async (limit = 20): Promise<ReorderSuggestion[]> => {
    const { data } = await apiClient.get<ReorderSuggestion[]>('/Zakups/reorder-suggestions', {
      params: { limit },
    });
    return data;
  },
};
