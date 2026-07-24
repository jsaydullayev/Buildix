import { apiClient } from '@/shared/api/client';

/** One row of the cash ledger (see Buildix.Domain CashMovement). */
export interface CashMovement {
  id: string;
  /** Opening | Sale | DebtPayment | Deposit | Expense | Collection */
  type: string;
  /** Signed: income positive, outflow negative. */
  amount: number;
  category: string | null;
  /** Source document number (Ч-####) for Sale / DebtPayment. */
  refNumber: number | null;
  userName: string | null;
  comment: string | null;
  createdAt: string;
}

export interface CashLedger {
  /** Authoritative till balance (CashRegister.CurrentBalance). */
  balance: number;
  incomeToday: number;
  expenseToday: number;
  incomeCount: number;
  expenseCount: number;
  items: CashMovement[];
}

export const cashLedgerApi = {
  /** The day's cash ledger. `date` is a local calendar day; omitted = today. */
  ledger: async (date?: string): Promise<CashLedger> => {
    const { data } = await apiClient.get<CashLedger>('/CashRegister/movements', {
      params: { date: date || undefined },
    });
    return data;
  },

  /** Внесение — add cash (change). Records a Deposit movement. */
  deposit: async (amount: number, comment: string | null): Promise<void> => {
    await apiClient.post('/CashRegister/add', { amount, comment });
  },

  /** Расход (with category) or Инкассация (isCollection). Server checks overdraft. */
  withdraw: async (body: {
    amount: number;
    comment: string;
    category?: string;
    isCollection: boolean;
  }): Promise<void> => {
    await apiClient.post('/CashRegister/withdraw', { ...body, withdrawType: 'cash' });
  },
};
