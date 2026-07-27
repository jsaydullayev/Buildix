import { forwardRef, type InputHTMLAttributes, useState } from 'react';
import { Eye, EyeOff } from 'lucide-react';
import { useTranslation } from 'react-i18next';
import { cn } from '@/shared/lib/cn';

/**
 * Ko'z belgisi bilan parol maydoni — sahifaning O'Z uslubidagi xom
 * <code>&lt;input&gt;</code> ustiga.
 *
 * <p>Nima uchun alohida komponent: <see cref="Input"/> ning tayyor
 * ko'rinishi (h-14, 16px) modallardagi ixchamroq maydonlarga to'g'ri kelmaydi,
 * shuning uchun u yerlarda xom input ishlatiladi. Ko'z belgisi esa har bir
 * parol maydonida bo'lishi kerak — aks holda odam bir joyda parolini ko'ra
 * oladi, boshqasida yo'q.</p>
 */
export const PasswordInput = forwardRef<HTMLInputElement, InputHTMLAttributes<HTMLInputElement>>(
  function PasswordInput({ className, ...props }, ref) {
    const { t } = useTranslation();
    const [revealed, setRevealed] = useState(false);

    return (
      <div className="relative flex">
        <input
          ref={ref}
          type={revealed ? 'text' : 'password'}
          className={cn('w-full pr-11', className)}
          {...props}
        />
        <button
          type="button"
          // Tab tartibidan chetda: u parol bilan keyingi maydon orasiga
          // suqilib kirmasligi kerak.
          tabIndex={-1}
          aria-label={revealed ? t('common.hidePassword') : t('common.showPassword')}
          title={revealed ? t('common.hidePassword') : t('common.showPassword')}
          aria-pressed={revealed}
          onClick={() => setRevealed((v) => !v)}
          className="absolute right-0 top-0 flex h-full w-11 items-center justify-center text-muted-2 transition-colors hover:text-primary"
        >
          {revealed ? <EyeOff size={16} /> : <Eye size={16} />}
        </button>
      </div>
    );
  },
);
