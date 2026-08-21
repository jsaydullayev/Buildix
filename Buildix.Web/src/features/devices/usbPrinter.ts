/**
 * USB printerni ANIQLASH (WebUSB).
 *
 * <p>Brauzer operatsion tizimdagi printerlar ro'yxatini ko'ra olmaydi — bu
 * xavfsizlik cheklovi, uni chetlab o'tib bo'lmaydi. WebUSB esa boshqa yo'l:
 * foydalanuvchi o'zi tanlagan qurilmaga kirish beradi. Brauzer ko'rsatadigan
 * ro'yxat ULANGAN qurilmalardan yig'iladi, ya'ni "printer ulanganmi" degan
 * savolga aynan shu javob beradi.</p>
 *
 * <p>Cheklovlar ochiq aytilishi kerak: faqat Chrome/Edge da va faqat HTTPS
 * ustida ishlaydi; Windows'da drayver qurilmani band qilgan bo'lsa unga
 * ULANIB bo'lmaydi (lekin ro'yxatda ko'rinadi — bizga shuning o'zi yetarli).
 * Shuning uchun bu qo'shimcha tekshiruv, asosiysi emas: yakuniy hukmni sinov
 * yorlig'i chiqarib beradi.</p>
 */

/** USB printer sinfi (bInterfaceClass = 7). Yorliq printerlari shu sinfda. */
const USB_PRINTER_CLASS = 7;

type UsbDeviceLike = {
  productName?: string;
  manufacturerName?: string;
  serialNumber?: string;
  vendorId: number;
  productId: number;
};

type UsbLike = {
  getDevices(): Promise<UsbDeviceLike[]>;
  requestDevice(options: { filters: { classCode?: number }[] }): Promise<UsbDeviceLike>;
};

function usb(): UsbLike | null {
  const nav = navigator as Navigator & { usb?: UsbLike };
  return nav.usb ?? null;
}

export const usbSupported = () => usb() !== null;

export type UsbPrinter = {
  name: string;
  vendorId: string;
  productId: string;
};

const describe = (d: UsbDeviceLike): UsbPrinter => ({
  name: [d.manufacturerName, d.productName].filter(Boolean).join(' ').trim() || 'USB printer',
  vendorId: '0x' + d.vendorId.toString(16).padStart(4, '0'),
  productId: '0x' + d.productId.toString(16).padStart(4, '0'),
});

/** Ilgari ruxsat berilgan qurilmalar — foydalanuvchi ishtirokisiz. */
export async function knownPrinters(): Promise<UsbPrinter[]> {
  const u = usb();
  if (!u) return [];
  try {
    return (await u.getDevices()).map(describe);
  } catch {
    return [];
  }
}

export type PickResult =
  | { kind: 'picked'; printer: UsbPrinter }
  | { kind: 'cancelled' }        // foydalanuvchi oynani yopdi yoki ro'yxat bo'sh
  | { kind: 'unsupported' }
  | { kind: 'error'; message: string };

/**
 * Brauzer oynasini ochib, ulangan printerni tanlashni so'raydi.
 * FAQAT foydalanuvchi bosishidan chaqirilishi mumkin (brauzer talabi).
 */
export async function pickPrinter(): Promise<PickResult> {
  const u = usb();
  if (!u) return { kind: 'unsupported' };
  try {
    const device = await u.requestDevice({ filters: [{ classCode: USB_PRINTER_CLASS }] });
    return { kind: 'picked', printer: describe(device) };
  } catch (e) {
    // Foydalanuvchi bekor qilsa yoki mos qurilma bo'lmasa — NotFoundError.
    const name = (e as { name?: string })?.name;
    if (name === 'NotFoundError') return { kind: 'cancelled' };
    return { kind: 'error', message: (e as Error)?.message ?? 'USB xatosi' };
  }
}
