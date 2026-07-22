import axios, {
  AxiosError,
  type AxiosInstance,
  type InternalAxiosRequestConfig,
} from 'axios';
import { env } from '@/shared/config/env';
import { sessionApi } from '@/shared/auth/sessionStore';
import { ACCESS_BLOCK, type ApiError, type AuthResponse } from './types';

/** Bare client with NO interceptors — used for the refresh call to avoid loops. */
const rawClient: AxiosInstance = axios.create({ baseURL: env.apiBaseUrl });

/** Main client used by the whole app. */
export const apiClient: AxiosInstance = axios.create({ baseURL: env.apiBaseUrl });

// --- Request: attach the bearer token ---------------------------------------
apiClient.interceptors.request.use((config: InternalAxiosRequestConfig) => {
  const token = sessionApi.getAccessToken();
  if (token) {
    config.headers.Authorization = `Bearer ${token}`;
  }
  return config;
});

// --- Single-flight refresh --------------------------------------------------
let refreshPromise: Promise<string | null> | null = null;

async function refreshAccessToken(): Promise<string | null> {
  const session = sessionApi.get();
  if (!session?.refreshToken || !session.accessToken) return null;
  try {
    const { data } = await rawClient.post<AuthResponse>('/Auth/RefreshToken', {
      accessToken: session.accessToken,
      refreshToken: session.refreshToken,
    });
    sessionApi.setTokens(data.accessToken, data.refreshToken, data.expiresAt);
    return data.accessToken;
  } catch {
    return null;
  }
}

interface RetryableConfig extends InternalAxiosRequestConfig {
  _retry?: boolean;
}

// --- Response: refresh on 401, map subscription blocks, normalise errors -----
apiClient.interceptors.response.use(
  (response) => response,
  async (error: AxiosError) => {
    const original = error.config as RetryableConfig | undefined;
    const status = error.response?.status;
    const url = original?.url ?? '';

    // Subscription / market-state enforcement (TZ §5).
    if (status === 402) sessionApi.setAccessBlock(ACCESS_BLOCK.expired);
    if (status === 423) sessionApi.setAccessBlock(ACCESS_BLOCK.blocked);

    const isAuthCall =
      url.includes('/Auth/Login') || url.includes('/Auth/RefreshToken');

    if (status === 401 && original && !original._retry && !isAuthCall) {
      original._retry = true;
      refreshPromise ??= refreshAccessToken().finally(() => {
        refreshPromise = null;
      });
      const newToken = await refreshPromise;
      if (newToken) {
        original.headers.Authorization = `Bearer ${newToken}`;
        return apiClient(original);
      }
      // Refresh failed → session is dead. Guards will redirect to login.
      sessionApi.clear();
    }

    return Promise.reject(normalizeError(error));
  },
);

/** Turn any Axios failure into the app-wide ApiError shape. */
export function normalizeError(error: unknown): ApiError {
  if (axios.isAxiosError(error)) {
    const status = error.response?.status ?? 0;
    const data = error.response?.data as
      | { message?: string; code?: string; retryAfterSeconds?: number; error?: string }
      | string
      | undefined;

    let message = 'Network error';
    let code: string | undefined;
    let retryAfterSeconds: number | undefined;

    if (typeof data === 'string') {
      message = data;
    } else if (data) {
      message = data.message ?? data.error ?? message;
      code = data.code;
      retryAfterSeconds = data.retryAfterSeconds;
    }

    return { status, message, code, retryAfterSeconds };
  }
  return { status: 0, message: 'Unknown error' };
}
