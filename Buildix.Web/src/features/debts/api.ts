import { apiClient } from '@/shared/api/client';

export interface DebtorSummary {
  customerId: string;
  customerName: string | null;
  customerPhone: string;
  customerType: string; // "Individual" | "Legal"
  remainingDebt: number;
  debtCount: number;
  nearestDueDate: string | null;
  isOverdue: boolean;
}

export interface DebtSummaryStats {
  totalDebt: number;
  overdueTotal: number;
  overdueCount: number;
  customerCount: number;
  paidToday: number;
  paidThisMonth: number;
}

export interface CustomerDebt {
  id: string;
  saleId: string;
  customerId: string;
  totalDebt: number;
  remainingDebt: number;
  status: string;
  createdAt: string;
  dueDate: string | null;
}

export const debtsApi = {
  debtors: async (search?: string, due?: string | null): Promise<DebtorSummary[]> => {
    const { data } = await apiClient.get<DebtorSummary[]>('/Debts/debtors', {
      params: { search: search || undefined, due: due || undefined },
    });
    return data;
  },

  summary: async (): Promise<DebtSummaryStats> => {
    const { data } = await apiClient.get<DebtSummaryStats>('/Debts/summary');
    return data;
  },

  customerDebts: async (customerId: string): Promise<CustomerDebt[]> => {
    const { data } = await apiClient.get<CustomerDebt[]>(`/Debts/GetCustomerDebts/${customerId}`);
    return data;
  },

  pay: async (debtId: string, amount: number, paymentType: string): Promise<void> => {
    await apiClient.post(`/Debts/${debtId}/pay`, { amount, paymentType });
  },

  exportExcel: async (lang: string): Promise<Blob> => {
    const { data } = await apiClient.get('/Debts/export', {
      params: { lang },
      responseType: 'blob',
    });
    return data as Blob;
  },
};

