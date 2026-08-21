/**
 * Printer tekshiruvining oxirgi natijasi.
 *
 * <p>Brauzerda saqlanadi, serverda emas — va bu ataylab: printer aynan SHU
 * kompyuterga ulangan. Do'konda uchta kompyuter bo'lsa, ularning har birida
 * o'z printeri va o'z holati bo'ladi; serverdagi bitta yozuv esa yolg'on
 * ma'lumot berardi ("printer ishlayapti" deb — lekin qaysi kompyuterda?).</p>
 */
const KEY = 'buildix.printerCheck';

export type PrinterCheck = {
  /** ISO sana — oxirgi sinov qachon o'tkazilgan. */
  at: string;
  ok: boolean;
  /** Qaysi o'lchamda sinalgan: "58x40". */
  size: string;
  /** WebUSB orqali tanlangan qurilma nomi, bo'lsa. */
  usbName?: string;
};

export function readCheck(): PrinterCheck | null {
  try {
    const raw = localStorage.getItem(KEY);
    if (!raw) return null;
    const v = JSON.parse(raw) as PrinterCheck;
    return typeof v?.at === 'string' && typeof v?.ok === 'boolean' ? v : null;
  } catch {
    return null;
  }
}

export function writeCheck(v: PrinterCheck): void {
  try {
    localStorage.setItem(KEY, JSON.stringify(v));
  } catch {
    // Xotira yopiq bo'lsa (private rejim) — holat eslab qolinmaydi, xolos.
  }
}
