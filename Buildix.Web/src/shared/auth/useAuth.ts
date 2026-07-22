import { useCallback } from 'react';
import { useMutation } from '@tanstack/react-query';
import { authApi } from '@/shared/api/auth';
import type { LoginRequest } from '@/shared/api/types';
import { ROLES } from '@/shared/config/permissions';
import { useSessionStore } from './sessionStore';

/** Reactive access to the current session and permission checks. */
export function useAuth() {
  const session = useSessionStore((s) => s.session);
  const accessBlock = useSessionStore((s) => s.accessBlock);
  const bootstrapping = useSessionStore((s) => s.bootstrapping);
  const setSession = useSessionStore((s) => s.setSession);
  const clearSession = useSessionStore((s) => s.clearSession);

  const hasPermission = useCallback(
    (key: string): boolean => {
      if (!session) return false;
      if (session.role === ROLES.Owner || session.role === ROLES.SuperAdmin) return true;
      return session.permissions.includes(key);
    },
    [session],
  );

  const hasRole = useCallback(
    (...roles: string[]) => !!session && roles.includes(session.role),
    [session],
  );

  return {
    session,
    accessBlock,
    bootstrapping,
    isAuthenticated: !!session,
    hasPermission,
    hasRole,
    setSession,
    clearSession,
  };
}

/** Login mutation — stores the session on success. */
export function useLogin() {
  const setSession = useSessionStore((s) => s.setSession);
  return useMutation({
    mutationFn: (payload: LoginRequest) => authApi.login(payload),
    onSuccess: (data) => setSession(data),
  });
}

/** Logout — best-effort server revoke, then always clear local session. */
export function useLogout() {
  const session = useSessionStore((s) => s.session);
  const clearSession = useSessionStore((s) => s.clearSession);
  return useCallback(async () => {
    if (session?.refreshToken && session.accessToken) {
      try {
        await authApi.logout(session.refreshToken, session.accessToken);
      } catch {
        // Ignore — clearing the local session is what matters for the user.
      }
    }
    clearSession();
  }, [session, clearSession]);
}
