/** API DTO types — mirror of Buildix.Application/DTOs (camelCase JSON). */

export interface LoginRequest {
  username: string;
  password: string;
  /** Market slug taken from the URL sub-path; scopes login to one tenant. */
  subdomain?: string | null;
}

export interface RefreshTokenRequest {
  accessToken: string;
  refreshToken: string;
}

export interface AuthResponse {
  userId: string;
  username: string;
  fullName: string;
  role: string;
  language: string;
  accessToken: string;
  refreshToken: string;
  expiresAt: string; // ISO
  permissions: string[];
  marketId: number | null;
  subdomain: string | null;
}

/** GET /api/public/market/{subdomain} — login page market state (PublicMarketStateDto). */
export interface PublicMarketState {
  subdomain: string;
  marketName: string;
  /** "active" (show form) | "expired" | "blocked" (see TZ §6). */
  state: string;
  expiresAt: string | null;
}

/** Normalised error shape surfaced to the UI. */
export interface ApiError {
  status: number;
  message: string;
  /** Domain code when present (e.g. SUBSCRIPTION_EXPIRED, MARKET_BLOCKED). */
  code?: string;
  retryAfterSeconds?: number;
}

export const ACCESS_BLOCK = {
  expired: 'expired',
  blocked: 'blocked',
} as const;

export type AccessBlock = (typeof ACCESS_BLOCK)[keyof typeof ACCESS_BLOCK];
