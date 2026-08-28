/**
 * Yorliqlarni ANIQ o'lchamda chop etadi.
 *
 * <p><b>Muammo.</b> Yorliq PDF i so'ralgan o'lchamda chiqadi — buni server
 * sinovi millimetrigacha tekshiradi. Lekin uni brauzerning chop etish oynasi
 * bosadi va u sukut bo'yicha «sahifaga moslash» (fit to page) qiladi:
 * Windows'da printer qog'ozi A4 bo'lsa, 58×40 mm maket A4 ga cho'zilib
 * ketardi. Qog'ozga xato o'lcham urilishining sababi aynan shu — PDF emas,
 * chop etish yo'li.</p>
 *
 * <p><b>Yechim.</b> Yorliqlar rasm bo'lib olinadi va <code>@page</code> da
 * o'lcham AYNAN yozilgan sahifaga qo'yiladi. Shunda brauzer o'lchamni
 * drayverga o'zi aytadi, chekka nolga tushadi va masshtab qo'llanmaydi.</p>
 *
 * <p>Maket bitta joyda qoladi: rasm ham, PDF ham serverdagi o'sha
 * chizuvchidan chiqadi. Alohida HTML maket yozilganda ikkalasi vaqt o'tib
 * bir-biridan uzoqlashardi.</p>
 */

import { desktopBridge, printViaDesktop } from './desktopPrint';

/** Server bergan yorliq: rasm (base64 PNG) va nusxa soni. */
export type LabelImage = { name: string; png: string; copies: number };

export type PrintOutcome =
  /** Chop etish oynasi ochildi. */
  | 'printed'
  /** Iframe ishlamadi — chop etib bo'lmadi. */
  | 'failed';

/** Iframe yuklanishini kutish chegarasi. */
const LOAD_TIMEOUT_MS = 15_000;
/** Chop etish oynasi yopilgach resurslarni bo'shatish kechikishi. */
const CLEANUP_DELAY_MS = 60_000;

/**
 * Chop etiladigan hujjat — har yorliq alohida sahifada.
 *
 * <p><b>Rasm bir marta yoziladi.</b> Ilgari har NUSXA uchun base64 satr
 * qaytadan qo'yilardi. Priyomkadan keyin «Sement — 100 dona» yorlig'i
 * bosilganda hujjat uch megabaytdan oshib ketardi va do'kon qobig'i uni
 * ochib ham ulgurmasdi: chop etish jimgina brauzer oynasiga tushar, u yerda
 * esa sukut bo'yicha A4 printer va «Masshtab: sukut» turardi — 58×40 mm
 * yorliq A4 varaqqa cho'zilib bosilardi. Do'konda eng ko'p uchraydigan
 * holat aynan shu edi.</p>
 *
 * <p>Endi har xil yorliq CSS sinfida bir marta e'lon qilinadi, sahifalar esa
 * o'sha sinfga ishora qiladigan bo'sh bloklar. Yuz nusxa hujjat hajmini
 * deyarli oshirmaydi.</p>
 */
export function buildLabelsHtml(labels: LabelImage[], widthMm: number, heightMm: number): string {
  // Bir xil rasm bir necha tovarda uchrashi mumkin emas, lekin himoya arzon.
  const unique = new Map<string, number>();
  for (const l of labels) if (!unique.has(l.png)) unique.set(l.png, unique.size);

  const classes = [...unique.entries()]
    .map(([png, i]) => `.l${i}{background-image:url(data:image/png;base64,${png})}`)
    .join('');

  const pages = labels
    .flatMap((l) => Array.from({ length: Math.max(1, l.copies) }, () => unique.get(l.png)!))
    .map((i) => `<i class="l${i}"></i>`)
    .join('');

  return `<!doctype html>
<html><head><meta charset="utf-8"><title>labels</title><style>
  /* Sahifa o'lchami AYNAN yorliq o'lchami. Brauzer shu qiymatni drayverga
     uzatadi; chekka nol, ya'ni maket qirqilmaydi va siljimaydi. */
  @page { size: ${widthMm}mm ${heightMm}mm; margin: 0; }
  html, body { margin: 0; padding: 0; }
  /* Fon rasmlari sukut bo'yicha CHOP ETILMAYDI — brauzer ularni siyoh
     tejash uchun tashlab yuboradi. Yorliq esa aynan o'sha rasmning o'zi,
     ya'ni bu yerda u majburan yoqiladi. Busiz qog'ozdan BO'SH yorliq
     chiqardi. */
  i {
    display: block;
    width: ${widthMm}mm;
    height: ${heightMm}mm;
    background-size: ${widthMm}mm ${heightMm}mm;
    background-repeat: no-repeat;
    -webkit-print-color-adjust: exact;
    print-color-adjust: exact;
    break-after: page;
    page-break-after: always;
  }
  i:last-child { break-after: auto; page-break-after: auto; }
  ${classes}
</style></head><body>${pages}</body></html>`;
}

