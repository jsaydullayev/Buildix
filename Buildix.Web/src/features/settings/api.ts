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
  ownerTelegram: string | null;
  ownerTelegramLinked?: boolean;
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
