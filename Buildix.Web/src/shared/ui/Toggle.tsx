import { cn } from '@/shared/lib/cn';

/** iOS-style switch matching the Settings design. */
export function Toggle({
  checked,
  onChange,
  disabled,
}: {
  checked: boolean;
  onChange: (v: boolean) => void;
  disabled?: boolean;
}) {
  return (
    <button
      type="button"
      role="switch"
      aria-checked={checked}
      disabled={disabled}
      onClick={() => onChange(!checked)}
      className={cn(
        'relative h-[26px] w-[46px] flex-none rounded-pill transition-colors',
        checked ? 'bg-primary' : 'bg-input-border',
        disabled && 'opacity-50',
      )}
    >
      <span
        className={cn(
          'absolute top-[3px] h-5 w-5 rounded-full bg-white shadow transition-all',
          checked ? 'left-[23px]' : 'left-[3px]',
        )}
      />
    </button>
  );
}
