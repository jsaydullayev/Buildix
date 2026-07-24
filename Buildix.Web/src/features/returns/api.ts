import { apiClient } from '@/shared/api/client';
import type { PagedResult } from '@/shared/api/paged';

export interface SaleReturnItem {
  productName: string;
  quantity: number;
  unitPrice: number;
  lineTotal: number;
}

export interface SaleReturn {
  id: string;
  number: number;
  saleId: string;
  saleNumber: number;
  /** Defect | NotFit | SellerError | Other */
  reason: string;
  /** Cash | Terminal | Transfer */
  refundMethod: string;
  totalAmount: number;
  comment: string | null;
  userName: string | null;
  createdAt: string;
  items: SaleReturnItem[];
}

export interface ReturnsSummary {
  count: number;
  totalAmount: number;
  revenuePercent: number;
}

export interface CreateReturnBody {
  saleId: string;
  reason: string;
  refundMethod: string;
  comment?: string | null;
  items: { saleItemId: string; quantity: number }[];
}

export const returnsApi = {
  list: async (params: { page?: number; size?: number; reason?: string | null; search?: string }): Promise<PagedResult<SaleReturn>> => {
    const { data } = await apiClient.get<PagedResult<SaleReturn>>('/Sales/returns', {
      params: {
        page: params.page ?? 1,
        size: params.size ?? 30,
        reason: params.reason || undefined,
        search: params.search || undefined,
      },
    });
    return data;
  },

  summary: async (from?: string): Promise<ReturnsSummary> => {
    const { data } = await apiClient.get<ReturnsSummary>('/Sales/returns/summary', {
      params: { from: from || undefined },
    });
    return data;
  },

  create: async (body: CreateReturnBody): Promise<SaleReturn> => {
    const { data } = await apiClient.post<SaleReturn>('/Sales/returns', body);
    return data;
  },
};
