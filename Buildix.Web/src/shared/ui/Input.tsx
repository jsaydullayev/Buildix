import { forwardRef, type InputHTMLAttributes, useId, useState } from 'react';
import { Eye, EyeOff } from 'lucide-react';
import { useTranslation } from 'react-i18next';
import { cn } from '@/shared/lib/cn';

export interface InputProps extends InputHTMLAttributes<HTMLInputElement> {
  label?: string;
  error?: string;
  /** Optional slot rendered on the label row's right side (e.g. "Forgot?"). */
  labelAddon?: React.ReactNode;
}

export const Input = forwardRef<HTMLInputElement, InputProps>(function Input(
  { label, error, labelAddon, className, id, type, ...props },
  ref,
) {
  const autoId = useId();
  const inputId = id ?? autoId;
  const { t } = useTranslation();

  // Parol maydonida ko'z belgisi. Uzun yoki tasodifiy parolni (masalan,
  // SuperAdmin bergan «7fKm2Qx9») terganda odam nima yozganini ko'rmaydi va
  // «Login yoki parol noto'g'ri» xabaridan keyin ham xatosini topa olmaydi.
  // Shu sababli toggle butun ilovaga umumiy — har bir parol maydonida bor.
  const isPassword = type === 'password';
  const [revealed, setRevealed] = useState(false);
  const effectiveType = isPassword && revealed ? 'text' : type;

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
      <div className="relative flex">
        <input
          ref={ref}
          id={inputId}
          type={effectiveType}
          aria-invalid={!!error}
          className={cn(
            'h-14 w-full rounded-input border-[1.5px] border-input-border bg-surface px-[18px] text-[16px] text-text',
            'placeholder:text-muted-2 outline-none transition-shadow',
            'focus:border-primary focus:shadow-focus-ring',
            // Ko'z tugmasi matn ustiga tushmasin.
            isPassword && 'pr-[52px]',
            error && 'border-danger',
            className,
          )}
          {...props}
        />
        {isPassword && (
          <button
            type="button"
            // Faqat sichqoncha uchun: Tab bilan yurganda tugma parol va
            // «Kirish» orasiga suqilib kirmasligi kerak.
            tabIndex={-1}
            aria-label={revealed ? t('common.hidePassword') : t('common.showPassword')}
            title={revealed ? t('common.hidePassword') : t('common.showPassword')}
            aria-pressed={revealed}
            onClick={() => setRevealed((v) => !v)}
            className="absolute right-0 top-0 flex h-14 w-[52px] items-center justify-center text-muted-2 transition-colors hover:text-primary"
          >
            {revealed ? <EyeOff size={18} /> : <Eye size={18} />}
          </button>
        )}
      </div>
      {error && <span className="text-[12.5px] text-danger">{error}</span>}
    </div>
  );
});
