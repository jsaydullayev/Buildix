import type { PosSaleItem } from './api';

/**
 * Serverga yuborilgan, lekin javobi hali kelmagan savat qatori.
 *
 * <p>Kassada tezlik hal qiluvchi: mijoz turibdi, kassir esa ketma-ket
 * skanerlaydi. Optimistik qator bo'lmasa har «bip» dan keyin uchta so'rov
 * ketma-ket ketardi (kodni izlash → qatorni qo'shish → chekni qayta o'qish)
 * va tovar ekranda faqat uchalasi tugagach ko'rinardi.</p>
 *
 * <p>Nega alohida ro'yxat, so'rov keshini yamash emas: chekning birinchi
 * tovarida <c>saleId</c> hali yo'q (qoralama aynan shu paytda yaratiladi),
 * ya'ni yamash uchun kalit yo'q. Bu ro'yxat esa saleId dan mustaqil.</p>
 */
export type PendingLine = {
  key: string;
  productId: string;
  productName: string;
  quantity: number;
  salePrice: number;
  unit: string;
  unitValue: number;
};

/**
 * Server qatorlariga tasdiqlanmaganlarini qo'shadi.
 *
 * <p>Bir xil tovar bo'lsa miqdor qo'shiladi — server ham aynan shunday
 * birlashtiradi, ya'ni tasdiqlangach ro'yxat sakramaydi va kassir «ikkita
 * qator chiqdimi?» deb ikkilanmaydi.</p>
 */
export function mergePending(serverItems: PosSaleItem[], pending: PendingLine[]): PosSaleItem[] {
  if (pending.length === 0) return serverItems;

  const out = serverItems.map((it) => ({ ...it }));
  for (const p of pending) {
    const line = out.find((it) => it.productId === p.productId && !it.isExternal);
    if (line) {
      line.quantity += p.quantity;
      line.totalPrice = line.quantity * line.salePrice;
      continue;
    }
    out.push({
      id: p.key,
      saleId: '',
      productId: p.productId,
      productName: p.productName,
      quantity: p.quantity,
      costPrice: 0,
      salePrice: p.salePrice,
      totalPrice: p.quantity * p.salePrice,
      profit: 0,
      unit: p.unit,
      unitValue: p.unitValue,
      comment: null,
      isExternal: false,
    });
  }
  return out;
}
