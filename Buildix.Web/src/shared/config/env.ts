/** Centralised, typed access to build-time environment configuration. */
export const env = {
  apiBaseUrl: import.meta.env.VITE_API_BASE_URL ?? '/api',
  hubBaseUrl: import.meta.env.VITE_HUB_BASE_URL ?? '/hubs',
} as const;
