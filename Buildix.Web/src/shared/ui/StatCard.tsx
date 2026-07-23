import type { ReactNode } from 'react';
import { cn } from '@/shared/lib/cn';

type Tone = 'default' | 'warn' | 'danger' | 'primary';

const TONES: Record<Tone, string> = {
  default: 'border-border bg-surface',
  warn: 'border-warn/25 bg-warn-soft',
  danger: 'border-danger/25 bg-danger-soft',
  primary: 'border-primary/25 bg-primary-soft',
};

/** Tint of the 32×32 icon chip in the card's top-right corner. */
const ICON_TONES: Record<Tone, string> = {
  default: 'bg-primary-soft text-primary',
  warn: 'bg-warn-soft text-warn-strong',
  danger: 'bg-danger-soft text-danger',
  primary: 'bg-primary-soft text-primary',
};

/** Compact KPI card used across dashboard/warehouse/purchases headers. */
export function StatCard({
  label,
  value,
  suffix,
  hint,
  icon,
  tone = 'default',
  className,
}: {
  label: string;
  value: ReactNode;
  suffix?: string;
  hint?: ReactNode;
  /** Optional glyph shown in a tinted chip beside the label (design: 32×32). */
  icon?: ReactNode;
  tone?: Tone;
  className?: string;
}) {
  return (
    <div className={cn('rounded-card border px-[22px] py-5', TONES[tone], className)}>
      <div className="flex items-center justify-between gap-3">
        <div className="text-[12.5px] text-muted">{label}</div>
        {icon && (
          <div className={cn('flex h-8 w-8 flex-none items-center justify-center rounded-lg', ICON_TONES[tone])}>
            {icon}
          </div>
        )}
      </div>
      <div className={cn('text-[23px] font-bold leading-none tracking-[-0.3px] nums', icon ? 'mt-3' : 'mt-2')}>
        {value}
        {suffix && <span className="ml-1 text-[13px] font-medium text-muted-2">{suffix}</span>}
      </div>
      {hint && <div className="mt-2 text-[12.5px] text-muted-2">{hint}</div>}
    </div>
  );
}
