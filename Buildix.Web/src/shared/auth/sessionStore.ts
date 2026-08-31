import { create } from 'zustand';
import { persist } from 'zustand/middleware';
import type { AuthResponse, AccessBlock } from '@/shared/api/types';

interface SessionState {
  session: AuthResponse | null;
  /** Subscription / block state surfaced by the API (402 / 423). */
  accessBlock: AccessBlock | null;
  /** True until the one-time bootstrap (silent refresh) resolves. */
  bootstrapping: boolean;

  setSession: (session: AuthResponse) => void;
  /** Replace only the token pair after a successful refresh. */
  setTokens: (accessToken: string, refreshToken: string, expiresAt: string) => void;
  clearSession: () => void;
  setAccessBlock: (block: AccessBlock | null) => void;
  setBootstrapping: (value: boolean) => void;
}

export const useSessionStore = create<SessionState>()(
  persist(
    (set) => ({
      session: null,
      accessBlock: null,
      bootstrapping: true,

      setSession: (session) => set({ session, accessBlock: null }),
      setTokens: (accessToken, refreshToken, expiresAt) =>
        set((state) =>
          state.session
            ? { session: { ...state.session, accessToken, refreshToken, expiresAt } }
            : state,
        ),
      clearSession: () => set({ session: null, accessBlock: null }),
      setAccessBlock: (accessBlock) => set({ accessBlock }),
      setBootstrapping: (bootstrapping) => set({ bootstrapping }),
    }),
    {
      name: 'buildix.session',
      // The whole session (incl. tokens) is persisted so we can silently
      // re-enter after a reload. The access token is required by the refresh
      // endpoint (AuthService.RefreshTokenAsync derives the user from it), so
      // it cannot be dropped. On bootstrap we proactively rotate the pair.
      partialize: (state) => ({ session: state.session }),
    },
  ),
);

// --- Non-reactive accessors (for the axios interceptor, outside React) ---

export const sessionApi = {
  get: () => useSessionStore.getState().session,
  getAccessToken: () => useSessionStore.getState().session?.accessToken ?? null,
  getRefreshToken: () => useSessionStore.getState().session?.refreshToken ?? null,
  setTokens: useSessionStore.getState().setTokens,
  clear: () => useSessionStore.getState().clearSession(),
  setAccessBlock: (block: AccessBlock | null) =>
    useSessionStore.getState().setAccessBlock(block),
};
