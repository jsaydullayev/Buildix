import { apiClient } from '@/shared/api/client';

/** Bitta kategoriyaning davr ichidagi sotuvi. */
export interface CategorySales {
  categoryId: number;
  categoryName: string;
  totalSales: number;
  totalQuantity: number;
  totalProfit: number | null;
}

export interface CategorySalesResponse {
  date: string;
  categories: CategorySales[];
  /**
   * Chegirma AYIRILGAN jami. Ulush hisoblashda bunga bo'lish mumkin emas:
   * kategoriya qatorlari chegirmasiz keladi va foizlar 100 dan oshib
   * ketardi — hisobot ekranida aynan shu «111%» bo'lib ko'ringan edi.
   */
  totalSales: number;
  totalProfit: number | null;
}

export const categorySalesApi = {
  /** Ixtiyoriy davr uchun — hafta / oy / chorak. */
  forPeriod: async (startIso: string, endIso: string): Promise<CategorySalesResponse> => {
    const { data } = await apiClient.get<CategorySalesResponse>('/Reports/category-sales', {
      params: { startDate: startIso, endDate: endIso },
    });
    return data;
  },
};
