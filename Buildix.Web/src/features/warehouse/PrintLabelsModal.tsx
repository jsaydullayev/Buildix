import { useEffect, useState } from 'react';
import { useTranslation } from 'react-i18next';
import { useMutation, useQuery } from '@tanstack/react-query';
import { Printer, Info } from 'lucide-react';
import { Modal, Button, Spinner } from '@/shared/ui';
import { cn } from '@/shared/lib/cn';
import type { ApiError } from '@/shared/api/types';
import { productsApi } from './api';

/** Yorliq chop etiladigan bitta tovar. */
export interface LabelTarget {
  id: string;
  name: string;
  sku?: string | null;
  /**
   * `string` — kod bor; `null` — kodi yo'qligi ANIQ; `undefined` — noma'lum
   * (chaqiruvchida bu ma'lumot yo'q, masalan priyomka qatorlarida).
   *
   * Farq muhim: noma'lum holatda «kod yaratiladi» deb yozib qo'yish yolg'on
   * bo'lardi — tovarda kod allaqachon bo'lishi mumkin.
   */
  barcode?: string | null;
  /** Boshlang'ich nusxa soni — priyomkadan kelganda qabul qilingan miqdor. */
  copies?: number;
}

/**
 * Yorliq o'lchamlari. Printer hali sotib olinmagan, shuning uchun uchta keng
 * tarqalgan rulon taklif qilinadi; server istalgan o'lchamni qabul qiladi.
 */
const SIZES = [
  { key: '58x40', w: 58, h: 40 },
  { key: '40x30', w: 40, h: 30 },
  { key: '30x20', w: 30, h: 20 },
] as const;

/**
 * Yorliq chop etish — uch joydan (tovar kartasi, ro'yxatdan ko'plab,
 * priyomkadan keyin) ochiladi. Oqim bitta bo'lgani ma'qul: omborchi qayerdan
 * kelmasin bir xil oynani ko'radi.
 */
