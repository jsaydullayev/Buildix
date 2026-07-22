import { forwardRef, type InputHTMLAttributes, useId } from 'react';
import { cn } from '@/shared/lib/cn';

export interface InputProps extends InputHTMLAttributes<HTMLInputElement> {
  label?: string;
  error?: string;
  /** Optional slot rendered on the label row's right side (e.g. "Forgot?"). */
  labelAddon?: React.ReactNode;
}

export const Input = forwardRef<HTMLInputElement, InputProps>(function Input(
  { label, error, labelAddon, className, id, ...props },
  ref,
) {
  const autoId = useId();
  const inputId = id ?? autoId;

  return (
    <div className="flex flex-col gap-1.5">
      {(label || labelAddon) && (
        <div className="flex items-baseline justify-between">
          {label && (
            <label htmlFor={inputId} className="text-[14.5px] font-medium text-label">
              {label}
            </label>
          )}
          {labelAddon}
        </div>
      )}
      <input
        ref={ref}
        id={inputId}
        aria-invalid={!!error}
        className={cn(
          'h-14 rounded-input border-[1.5px] border-input-border bg-surface px-[18px] text-[16px] text-text',
          'placeholder:text-muted-2 outline-none transition-shadow',
          'focus:border-primary focus:shadow-focus-ring',
          error && 'border-danger',
          className,
        )}
        {...props}
      />
      {error && <span className="text-[12.5px] text-danger">{error}</span>}
    </div>
  );
});
