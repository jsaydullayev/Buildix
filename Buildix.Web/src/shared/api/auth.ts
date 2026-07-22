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

export const publicMarketApi = {
  /** GET /api/public/market/{subdomain} — market state for the login page. */
  getState: async (subdomain: string): Promise<PublicMarketState> => {
    const { data } = await apiClient.get<PublicMarketState>(
      `/public/market/${encodeURIComponent(subdomain)}`,
    );
    return data;
  },
};
