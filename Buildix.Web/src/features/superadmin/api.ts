import { apiClient } from '@/shared/api/client';

/**
 * SuperAdmin konsoli API'si.
 *
 * Backend yo'li yashirin segment bilan himoyalangan: `/api/_sa/{segment}/...`,
 * segment noto'g'ri bo'lsa autentifikatsiyagacha 404 qaytadi
 * (SuperAdminPathGateMiddleware). Shuning uchun segment bu yerda ham
 * QAT'IY ravishda marshrut parametridan (`/_sa/:segment/...`) uzatiladi —
 * `.env` ga yoki bundle ichiga yozilsa, ochiq JS'da har kimga ko'rinib,
 * yashirinlik qatlami butunlay yo'qolardi.
 */

/** Backenddagi RegistrationRequestStatus nomlari (DB kontrakti). */
export type SaRequestStatus = 'Pending' | 'Accepted' | 'Approved' | 'Rejected';

export interface SaRequestRow {
  id: string;
  fullName: string;
  phone: string;
  status: SaRequestStatus;
  createdAt: string;
  processedAt: string | null;
  processedByUserName: string | null;
  createdUserId: string | null;
  createdMarketId: number | null;
  rejectReason: string | null;
  note: string | null;
  /** Approved + do'kon haqiqatan yaratilgan = «Подключена». */
  isConnected: boolean;
}

export interface SaAvailability {
  usernameAvailable: boolean | null;
  marketNameAvailable: boolean | null;
  subdomainAvailable: boolean | null;
  /** Do'kon nomidan yasalgan sub-path — server yozadigan qiymatning aynan o'zi. */
  suggestedSubdomain: string | null;
}

export interface CreateStoreBody {
  username: string;
  password: string;
  marketName: string;
  subdomain?: string | null;
  expiresAt?: string | null;
  language?: string | null;
}

export interface CreateStoreResult {
  userId: string;
  marketId: number;
  username: string;
  subdomain: string | null;
  marketName: string;
}

export interface SaDashboardStore {
  marketId: number;
  name: string;
  expiresAt: string | null;
  users: number;
  /** 'Active' | 'Overdue' | 'Blocked' — matn emas, klient o'zi bo'yaydi/tarjima qiladi. */
  status: 'Active' | 'Overdue' | 'Blocked';
  isBlocked: boolean;
  lastActivityUtc: string | null;
}

export interface SaDashboard {
  kpis: {
    activeStores: number;
    newStoresThisMonth: number;
    newRequests: number;
    /** null — tarif modeli hali yo'q (S3). Nol emas: nol yolg'on bo'lardi. */
    monthlyRevenueUzs: number | null;
    overdueStores: number;
  };
  newRequests: {
    id: string;
    fullName: string;
    phone: string;
    note: string | null;
    createdAt: string;
  }[];
  overdue: SaDashboardStore[];
  expiringSoon: SaDashboardStore[];
  stores: SaDashboardStore[];
}

export interface SaStoreRow {
  marketId: number;
  name: string;
  city: string | null;
  subdomain: string | null;
  createdAt: string;
  ownerId: string;
  ownerName: string;
  ownerPhone: string | null;
  /** null — tarif modeli S3 da keladi. */
  plan: string | null;
  expiresAt: string | null;
  users: number;
  status: 'Active' | 'Overdue' | 'Blocked';
  isBlocked: boolean;
  lastActivityUtc: string | null;
}

export interface SaStoreDetail {
  store: SaStoreRow;
  blockedAt: string | null;
  blockedReason: string | null;
  stats: {
    users: number;
    checksThisMonth: number;
    lastActivityUtc: string | null;
    outstandingDebt: number;
  };
  /** S3 gacha har doim bo'sh (BE-S2). */
  payments: { paidAtUtc: string; method: string; amountUzs: number }[];
}

export type SaPlanCode = 'Start' | 'Standard' | 'Pro';
export type SaPaymentChannel = 'Cash' | 'Click' | 'Payme' | 'Transfer';

export interface SaPlan {
  code: SaPlanCode;
  priceUzs: number;
  /** 0 = limitsiz. */
  maxUsers: number;
  maxPoints: number;
  stores: number;
}

