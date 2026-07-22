import { useParams } from 'react-router-dom';
import { NAV_ITEMS } from '@/shared/config/navigation';
import { useAuth } from './useAuth';

/**
 * The first route the user is allowed to see — used as a safe post-login /
 * fallback landing. `account` is always accessible (no permission gate), so it
 * is the ultimate fallback and this never resolves to a gated page the user
 * can't open (which is what caused the redirect loop in review H-9).
 */
export function useFirstAccessiblePath(): string {
  const { subdomain } = useParams();
  const { hasPermission } = useAuth();
  const item = NAV_ITEMS.find((i) => !i.permission || hasPermission(i.permission));
  return `/${subdomain}/${item ? item.path : 'account'}`;
}
