import { useTranslation } from 'react-i18next';
import { cn } from '@/shared/lib/cn';
import { SUPPORTED_LANGUAGES, LANGUAGE_LABELS, chooseLanguage, type AppLanguage } from '@/shared/i18n';

/** UZ / RU / EN pill switcher (matches the login/landing design). */
export function LanguageSwitch({
  className,
  onChange,
}: {
  className?: string;
  /**
   * Called after the UI has switched — used by Settings to persist the choice
   * on the user's account. Pre-auth screens (login/landing) leave it off and
   * rely on the browser-local LanguageDetector cache.
   */
  onChange?: (lang: AppLanguage) => void;
}) {
  const { i18n } = useTranslation();
  const current = (SUPPORTED_LANGUAGES as readonly string[]).includes(i18n.language)
    ? (i18n.language as AppLanguage)
    : 'ru';

  return (
    <div className={cn('inline-flex rounded-pill bg-hairline p-1', className)}>
      {SUPPORTED_LANGUAGES.map((lang) => {
        const active = lang === current;
        return (
          <button
            key={lang}
            type="button"
            onClick={() => {
              if (active) return;
              // `chooseLanguage`, oddiy `changeLanguage` emas: tanlov
              // ATAYLAB qilingan deb belgilanadi va kirishdan keyin
              // hisobdagi sukut qiymati uni bosib ketmaydi.
              chooseLanguage(lang);
              onChange?.(lang);
            }}
            className={cn(
              'rounded-pill px-4 py-[7px] text-[14px] transition-colors',
              active
                ? 'bg-surface font-semibold text-text shadow-card'
                : 'font-medium text-muted hover:text-text',
            )}
          >
            {LANGUAGE_LABELS[lang]}
          </button>
        );
      })}
    </div>
  );
}
