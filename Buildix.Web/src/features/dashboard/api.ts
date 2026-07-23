import { apiClient } from '@/shared/api/client';

/** GET /api/CashRegister/today-sales — TodaySalesSummaryDto */
export interface TodaySalesSummary {
  totalSales: number; // count of sales (чеков)
  totalAmount: number;
  totalPaid: number;
  debtAmount: number;
  cashPaid: number;
  cardPaid: number;
  clickPaid: number;
  date: string;
}

/** GET /api/Reports/profit-summary — ProfitSummaryDto (data.profit gated) */
export interface ProfitSummary {
  todayProfit: number;
  weekProfit: number;
  monthProfit: number;
  totalProfit: number;
}

export interface CashWithdrawal {
  id: string;
  amount: number;
  comment: string;
  withdrawalDate: string;
  userName: string | null;
  withdrawType: string; // "cash" | "click"
}

/** GET /api/CashRegister — CashRegisterDto */
export interface CashRegister {
  id: string;
  currentBalance: number;
  lastUpdated: string;
  withdrawals: CashWithdrawal[];
}

/** GET /api/Reports/dashboard-summary — DashboardSummaryDto */
export interface DashboardSummary {
  customerCount: number;
  lowStockCount: number;
  pendingDebtsCount: number;
  pendingDebtsTotal: number;
  overdueDebtsCount: number;
}

export interface WeeklyPoint {
  date: string;
  revenue: number;
  profit: number; // 0 for non-profit callers (not null)
  checkCount: number;
}

/** GET /api/Reports/weekly-series — WeeklySeriesDto */
export interface WeeklySeries {
  points: WeeklyPoint[];
  currentTotal: number;
  previousTotal: number | null;
}

/** One line of a sale — name + quantity only, never cost/profit. */
export interface DailySaleLine {
  productName: string;
  quantity: number;
}

export interface DailySale {
  id: string;
  createdAt: string;
  sellerName: string;
  totalAmount: number;
  paymentType: string; // "cash" | "card" | "click" | "debt"
  status: string;
  profit: number | null; // data.profit gated
  customerName: string | null;
  items: DailySaleLine[];
}

/** GET /api/Reports/daily-sales-list — DailySalesListDto */
export interface DailySalesList {
  date: string;
  sales: DailySale[];
  totalSales: number;
  totalPaidSales: number;
  totalDebtSales: number;
  totalTransactions: number;
  summaryProfit: number | null;
}

// Smena tiplari/chaqiruvlari `@/features/shifts/api` da — bu yerda nusxasi bor
// edi va u /Shifts/current ning 204 javobini hisobga olmasdi (axios bo'sh satr
// qaytarardi, tip esa `Shift | null` deb turardi). Dashboard endi o'sha yagona
// manbadan foydalanadi.

export const dashboardApi = {
  todaySales: async (): Promise<TodaySalesSummary> => {
    const { data } = await apiClient.get<TodaySalesSummary>('/CashRegister/today-sales');
    return data;
  },

  profitSummary: async (): Promise<ProfitSummary> => {
    const { data } = await apiClient.get<ProfitSummary>('/Reports/profit-summary');
    return data;
  },

  cashRegister: async (): Promise<CashRegister> => {
    const { data } = await apiClient.get<CashRegister>('/CashRegister');
    return data;
  },

  summary: async (): Promise<DashboardSummary> => {
    const { data } = await apiClient.get<DashboardSummary>('/Reports/dashboard-summary');
    return data;
  },

  weeklySeries: async (days = 7, compare = true): Promise<WeeklySeries> => {
    const { data } = await apiClient.get<WeeklySeries>('/Reports/weekly-series', {
      params: { days, compare },
    });
    return data;
  },

  dailySales: async (date: string): Promise<DailySalesList> => {
    const { data } = await apiClient.get<DailySalesList>('/Reports/daily-sales-list', {
      params: { date },
    });
    return data;
  },
};
