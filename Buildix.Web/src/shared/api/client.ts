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

    // Blob so'ralgan javobda XATO ham Blob bo'lib keladi (PDF, Excel, chek).
    // normalizeError undan matn o'qiy olmaydi va hammasi «Network error» bo'lib
    // ko'rinadi — server aslida aniq sabab yozgan bo'lsa ham. Shuning uchun
    // Blob'ni matnga o'girib, JSON bo'lsa ochamiz.
    const blob = error.response?.data;
    if (blob instanceof Blob) {
      try {
        const text = await blob.text();
        const trimmed = text.trim();
        error.response!.data =
          trimmed.startsWith('{') || trimmed.startsWith('[') ? JSON.parse(trimmed) : trimmed;
      } catch {
        // O'qib bo'lmadi — avvalgi xulq saqlanadi.
      }
    }

    return Promise.reject(normalizeError(error));
  },
);

/**
 * ASP.NET validatsiya javobidagi birinchi aniq xato. `errors` shakli:
 * `{ "widthMm": ["The value 'abc' is not valid."] }`.
 */
function firstValidationError(errors: Record<string, string[]> | undefined): string | undefined {
  if (!errors) return undefined;
  for (const list of Object.values(errors)) {
    const first = list?.[0];
    if (first) return first;
  }
  return undefined;
}

/** Turn any Axios failure into the app-wide ApiError shape. */
export function normalizeError(error: unknown): ApiError {
  if (axios.isAxiosError(error)) {
    const status = error.response?.status ?? 0;
    const data = error.response?.data as
      | {
          message?: string;
          code?: string;
          retryAfterSeconds?: number;
          error?: string;
          // ASP.NET ProblemDetails: validatsiya xatolari shu ikki maydonda
          // keladi, `message` esa umuman bo'lmaydi.
          title?: string;
          errors?: Record<string, string[]>;
        }
      | string
      | undefined;

    // «Network error» — faqat javob UMUMAN kelmaganda. Javob kelgan, lekin
    // tanasi bo'sh bo'lsa xabar berilmaydi va chaqiruvchi o'z matnini qo'yadi.
    let message: string | undefined = error.response ? undefined : 'Network error';
    let code: string | undefined;
    let retryAfterSeconds: number | undefined;

    if (typeof data === 'string') {
      message = data.trim() === '' ? message : data;
    } else if (data instanceof Blob) {
      // Bu yerga faqat interceptor chetlab o'tilganda kelinadi. Blob'ni
      // sinxron o'qib bo'lmaydi, lekin «Network error» deyish yolg'on bo'lardi:
      // so'rov serverga yetgan va u javob qaytargan.
      message = `Server xatosi (${status})`;
    } else if (data) {
      // Tartib muhim: server o'z xabarini bergan bo'lsa o'sha ko'rsatiladi.
      // Bo'lmasa — validatsiya xatosining AYNAN o'zi (qaysi maydon, nima
      // noto'g'ri), va faqat oxirida umumiy sarlavha.
      message = data.message ?? data.error ?? firstValidationError(data.errors) ?? data.title ?? message;
      // Bo'sh satr ham «xabar yo'q» degani — `??` uni o'tkazib yuboradi.
      if (message !== undefined && message.trim() === '') message = undefined;
      code = data.code;
      retryAfterSeconds = data.retryAfterSeconds;
    }

    return { status, message, code, retryAfterSeconds };
  }
  return { status: 0, message: 'Unknown error' };
}
