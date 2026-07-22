import type { ReactNode } from 'react';
import { Navigate, useParams, useNavigate } from 'react-router-dom';
import { useTranslation } from 'react-i18next';
import { Lock } from 'lucide-react';
import { FullscreenLoader } from '@/shared/ui/FullscreenLoader';
import { Button } from '@/shared/ui';
import { AccessBlockedScreen } from '@/features/auth/AccessBlockedScreen';
import { useAuth } from './useAuth';
import { useFirstAccessiblePath } from './useFirstAccessiblePath';

/** Gate a subtree behind an authenticated session (with silent-login wait). */
export function RequireAuth({ children }: { children: ReactNode }) {
  const { subdomain } = useParams();
  const { isAuthenticated, bootstrapping } = useAuth();

  if (bootstrapping) return <FullscreenLoader />;
  if (!isAuthenticated) {
    const loginPath = subdomain ? `/${subdomain}/login` : '/';
    return <Navigate to={loginPath} replace />;
  }
  return <>{children}</>;
}

/** Surface the subscription/blocked screens when the API reports 402 / 423. */
export function RequireSubscription({ children }: { children: ReactNode }) {
  const { accessBlock } = useAuth();
  if (accessBlock) return <AccessBlockedScreen block={accessBlock} />;
  return <>{children}</>;
}

/**
 * A user with an authenticated session but no permission for this route. We
 * RENDER a message rather than <Navigate> — navigating to another gated route
 * (e.g. dashboard) could bounce straight back here and spin the router (H-9).
 */
function NoAccess() {
  const { t } = useTranslation();
  const navigate = useNavigate();
  const home = useFirstAccessiblePath();
  return (
    <div className="flex min-h-screen flex-1 flex-col items-center justify-center gap-4 bg-bg px-6 text-center">
      <div className="flex h-16 w-16 items-center justify-center rounded-2xl bg-hairline text-muted-2">
        <Lock size={28} />
      </div>
      <p className="max-w-sm text-[15px] text-muted">{t('common.noAccess')}</p>
      <Button variant="secondary" onClick={() => navigate(home, { replace: true })}>
        {t('common.back')}
      </Button>
    </div>
  );
}

/** Gate a route behind a permission key. */
export function RequirePermission({
  permission,
  children,
}: {
  permission: string;
  children: ReactNode;
}) {
  const { hasPermission } = useAuth();
  if (!hasPermission(permission)) return <NoAccess />;
  return <>{children}</>;
}

/** Gate a route behind one or more roles. */
export function RequireRole({ roles, children }: { roles: string[]; children: ReactNode }) {
  const { hasRole } = useAuth();
  if (!hasRole(...roles)) return <NoAccess />;
  return <>{children}</>;
}

/** Index landing under "/:subdomain" — the first section the user may open. */
export function IndexRedirect() {
  const home = useFirstAccessiblePath();
  return <Navigate to={home} replace />;
}
