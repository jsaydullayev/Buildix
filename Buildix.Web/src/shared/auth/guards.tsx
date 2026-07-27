import type { ReactNode } from 'react';
import { Navigate, useParams, useNavigate } from 'react-router-dom';
import { useQuery } from '@tanstack/react-query';
import { useTranslation } from 'react-i18next';
import { Lock } from 'lucide-react';
import { FullscreenLoader } from '@/shared/ui/FullscreenLoader';
import { Button } from '@/shared/ui';
import { AccessBlockedScreen } from '@/features/auth/AccessBlockedScreen';
import { consoleApi } from '@/shared/api/auth';
import { ROLES } from '@/shared/config/permissions';
import { useAuth } from './useAuth';
import { useFirstAccessiblePath } from './useFirstAccessiblePath';

/** Gate a subtree behind an authenticated session (with silent-login wait). */
export function RequireAuth({ children }: { children: ReactNode }) {
  const { subdomain, segment } = useParams();
  const { isAuthenticated, bootstrapping } = useAuth();

  if (bootstrapping) return <FullscreenLoader />;
  if (!isAuthenticated) {
    // SuperAdmin konsoli (`/_sa/:segment/...`) o'z login sahifasiga qaytadi —
    // landingga tashlansa, operator yashirin segmentni qaytadan yozishga
    // majbur bo'lardi.
    const loginPath = segment
      ? `/_sa/${segment}/login`
      : subdomain
        ? `/${subdomain}/login`
        : '/';
    return <Navigate to={loginPath} replace />;
  }
  return <>{children}</>;
}

/**
 * Do'kon qobig'ini (`/{sub-path}/...`) SESSIYAGA moslashtiradi.
 *
 * <p>Ikki nomuvofiqlikni to'xtatadi:</p>
 * <ol>
 *   <li><b>SuperAdmin do'kon manzilida.</b> Uning tokenida <code>MarketId</code>
 *   yo'q, shuning uchun backend har bir do'kon endpointiga 401 qaytaradi.
 *   Ilgari qobiq baribir ochilib, hamma joyda nol va «topilmadi» ko'rinardi —
 *   go'yo do'konda savdo yo'qdek. Endi u konsolga qaytariladi.</li>
 *   <li><b>Xodim BOSHQA do'konning manzilida.</b> Ijara faqat tokendan
 *   aniqlanadi, ya'ni u o'z ma'lumotini ko'radi, lekin manzilda begona do'kon
 *   turadi — ekrandagi narsa qaysi do'konniki ekani yolg'on bo'lib qoladi.
 *   O'z sub-path'iga qaytariladi.</li>
 * </ol>
 */
export function RequireTenant({ children }: { children: ReactNode }) {
  const { subdomain } = useParams();
  const { session } = useAuth();
  const isSuperAdmin = session?.role === ROLES.SuperAdmin;

  // Segment sessiyada saqlanmaydi (u sir) — serverdan so'raladi.
  const consoleQuery = useQuery({
    queryKey: ['console-segment'],
    queryFn: consoleApi.segment,
    enabled: isSuperAdmin,
    staleTime: Infinity,
    retry: false,
  });

  if (isSuperAdmin) {
    if (consoleQuery.isPending) return <FullscreenLoader />;
    // Segment sozlanmagan bo'lsa konsol yo'q — kirish sahifasi yagona
    // mantiqiy joy (u yerda tushunarli xato ko'rsatiladi).
    return consoleQuery.data ? (
      <Navigate to={`/_sa/${consoleQuery.data}/dashboard`} replace />
    ) : (
      <Navigate to="/login" replace />
    );
  }

  if (session?.subdomain && subdomain && session.subdomain !== subdomain) {
    return <Navigate to={`/${session.subdomain}`} replace />;
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
