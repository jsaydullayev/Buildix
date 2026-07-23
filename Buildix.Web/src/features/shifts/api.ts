import { apiClient } from '@/shared/api/client';

export interface Shift {
  id: string;
  userId: string;
  cashierName: string;
  openedAt: string;
  closedAt: string | null;
  isOpen: boolean;
  durationMinutes: number;
  openingCash: number;
  countedCash: number | null;
  discrepancy: number;
  reconStatus: string; // Open | Balanced | Discrepancy
  checkCount: number;
  revenue: number;
  cashIn: number;
  cardIn: number;
  withdrawals: number;
  expectedCash: number;
  // Per-tender breakdown. cashIn/cardIn stay NET of refunds (they drive
  // expectedCash); returns are reported separately.
  debtIn: number;
  cashCount: number;
  cardCount: number;
  debtCount: number;
  returnAmount: number;
  returnCount: number;
}

/** The caller's own shift history for a period + its totals. */
export interface MyShifts {
  items: Shift[];
  totalRevenue: number;
  totalChecks: number;
  avgCheck: number;
}

export type ShiftRange = 'week' | 'month' | 'all';

export interface Withdrawal {
  id: string;
  amount: number;
  comment: string;
  withdrawType: string;
  approvalStatus: string; // NotRequired | Pending | Approved | Rejected
  requestedByName: string | null;
  requestedAt: string;
  approvedByName: string | null;
  approvedAt: string | null;
}

export const cashApi = {
  withdrawals: async (status?: string): Promise<Withdrawal[]> => {
    const { data } = await apiClient.get<Withdrawal[]>('/CashRegister/withdrawals', {
      params: { status: status || undefined },
    });
    return data;
  },
  request: async (amount: number, comment: string): Promise<void> => {
    await apiClient.post('/CashRegister/withdraw-request', { amount, comment, withdrawType: 'cash' });
  },
  withdraw: async (amount: number, comment: string): Promise<void> => {
    await apiClient.post('/CashRegister/withdraw', { amount, comment, withdrawType: 'cash' });
  },
  approve: async (id: string): Promise<void> => {
    await apiClient.post(`/CashRegister/withdrawals/${id}/approve`);
  },
  reject: async (id: string): Promise<void> => {
    await apiClient.post(`/CashRegister/withdrawals/${id}/reject`);
  },
};

export const shiftsApi = {
  current: async (): Promise<Shift | null> => {
    const res = await apiClient.get<Shift>('/Shifts/current', {
      validateStatus: (s) => s === 200 || s === 204,
    });
    return res.status === 204 ? null : res.data;
  },

  open: async (): Promise<Shift> => {
    const { data } = await apiClient.post<Shift>('/Shifts/open');
    return data;
  },

  close: async (countedCash: number | null): Promise<Shift> => {
    const { data } = await apiClient.post<Shift>('/Shifts/close', { countedCash });
    return data;
  },

  history: async (limit = 20): Promise<Shift[]> => {
    const { data } = await apiClient.get<Shift[]>('/Shifts', { params: { limit } });
    return data;
  },

  /** The caller's OWN history — self-service, so a Seller (who lacks
   *  users.shift and cannot call /Shifts) still sees their shifts. */
  myHistory: async (range: ShiftRange): Promise<MyShifts> => {
    const { data } = await apiClient.get<MyShifts>('/Shifts/my', { params: { range } });
    return data;
  },
};
