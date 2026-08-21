import { useCallback, useRef, useState } from 'react';

/**
 * Skanerni ANIQLASH: qurilma klaviatura kabi ishlaydi, shuning uchun uni
 * odamdan faqat TERISH TEZLIGI ajratadi.
 *
 * <p>Odam eng tez terganda ham belgilar orasi ~80–150 ms. Apparat skaner butun
 * kodni 5–20 ms oralig'ida yuboradi va oxirida Enter bosadi. Shu ikki belgidan
 * biri ham yetarli emas: tez terilgan qisqa satr ham, sekin kelgan uzun kod ham
 * bo'lishi mumkin — shuning uchun uzunlik, o'rtacha oraliq va Enter birga
 * qaraladi.</p>
 *
 * <p>Nega WebUSB emas: HID rejimidagi skaner brauzerga klaviatura bo'lib
 * ko'rinadi va WebUSB uni umuman ko'rmaydi. Bu usul esa har qanday
 * klaviatura-skanerda, hech qanday ruxsatsiz ishlaydi.</p>
 */
export type ScannerVerdict = {
  kind: 'scanner' | 'typed' | 'unknown';
  code: string;
  chars: number;
  /** Belgilar orasidagi o'rtacha vaqt (ms). */
  avgGapMs: number;
  endedWithEnter: boolean;
};

/** Shu oraliqdan tez kelgan belgilar — odam qo'li emas. */
const SCANNER_MAX_GAP_MS = 35;
/** Shundan qisqa satrni tezlikka qarab baholash ishonchsiz. */
const MIN_CHARS = 6;

export function useScannerProbe() {
  const [verdict, setVerdict] = useState<ScannerVerdict | null>(null);
  const stamps = useRef<number[]>([]);
  const buffer = useRef('');

  const reset = useCallback(() => {
    stamps.current = [];
    buffer.current = '';
    setVerdict(null);
  }, []);

  const onKeyDown = useCallback((e: React.KeyboardEvent<HTMLInputElement>) => {
    const now = performance.now();

    if (e.key === 'Enter') {
      e.preventDefault();
      finish(true);
      return;
    }
    // Boshqaruv tugmalari o'lchovga kirmaydi.
    if (e.key.length !== 1) return;

    // Belgilar orasi uzoq bo'lsa — bu yangi urinishning boshlanishi.
    const last = stamps.current[stamps.current.length - 1];
    if (last !== undefined && now - last > 700) {
      stamps.current = [];
      buffer.current = '';
    }
    stamps.current.push(now);
    buffer.current += e.key;

    function finish(enter: boolean) {
      const code = buffer.current;
      const t = stamps.current;
      const gaps = t.slice(1).map((v, i) => v - t[i]);
      const avg = gaps.length ? gaps.reduce((a, b) => a + b, 0) / gaps.length : Number.POSITIVE_INFINITY;

      let kind: ScannerVerdict['kind'] = 'unknown';
      if (code.length >= MIN_CHARS) kind = avg <= SCANNER_MAX_GAP_MS ? 'scanner' : 'typed';

      setVerdict({
        kind,
        code,
        chars: code.length,
        avgGapMs: Number.isFinite(avg) ? Math.round(avg) : 0,
        endedWithEnter: enter,
      });
      stamps.current = [];
      buffer.current = '';
    }
  }, []);

  return { verdict, onKeyDown, reset };
}