export function PrintLabelsModal({
  open,
  onClose,
  targets,
}: {
  open: boolean;
  onClose: () => void;
  targets: LabelTarget[];
}) {
  const { t } = useTranslation();
  const [copies, setCopies] = useState<Record<string, number>>({});
  const [size, setSize] = useState<(typeof SIZES)[number]>(SIZES[0]);
  const [error, setError] = useState<string | null>(null);

  // Oyna har ochilganda qaytadan to'ldiriladi: priyomkadan kelgan miqdor
  // oldingi seansdan qolgan qiymat bilan almashib ketmasin.
  useEffect(() => {
    if (!open) return;
    setCopies(Object.fromEntries(targets.map((p) => [p.id, Math.max(1, p.copies ?? 1)])));
    setError(null);
  }, [open, targets]);

  const total = targets.reduce((sum, p) => sum + (copies[p.id] ?? 1), 0);

  // Ko'rinish birinchi tovar bo'yicha: bir nechta tovar tanlanganda ham maket
  // bir xil, farq faqat matnda. Server rasmni chop etiladigan hujjatning
  // O'ZIDAN chiqaradi, ya'ni ko'rgan narsa bosiladi.
  const sample = targets[0];
  const preview = useQuery({
    queryKey: ['label-preview', sample?.id, sample?.barcode, size.key],
    queryFn: () =>
      productsApi.labelPreview({
        name: sample!.name,
        sku: sample!.sku,
        barcode: sample!.barcode,
        widthMm: size.w,
        heightMm: size.h,
      }),
    enabled: open && !!sample,
    staleTime: 5 * 60_000,
  });

  // Blob → URL, va almashganda eskisini bo'shatamiz (aks holda oyna har
  // o'lcham almashganda xotirada rasm qoldirib ketardi).
  const [previewUrl, setPreviewUrl] = useState<string | null>(null);
  useEffect(() => {
    if (!preview.data) return;
    const url = URL.createObjectURL(preview.data);
    setPreviewUrl(url);
    return () => URL.revokeObjectURL(url);
  }, [preview.data]);
  // Faqat ANIQ kodsizlar sanaladi — noma'lum (undefined) holat hisobga olinmaydi.
  const missingCode = targets.filter((p) => p.barcode === null).length;

  const print = useMutation({
    mutationFn: () =>
      productsApi.labelsPdf(
        targets.map((p) => ({ productId: p.id, copies: copies[p.id] ?? 1 })),
        size.w,
        size.h,
      ),
    onSuccess: (blob) => {
      // Chek chop etish bilan bir xil naqsh: PDF yangi oynada ochiladi va
      // foydalanuvchi brauzerning chop etish oynasidan bosadi.
      const url = URL.createObjectURL(blob);
      window.open(url, '_blank', 'noopener');
      setTimeout(() => URL.revokeObjectURL(url), 60_000);
      onClose();
    },
    onError: (e) => setError((e as unknown as ApiError).message ?? t('common.somethingWrong')),
  });

  return (
    <Modal
      open={open}
      onClose={onClose}
      width="lg"
      title={t('labels.title')}
      subtitle={t('labels.subtitle', { count: targets.length })}
      footer={
        <>
          <Button variant="secondary" onClick={onClose}>
            {t('common.cancel')}
          </Button>
          <Button disabled={total === 0} loading={print.isPending} onClick={() => print.mutate()}>
            <Printer size={15} />
            {t('labels.print', { count: total })}
          </Button>
        </>
      }
    >
      <div className="flex flex-col gap-5">
        {/* Ko'rinish — chop etiladigan hujjatning aynan o'zidan. Kulrang fon
            ustidagi oq to'rtburchak yorliqning haqiqiy nisbatlarini beradi. */}
        <div className="flex flex-col items-center gap-2 rounded-card bg-bg py-5">
          <div
            className="flex items-center justify-center overflow-hidden rounded-[3px] bg-white shadow-card"
            style={{ width: `${size.w * 4.2}px`, height: `${size.h * 4.2}px` }}
          >
            {preview.isPending ? (
              <Spinner size={18} className="text-primary" />
            ) : previewUrl ? (
              <img src={previewUrl} alt="" className="h-full w-full object-contain" />
            ) : (
              <span className="px-3 text-center text-[11px] text-muted-2">{t('labels.previewFailed')}</span>
            )}
          </div>
          <span className="text-[11.5px] text-muted-2">
            {t('labels.previewCaption', { w: size.w, h: size.h })}
          </span>
        </div>

        {/* Tovarlar va nusxa soni */}
        <div className="overflow-hidden rounded-card border border-border">
          {targets.map((p, i) => (
            <div
              key={p.id}
              className={cn(
                'flex items-center gap-3 px-4 py-3',
                i > 0 && 'border-t border-hairline',
              )}
            >
              <div className="min-w-0 flex-1">
                <div className="truncate text-[14px] font-medium">{p.name}</div>
                <div className="mt-0.5 flex items-center gap-2 text-[12px] text-muted-2">
                  {p.sku && <span className="truncate">{p.sku}</span>}
                  {p.barcode ? (
                    <span className="nums">{p.barcode}</span>
                  ) : p.barcode === null ? (
                    // Kodi yo'qligi aniq — server uni chop etishdan oldin
                    // yaratadi, omborchi nima bo'layotganini bilib tursin.
                    <span className="text-primary">{t('labels.willGenerate')}</span>
                  ) : null}
                </div>
              </div>
              <div className="flex flex-none items-center gap-2">
                <input
                  type="number"
                  min={1}
                  max={500}
                  value={copies[p.id] ?? 1}
                  onChange={(e) =>
                    setCopies((prev) => ({
                      ...prev,
                      [p.id]: Math.min(500, Math.max(1, Number(e.target.value) || 1)),
                    }))
                  }
                  className="h-10 w-[72px] rounded-input border border-input-border bg-surface px-3 text-right text-[14px] outline-none focus:border-primary focus:shadow-focus-ring nums"
                />
                <span className="text-[12.5px] text-muted-2">{t('labels.pcs')}</span>
              </div>
            </div>
          ))}
        </div>

        {/* O'lcham */}
        <div className="flex flex-wrap items-center gap-3">
          <span className="text-[13px] font-medium text-label">{t('labels.size')}</span>
          <div className="inline-flex rounded-input bg-hairline p-1">
            {SIZES.map((s) => (
              <button
                key={s.key}
                type="button"
                onClick={() => setSize(s)}
                className={cn(
                  'rounded-md px-4 py-1.5 text-[13px] font-medium transition-colors nums',
                  size.key === s.key ? 'bg-surface text-text shadow-card' : 'text-muted hover:text-text',
                )}
              >
                {s.w}×{s.h}
              </button>
            ))}
          </div>
          <span className="text-[12.5px] text-muted-2">{t('labels.mm')}</span>
        </div>

        {missingCode > 0 && (
          <p className="text-[12.5px] text-muted">{t('labels.generateNote', { count: missingCode })}</p>
        )}

        {/* Printerni sozlash haqida — PDF sahifasi 58×40mm, printer esa A4 ga
            sozlangan bo'lsa yorliq varaq burchagida kichkina bo'lib chiqadi. */}
        <div className="flex items-start gap-2.5 rounded-input bg-primary-soft px-4 py-3 text-[12.5px] leading-relaxed text-primary-hover">
          <Info size={15} className="mt-0.5 flex-none" />
          <span>{t('labels.printerHint')}</span>
        </div>

        {error && <p className="text-[12.5px] text-danger">{error}</p>}
      </div>
    </Modal>
  );
}