export interface SaBillingRow {
  marketId: number;
  name: string;
  plan: SaPlanCode;
  priceUzs: number;
  expiresAt: string | null;
  status: 'Active' | 'Soon' | 'Overdue' | 'Blocked';
  lastPaymentAtUtc: string | null;
  lastPaymentChannel: SaPaymentChannel | null;
  /** false — egaga Telegram eslatmasi yetib bormaydi (qo'ng'iroq kerak). */
  ownerTelegramLinked: boolean;
}

export interface SaPaymentPreview {
  currentExpiresAt: string | null;
  newExpiresAt: string;
  amountUzs: number;
  plan: SaPlanCode;
  /** true — langar eski muddat (xizmat uzilmagan edi). */
  anchoredOnExpiry: boolean;
  /** true — muddat operator tomonidan qo'lda kiritilgan. */
  manual: boolean;
}

export interface SaPaymentLog {
  id: string;
  marketId: number;
  storeName: string;
  plan: SaPlanCode;
  channel: SaPaymentChannel;
  amountUzs: number;
  paidAtUtc: string;
}

export type SaRole = 'Owner' | 'Admin' | 'Seller';

export interface SaUserRow {
  id: string;
  fullName: string;
  username: string;
  phone: string | null;
  role: SaRole;
  marketId: number | null;
  storeName: string | null;
  lastActiveAt: string | null;
  isActive: boolean;
}

export interface SaPaged<T> {
  items: T[];
  page: number;
  size: number;
  total: number;
  totalPages: number;
}

export interface SaPlanPrice {
  code: SaPlanCode;
  priceUzs: number;
  /** 0 = limitsiz. */
  maxUsers: number;
  maxPoints: number;
}

export interface SaSettings {
  plans: SaPlanPrice[];
  graceDays: number;
  warnOnOverdue: boolean;
  restrictAfterGrace: boolean;
  fullBlockAfterDays: number;
  soonThresholdDays: number;
  notifyExpiring: boolean;
  notifyBlocked: boolean;
  expiryReminderDays: number;
  supportPhone: string | null;
  supportTelegram: string | null;
  supportEmail: string | null;
}

