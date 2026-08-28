import { imageSize, printViaDesktop, toDataUrl } from './desktopPrint';

/**
 * Kassa chekini ANIQ rulon enida chop etadi.
 *
 * <p><b>Muammo.</b> Chek PDF bo'lib yaratilar va brauzerning chop etish
 * yo'liga uzatilardi. Do'kon dasturi (WebView2) PDF ni ichida bosa
 * olmaydi: chop etish zaxira yo'lga tushib <code>blob:</code> havolasini
 * tashqi dasturda ochishga urinardi va Windows «bu havolani ochadigan
 * dastur yo'q» deb chiqarardi. Chek printerga umuman yetib bormasdi.</p>
 *
 * <p>Yetib borgan holatda ham o'lcham noto'g'ri edi: chop etish oynasida
 * sukut bo'yicha A4 qog'oz va «sahifaga moslash» turadi. 80 mm chek
 * siqilib, har bir harf alohida qatorga tushar va chek yarim metrga
 * cho'zilardi.</p>
 *
 * <p><b>Yechim.</b> Chek serverda RASM qilib chiziladi (aynan o'sha
 * hujjatdan, ya'ni PDF bilan bir xil) va qobiq uni ko'rinmas oynada,
 * chek printeriga, aynan rulon enida bosadi. Masshtab qo'llanmaydi.</p>
 *
 * <p>Chek uzunligi tarkibga qarab o'sadi va uni oldindan bilib bo'lmaydi —
 * shuning uchun balandlik rasmning O'Z nisbatidan hisoblanadi.</p>
 */
export type ReceiptPrintOutcome =
  /** Qobiq chop etdi — oyna ochilmadi. */
  | 'printed'
  /** Qobiq yo'q yoki bosa olmadi — chaqiruvchi odatdagi yo'lga tushsin. */
  | 'unavailable';

export async function printReceiptImage(
  png: Blob,
  widthMm: number,
): Promise<ReceiptPrintOutcome> {
  const dataUrl = await toDataUrl(png);
  const size = await imageSize(dataUrl);
  if (!size || size.width === 0) return 'unavailable';

  // Balandlik rasm nisbatidan: chek qancha uzun bo'lsa, qog'oz shuncha
  // uziladi. Qat'iy balandlik qo'yilsa uzun chek qirqilib qolardi.
  const heightMm = (widthMm * size.height) / size.width;

  const ok = await printViaDesktop(buildReceiptHtml(dataUrl, widthMm, heightMm), widthMm, heightMm, 'receipt');
  return ok ? 'printed' : 'unavailable';
}

/**
 * Chop etiladigan hujjat — bitta rasm, aynan sahifa o'lchamida.
 */
function buildReceiptHtml(dataUrl: string, widthMm: number, heightMm: number): string {
  return `<!doctype html>
<html><head><meta charset="utf-8"><title>chek</title><style>
  /* Sahifa o'lchami AYNAN rulon eni va chek uzunligi. Brauzer shu qiymatni
     drayverga o'zi aytadi; chekka nol, ya'ni chek qirqilmaydi va
     siljimaydi. */
  @page { size: ${widthMm}mm ${heightMm}mm; margin: 0; }
  html, body { margin: 0; padding: 0; }
  /* Fon rasmlari sukut bo'yicha CHOP ETILMAYDI — brauzer ularni siyoh
     tejash uchun tashlab yuboradi. Chek esa aynan o'sha rasmning o'zi,
     ya'ni bu yerda u majburan yoqiladi. Busiz qog'ozdan BO'SH chek
     chiqardi. */
  img {
    display: block;
    width: ${widthMm}mm;
    height: ${heightMm}mm;
    -webkit-print-color-adjust: exact;
    print-color-adjust: exact;
  }
</style></head><body><img src="${dataUrl}" alt=""></body></html>`;
}
