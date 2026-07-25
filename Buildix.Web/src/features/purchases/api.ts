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
  deliveryStatus: string; // Accepted | InTransit
  itemCount: number;
  createdAt: string;
  /** «В пути» delivery info (seller Поставки cards). */
  driverPhone?: string | null;
  expectedDate?: string | null;
  /** Line items (product + qty). Seller view carries no cost. */
  items?: { productId: string; productName: string; quantity: number }[];
}

export interface Supplier {
  id: string;
  name: string;
  phone: string | null;
  address?: string | null;
  comment?: string | null;
  outstandingDebt: number;
  receiptCount: number;
  contactPerson?: string | null;
  deliveryTerm?: string | null;
}

/** One product line of a receipt (Owner view — carries cost). */
export interface ReceiptLine {
  id: string;
  productId: string;
  productName: string;
  quantity: number;
  costPrice: number;
  lineTotal: number;
}

/** Full goods-receipt with its lines — GET /Zakups/GetReceipt/{id}. */
export interface ReceiptDetail {
  id: string;
  receiptNumber: number;
  supplierId: string | null;
  supplierName: string | null;
  invoiceNumber: string | null;
  totalAmount: number;
  paidAmount: number;
  outstandingAmount: number;
  paymentStatus: string;
  deliveryStatus: string; // Accepted | InTransit
  comment: string | null;
  itemCount: number;
  createdAt: string;
  createdBy: string;
  items: ReceiptLine[];
}

/** One line being posted with a new receipt. */
export interface CreateReceiptLine {
  productId: string;
  quantity: number;
  costPrice: number;
}

export interface CreateReceiptBody {
  supplierId: string | null;
  invoiceNumber: string | null;
  paidAmount: number;
  comment: string | null;
  items: CreateReceiptLine[];
  /** true → created as «В пути» (no stock until accepted). Default immediate. */
  inTransit?: boolean;
  /** «В пути» ETA (ISO date) — arrival date shown on the supply cards. */
  expectedDate?: string | null;
  /** «Способ оплаты» — how the paid amount was tendered ("Cash" | "Transfer"). */
  paymentMethod?: string | null;
}

export interface SupplierBody {
  name: string;
  phone?: string | null;
  address?: string | null;
  comment?: string | null;
  contactPerson?: string | null;
  deliveryTerm?: string | null;
}

/** Blocker check before deleting a supplier. */
export interface SupplierDeleteInfo {
  canDelete: boolean;
  receiptsCount: number;
  outstandingDebt: number;
  warningMessage: string | null;
}

export interface ReorderSuggestion {
  productId: string;
  name: string;
  /** Server-side Uzbek abbreviation — prefer `unit` + unitLabel() for display. */
  unitName: string;
  /** UnitType number; localise from this. */
  unit: number;
  currentQty: number;
  minThreshold: number;
  avgDailySales: number;
  daysOfCover: number | null;
  suggestedQty: number;
}

export const purchasesApi = {
  receiptsPaged: async (
    page = 1,
    size = 20,
    opts?: { supplierId?: string; search?: string; deliveryStatus?: string },
  ): Promise<PagedResult<ZakupReceipt>> => {
    const { data } = await apiClient.get<PagedResult<ZakupReceipt>>('/Zakups/GetReceiptsPaged', {
      params: {
        page,
        size,
        supplierId: opts?.supplierId || undefined,
        search: opts?.search || undefined,
        deliveryStatus: opts?.deliveryStatus || undefined,
      },
    });
    return data;
  },

  /** Delete a purchase receipt (reverses stock if it was accepted). zakup.delete. */
  deleteReceipt: async (id: string): Promise<void> => {
    await apiClient.delete(`/Zakups/${id}`);
  },

  /** «Погасить долг» — FIFO pay across the supplier's unpaid receipts. */
  paySupplierDebt: async (supplierId: string, amount: number): Promise<void> => {
    await apiClient.post(`/Suppliers/${supplierId}/pay-debt`, { amount });
  },

  allReceipts: async (): Promise<ZakupReceipt[]> => {
    const { data } = await apiClient.get<ZakupReceipt[]>('/Zakups/GetAllReceipts');
    return data;
  },

  /** Month KPI tiles (count + total) aggregated server-side. `from` = Tashkent
   *  month-start as a UTC ISO instant. */
  summary: async (fromUtc: string): Promise<{ count: number; total: number }> => {
    const { data } = await apiClient.get<{ count: number; total: number }>('/Zakups/GetReceiptsSummary', {
      params: { from: fromUtc },
    });
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

  // ── Goods-receipt (priyomka) ──────────────────────────────────────────────

  /** Create a goods-receipt: adds stock, raises supplier debt for the unpaid part. */
  createReceipt: async (body: CreateReceiptBody): Promise<ReceiptDetail> => {
    const { data } = await apiClient.post<ReceiptDetail>('/Zakups/CreateReceipt', body);
    return data;
  },

  /** One receipt with its lines. */
  receipt: async (id: string): Promise<ReceiptDetail> => {
    const { data } = await apiClient.get<ReceiptDetail>(`/Zakups/GetReceipt/${id}`);
    return data;
  },

  /** «Отметить принятым» — accept an in-transit delivery (adds stock). */
  accept: async (id: string): Promise<ReceiptDetail> => {
    const { data } = await apiClient.post<ReceiptDetail>(`/Zakups/${id}/accept`, {});
    return data;
  },

  /** Pay down a receipt's outstanding supplier debt. */
  registerPayment: async (id: string, amount: number): Promise<ReceiptDetail> => {
    const { data } = await apiClient.post<ReceiptDetail>(`/Zakups/RegisterSupplierPayment/${id}`, {
      amount,
    });
    return data;
  },

  // ── Suppliers (spravochnik) ───────────────────────────────────────────────

  suppliersPaged: async (page = 1, size = 50, search?: string): Promise<PagedResult<Supplier>> => {
    const { data } = await apiClient.get<PagedResult<Supplier>>('/Suppliers/GetSuppliersPaged', {
      params: { page, size, search: search || undefined },
    });
    return data;
  },

  createSupplier: async (body: SupplierBody): Promise<Supplier> => {
    const { data } = await apiClient.post<Supplier>('/Suppliers/CreateSupplier', body);
    return data;
  },

  updateSupplier: async (id: string, body: SupplierBody): Promise<Supplier> => {
    const { data } = await apiClient.put<Supplier>('/Suppliers/UpdateSupplier', { id, ...body });
    return data;
  },

  /** Preflight before delete — reports outstanding debt / receipt count. */
  supplierDeleteInfo: async (id: string): Promise<SupplierDeleteInfo> => {
    const { data } = await apiClient.get<SupplierDeleteInfo>(`/Suppliers/GetSupplierDeleteInfo/${id}/delete-info`);
    return data;
  },

  deleteSupplier: async (id: string): Promise<void> => {
    await apiClient.delete(`/Suppliers/DeleteSupplier/${id}`);
  },

  // ── Exports ───────────────────────────────────────────────────────────────

  exportReceipts: async (): Promise<Blob> => {
    const { data } = await apiClient.get('/Zakups/export', { responseType: 'blob' });
    return data as Blob;
  },

  exportSuppliers: async (lang: string): Promise<Blob> => {
    const { data } = await apiClient.get('/Suppliers/export', { params: { lang }, responseType: 'blob' });
    return data as Blob;
  },
};
