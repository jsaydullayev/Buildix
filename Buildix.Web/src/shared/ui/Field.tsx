import type { ReactNode } from 'react';

/**
 * Yorliq + maydon juftligi — forma qatorining eng oddiy shakli.
 *
 * <p>Bu komponent to'rtta modalda so'zma-so'z bir xil yozilgan edi:
 * mijoz, ikkita xodim va yetkazib beruvchi formasi. Yorliqning o'lchami
 * yoki oraliq masofasi o'zgarsa, to'rt joyda qidirib topish kerak
 * bo'lardi va biri albatta unutilardi.</p>
 */
export function Field({ label, children }: { label: string; children: ReactNode }) {
  return (
    <div className="flex flex-col gap-1.5">
      <label className="text-[13px] font-medium text-label">{label}</label>
      {children}
    </div>
  );
}
