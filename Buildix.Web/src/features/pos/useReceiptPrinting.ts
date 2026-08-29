import { useEffect, useRef, useState } from 'react';
import { useQuery } from '@tanstack/react-query';
import { useTranslation } from 'react-i18next';
import { printPdfBlob } from '@/shared/lib/printPdf';
import { desktopBridge, printRawViaDesktop, toBase64 } from '@/shared/lib/desktopPrint';
import { printReceiptImage } from '@/shared/lib/printReceipt';
import { posApi, type PosSale } from './api';

/**
 * Chekni chop etadi — ikkala kassa (Owner/Admin va sotuvchi) uchun bitta
 * joyda.
 *
 * <p><b>Nega alohida.</b> Ikkala sahifa ham bir xil chekni bosadi va
 * mantiq ular ichida IKKI NUSXA yotardi: chek eni so'rovi, yo'llar
 * tartibi, xatoni ushlash, avtomatik bosish. Chop etishdagi har bir
 * tuzatishni ikki joyda takrorlash kerak bo'lardi va vaqt o'tib nusxalar
 * bir-biridan uzoqlashardi — chek esa qaysi kassadan bosilganiga qarab
 * farq qilmasligi kerak.</p>
 *
 * <h4>Yo'llar — eng tezidan boshlab</h4>
 * <ol>
 *   <li><b>ESC/POS</b>: bir necha kilobayt matn va buyruq printerga XOM
 *   holda ketadi. Chizishni printer O'ZI bajaradi va oxirida qog'ozni
 *   qirqadi. Printer USB (Windows navbati) yoki tarmoq (TCP:9100) orqali
 *   ulangan bo'lishi mumkin — bu yerdan farqi bilinmaydi.</li>
 *
 *   <li><b>Rasm</b>: printer ESC/POS ni tushunmasa. Sekinroq (server
 *   rasterlaydi, drayver qayta rasterlaydi), lekin o'lcham aniq.</li>
 *
 *   <li><b>PDF</b>: FAQAT brauzerda — odatdagi chop etish oynasi.</li>
 * </ol>
 *
 * <p><b>Qobiq ichida PDF yo'liga tushilmaydi.</b> U <code>window.open</code>
 * bilan <code>blob:</code> havolasini ochadi, qobiq esa uni tashqi dasturga
 * uzatadi va Windows «bu havolani ochadigan dastur yo'q, Microsoft
 * Store'dan qidiring» deb chiqaradi — chek o'rniga, navbat oldida. Sabab
 * ekranda ko'rsatiladi: u odatda «chek printeri tanlanmagan» bo'lib
 * chiqadi va uni bir marta tuzatish kifoya.</p>
 *
 * @param done Yakunlangan sotuv — avtomatik chop etish shundan boshlanadi.
 *   <c>null</c> berilsa faqat qo'lda chop etish qoladi: sotuv kartochkasidan
 *   eski chekni qayta bosish aynan shunday ishlaydi.
 */
export function useReceiptPrinting(done: PosSale | null) {
  const { t, i18n } = useTranslation();

  // Chek eni (58/80 mm) — do'kon sozlamasidan. Uzoq keshlanadi: u kuniga
  // o'zgaradigan qiymat emas, lekin qattiq yozib qo'yilsa 58 mm printerli
  // do'konda chek qog'ozga sig'masdi.
  const settingsQuery = useQuery({
    queryKey: ['pos-print-settings'],
    queryFn: posApi.printSettings,
    staleTime: 30 * 60_000,
  });

  // Chek chiqmagan bo'lsa SABABI. Ilgari u faqat jurnalga yozilardi va
  // kassir tugmani bosib hech narsa bo'lmaganini ko'rardi.
  const [problem, setProblem] = useState<string | null>(null);

  async function print(id: string) {
    const widthMm = settingsQuery.data?.receiptWidthMm ?? 80;
    setProblem(null);
    try {
      if (desktopBridge('receipt')) {
        const escpos = await posApi.receiptEscPos(id, i18n.language, widthMm);
        const raw = await printRawViaDesktop(await toBase64(escpos));
        if (raw.ok) return;

        const png = await posApi.receiptImage(id, i18n.language, widthMm);
        if ((await printReceiptImage(png, widthMm)) === 'printed') return;

        setProblem(raw.problem ?? t('pos.done.printFailed'));
        return;
      }
      const blob = await posApi.receiptPdf(id, i18n.language, widthMm);
      await printPdfBlob(blob, `chek-${id}.pdf`);
    } catch {
      // Sotuv allaqachon yakunlangan — chop etishni qayta urinib ko'rsa
      // bo'ladi, lekin kassir buni BILISHI kerak.
      setProblem(t('pos.done.printFailed'));
    }
  }

  // Sozlamadagi «Chek avtomatik chop etilsin» AYNAN shu yerda amalga
  // oshadi. Sozlama Sozlamalar ekranida ancha vaqt turgan, lekin uni hech
  // kim o'qimasdi: kassir har savdodan keyin tugmani qo'lda bosardi.
  //
  // Faqat qobiqda: sozlamaning va'dasi «chop etish oynasisiz» edi, brauzerda
  // esa oynasiz chop etib bo'lmaydi va u navbat oldida kutilmaganda ochilardi.
  // Oyna yopildi — keyingi chek uchun sabab ham tozalanadi. ALOHIDA effekt:
  // avtomatik chop etish sozlama yuklangach qayta ishga tushadi va tozalash
  // o'sha yerda tursa, kassir endigina ko'rgan xato sababi g'oyib bo'lardi.
  useEffect(() => {
    if (!done) setProblem(null);
  }, [done]);

  const printedFor = useRef<string | null>(null);
  useEffect(() => {
    if (!done || printedFor.current === done.id) return;
    if (!settingsQuery.data?.autoPrintReceipt || !desktopBridge('receipt')) return;

    printedFor.current = done.id;
    void print(done.id);
    // `print` har render'da qaytadan yaratiladi — bog'liqlikka qo'shilsa
    // effekt cheksiz takrorlanardi. Chek `id` si bo'yicha bir marta bosiladi.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [done, settingsQuery.data?.autoPrintReceipt]);

  return { print, problem };
}
