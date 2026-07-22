import type { ReactNode } from 'react';
import { cn } from '@/shared/lib/cn';

type Tone = 'default' | 'warn' | 'danger' | 'primary';

const TONES: Record<Tone, string> = {
  default: 'border-border bg-surface',
  warn: 'border-warn/25 bg-warn-soft',
  danger: 'border-danger/25 bg-danger-soft',
  primary: 'border-primary/25 bg-primary-soft',
};

/** Compact KPI card used across dashboard/warehouse/purchases headers. */
export function StatCard({
  label,
  value,
  suffix,
  hint,
  tone = 'default',
  className,
}: {
  label: string;
  value: ReactNode;
  suffix?: string;
  hint?: ReactNode;
  tone?: Tone;
  className?: string;
}) {
  return (
    <div className={cn('rounded-card border px-[22px] py-5', TONES[tone], className)}>
      <div className="text-[12.5px] text-muted">{label}</div>
      <div className="mt-2 text-[23px] font-bold leading-none tracking-[-0.3px] nums">
        {value}
        {suffix && <span className="ml-1 text-[13px] font-medium text-muted-2">{suffix}</span>}
      </div>
      {hint && <div className="mt-2 text-[12.5px] text-muted-2">{hint}</div>}
    </div>
  );
}
