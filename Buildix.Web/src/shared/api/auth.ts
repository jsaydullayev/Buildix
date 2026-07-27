import { apiClient } from './client';
import type { AuthResponse, LoginRequest, PublicMarketState } from './types';

export const authApi = {
  login: async (payload: LoginRequest): Promise<AuthResponse> => {
    const { data } = await apiClient.post<AuthResponse>('/Auth/Login', payload);
    return data;
  },

  /** Server-side refresh; the interceptor handles the automatic path. */
  refresh: async (accessToken: string, refreshToken: string): Promise<AuthResponse> => {
    const { data } = await apiClient.post<AuthResponse>('/Auth/RefreshToken', {
      accessToken,
      refreshToken,
    });
    return data;
  },

  logout: async (refreshToken: string, accessToken: string): Promise<void> => {
    await apiClient.post('/Auth/Logout', { refreshToken, accessToken });
  },
};

export interface PublicSupportContacts {
  phone: string | null;
  telegram: string | null;
  email: string | null;
}

export const consoleApi = {
  /**
   * SuperAdmin konsolining yashirin segmenti. Faqat autentifikatsiyadan
   * o'tgan SuperAdmin uchun — shuning uchun operator uzun sirni qo'lda
   * yozib yurmaydi: oddiy login/parol bilan kiradi va konsolga yo'naltiriladi.
   * Sozlanmagan bo'lsa 404 (konsol umuman ochilmaydi).
   */
  segment: async (): Promise<string> => {
    const { data } = await apiClient.get<{ segment: string }>('/Auth/ConsoleSegment');
    return data.segment;
  },
};

export const publicMarketApi = {
  /** GET /api/public/market/{subdomain} — market state for the login page. */
  getState: async (subdomain: string): Promise<PublicMarketState> => {
    const { data } = await apiClient.get<PublicMarketState>(
      `/public/market/${encodeURIComponent(subdomain)}`,
    );
    return data;
  },

  /**
   * GET /api/public/support — kirish sahifasidagi kontaktlar. Ular platforma
   * sozlamalaridan keladi: operator «Настройки → Контакты поддержки» ni
   * o'zgartirsa, login sahifasi darhol yangisini ko'rsatadi (ilgari ular
   * kodga yozib qo'yilgan edi).
   */
  support: async (): Promise<PublicSupportContacts> => {
    const { data } = await apiClient.get<PublicSupportContacts>('/public/support');
    return data;
  },
};
