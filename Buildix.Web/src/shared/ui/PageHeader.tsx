import type { ReactNode } from 'react';

/** White top bar shared by every module page (title + subtitle + actions). */
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
      {actions && <div className="flex items-center gap-3.5">{actions}</div>}
    </header>
  );
}
