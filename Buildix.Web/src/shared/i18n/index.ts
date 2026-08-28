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
 * Til shu sahifada QO'L bilan tanlanganmi.
 *
 * <p>Faqat shu yuklanish davomida yashaydi va ataylab hech qayerga
 * saqlanmaydi: u «foydalanuvchi hozir aytdi» degan ma'noni bildiradi,
 * «bu kompyuterning tili» degan ma'noni emas. Do'konda bitta kassaga
 * bir necha xodim kiradi va biri tanlagan til boshqasiga o'tib
 * qolmasligi kerak.</p>
 */
let picked: AppLanguage | null = null;

/**
 * Foydalanuvchi tugma bosib tanlagan til.
 *
 * <p>Oddiy <c>changeLanguage</c> dan farqi shunda: bu chaqiruv tanlovni
 * ATAYLAB qilingan deb belgilaydi va uni hisobdagi qiymat bosib
 * ketmaydi.</p>
 */
export function chooseLanguage(lang: AppLanguage): void {
  picked = lang;
  void i18n.changeLanguage(lang);
}

/** Shu yuklanishda qo'lda tanlangan til; tanlanmagan bo'lsa <c>null</c>. */
export function pickedLanguage(): AppLanguage | null {
  return picked;
}

/**
 * Switch the UI to the language stored on the user's account (login response).
 * Called once per login so the choice follows the user to a new browser or
 * device — the LanguageDetector alone only remembers it in this browser's
 * localStorage. Unknown/absent codes leave the current language alone.
 *
 * <p><b>Qo'lda tanlangan til bosilmaydi.</b> Hisobdagi til hech qachon
 * bo'sh bo'lmaydi: <c>Language.Uzbek = 0</c>, ya'ni tilni umuman
 * tanlamagan hisob ham «o'zbek» bo'lib keladi va uni haqiqiy tanlovdan
 * ajratib bo'lmaydi. Shu sababli kirish oynasida rus tilini tanlagan
 * kassir «Kirish» bosishi bilan o'zbek tiliga qaytib tushardi — u
 * hech kim tanlamagan sukut qiymati edi.</p>
 */
export function applyUserLanguage(code: string | null | undefined): void {
  if (picked) return;
  const lang = toAppLanguage(code);
  if (lang && lang !== i18n.language) void i18n.changeLanguage(lang);
}

export default i18n;
