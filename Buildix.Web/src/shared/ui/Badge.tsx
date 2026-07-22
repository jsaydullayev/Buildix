import type { HTMLAttributes } from 'react';
import { cn } from '@/shared/lib/cn';

type Tone = 'success' | 'info' | 'warn' | 'danger' | 'neutral';

const TONES: Record<Tone, string> = {
  success: 'bg-success-soft text-success-text',
  info: 'bg-primary-soft text-primary-hover',
  warn: 'bg-warn-soft text-warn-text',
  danger: 'bg-danger-soft text-danger',
  neutral: 'bg-hairline text-muted',
};

export interface BadgeProps extends HTMLAttributes<HTMLSpanElement> {
  tone?: Tone;
}

export function Badge({ tone = 'neutral', className, ...props }: BadgeProps) {
  return (
    <span
      className={cn(
        'inline-flex items-center rounded-pill px-2.5 py-[3px] text-[11.5px] font-semibold',
        TONES[tone],
        className,
      )}
      {...props}
    />
  );
}
