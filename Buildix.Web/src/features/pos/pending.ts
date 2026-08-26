import type { PosSaleItem } from './api';

/**
 * Serverga hali yetib bormagan miqdorlar — tovar bo'yicha.
 *
 * <p>Kassada tezlik hal qiluvchi: mijoz turibdi, kassir esa ketma-ket
 * bosadi yoki skanerlaydi. Ekran har bosishda DARHOL yangilanishi kerak,
 * tarmoqni kutmasdan.</p>
 *
 * <p><b>Nega ro'yxat emas, xarita.</b> Avval har bosish uchun alohida qator
 * saqlanardi va javob kelganda o'sha qator olib tashlanardi. Uchta tez
 * bosishda javoblar TARTIBSIZ kelar va har javob o'z qatorini o'chirar edi —
 * ekranda tovar goh yo'qolib, goh birdan uchta bo'lib ko'rinardi. Xaritada
 * esa faqat SON turadi: server tasdiqlagan miqdor ayiriladi, qolgani
 * joyida qoladi.</p>
 */
export type PendingLine = {
  productId: string;
  productName: string;
  salePrice: number;
  minSalePrice: number;
  unit: string;
  unitValue: number;
};

export type PendingMap = Record<string, { quantity: number; line: PendingLine }>;

/** Tovar sonini oshiradi (yoki yangi yozuv qo'shadi). */
export function bumpPending(map: PendingMap, line: PendingLine, by: number): PendingMap {
  const current = map[line.productId];
  return {
    ...map,
    [line.productId]: { line, quantity: (current?.quantity ?? 0) + by },
  };
}

/**
 * Server tasdiqlagan miqdorlarni ayiradi.
 *
 * <p>Butun xarita tozalanmaydi: tasdiq kutilayotgan paytda kassir yana
 * bosgan bo'lishi mumkin va o'sha yangi bosish YO'QOLMASLIGI kerak.</p>
 */
export function settlePending(map: PendingMap, confirmed: Record<string, number>): PendingMap {
  const out: PendingMap = {};
  for (const [productId, entry] of Object.entries(map)) {
    const left = entry.quantity - (confirmed[productId] ?? 0);
    if (left > 0) out[productId] = { ...entry, quantity: left };
  }
  return out;
}

/**
 * Server qatorlariga tasdiqlanmagan miqdorlarni qo'shadi.
 *
 * <p>Bir xil tovar bo'lsa miqdor qo'shiladi — server ham aynan shunday
 * birlashtiradi, ya'ni tasdiqlangach ro'yxat sakramaydi.</p>
 */
export function mergePending(serverItems: PosSaleItem[], pending: PendingMap): PosSaleItem[] {
  const entries = Object.values(pending);
  if (entries.length === 0) return serverItems;

  const out = serverItems.map((it) => ({ ...it }));
  for (const { line, quantity } of entries) {
    const existing = out.find((it) => it.productId === line.productId && !it.isExternal);
    if (existing) {
      existing.quantity += quantity;
      existing.totalPrice = existing.quantity * existing.salePrice;
      continue;
    }
    out.push({
      id: `pending-${line.productId}`,
      saleId: '',
      productId: line.productId,
      productName: line.productName,
      quantity,
      costPrice: 0,
      salePrice: line.salePrice,
      totalPrice: quantity * line.salePrice,
      profit: 0,
      unit: line.unit,
      unitValue: line.unitValue,
      comment: null,
      isExternal: false,
    });
  }
  return out;
}
