import type { QueryClient } from '@tanstack/react-query';

/**
 * Chek BEKOR QILINGANDA yoki QAYTARILGANDA eskiradigan keshlar.
 *
 * <p>Ikkala amal ham chekka manfiy to'lov yozadi, `PaidAmount` ni siljitadi,
 * naqd bo'lsa kassa qoldig'ini o'zgartiradi va qarzli bo'lsa qarzni yopadi.
 * Ya'ni ular sotuvlar ro'yxatidan ancha ko'proq joyga tegadi.</p>
 *
 * <p>Ro'yxatga <code>sale-detail</code> va <code>shift-current</code> SHART:
 * kartochka yopilib qayta ochilganda react-query 30 soniya davomida eski
 * nusxani ko'rsatar (chek hamon «Paid» bo'lib turardi), smena paneli esa
 * bekor qilingan chekning pulini kassada sanab turaverardi.</p>
 *
 * <p>Ro'yxat ATAYLAB aniq: <code>invalidateQueries()</code> ni argumentsiz
 * chaqirish butun keshni, jumladan katalogni ham, qayta tortardi.</p>
 */
const SALE_LIST_KEYS = [
  // Ro'yxatlar va panel
  'sales',
  'seller-sales',
  'today-sales',
  'dash-today',
  'dash-daily-sales',
  // Kartochkaning O'ZI
  'sale-detail',
  // Pul: smena va kassa jurnali
  'shift-current',
  'cash-ledger',
  // Qarz ekranlari (qarzli chek bekor qilinsa qarz yopiladi)
  'debt-checks',
  'debtors',
  'debt-summary',
  // Qaytarishlar — admin va sotuvchi qobiqlarida kalitlar boshqa-boshqa
  'returns',
  'returns-summary',
  'seller-returns',
  'seller-returns-summary',
];

export function invalidateSaleLists(qc: QueryClient) {
  void qc.invalidateQueries({
    predicate: (q) => SALE_LIST_KEYS.includes(q.queryKey[0] as string),
  });
}
