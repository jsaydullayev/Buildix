import { apiClient } from '@/shared/api/client';

/** Period toggle in the Отчёты header — drives every query. */
export type ReportPeriod = 'week' | 'month' | 'quarter';

// --- Wire DTOs (Buildix Backend Contract — Отчёты) --------------------------

/** GET /api/Reports/period — 4 stat cards + payment breakdown. */
export interface PaymentBreakdownRow {
  paymentType: string; // "Cash" | "Terminal" | "Click" | "Debt" | …
  amount: number;
  count: number;
}
export interface PeriodReport {
  startDate: string;
  endDate: string;
  totalSales: number;
  totalPaidSales: number;
  totalDebtSales: number;
  totalZakup: number;
  profit: number | null; // COST-GATED (data.profit)
  netIncome: number | null; // COST-GATED (data.profit)
  totalTransactions: number;
  averageSale: number;
  paymentBreakdown: PaymentBreakdownRow[];
}

/** GET /api/Reports/weekly-series — revenue/profit-by-day chart. */
export interface WeeklySeriesPoint {
  date: string;
  revenue: number;
  profit: number; // 0 (not null) for non-profit callers
  checkCount: number;
}
export interface WeeklySeries {
  points: WeeklySeriesPoint[];
  currentTotal: number;
  previousTotal: number | null;
}

/** GET /api/Reports/monthly-category-sales — «Продажи по категориям». */
export interface CategorySalesRow {
  categoryId: number;
  categoryName: string;
  totalSales: number;
  totalQuantity: number;
  totalProfit: number | null; // COST-GATED
}
export interface MonthlyCategorySales {
  date: string;
  categories: CategorySalesRow[];
  totalSales: number;
  totalProfit: number | null;
}

/** GET /api/Reports/top-products — «Топ-5 товаров». */
export interface TopProductRow {
  rank: number;
  productId: string;
  name: string;
  category: string;
  sellers: number;
  quantity: number;
  revenue: number;
  profit: number | null; // COST-GATED
}
export interface TopProducts {
  period: string;
  sortBy: string;
  items: TopProductRow[];
}

/** GET /api/Reports/staff-performance — «Продавцы». */
export interface StaffRow {
  rank: number;
  userId: string;
  fullName: string;
  role: string;
  saleCount: number;
  revenue: number;
  averageCheck: number;
  shiftCount: number;
  isActiveShift: boolean;
}
export interface StaffPerformance {
  period: string;
  staff: StaffRow[];
}

// --- Endpoints (Reports controller uses attribute routes) -------------------

export const reportsApi = {
  /** Aggregate stat cards + payment breakdown for a date range. */
  period: async (start: string, end: string): Promise<PeriodReport> => {
    const { data } = await apiClient.get<PeriodReport>('/Reports/period', {
      params: { start, end },
    });
    return data;
  },

  /** Zero-filled revenue/profit day series (service caps `days` at 30). */
  weeklySeries: async (days: number): Promise<WeeklySeries> => {
    const { data } = await apiClient.get<WeeklySeries>('/Reports/weekly-series', {
      params: { days },
    });
    return data;
  },

  /** Category split for the month containing `date`. */
  categorySales: async (date: string): Promise<MonthlyCategorySales> => {
    const { data } = await apiClient.get<MonthlyCategorySales>('/Reports/monthly-category-sales', {
      params: { date },
    });
    return data;
  },

  topProducts: async (
    period: string,
    sortBy: 'quantity' | 'revenue' | 'profit' = 'revenue',
    limit = 5,
  ): Promise<TopProducts> => {
    const { data } = await apiClient.get<TopProducts>('/Reports/top-products', {
      params: { period, sortBy, limit },
    });
    return data;
  },

  staffPerformance: async (period: string): Promise<StaffPerformance> => {
    const { data } = await apiClient.get<StaffPerformance>('/Reports/staff-performance', {
      params: { period },
    });
    return data;
  },

  /** GET /api/Reports/period/export-pdf — returns a file blob (reports.export). */
  exportPeriodPdf: async (start: string, end: string, lang: string): Promise<Blob> => {
    const { data } = await apiClient.get<Blob>('/Reports/period/export-pdf', {
      params: { start, end, lang },
      responseType: 'blob',
    });
    return data;
  },
};
