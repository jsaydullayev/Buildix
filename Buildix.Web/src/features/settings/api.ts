import { apiClient } from '@/shared/api/client';

/** Mirrors MarketSettingsDto / UpdateMarketSettingsRequest. */
export interface MarketSettings {
  phone: string | null;
  address: string | null;
  workingHours: string | null;
  salesOnlyWhenShiftOpen: boolean;
  cashWithdrawalNeedsApproval: boolean;
  debtOnlyForRegulars: boolean;
  defaultDebtLimit: number;
  allowedCashDiscrepancy: number;
  shiftAutoCloseTime: string | null;
  /** Посещаемость (davomat rejasi) — "HH:mm". */
  workDayStart: string;
  workDayEnd: string;
  lateThreshold: string;
  receiptHeader: string | null;
  receiptFooter: string | null;
  autoPrintReceipt: boolean;
  defaultLanguage: string; // "ru" | "uz"
  firstDayOfWeek: number;
  minStockAlertEnabled: boolean;
  blockSaleBelowCost: boolean;
  defaultMarkupPct: number;
  notifyDaySummary: boolean;
  notifyOverdueDebts: boolean;
  notifyWithdrawalRequests: boolean;
  // Telegram bog'lash User.telegramChatId'ga ko'chdi (Аккаунт ekrani).
  inactivityLogoutMinutes: number;
  auditEnabled: boolean;
}

export const settingsApi = {
  get: async (): Promise<MarketSettings> => {
    const { data } = await apiClient.get<MarketSettings>('/Markets/settings');
    return data;
  },
  update: async (body: MarketSettings): Promise<MarketSettings> => {
    const { data } = await apiClient.put<MarketSettings>('/Markets/settings', body);
    return data;
  },
};
