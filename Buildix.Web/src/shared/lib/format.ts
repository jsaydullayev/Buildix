import { format, formatDistanceToNowStrict, parseISO } from 'date-fns';
import { ru, uz, enUS } from 'date-fns/locale';
import type { Locale } from 'date-fns';

const DATE_FNS_LOCALES: Record<string, Locale> = {
  ru,
  uz,
  en: enUS,
};

function resolveLocale(lang: string): Locale {
  return DATE_FNS_LOCALES[lang] ?? ru;
}

// Intl grouping for ru-RU uses a narrow no-break space (U+202F) / NBSP (U+00A0).
// Normalise to a plain space so it matches the mockups and copy-pastes cleanly.
const GROUPING_SPACES = /\s/g;

function groupNumber(n: number, maximumFractionDigits: number): string {
  return new Intl.NumberFormat('ru-RU', { maximumFractionDigits })
    .format(n)
    .replace(GROUPING_SPACES, ' ');
}

/**
 * Money formatting to match the design: space-separated thousands, no decimals.
 * e.g. 318400000 -> "318 400 000". Currency word ("сум") is added by the caller.
 */
export function formatSum(value: number | string | null | undefined): string {
  if (value === null || value === undefined || value === '') return '—';
  const n = typeof value === 'string' ? Number(value) : value;
  if (Number.isNaN(n)) return '—';
  return groupNumber(n, 0);
}

/** A decimal quantity (e.g. 3.2 т) — up to 3 fraction digits, trimmed. */
export function formatQty(value: number | string | null | undefined): string {
  if (value === null || value === undefined || value === '') return '—';
  const n = typeof value === 'string' ? Number(value) : value;
  if (Number.isNaN(n)) return '—';
  return groupNumber(n, 3);
}

function toDate(value: string | Date): Date {
  return typeof value === 'string' ? parseISO(value) : value;
}

/** Full date like "Суббота, 19 июля 2026" (design header). */
export function formatFullDate(value: string | Date, lang = 'ru'): string {
  return format(toDate(value), 'EEEE, d MMMM yyyy', { locale: resolveLocale(lang) });
}

/** Short date like "19 июля". */
export function formatShortDate(value: string | Date, lang = 'ru'): string {
  return format(toDate(value), 'd MMMM', { locale: resolveLocale(lang) });
}

/** Time like "14:32". */
export function formatTime(value: string | Date): string {
  return format(toDate(value), 'HH:mm');
}

/** Relative time like "2 дня", "сейчас". */
export function formatRelative(value: string | Date, lang = 'ru'): string {
  return formatDistanceToNowStrict(toDate(value), { locale: resolveLocale(lang), addSuffix: false });
}
