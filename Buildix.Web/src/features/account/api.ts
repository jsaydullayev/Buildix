import { apiClient } from '@/shared/api/client';

export interface Session {
  id: string;
  device: string | null;
  ipAddress: string | null;
  lastUsedAt: string | null;
  createdAt: string;
  isCurrent: boolean;
}

export interface LoginHistoryItem {
  id: string;
  device: string | null;
  ipAddress: string | null;
  atUtc: string;
  success: boolean;
}

export interface MyProfile {
  id: string;
  fullName: string;
  username: string;
  role: string;
  phone: string | null;
  telegram: string | null;
  /** Telegram bot ID (string — a chat id exceeds JS safe-integer range). */
  telegramChatId: string | null;
  /** Saved UI language ("uz" | "ru" | "en"). */
  language: string;
  /** Per-user Telegram notification toggles (BE-9). */
  notifyDebt: boolean;
  notifyStock: boolean;
  notifyShift: boolean;
}

export interface UpdateProfileBody {
  fullName?: string | null;
  currentPassword?: string | null;
  newPassword?: string | null;
  phone?: string | null;
  telegram?: string | null;
  /**
   * Telegram bot ID. ONLY "" is accepted — it unlinks. A real id is rejected by
   * the server: typing an id proves nothing about owning that Telegram account,
   * so linking goes through `telegramLinkCode` instead.
   */
  telegramChatId?: '' | null;
  /** One-time 6-digit code the bot hands out; omit (or null) to leave unchanged. */
  telegramLinkCode?: string | null;
  /** UI language code; omit (or null) to leave it unchanged. */
  language?: string | null;
  /** Per-user Telegram notification toggles; omit (or null) to leave unchanged. */
  notifyDebt?: boolean | null;
  notifyStock?: boolean | null;
  notifyShift?: boolean | null;
}

export const accountApi = {
  profile: async (): Promise<MyProfile> => {
    const { data } = await apiClient.get<MyProfile>('/Users/MyProfile');
    return data;
  },

  sessions: async (): Promise<Session[]> => {
    const { data } = await apiClient.get<Session[]>('/Auth/Sessions');
    return data;
  },

  loginHistory: async (): Promise<LoginHistoryItem[]> => {
    const { data } = await apiClient.get<LoginHistoryItem[]>('/Auth/LoginHistory', {
      params: { limit: 20 },
    });
    return data;
  },

  /** «So'nggi kirishlar» ro'yxatini tozalaydi (faqat o'ziniki). */
  clearLoginHistory: async (): Promise<number> => {
    const { data } = await apiClient.post<{ cleared: number }>('/Auth/ClearLoginHistory');
    return data.cleared;
  },

  revokeOthers: async (refreshToken: string): Promise<number> => {
    const { data } = await apiClient.post<{ revoked: number }>('/Auth/RevokeOtherSessions', {
      refreshToken,
    });
    return data.revoked;
  },

  /** «Завершить» a single session by id. */
  revokeSession: async (id: string): Promise<void> => {
    await apiClient.post(`/Auth/RevokeSession/${id}`);
  },

  updateProfile: async (body: UpdateProfileBody): Promise<void> => {
    await apiClient.put('/Users/UpdateMyProfile', body);
  },

  /** Platforma botining @username'i — «Botni ochish» tugmasi uchun. */
  telegramBot: async (): Promise<string | null> => {
    const { data } = await apiClient.get<{ botUsername: string | null }>('/Users/TelegramBotInfo');
    return data.botUsername;
  },
};
