import type { ReactNode } from 'react';
import { NotificationBell } from '@/features/notifications/NotificationBell';

/**
 * White top bar shared by every module page (title + subtitle + actions).
 *
 * The notification bell lives here rather than in the sidebar — that is where the
 * design puts it, and it is the only element that must appear on every page
 * without each page passing it in. It hides itself where it does not belong
 * (SuperAdmin console, seller shell, no permission).
 */
export function PageHeader({
  title,
  subtitle,
  actions,
}: {
  title: string;
  subtitle?: string;
  actions?: ReactNode;
}) {
  return (
    <header className="flex items-center justify-between border-b border-border bg-surface px-8 py-5">
      <div>
        <h1 className="text-[20px] font-semibold tracking-[-0.2px]">{title}</h1>
        {subtitle && <p className="mt-0.5 text-[12.5px] text-muted-2">{subtitle}</p>}
      </div>
      <div className="flex items-center gap-3.5">
        <NotificationBell />
        {actions}
      </div>
    </header>
  );
}