export const superAdminApi = {
  dashboard: async (segment: string): Promise<SaDashboard> => {
    const { data } = await apiClient.get<SaDashboard>(`/_sa/${segment}/dashboard`);
    return data;
  },

  requests: async (segment: string, status?: SaRequestStatus): Promise<SaRequestRow[]> => {
    const { data } = await apiClient.get<SaRequestRow[]>(`/_sa/${segment}/requests`, {
      params: status ? { status } : undefined,
    });
    return data;
  },

  /** «Принять» — qo'ng'iroq qilindi. Do'kon yaratilmaydi. */
  acceptRequest: async (segment: string, id: string) => {
    await apiClient.post(`/_sa/${segment}/requests/${id}/accept`, {});
  },

  /** «Вернуть» — qabul qilish yoki rad etishni bekor qiladi. */
  reopenRequest: async (segment: string, id: string) => {
    await apiClient.post(`/_sa/${segment}/requests/${id}/reopen`, {});
  },

  rejectRequest: async (segment: string, id: string, reason: string) => {
    await apiClient.post(`/_sa/${segment}/requests/${id}/reject`, { reason });
  },

  /** «Создать магазин» — do'kon + egasi akkaunti bitta tranzaksiyada. */
  approveRequest: async (
    segment: string,
    id: string,
    body: CreateStoreBody,
  ): Promise<CreateStoreResult> => {
    const { data } = await apiClient.post<CreateStoreResult>(
      `/_sa/${segment}/requests/${id}/approve`,
      body,
    );
    return data;
  },

  /**
   * Arizasiz do'kon yaratish — «Do'konlar» sahifasidagi tugma uchun.
   * Ariza orqali yaratish (`approveRequest`) bilan bir xil natija qaytaradi,
   * farqi: ega ismi va telefoni shu yerda qo'lda kiritiladi.
   */
  createStore: async (
    segment: string,
    body: CreateStoreBody & { fullName: string; phone: string },
  ): Promise<CreateStoreResult> => {
    const { data } = await apiClient.post<CreateStoreResult>(`/_sa/${segment}/owners`, body);
    return data;
  },

  checkAvailability: async (
    segment: string,
    params: { username?: string; marketName?: string; subdomain?: string },
  ): Promise<SaAvailability> => {
    const { data } = await apiClient.get<SaAvailability>(`/_sa/${segment}/check-availability`, {
      params,
    });
    return data;
  },

  users: async (
    segment: string,
    params: { role?: SaRole; marketId?: number; search?: string; page?: number; size?: number },
  ): Promise<SaPaged<SaUserRow>> => {
    const { data } = await apiClient.get<SaPaged<SaUserRow>>(`/_sa/${segment}/users`, { params });
    return data;
  },

  /** Yangi parolni SuperAdmin qo'yadi; foydalanuvchining sessiyalari uziladi. */
  resetPassword: async (segment: string, userId: string, newPassword: string) => {
    await apiClient.post(`/_sa/${segment}/users/${userId}/reset-password`, { newPassword });
  },

  setUserActive: async (segment: string, userId: string, active: boolean) => {
    await apiClient.post(`/_sa/${segment}/users/${userId}/${active ? 'unblock' : 'block'}`, {});
  },

  settings: async (segment: string): Promise<SaSettings> => {
    const { data } = await apiClient.get<SaSettings>(`/_sa/${segment}/settings`);
    return data;
  },

  /** Saqlangach blok qoidalari SHU ZAHOTI kuchga kiradi (server keshni yangilaydi). */
  updateSettings: async (segment: string, body: SaSettings): Promise<SaSettings> => {
    const { data } = await apiClient.put<SaSettings>(`/_sa/${segment}/settings`, body);
    return data;
  },

  plans: async (segment: string): Promise<SaPlan[]> => {
    const { data } = await apiClient.get<SaPlan[]>(`/_sa/${segment}/plans`);
    return data;
  },

  billing: async (segment: string): Promise<SaBillingRow[]> => {
    const { data } = await apiClient.get<SaBillingRow[]>(`/_sa/${segment}/billing`);
    return data;
  },

  recentPayments: async (segment: string, take = 10): Promise<SaPaymentLog[]> => {
    const { data } = await apiClient.get<SaPaymentLog[]>(`/_sa/${segment}/payments`, {
      params: { take },
    });
    return data;
  },

  paymentPreview: async (
    segment: string,
    marketId: number,
    months: number,
    /** Qo'lda kiritilgan tugash sanasi (ISO). Berilsa hisoblanganidan ustun. */
    expiresAt?: string | null,
  ): Promise<SaPaymentPreview> => {
    const { data } = await apiClient.get<SaPaymentPreview>(
      `/_sa/${segment}/markets/${marketId}/payment-preview`,
      { params: { months, expiresAt: expiresAt || undefined } },
    );
    return data;
  },

  /**
   * «Оплата получена». `idempotencyKey` MAJBURIY: bu amal qaytarib
   * bo'lmaydi, ikki marta bosilgan tugma ikki oy bermasligi kerak.
   */
  recordPayment: async (
    segment: string,
    marketId: number,
    body: {
      months: number;
      channel: SaPaymentChannel;
      plan?: SaPlanCode | null;
      note?: string | null;
      /** Qo'lda kiritilgan tugash sanasi — hisoblanganidan ustun turadi. */
      expiresAt?: string | null;
    },
    idempotencyKey: string,
  ): Promise<{ paymentId: string; amountUzs: number; newExpiresAt: string }> => {
    const { data } = await apiClient.post(`/_sa/${segment}/markets/${marketId}/payments`, body, {
      headers: { 'Idempotency-Key': idempotencyKey },
    });
    return data;
  },

  /** «Напомнить всем должникам» — Telegram eslatmasi (SMS emas). */
  remindOverdue: async (segment: string): Promise<{ sent: number; unreachable: number }> => {
    const { data } = await apiClient.post(`/_sa/${segment}/reminders/overdue`, {});
    return data;
  },

  stores: async (segment: string): Promise<SaStoreRow[]> => {
    const { data } = await apiClient.get<SaStoreRow[]>(`/_sa/${segment}/stores`);
    return data;
  },

  store: async (segment: string, marketId: number): Promise<SaStoreDetail> => {
    const { data } = await apiClient.get<SaStoreDetail>(`/_sa/${segment}/stores/${marketId}`);
    return data;
  },

  blockMarket: async (segment: string, marketId: number, reason: string) => {
    await apiClient.post(`/_sa/${segment}/markets/${marketId}/block`, { reason });
  },

  unblockMarket: async (segment: string, marketId: number) => {
    await apiClient.post(`/_sa/${segment}/markets/${marketId}/unblock`, {});
  },
};
