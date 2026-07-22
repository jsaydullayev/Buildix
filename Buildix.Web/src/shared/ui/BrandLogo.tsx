import { cn } from '@/shared/lib/cn';

/** The Buildix mark: blue rounded square "B" + wordmark (Unbounded). */
export function BrandLogo({
  size = 'md',
  onDark = false,
  className,
}: {
  size?: 'sm' | 'md';
  onDark?: boolean;
  className?: string;
}) {
  const box = size === 'sm' ? 'h-[30px] w-[30px] text-[14px]' : 'h-8 w-8 text-[15px]';
  const word = size === 'sm' ? 'text-[16px]' : 'text-[18px]';
  return (
    <span className={cn('inline-flex items-center gap-[11px]', className)}>
      <span
        className={cn(
          'flex items-center justify-center rounded-lg bg-primary font-brand font-bold text-white',
          box,
        )}
      >
        B
      </span>
      <span
        className={cn(
          'font-brand font-semibold tracking-[0.4px]',
          word,
          onDark ? 'text-white' : 'text-text',
        )}
      >
        Buildix
      </span>
    </span>
  );
}
