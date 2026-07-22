import { cn } from '@/shared/lib/cn';

export function Spinner({ className, size = 18 }: { className?: string; size?: number }) {
  return (
    <span
      role="status"
      aria-label="loading"
      className={cn('inline-block animate-spin rounded-full border-2 border-current border-t-transparent', className)}
      style={{ width: size, height: size }}
    />
  );
}
