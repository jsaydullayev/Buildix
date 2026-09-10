import { useEffect, useState } from 'react';
import { useTranslation } from 'react-i18next';
import { useMutation, useQuery } from '@tanstack/react-query';
import { Printer, Info } from 'lucide-react';
import { Modal, Button, Spinner } from '@/shared/ui';
import { cn } from '@/shared/lib/cn';
import { printLabels } from '@/shared/lib/printLabels';
import { canPrintRaw, printRawViaDesktop, toBase64 } from '@/shared/lib/desktopPrint';
import type { ApiError } from '@/shared/api/types';
// Chop etish sozlamalari kassa bilan BIR endpoint'dan keladi
// (`/Markets/pos-settings`) va bir xil so'rov kaliti ishlatiladi — ya'ni
// butun ilova uchun bitta so'rov. Egaga tegishli `/Markets/settings` bu
// yerda yaramaydi: yorliq oynasini `products.edit` ruxsatli omborchi
// ochadi va unga o'sha yo'l berilmagan.
import { posApi } from '@/features/pos/api';
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
 * Rulon o'lchami — sozlamadan kelmasa ishlatiladigan zaxira.
 *
 * <p>Ilgari bu yerda uchta qattiq yozilgan variant turardi (58×40, 40×30,
 * 30×20) va omborchi har chop etishda birini tanlardi. Do'konning haqiqiy
 * rulonlari (57×38, 57×30) ro'yxatda umuman yo'q edi — u eng yaqinini
 * tanlar, maket esa yorliqqa siljib tushardi. Rulon do'konning fizik
 * xususiyati, chop etish tugmasini bosgan odamning tanlovi emas, shuning
 * uchun u endi Sozlamalarda turadi.</p>
 */
const FALLBACK_ROLL = { widthMm: 58, heightMm: 40 };

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
  const [error, setError] = useState<string | null>(null);

  // Rulon o'lchami — do'kon sozlamasidan. Uzoq keshlanadi: u kuniga
  // o'zgaradigan qiymat emas.
  const settingsQuery = useQuery({
    queryKey: ['pos-print-settings'],
    queryFn: posApi.printSettings,
    staleTime: 30 * 60_000,
  });
  const size = {
    w: settingsQuery.data?.labelWidthMm ?? FALLBACK_ROLL.widthMm,
    h: settingsQuery.data?.labelHeightMm ?? FALLBACK_ROLL.heightMm,
  };

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
    queryKey: ['label-preview', sample?.id, sample?.barcode, size.w, size.h],
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

  /**
   * Chop etish yo'llari — eng aniqidan boshlab.
   *
   * <ol>
   *   <li><b>TSPL</b>: printerning O'Z tili. O'lcham jobning ichida
   *   beriladi, ya'ni drayverdagi qog'oz o'lchami ahamiyatsiz va istalgan
   *   rulon ishlaydi. Shtrix kodni printer 203 dpi da o'zi chizadi.</li>
   *
   *   <li><b>Rasm</b>: printer TSPL ni tushunmasa yoki qobiqda yorliq
   *   printeri tanlanmagan bo'lsa. Sekinroq va drayverdagi qog'oz
   *   o'lchamiga bog'liq, lekin ishlaydi.</li>
   * </ol>
   *
   * <p>Chekdagi bilan bir xil narvon (<code>useReceiptPrinting</code>).</p>
   */
  const print = useMutation({
    mutationFn: async () => {
      const items = targets.map((p) => ({ productId: p.id, copies: copies[p.id] ?? 1 }));

      if (canPrintRaw('label')) {
        const tspl = await productsApi.labelsTspl(items);
        const raw = await printRawViaDesktop(await toBase64(new Blob([tspl])), 'label');
        if (raw.ok) return { ok: true as const };
        // Sabab YO'QOLMAYDI: zaxira yo'l ham ishlamasa, kassirga aynan
        // shu xabar ko'rsatiladi («Yorliq printeri tanlanmagan» kabi).
        const labels = await productsApi.labelImages(items, size.w, size.h);
        const outcome = await printLabels(labels, size.w, size.h);
        return outcome === 'failed'
          ? { ok: false as const, problem: raw.problem }
          : { ok: true as const };
      }

      const labels = await productsApi.labelImages(items, size.w, size.h);
      const outcome = await printLabels(labels, size.w, size.h);
      return outcome === 'failed' ? { ok: false as const } : { ok: true as const };
    },
    onSuccess: (result) => {
      if (!result.ok) {
        setError(result.problem ?? t('labels.printFailed'));
        return;
      }
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

        {/* O'lcham — TANLANMAYDI, ko'rsatiladi. Rulon do'konning fizik
            xususiyati va u Sozlamalarda bir marta beriladi. */}
        <div className="flex flex-wrap items-center gap-2">
          <span className="text-[13px] font-medium text-label">{t('labels.size')}</span>
          <span className="rounded-input bg-hairline px-3 py-1.5 text-[13px] font-medium nums">
            {size.w}×{size.h} {t('labels.mm')}
          </span>
          <span className="text-[12.5px] text-muted-2">{t('labels.sizeFromSettings')}</span>
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
