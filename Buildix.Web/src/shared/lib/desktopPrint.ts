/**
 * Do'kon dasturining chop etish ko'prigi.
 *
 * <p>Qobiq sahifani ko'rinmas oynada ochib, AYNAN berilgan qog'oz o'lchamida
 * va sozlamada tanlangan printerga bosadi. Brauzerning chop etish oynasi
 * umuman ochilmaydi — u yerda sukut bo'yicha A4 printer va «sahifaga
 * moslash» turadi, ya'ni 80 mm chek ham, 58×40 mm yorliq ham qog'ozga
 * sig'may qolardi.</p>
 *
 * <p>Bu modul ikkala chop etuvchi (yorliq va chek) uchun umumiy. Ilgari
 * ko'prik faqat yorliq modulida yashagan va chekni bosish uchun uni
 * ko'chirib olish kerak bo'lardi — ikkita nusxa esa vaqt o'tib
 * bir-biridan uzoqlashardi.</p>
 */

/** Qobiq bilan gaplashish uchun WebView2 ko'prigi. */
type WebViewBridge = {
  postMessage: (message: unknown) => void;
  addEventListener: (type: 'message', listener: (e: { data: unknown }) => void) => void;
  removeEventListener: (type: 'message', listener: (e: { data: unknown }) => void) => void;
};

/** Qaysi printerga: yorliq yoki chek. Qobiqda ular alohida sozlanadi. */
export type PrintTarget = 'label' | 'receipt';

/** Do'kon dasturi ichidamizmi va u shu turdagi hujjatni bosa oladimi. */
export function desktopBridge(target: PrintTarget): WebViewBridge | null {
  const w = window as unknown as {
    chrome?: { webview?: WebViewBridge };
    buildixDesktop?: { canPrintLabels?: boolean; canPrintReceipts?: boolean };
  };
  const can = target === 'receipt'
    ? w.buildixDesktop?.canPrintReceipts
    : w.buildixDesktop?.canPrintLabels;
  return can && w.chrome?.webview ? w.chrome.webview : null;
}

/** Qobiq javobini kutish chegarasi — sekin printerlarda chop etish uzoq. */
const TIMEOUT_MS = 60_000;

/**
 * Hujjatni qobiq orqali bosadi. Muvaffaqiyatli bo'lsa <c>true</c>.
 *
 * <p>Javob KUTILADI: qobiq «printer tanlanmagan» deyishi mumkin va o'shanda
 * odatdagi chop etish oynasiga tushish kerak. Javobsiz ketilsa, kassir
 * tugmani bosib hech narsa bo'lmaganini ko'rardi.</p>
 */
export function printViaDesktop(
  html: string,
  widthMm: number,
  heightMm: number,
  target: PrintTarget,
): Promise<boolean> {
  const bridge = desktopBridge(target);
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
        console.warn('[buildix] printer:', d.problem);
      }
      finish(d.ok === true);
    };

    const timer = window.setTimeout(() => finish(false), TIMEOUT_MS);
    bridge.addEventListener('message', onMessage);
    bridge.postMessage({ kind: 'buildix.print-labels', id, html, widthMm, heightMm, target });
  });
}

/** Rasmning piksel o'lchamlari — balandlikni hisoblash uchun. */
export function imageSize(dataUrl: string): Promise<{ width: number; height: number } | null> {
  return new Promise((resolve) => {
    const img = new Image();
    img.onload = () => resolve({ width: img.naturalWidth, height: img.naturalHeight });
    img.onerror = () => resolve(null);
    img.src = dataUrl;
  });
}

/** Blob'ni base64 data-URL ga o'giradi. */
export function toDataUrl(blob: Blob): Promise<string> {
  return new Promise((resolve, reject) => {
    const reader = new FileReader();
    reader.onload = () => resolve(String(reader.result));
    reader.onerror = () => reject(reader.error);
    reader.readAsDataURL(blob);
  });
}
