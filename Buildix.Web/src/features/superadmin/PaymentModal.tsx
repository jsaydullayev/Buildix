import { useEffect, useMemo, useState } from 'react';
import { useMutation, useQuery } from '@tanstack/react-query';
import { useTranslation } from 'react-i18next';
import { ArrowRight, CalendarDays, RotateCcw } from 'lucide-react';
import { Modal, Button, Input, Spinner } from '@/shared/ui';
import { cn } from '@/shared/lib/cn';
import { formatShortDate, formatSum } from '@/shared/lib/format';
import type { ApiError } from '@/shared/api/types';
import { superAdminApi, type SaBillingRow, type SaPaymentChannel } from './api';

const CHANNELS: SaPaymentChannel[] = ['Cash', 'Click', 'Payme', 'Transfer'];
const MONTHS = [1, 3, 6, 12];

/**
 * «Оплата получена».
 *
 * <p>Uch narsa ataylab shunday:</p>
 * <ol>
 *   <li><b>Natija oldindan ko'rsatiladi</b> — «28 июля → 28 августа». Sanani
 *   server hisoblaydi (<code>payment-preview</code>), ya'ni ko'rilgan narsa
 *   yoziladi; klient formulani takrorlamaydi.</li>
 *   <li><b>Idempotency-Key</b> oyna ochilganda bir marta yaratiladi. Ikki
 *   marta bosilgan tugma ikki oy bermaydi — amal qaytarib bo'lmaydi.</li>
 *   <li>Langar izohi ko'rsatiladi: muddat eski sanadan uzaytirilganmi yoki
 *   bugundan — operator nega shunday chiqqanini ko'rib tursin.</li>
 * </ol>
 */
