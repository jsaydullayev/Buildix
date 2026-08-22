import { useEffect, useRef } from 'react';

/**
 * Skanerni BUTUN SAHIFA bo'ylab ushlaydi — fokus qayerda bo'lishidan qat'i
 * nazar.
 *
 * <p><b>Muammo.</b> Ilgari kod faqat qidiruv maydoni fokusda bo'lsa qabul
 * qilinardi. Kassir mijoz tanlagach, chegirma yozgach yoki shunchaki bo'sh
 * joyga bosgach fokus yo'qolar va skaner «ishlamay» qolardi. Bundan ham
 * yomoni: fokus miqdor yoki chegirma maydonida bo'lsa, skaner kodni O'SHA
 * yerga yozib yuborardi.</p>
 *
 * <p><b>Yechim.</b> Skanerni odamdan TERISH TEZLIGI ajratadi: odam eng tez
 * terganda ham belgilar orasi 80–150 ms, apparat skaner esa 5–20 ms.</p>
 *
 * <p><b>Nega belgilar to'siqlanmaydi.</b> Birinchi urinishda tezlikka qarab
 * <code>preventDefault</code> qilingan edi va u mo'rt chiqdi: brauzer ba'zan
 * belgilar orasiga kutilmagan pauza qo'shadi, bo'linish uziladi va kodning
 * bir qismi maydonga sizib chiqadi. Bundan ham xavflisi — odam tez terganda
 * belgilar to'siqlanib, matni butunlay yo'qolishi mumkin edi.</p>
 *
 * <p>Shuning uchun belgilar odatdagidek maydonga tushaveradi, lekin
 * bo'linish boshlanishida maydon qiymati eslab qolinadi. Oxirida hukm
 * «skaner» bo'lsa — maydon o'sha holatiga tiklanadi va kod savatga uzatiladi.
 * Odam tergan bo'lsa hech narsaga tegilmaydi. Natijada terish hech qachon
 * buzilmaydi, skanerdan esa iz qolmaydi.</p>
 */

/** Shu O'RTACHA oraliqdan tez kelgan belgilar — odam qo'li emas. */
const SCANNER_MAX_AVG_GAP_MS = 35;
/** Shundan qisqa satrni tezlikka qarab baholash ishonchsiz. */
const MIN_CHARS = 6;
/**
 * Bo'linishni davom ettirish chegarasi. Kengroq olingan: brauzer jitteri
 * bitta pauza qo'shsa ham bo'linish uzilmasin. Hukmni baribir o'rtacha
 * qiymat chiqaradi, shuning uchun bu odam terishini «skaner» qilib
 * yubormaydi.
 */
const CONTINUE_GAP_MS = 250;
/**
 * Enter yubormaydigan skanerlar uchun: oxirgi belgidan keyin shuncha kutib,
 * to'plangan kod baribir qabul qilinadi.
 */
const FLUSH_AFTER_MS = 140;

/** React boshqaradigan maydon qiymatini tashqaridan tiklash. */
function restoreValue(el: HTMLInputElement | HTMLTextAreaElement, value: string) {
  const proto = el instanceof HTMLTextAreaElement ? HTMLTextAreaElement.prototype : HTMLInputElement.prototype;
  const setter = Object.getOwnPropertyDescriptor(proto, 'value')?.set;
  // Oddiy `el.value = …` React holatini yangilamaydi — u o'z setter'ini
  // kuzatadi. Native setter + `input` hodisasi bilan React qayta o'qiydi.
  setter?.call(el, value);
  el.dispatchEvent(new Event('input', { bubbles: true }));
}

export function useGlobalScanner(onScan: (code: string) => void, enabled = true) {
  const onScanRef = useRef(onScan);
  onScanRef.current = onScan;

  useEffect(() => {
    if (!enabled) return;

    let chars: string[] = [];
    let stamps: number[] = [];
    /** Bo'linish boshlangandagi maydon holati — oxirida tiklash uchun. */
    let snapshot: { el: HTMLInputElement | HTMLTextAreaElement; value: string } | null = null;
    let flushTimer: number | undefined;

    const reset = () => {
      chars = [];
      stamps = [];
      snapshot = null;
      window.clearTimeout(flushTimer);
    };

    const takeSnapshot = () => {
      const el = document.activeElement;
      snapshot =
        el instanceof HTMLInputElement || el instanceof HTMLTextAreaElement
          ? { el, value: el.value }
          : null;
    };

    const looksLikeScanner = () => {
      if (chars.length < MIN_CHARS || stamps.length < 2) return false;
      let sum = 0;
      for (let i = 1; i < stamps.length; i++) {
        const prev = stamps[i - 1];
        const cur = stamps[i];
        if (prev === undefined || cur === undefined) return false;
        sum += cur - prev;
      }
      return sum / (stamps.length - 1) <= SCANNER_MAX_AVG_GAP_MS;
    };

    const flush = () => {
      const code = chars.join('');
      const snap = snapshot;
      reset();
      // Maydonni skanerdan oldingi holatiga qaytaramiz — kod u yerda
      // qolib ketmasin (chegirma, miqdor, mijoz ismi…).
      if (snap) restoreValue(snap.el, snap.value);
      onScanRef.current(code);
    };

    const onKeyDown = (e: KeyboardEvent) => {
      if (e.ctrlKey || e.altKey || e.metaKey) return;

      if (e.key === 'Enter') {
        if (!looksLikeScanner()) {
          // Odam terib Enter bosdi — maydonning o'z ishlovchisi bajarsin.
          reset();
          return;
        }
        // Skaner Enter'i maydon ishlovchisiga yetmasin, aks holda kod ikki
        // marta qayta ishlanardi.
        e.preventDefault();
        e.stopPropagation();
        flush();
        return;
      }

      if (e.key.length !== 1) {
        // Escape, Tab, o'q tugmalari urinishni bekor qiladi (Shift bundan
        // mustasno — u katta harf uchun bosiladi).
        if (e.key !== 'Shift') reset();
        return;
      }

      const now = performance.now();
      const last = stamps[stamps.length - 1];
      if (last === undefined || now - last > CONTINUE_GAP_MS) {
        reset();
        takeSnapshot();
      }
      chars.push(e.key);
      stamps.push(now);

      // Enter yubormaydigan skanerlar uchun kechiktirilgan qabul.
      window.clearTimeout(flushTimer);
      flushTimer = window.setTimeout(() => {
        if (looksLikeScanner()) flush();
        else reset();
      }, FLUSH_AFTER_MS);
    };

    // Capture bosqichi: maydonlarning o'z ishlovchilaridan OLDIN ko'ramiz.
    document.addEventListener('keydown', onKeyDown, true);
    return () => {
      document.removeEventListener('keydown', onKeyDown, true);
      window.clearTimeout(flushTimer);
    };
  }, [enabled]);
}
