import i18n from 'i18next';
import { initReactI18next } from 'react-i18next';
import LanguageDetector from 'i18next-browser-languagedetector';
import { ru } from './locales/ru';
import { uz } from './locales/uz';
import { en } from './locales/en';

export const SUPPORTED_LANGUAGES = ['ru', 'uz', 'en'] as const;
export type AppLanguage = (typeof SUPPORTED_LANGUAGES)[number];

export const LANGUAGE_LABELS: Record<AppLanguage, string> = {
  uz: 'UZ',
  ru: 'RU',
  en: 'EN',
};

void i18n
  .use(LanguageDetector)
  .use(initReactI18next)
  .init({
    resources: {
      ru: { translation: ru },
      uz: { translation: uz },
      en: { translation: en },
    },
    fallbackLng: 'ru',
    supportedLngs: SUPPORTED_LANGUAGES as unknown as string[],
    interpolation: { escapeValue: false },
    detection: {
      order: ['localStorage', 'navigator'],
      lookupLocalStorage: 'buildix.lang',
      caches: ['localStorage'],
    },
  });

/** Narrow an arbitrary string to a language we actually ship. */
export function toAppLanguage(code: string | null | undefined): AppLanguage | null {
  const lang = code?.trim().toLowerCase();
  return lang && (SUPPORTED_LANGUAGES as readonly string[]).includes(lang)
    ? (lang as AppLanguage)
    : null;
}

/**
 * Switch the UI to the language stored on the user's account (login response).
 * Called once per login so the choice follows the user to a new browser or
 * device — the LanguageDetector alone only remembers it in this browser's
 * localStorage. Unknown/absent codes leave the current language alone.
 */
export function applyUserLanguage(code: string | null | undefined): void {
  const lang = toAppLanguage(code);
  if (lang && lang !== i18n.language) void i18n.changeLanguage(lang);
}

export default i18n;
