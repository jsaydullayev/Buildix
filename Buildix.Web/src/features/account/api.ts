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

  revokeOthers: async (refreshToken: string): Promise<number> => {
    const { data } = await apiClient.post<{ revoked: number }>('/Auth/RevokeOtherSessions', {
      refreshToken,
    });
    return data.revoked;
  },

  updateProfile: async (body: UpdateProfileBody): Promise<void> => {
    await apiClient.put('/Users/UpdateMyProfile', body);
  },
};