/**
 * Hujjatni ko'rinmas iframe ga yuklab, chop etish oynasini ochadi.
 *
 * <p>Rasmlar yuklanib bo'lguncha kutiladi: yuklanmagan rasm chop etishga
 * BO'SH sahifa bo'lib tushardi va omborchi buni faqat qog'ozdan bilardi.</p>
 */
export async function printLabels(
  labels: LabelImage[],
  widthMm: number,
  heightMm: number,
): Promise<PrintOutcome> {
  if (labels.length === 0) return 'failed';

  const html = buildLabelsHtml(labels, widthMm, heightMm);

  // Do'kon dasturi ichida bo'lsak — chop etish oynasi umuman kerak emas:
  // qobiq yorliqni sozlamada tanlangan printerga o'zi yuboradi. Bu yo'l
  // yiqilsa (printer tanlanmagan, o'chirilgan) pastdagi odatdagi oynaga
  // tushamiz — omborchi ishsiz qolmaydi.
  if (desktopBridge('label')) {
    const sent = await printViaDesktop(html, widthMm, heightMm, 'label');
    if (sent) return 'printed';
  }

  const frame = document.createElement('iframe');
  // Ko'rinmas, lekin `display:none` EMAS: yashirilgan iframe ni ba'zi
  // brauzerlar umuman render qilmaydi va chop etishga hech narsa bermaydi.
  frame.style.position = 'fixed';
  frame.style.right = '0';
  frame.style.bottom = '0';
  frame.style.width = '1px';
  frame.style.height = '1px';
  frame.style.opacity = '0';
  frame.style.border = '0';
  frame.setAttribute('aria-hidden', 'true');
  document.body.appendChild(frame);

  const cleanup = () => window.setTimeout(() => frame.remove(), CLEANUP_DELAY_MS);

  try {
    const doc = frame.contentDocument;
    const win = frame.contentWindow;
    if (!doc || !win) {
      frame.remove();
      return 'failed';
    }

    doc.open();
    doc.write(html);
    doc.close();

    // Rasmlar CSS fonida, ya'ni `doc.images` bo'sh bo'ladi. Ular chop
    // etishdan OLDIN dekodlanishi kerak: aks holda birinchi sahifalar bo'sh
    // chiqishi mumkin va omborchi buni faqat qog'ozdan bilardi.
    await decodeAll(labels);

    win.focus();
    win.print();
    cleanup();
    return 'printed';
  } catch {
    frame.remove();
    return 'failed';
  }
}

/**
 * Rasmlarni chop etishdan OLDIN dekodlaydi.
 *
 * <p>Yorliqlar CSS fonida turadi, ya'ni hujjatda <code>img</code> elementi
 * yo'q va uning yuklanishini kutib bo'lmaydi. Buning o'rniga har xil rasm
 * shu yerda bir marta dekodlanadi — brauzer keshi tayyor bo'lgach iframe
 * darhol chiziladi.</p>
 *
 * <p>Muddat tugasa ham davom etamiz: yarim tayyor hujjatni chop etish
 * umuman chop etmaslikdan yaxshiroq — omborchi natijani ko'rib qaror
 * qiladi.</p>
 */
function decodeAll(labels: LabelImage[]): Promise<void> {
  const unique = [...new Set(labels.map((l) => l.png))];
  if (unique.length === 0) return Promise.resolve();

  const ready = unique.map(
    (png) =>
      new Promise<void>((resolve) => {
        const img = new Image();
        img.onload = () => resolve();
        img.onerror = () => resolve();
        img.src = `data:image/png;base64,${png}`;
      }),
  );

  return Promise.race([
    Promise.all(ready).then(() => undefined),
    new Promise<void>((resolve) => window.setTimeout(resolve, LOAD_TIMEOUT_MS)),
  ]);
}
