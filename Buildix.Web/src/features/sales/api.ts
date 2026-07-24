import { apiClient } from '@/shared/api/client';
import type { PagedResult } from '@/shared/api/paged';

export interface SaleItem {
  id: string;
  productName: string;
  quantity: number;
  salePrice: number;
  totalPrice: number;
  /** Server-side Uzbek abbreviation — prefer `unitValue` + unitLabel() for display. */
  unit: string;
  /** UnitType number; 0 for external lines. Localise from this. */
  unitValue: number;
}

export interface SalePayment {
  paymentType: string;
  amount: number;
  /** Populated by the sale-detail read; absent in list rows. */
  paymentId?: string;
  createdAt?: string;
  /** Set when a different cashier collected this payment (debt paid off later). */
  collectedByName?: string | null;
}

export interface Sale {
  id: string;
  saleNumber: number;
  sellerId: string;
  sellerName: string;
  customerId: string | null;
  customerName: string | null;
  customerPhone: string | null;
  status: string;
  totalAmount: number;
  paidAmount: number;
  remainingAmount: number;
  discountAmount: number;
  createdAt: string;
  items: SaleItem[];
  payments: SalePayment[];
  /** Shift this receipt belongs to; 0 for sales made before shifts were numbered. */
  shiftNumber?: number;
}

export interface SalesQuery {
  page?: number;
  size?: number;
  search?: string;
  paymentType?: string | null;
  status?: string | null;
  from?: string | null;
  to?: string | null;
  /** Narrow to one cash shift's receipts ("Мои продажи за смену"). */
  shiftId?: string | null;
}

export interface TodaySalesSummary {
  totalSales: number;
  totalAmount: number;
  totalPaid: number;
  debtAmount: number;
  cashPaid: number;
  cardPaid: number;
  clickPaid: number;
}

export const salesApi = {
  listPaged: async (q: SalesQuery): Promise<PagedResult<Sale>> => {
    const { data } = await apiClient.get<PagedResult<Sale>>('/Sales', {
      params: {
        page: q.page ?? 1,
        size: q.size ?? 50,
        search: q.search || undefined,
        paymentType: q.paymentType || undefined,
        status: q.status || undefined,
        from: q.from || undefined,
        to: q.to || undefined,
        shiftId: q.shiftId || undefined,
      },
    });
    return data;
  },

  /** Full receipt: items, payment history, shift, customer. */
  byId: async (saleId: string): Promise<Sale> => {
    const { data } = await apiClient.get<Sale>(`/Sales/${saleId}`);
    return data;
  },

  todaySummary: async (): Promise<TodaySalesSummary> => {
    const { data } = await apiClient.get<TodaySalesSummary>('/CashRegister/today-sales');
    return data;
  },

  /** Faktura PDF (sales.invoice ruxsati bilan himoyalangan). */
  invoicePdf: async (saleId: string, lang: string): Promise<Blob> => {
    const { data } = await apiClient.get(`/Sales/${saleId}/invoice`, {
      params: { lang },
      responseType: 'blob',
    });
    return data as Blob;
  },
};
