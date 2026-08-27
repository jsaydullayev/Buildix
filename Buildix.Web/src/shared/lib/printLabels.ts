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

/** Chop etiladigan hujjat — har yorliq alohida sahifada. */
export function buildLabelsHtml(labels: LabelImage[], widthMm: number, heightMm: number): string {
  const pages = labels
    .flatMap((l) => Array.from({ length: Math.max(1, l.copies) }, () => l.png))
    .map((png) => `<img src="data:image/png;base64,${png}" alt="">`)
    .join('');

  return `<!doctype html>
<html><head><meta charset="utf-8"><title>labels</title><style>
  /* Sahifa o'lchami AYNAN yorliq o'lchami. Brauzer shu qiymatni drayverga
     uzatadi; chekka nol, ya'ni maket qirqilmaydi va siljimaydi. */
  @page { size: ${widthMm}mm ${heightMm}mm; margin: 0; }
  html, body { margin: 0; padding: 0; }
  /* Har rasm — bitta sahifa. Yorliq printeri sahifadan keyin qog'ozni uzadi,
     shuning uchun nusxalar ham alohida sahifa bo'lishi kerak. */
  img {
    display: block;
    width: ${widthMm}mm;
    height: ${heightMm}mm;
    break-after: page;
    page-break-after: always;
  }
  img:last-child { break-after: auto; page-break-after: auto; }
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
  if (desktopBridge()) {
    const sent = await printViaDesktop(html, widthMm, heightMm);
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

    await imagesReady(doc, win);

    win.focus();
    win.print();
    cleanup();
    return 'printed';
  } catch {
    frame.remove();
    return 'failed';
  }
}

/** Qobiq bilan gaplashish uchun WebView2 ko'prigi (faqat do'kon dasturida). */
type WebViewBridge = {
  postMessage: (message: unknown) => void;
  addEventListener: (type: 'message', listener: (e: { data: unknown }) => void) => void;
  removeEventListener: (type: 'message', listener: (e: { data: unknown }) => void) => void;
};

/** Do'kon dasturi ichidamizmi va u yorliq bosa oladimi. */
function desktopBridge(): WebViewBridge | null {
  const w = window as unknown as {
    chrome?: { webview?: WebViewBridge };
    buildixDesktop?: { canPrintLabels?: boolean };
  };
  return w.buildixDesktop?.canPrintLabels && w.chrome?.webview ? w.chrome.webview : null;
}

/** Qobiq javobini kutish chegarasi — chop etish sekin printerlarda uzoq. */
const DESKTOP_TIMEOUT_MS = 60_000;

/**
 * Yorliqni qobiq orqali bosadi. Muvaffaqiyatli bo'lsa `true`.
 *
 * <p>Javob KUTILADI: qobiq «printer tanlanmagan» deyishi mumkin va o'shanda
 * odatdagi chop etish oynasiga tushish kerak. Javobsiz ketilsa, omborchi
 * tugmani bosib hech narsa bo'lmaganini ko'rardi.</p>
 */
function printViaDesktop(html: string, widthMm: number, heightMm: number): Promise<boolean> {
  const bridge = desktopBridge();
  if (!bridge) return Promise.resolve(false);

  const id = `${Date.now()}-${Math.random().toString(36).slice(2)}`;

  return new Promise<boolean>((resolve) => {
    let settled = false;
    const finish = (ok: boolean) => {
      if (settled) return;
      settled = true;
      window.clearTimeout(timer);
      bridge.removeEventListener('message', onMessage);
      resolve(ok);
    };

    const onMessage = (e: { data: unknown }) => {
      const d = e.data as { kind?: string; id?: string; ok?: boolean; problem?: string } | null;
      if (!d || d.kind !== 'buildix.print-labels.result' || d.id !== id) return;
      if (!d.ok && d.problem) {
        // Sababni jurnalda qoldiramiz: ekranda esa odatdagi oyna ochiladi,
        // ya'ni ish davom etadi.
        console.warn('[buildix] yorliq printeri:', d.problem);
      }
      finish(d.ok === true);
    };

    const timer = window.setTimeout(() => finish(false), DESKTOP_TIMEOUT_MS);
    bridge.addEventListener('message', onMessage);
    bridge.postMessage({ kind: 'buildix.print-labels', id, html, widthMm, heightMm });
  });
}

/** Hujjatdagi barcha rasmlar yuklanishini kutadi (yoki muddat tugashini). */
function imagesReady(doc: Document, win: Window): Promise<void> {
  const images = [...doc.images];
  const pending = images.filter((img) => !img.complete);
  if (pending.length === 0) return Promise.resolve();

  return new Promise((resolve) => {
    let left = pending.length;
    const done = () => {
      left -= 1;
      if (left <= 0) {
        win.clearTimeout(timer);
        resolve();
      }
    };
    // Muddat tugasa ham davom etamiz: yarim yuklangan hujjatni chop etish
    // umuman chop etmaslikdan yaxshiroq — omborchi natijani ko'rib qaror
    // qiladi.
    const timer = win.setTimeout(resolve, LOAD_TIMEOUT_MS);
    for (const img of pending) {
      img.addEventListener('load', done, { once: true });
      img.addEventListener('error', done, { once: true });
    }
  });
}
