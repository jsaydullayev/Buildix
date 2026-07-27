import { cn } from '@/shared/lib/cn';

/**
 * The Buildix mark — two rounded squares + an amber dot, forming a "B".
 * 1:1 with docs/brand/buildix-mark-*.svg; on dark surfaces the squares go white.
 */
export function BrandMark({ onDark = false, className }: { onDark?: boolean; className?: string }) {
  return (
    <svg
      viewBox="0 0 56 56"
      className={cn('h-8 w-8', className)}
      role="img"
      aria-label="Buildix"
      focusable="false"
    >
      <rect x="0" y="0" width="26" height="26" rx="7" fill={onDark ? '#FFFFFF' : '#2563EB'} />
      <rect x="0" y="30" width="26" height="26" rx="7" fill={onDark ? '#FFFFFF' : '#2563EB'} />
      <rect x="30" y="30" width="26" height="26" rx="13" fill="#F5A623" />
    </svg>
  );
}

/** The Buildix mark + wordmark (Unbounded) — matches docs/brand/buildix-logo-horizontal-*.svg. */
export function BrandLogo({
  size = 'md',
  onDark = false,
  className,
}: {
  size?: 'sm' | 'md';
  onDark?: boolean;
  className?: string;
}) {
  const mark = size === 'sm' ? 'h-[30px] w-[30px]' : 'h-8 w-8';
  const word = size === 'sm' ? 'text-[16px]' : 'text-[18px]';
  return (
    <span className={cn('inline-flex items-center gap-[11px]', className)}>
      <BrandMark onDark={onDark} className={mark} />
      <span
        className={cn(
          'font-brand font-bold tracking-[0.4px]',
          word,
          onDark ? 'text-white' : 'text-text',
        )}
        aria-hidden="true"
      >
        Buildix
      </span>
    </span>
  );
}
