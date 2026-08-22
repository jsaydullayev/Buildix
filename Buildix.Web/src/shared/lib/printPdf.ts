import { downloadBlob } from './download';

/**
 * PDF ni to'g'ridan-to'g'ri chop etishga yuboradi.
 *
 * <p><b>Muammo.</b> Ilgari PDF shunchaki yangi oynada ochilardi va u yerda
 * to'xtardi. Omborchi «Chop etish» bosgach printerdan hech narsa chiqmasdi:
 * uning o'zi Ctrl+P bosib, printerni tanlashi kerak edi — buni esa hech kim
 * aytmagan. Tashqaridan bu «ma'lumot printerga yetib bormayapti» bo'lib
 * ko'rinardi.</p>
 *
 * <p><b>Yechim.</b> PDF ko'rinmas iframe ga yuklanadi va uning ustidan
 * <code>print()</code> chaqiriladi — brauzerning chop etish oynasi darhol
 * ochiladi, yorliq allaqachon yuklangan holda. Kassir faqat printerni tanlab
 * tasdiqlaydi.</p>
 *
 * <p><b>Zaxira yo'llar.</b> Ba'zi brauzerlar iframe ichidagi PDF ni chop eta
 * olmaydi (Safari, eski Firefox). Unda avvalgidek yangi oyna ochiladi; u ham
 * bloklangan bo'lsa fayl yuklab olinadi. Har uchala holatda chaqiruvchi NIMA
 * bo'lganini biladi va foydalanuvchiga to'g'ri xabar bera oladi.</p>
 */
export type PrintOutcome =
  /** Chop etish oynasi ochildi. */
  | 'printed'
  /** Iframe ishlamadi — PDF yangi oynada ochildi, foydalanuvchi o'zi bosadi. */
  | 'opened'
  /** Yangi oyna ham bloklandi — fayl yuklab olindi. */
  | 'downloaded';

/** Iframe yuklanishini kutish chegarasi. */
const LOAD_TIMEOUT_MS = 8000;
/** Chop etish oynasi yopilgach resurslarni bo'shatish kechikishi. */
const CLEANUP_DELAY_MS = 60_000;

export async function printPdfBlob(blob: Blob, filename: string): Promise<PrintOutcome> {
  const url = URL.createObjectURL(blob);

  const cleanup = (frame?: HTMLIFrameElement) => {
    window.setTimeout(() => {
      URL.revokeObjectURL(url);
      frame?.remove();
    }, CLEANUP_DELAY_MS);
  };

  try {
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
    frame.src = url;

    const loaded = new Promise<boolean>((resolve) => {
      const timer = window.setTimeout(() => resolve(false), LOAD_TIMEOUT_MS);
      frame.onload = () => {
        window.clearTimeout(timer);
        resolve(true);
      };
      frame.onerror = () => {
        window.clearTimeout(timer);
        resolve(false);
      };
    });

    document.body.appendChild(frame);
    if (await loaded) {
      const win = frame.contentWindow;
      if (win) {
        win.focus();
        win.print();
        cleanup(frame);
        return 'printed';
      }
    }
    frame.remove();
  } catch {
    // Quyidagi zaxira yo'lga tushamiz.
  }

  const opened = window.open(url, '_blank', 'noopener');
  if (opened) {
    cleanup();
    return 'opened';
  }

  URL.revokeObjectURL(url);
  downloadBlob(blob, filename);
  return 'downloaded';
}
