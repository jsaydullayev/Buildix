import { useEffect, useRef } from 'react';
import { authApi } from '@/shared/api/auth';
import { useSessionStore } from './sessionStore';

/**
 * One-time silent auto-login on app load. The full session (incl. tokens) is
 * persisted; on reload we proactively rotate the token pair via the refresh
 * endpoint (which derives the user from the — possibly expired — access token).
 * Success => silent re-entry; failure => the session is cleared and the user
 * lands on the login page.
 */
export function useBootstrap() {
  const ran = useRef(false);

  useEffect(() => {
    if (ran.current) return;
    ran.current = true;

    const { session, setSession, clearSession, setBootstrapping } =
      useSessionStore.getState();

    if (!session?.refreshToken || !session.accessToken) {
      setBootstrapping(false);
      return;
    }

    authApi
      .refresh(session.accessToken, session.refreshToken)
      .then((fresh) => setSession(fresh))
      .catch(() => clearSession())
      .finally(() => setBootstrapping(false));
  }, []);
}
