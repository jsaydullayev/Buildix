import { formatMoneyInput } from '@/shared/lib/format';

/**
 * Aralash ("Miks") to'lovning umumiy mantig'i — admin POS va kassir kassasi
 * SHU YERDAN oladi.
 *
 * Ilgari admin uchta usulni (naqd/karta/o'tkazma) qo'llab-quvvatlardi, kassir
 * esa ikkitasini (naqd + qolgani kartaga) — o'sha ikki nusxa jimgina bir-biridan
 * uzoqlashib ketgan edi va kassir o'tkazma aralashgan chekni umuman yopa
 * olmasdi. Bitta manba ularni yana ajralib ketishdan saqlaydi.
 */

/** Uch ulush — o'nlik satr sifatida (kasr mumkin: "3002.5"). */
export type MixParts = { cash: string; card: string; transfer: string };

export const EMPTY_MIX: MixParts = { cash: '', card: '', transfer: '' };

export const MIX_ROWS = [
  { key: 'cash', type: 'Cash' },
  { key: 'card', type: 'Terminal' },
  { key: 'transfer', type: 'Transfer' },
] as const;

/**
 * Miks maydoni uchun kiritishni tozalash: raqamlar + bitta o'nlik nuqta.
 * Jami summa kasr bo'lishi mumkin (2.5 kg × 1 201 = 3 002.5) — faqat butun
 * son qabul qilinsa, bunday chekni Miks bilan yopib bo'lmay qolardi.
 */
export function parseMixInput(raw: string): string {
  const cleaned = raw.replace(/[^\d.,]/g, '').replace(/,/g, '.');
  const i = cleaned.indexOf('.');
  const single = i === -1 ? cleaned : cleaned.slice(0, i + 1) + cleaned.slice(i + 1).replace(/\./g, '');
  return single.replace(/^0+(?=\d)/, '');
}

/** Butun qism guruhlanadi («12 600»), kasr qismi o'z holicha qoladi. */
export function formatMixInput(v: string): string {
  const dot = v.indexOf('.');
  if (dot === -1) return formatMoneyInput(v);
  return `${formatMoneyInput(v.slice(0, dot))}.${v.slice(dot + 1)}`;
}

/** Pul yig'indilarini tiyingacha yaxlitlab solishtirish (float xatosiz). */
export const money = (n: number) => Math.round(n * 100) / 100;

/** Kiritilgan ulushlar yig'indisi. */
export const mixSumOf = (parts: MixParts) =>
  money(MIX_ROWS.reduce((sum, r) => sum + (Number(parts[r.key]) || 0), 0));

/**
 * Serverga yuboriladigan to'lovlar ro'yxati. Nolga teng ulushlar tashlanadi —
 * server `amount > 0` talab qiladi.
 */
export const mixPayments = (parts: MixParts) =>
  MIX_ROWS.map((r) => ({ paymentType: r.type as string, amount: Number(parts[r.key]) || 0 })).filter(
    (x) => x.amount > 0,
  );
