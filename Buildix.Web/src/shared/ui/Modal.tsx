import { type ReactNode, useEffect } from 'react';
import { X } from 'lucide-react';
import { cn } from '@/shared/lib/cn';

/** Centered dialog with a scrim. Closes on Esc / backdrop click. */
export function Modal({
  open,
  onClose,
  title,
  subtitle,
  children,
  footer,
  width = 'md',
}: {
  open: boolean;
  onClose: () => void;
  title: string;
  subtitle?: string;
  children: ReactNode;
  footer?: ReactNode;
  width?: 'md' | 'lg' | 'xl';
}) {
  useEffect(() => {
    if (!open) return;
    const onKey = (e: KeyboardEvent) => e.key === 'Escape' && onClose();
    window.addEventListener('keydown', onKey);
    return () => window.removeEventListener('keydown', onKey);
  }, [open, onClose]);

  if (!open) return null;

  const widthClass = width === 'xl' ? 'max-w-3xl' : width === 'lg' ? 'max-w-2xl' : 'max-w-lg';

  return (
    <div
      className="fixed inset-0 z-50 flex items-start justify-center overflow-y-auto bg-text/40 px-4 py-10"
      onMouseDown={(e) => e.target === e.currentTarget && onClose()}
    >
      {/*
        Oyna EKRANDAN oshmaydi: balandligi cheklangan va ichi ustun bo'lib
        taqsimlanadi.

        Ilgari oyna kontent qancha bo'lsa shuncha o'sardi va butun sahifa
        skroll bo'lardi. Ro'yxat uzun bo'lganda — masalan omborda tovar
        harakati — sarlavha ham, pastdagi amal tugmasi ham ekrandan chiqib
        ketardi: foydalanuvchi «Tuzatish» ni topish uchun o'nlab qatorni
        aylantirib o'tishga majbur edi.

        Endi faqat O'RTA qism aylanadi; sarlavha va pastki qator joyida
        turadi. `dvh` telefon brauzerlarining yig'iladigan paneli uchun:
        `vh` da oynaning pasti panel ostida qolib ketardi.
      */}
      <div
        className={cn(
          'flex max-h-[calc(100dvh-5rem)] w-full animate-fade-in flex-col rounded-card bg-surface shadow-xl',
          widthClass,
        )}
      >
        <div className="flex flex-none items-start justify-between border-b border-hairline px-6 py-4">
          <div>
            <h2 className="text-[17px] font-semibold">{title}</h2>
            {subtitle && <p className="mt-0.5 text-[12.5px] text-muted-2">{subtitle}</p>}
          </div>
          <button
            type="button"
            onClick={onClose}
            className="-mr-1 flex h-8 w-8 items-center justify-center rounded-md text-muted-2 hover:bg-hairline hover:text-text"
            aria-label="Close"
          >
            <X size={18} />
          </button>
        </div>
        {/* `overscroll-contain` — ro'yxat oxiriga yetganda sahifa ortidan
            aylanib ketmasin. */}
        <div className="min-h-0 flex-1 overflow-y-auto overscroll-contain px-6 py-5">{children}</div>
        {footer && (
          <div className="flex flex-none items-center justify-end gap-3 border-t border-hairline px-6 py-4">
            {footer}
          </div>
        )}
      </div>
    </div>
  );
}