export function PaymentModal({
  segment,
  row,
  onClose,
  onPaid,
}: {
  segment: string;
  row: SaBillingRow | null;
  onClose: () => void;
  onPaid: () => void;
}) {
  const { t, i18n } = useTranslation();
  const [months, setMonths] = useState(1);
  const [channel, setChannel] = useState<SaPaymentChannel>('Cash');
  const [note, setNote] = useState('');
  const [idempotencyKey, setIdempotencyKey] = useState('');
  // Qo'lda kiritilgan tugash sanasi (yyyy-MM-dd). Bo'sh — muddat odatdagi
  // qoida bo'yicha hisoblanadi.
  const [customDate, setCustomDate] = useState('');

  useEffect(() => {
    if (row) {
      setMonths(1);
      setChannel('Cash');
      setNote('');
      setCustomDate('');
      // Har ochilish — bitta kalit. Qayta ochilsa yangi to'lov demak.
      setIdempotencyKey(crypto.randomUUID());
    }
  }, [row]);

  // Sana kiritilganda kun oxiri olinadi: «31 dekabrgacha» degani o'sha kun
  // tugagunicha ishlashi kerak, 00:00 da uzilib qolishi emas.
  const customIso = customDate ? new Date(`${customDate}T23:59:59`).toISOString() : null;

  const preview = useQuery({
    queryKey: ['sa-payment-preview', segment, row?.marketId, months, customIso],
    queryFn: () => superAdminApi.paymentPreview(segment, row!.marketId, months, customIso),
    enabled: !!row,
    retry: false,
  });

  const pay = useMutation({
    mutationFn: () =>
      superAdminApi.recordPayment(
        segment,
        row!.marketId,
        { months, channel, note: note.trim() || null, expiresAt: customIso },
        idempotencyKey,
      ),
    onSuccess: () => {
      onPaid();
      onClose();
    },
  });

  // Preview xatosi ham ko'rsatiladi: qo'lda kiritilgan sana yaroqsiz bo'lsa
  // («o'tgan sana») operator buni tugmani bosishdan oldin bilishi kerak.
  const err = pay.error
    ? ((pay.error as unknown as ApiError).message ?? t('common.somethingWrong'))
    : preview.error
      ? ((preview.error as unknown as ApiError).message ?? t('common.somethingWrong'))
      : null;

  const p = preview.data;
  const amount = useMemo(() => p?.amountUzs ?? (row ? row.priceUzs * months : 0), [p, row, months]);

  return (
    <Modal
      open={!!row}
      onClose={onClose}
      title={t('sa.billing.payTitle')}
      subtitle={row?.name}
      footer={
        <>
          <Button variant="ghost" onClick={onClose}>
            {t('common.cancel')}
          </Button>
          <Button onClick={() => pay.mutate()} loading={pay.isPending} disabled={!p}>
            {t('sa.billing.payConfirm')}
          </Button>
        </>
      }
    >
      <div className="flex flex-col gap-4 py-1">
        <div className="flex flex-col gap-1.5">
          <span className="text-[14.5px] font-medium text-label">{t('sa.billing.period')}</span>
          <div className="flex flex-wrap items-center gap-2">
            {MONTHS.map((m) => (
              <button
                key={m}
                type="button"
                onClick={() => setMonths(m)}
                className={cn(
                  'rounded-btn border px-4 py-2 text-[13px] transition-colors',
                  m === months
                    ? 'border-primary bg-primary font-semibold text-white'
                    : 'border-border bg-surface text-muted hover:border-primary hover:text-primary',
                )}
              >
                {t('sa.create.months', { count: m })}
              </button>
            ))}
            {/* Erkin oy soni — tayyor tugmalarda yo'q davrlar uchun (2, 9, 18…). */}
            <label className="flex items-center gap-1.5 rounded-btn border border-border bg-surface px-3 py-1.5">
              <span className="text-[12px] text-muted-2">{t('sa.billing.customMonths')}</span>
              <input
                type="text"
                inputMode="numeric"
                value={MONTHS.includes(months) ? '' : String(months)}
                placeholder="—"
                onChange={(e) => {
                  const n = Number(e.target.value.replace(/\D/g, ''));
                  if (n >= 1 && n <= 24) setMonths(n);
                  else if (!e.target.value) setMonths(1);
                }}
                className="w-9 bg-transparent text-center text-[13px] font-semibold outline-none nums"
              />
            </label>
          </div>

          {/* Tugash sanasini QO'LDA kiritish — hisoblanganidan ustun turadi.
              Kelishuv bo'yicha boshqacha sana kelishilgan holatlar uchun. */}
          <div className="mt-1 flex flex-wrap items-center gap-2">
            <label className="flex items-center gap-2 rounded-btn border border-border bg-surface px-3 py-1.5">
              <CalendarDays size={14} className="text-muted-2" />
              <span className="text-[12px] text-muted-2">{t('sa.billing.customDate')}</span>
              <input
                type="date"
                value={customDate}
                onChange={(e) => setCustomDate(e.target.value)}
                className="bg-transparent text-[13px] outline-none nums"
              />
            </label>
            {customDate && (
              <button
                type="button"
                onClick={() => setCustomDate('')}
                className="flex items-center gap-1.5 text-[12.5px] text-muted transition-colors hover:text-primary"
              >
                <RotateCcw size={13} />
                {t('sa.billing.autoDate')}
              </button>
            )}
          </div>
        </div>

        <div className="flex flex-col gap-1.5">
          <span className="text-[14.5px] font-medium text-label">{t('sa.billing.channel')}</span>
          <div className="flex items-center gap-2">
            {CHANNELS.map((c) => (
              <button
                key={c}
                type="button"
                onClick={() => setChannel(c)}
                className={cn(
                  'rounded-btn border px-4 py-2 text-[13px] transition-colors',
                  c === channel
                    ? 'border-primary bg-primary font-semibold text-white'
                    : 'border-border bg-surface text-muted hover:border-primary hover:text-primary',
                )}
              >
                {t(`sa.billing.channels.${c}` as never)}
              </button>
            ))}
          </div>
        </div>

        {/* Natija — tugmani bosishdan OLDIN. */}
        <div className="rounded-card border border-border bg-bg px-4 py-3">
          {preview.isFetching || !p ? (
            <div className="flex justify-center py-2 text-primary">
              <Spinner size={18} />
            </div>
          ) : (
            <>
              <div className="flex items-center gap-2.5 text-[15px]">
                <span className="text-muted">
                  {p.currentExpiresAt
                    ? formatShortDate(p.currentExpiresAt, i18n.language)
                    : t('sa.store.noExpiry')}
                </span>
                <ArrowRight size={15} className="text-muted-2" />
                <span className="font-semibold text-success-text">
                  {formatShortDate(p.newExpiresAt, i18n.language)}
                </span>
              </div>
              <div className="mt-1.5 text-[12.5px] text-muted">
                {p.manual
                  ? t('sa.billing.anchorManual')
                  : p.anchoredOnExpiry
                    ? t('sa.billing.anchorOnExpiry')
                    : t('sa.billing.anchorOnToday')}
              </div>
            </>
          )}
        </div>

        <div className="flex items-center justify-between rounded-card bg-primary-soft px-4 py-3">
          <span className="text-[13.5px] text-muted">{t('sa.billing.amount')}</span>
          <span className="text-[18px] font-semibold text-primary">
            {formatSum(amount)} {t('common.currency')}
          </span>
        </div>

        <Input
          label={t('sa.billing.note')}
          placeholder={t('sa.billing.notePlaceholder')}
          value={note}
          onChange={(e) => setNote(e.target.value)}
        />

        {err && <span className="text-[12.5px] text-danger">{err}</span>}
      </div>
    </Modal>
  );
}
