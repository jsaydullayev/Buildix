import { apiClient } from '@/shared/api/client';
import type { PagedResult } from '@/shared/api/paged';

export interface SaleItem {
  id: string;
  productName: string;
  quantity: number;
  salePrice: number;
  totalPrice: number;
  unit: string;
}

export interface SalePayment {
  paymentType: string;
  amount: number;
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
}

export interface SalesQuery {
  page?: number;
  size?: number;
  search?: string;
  paymentType?: string | null;
  status?: string | null;
  from?: string | null;
  to?: string | null;
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
      },
    });
    return data;
  },

  todaySummary: async (): Promise<TodaySalesSummary> => {
    const { data } = await apiClient.get<TodaySalesSummary>('/CashRegister/today-sales');
    return data;
  },
};
