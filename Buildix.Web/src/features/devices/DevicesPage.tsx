import { useState } from 'react';
import { useMutation } from '@tanstack/react-query';
import { useTranslation } from 'react-i18next';
import { Printer, ScanLine, Check, X, AlertTriangle, Download } from 'lucide-react';
import { PageHeader, Card, Button, Badge, Spinner } from '@/shared/ui';
import { cn } from '@/shared/lib/cn';
import { downloadBlob } from '@/shared/lib/download';
import type { ApiError } from '@/shared/api/types';
import { posApi } from '@/features/pos/api';
import { devicesApi } from './api';
import { useScannerProbe } from './useScannerProbe';

const SIZES = [
  { key: '58x40', w: 58, h: 40 },
  { key: '40x30', w: 40, h: 30 },
  { key: '30x20', w: 30, h: 20 },
] as const;

/** Printer sinovining holati. */
type PrintState =
  | { kind: 'idle' }
  | { kind: 'blocked'; blob: Blob }      // brauzer yangi oynani bloklagan
  | { kind: 'sent' }                     // oyna ochildi, natijani foydalanuvchi aytadi
  | { kind: 'ok' }
  | { kind: 'failed' }
  | { kind: 'error'; message: string };

/**
 * Qurilmalar — printer va skaner ulanishini tekshirish.
 *
 * <p>Brauzer operatsion tizimdagi printerlar ro'yxatini KO'RA OLMAYDI, shuning
 * uchun "ulangan printerlar ro'yxati" texnik jihatdan mumkin emas. Buning
 * o'rniga eng ishonchli tekshiruv beriladi: haqiqiy sinov yorlig'i chop
 * etiladi. Chiqdi — zanjir butun (brauzer → PDF → printer). Chiqmadi — qaysi
 * bo'g'in uzilgani shu yerda aytiladi.</p>
 *
 * <p>Skaner esa klaviatura bo'lib ko'rinadi, ya'ni uni ro'yxatdan topib
 * bo'lmaydi — lekin terish tezligidan aniq ajratsa bo'ladi.</p>
 */
export default function DevicesPage() {
  const { t } = useTranslation();
  const [size, setSize] = useState<(typeof SIZES)[number]>(SIZES[0]);
  const [print, setPrint] = useState<PrintState>({ kind: 'idle' });

  const scanner = useScannerProbe();
  const [lookup, setLookup] = useState<{ state: 'idle' | 'busy' | 'found' | 'missing'; name?: string }>({
    state: 'idle',
  });

  const testPrint = useMutation({
    mutationFn: () => devicesApi.testLabel(size.w, size.h),
    onSuccess: (blob) => {
      const url = URL.createObjectURL(blob);
      const win = window.open(url, '_blank', 'noopener');
      // window.open null qaytarsa — bu brauzerning qalqib chiquvchi oynalar
      // bloki. Foydalanuvchi buni "printer ishlamadi" deb tushunadi, shuning
      // uchun sababini aytamiz va yuklab olish yo'lini beramiz.
      if (!win) {
        setPrint({ kind: 'blocked', blob });
        URL.revokeObjectURL(url);
        return;
      }
      setPrint({ kind: 'sent' });
      setTimeout(() => URL.revokeObjectURL(url), 60_000);
    },
    onError: (e) =>
      setPrint({ kind: 'error', message: (e as unknown as ApiError).message ?? t('common.somethingWrong') }),
  });

  async function checkCode(code: string) {
    setLookup({ state: 'busy' });
    try {
      const p = await posApi.findByBarcode(code);
      setLookup(p ? { state: 'found', name: p.name } : { state: 'missing' });
    } catch {
      setLookup({ state: 'missing' });
    }
  }

  return (
    <>
      <PageHeader title={t('devices.title')} subtitle={t('devices.subtitle')} />

      <div className="mx-auto flex w-full max-w-[980px] flex-1 flex-col gap-[18px] p-4 sm:p-6 lg:p-8">
        {/* ── Printer ─────────────────────────────────────────────────── */}
        <Card className="p-4 sm:p-6">
          <div className="flex items-start gap-3">
            <span className="flex h-10 w-10 flex-none items-center justify-center rounded-lg bg-primary-soft text-primary">
              <Printer size={20} />
            </span>
            <div className="min-w-0 flex-1">
              <h2 className="text-[16px] font-semibold">{t('devices.printer.title')}</h2>
              <p className="mt-0.5 text-[12.5px] text-muted-2">{t('devices.printer.subtitle')}</p>
            </div>
          </div>

          <div className="mt-4 flex flex-col gap-3">
            <div className="flex flex-wrap items-center gap-2">
              <span className="text-[13px] font-medium text-label">{t('devices.printer.size')}</span>
              {SIZES.map((s) => (
                <button
                  key={s.key}
                  type="button"
                  onClick={() => setSize(s)}
                  className={cn(
                    'flex-none whitespace-nowrap rounded-input px-3.5 py-2 text-[13px] font-medium transition-colors',
                    size.key === s.key
                      ? 'bg-primary text-white'
                      : 'border border-input-border bg-surface text-muted hover:text-text',
                  )}
                >
                  {s.w}×{s.h} {t('labels.mm')}
                </button>
              ))}
            </div>

            <div className="flex flex-wrap items-center gap-3">
              <Button loading={testPrint.isPending} onClick={() => { setPrint({ kind: 'idle' }); testPrint.mutate(); }}>
                <Printer size={15} />
                {t('devices.printer.test')}
              </Button>
              {print.kind === 'sent' && (
                <>
                  <span className="text-[13px] text-muted">{t('devices.printer.askResult')}</span>
                  <Button variant="secondary" onClick={() => setPrint({ kind: 'ok' })}>
                    <Check size={15} /> {t('devices.printer.yes')}
                  </Button>
                  <Button variant="secondary" onClick={() => setPrint({ kind: 'failed' })}>
                    <X size={15} /> {t('devices.printer.no')}
                  </Button>
                </>
              )}
            </div>

            {print.kind === 'ok' && (
              <StatusLine tone="ok" text={t('devices.printer.ok')} />
            )}
            {print.kind === 'error' && (
              <StatusLine tone="bad" text={print.message} />
            )}
            {print.kind === 'blocked' && (
              <div className="flex flex-col gap-2 rounded-card border border-warn-amber/40 bg-warn-soft px-4 py-3">
                <StatusLine tone="warn" text={t('devices.printer.popupBlocked')} />
                <Button
                  variant="secondary"
                  className="self-start"
                  onClick={() => downloadBlob(print.blob, 'buildix-test-label.pdf')}
                >
                  <Download size={15} /> {t('devices.printer.download')}
                </Button>
              </div>
            )}
            {print.kind === 'failed' && (
              <div className="rounded-card border border-danger/25 bg-danger-soft px-4 py-3">
                <StatusLine tone="bad" text={t('devices.printer.troubleTitle')} />
                <ul className="mt-2 flex list-disc flex-col gap-1 pl-5 text-[12.5px] text-muted">
                  <li>{t('devices.printer.trouble1')}</li>
                  <li>{t('devices.printer.trouble2')}</li>
                  <li>{t('devices.printer.trouble3')}</li>
                  <li>{t('devices.printer.trouble4')}</li>
                </ul>
              </div>
            )}
          </div>
        </Card>

        {/* ── Skaner ──────────────────────────────────────────────────── */}
        <Card className="p-4 sm:p-6">
          <div className="flex items-start gap-3">
            <span className="flex h-10 w-10 flex-none items-center justify-center rounded-lg bg-primary-soft text-primary">
              <ScanLine size={20} />
            </span>
            <div className="min-w-0 flex-1">
              <h2 className="text-[16px] font-semibold">{t('devices.scanner.title')}</h2>
              <p className="mt-0.5 text-[12.5px] text-muted-2">{t('devices.scanner.subtitle')}</p>
            </div>
          </div>

          <div className="mt-4 flex flex-col gap-3">
            <input
              autoFocus
              placeholder={t('devices.scanner.placeholder')}
              onKeyDown={scanner.onKeyDown}
              onChange={() => {}}
              value=""
              className="h-12 w-full rounded-input border border-input-border bg-surface px-4 text-[15px] outline-none focus:border-primary focus:shadow-focus-ring nums"
            />

            {scanner.verdict && (
              <div
                className={cn(
                  'flex flex-col gap-2 rounded-card border px-4 py-3',
                  scanner.verdict.kind === 'scanner'
                    ? 'border-success/25 bg-success-soft'
                    : 'border-warn-amber/40 bg-warn-soft',
                )}
              >
                <StatusLine
                  tone={scanner.verdict.kind === 'scanner' ? 'ok' : 'warn'}
                  text={
                    scanner.verdict.kind === 'scanner'
                      ? t('devices.scanner.detected')
                      : scanner.verdict.kind === 'typed'
                        ? t('devices.scanner.typed')
                        : t('devices.scanner.tooShort')
                  }
                />
                <div className="flex flex-wrap gap-x-5 gap-y-1 text-[12.5px] text-muted nums">
                  <span>{t('devices.scanner.code')}: <b>{scanner.verdict.code || '—'}</b></span>
                  <span>{t('devices.scanner.chars')}: {scanner.verdict.chars}</span>
                  <span>{t('devices.scanner.speed')}: {scanner.verdict.avgGapMs} {t('devices.scanner.ms')}</span>
                  <span>
                    Enter:{' '}
                    {scanner.verdict.endedWithEnter ? t('devices.printer.yes') : t('devices.printer.no')}
                  </span>
                </div>
                {!scanner.verdict.endedWithEnter && scanner.verdict.kind === 'scanner' && (
                  <StatusLine tone="warn" text={t('devices.scanner.noEnter')} />
                )}

                <div className="flex flex-wrap items-center gap-3 pt-1">
                  <Button
                    variant="secondary"
                    disabled={!scanner.verdict.code}
                    onClick={() => void checkCode(scanner.verdict!.code)}
                  >
                    {t('devices.scanner.checkInBase')}
                  </Button>
                  {lookup.state === 'busy' && <Spinner size={16} className="text-primary" />}
                  {lookup.state === 'found' && (
                    <Badge tone="success">{t('devices.scanner.found', { name: lookup.name ?? '' })}</Badge>
                  )}
                  {lookup.state === 'missing' && <Badge tone="warn">{t('devices.scanner.notFound')}</Badge>}
                </div>
              </div>
            )}

            <p className="text-[12px] text-muted-2">{t('devices.scanner.hint')}</p>
          </div>
        </Card>
      </div>
    </>
  );
}

function StatusLine({ tone, text }: { tone: 'ok' | 'warn' | 'bad'; text: string }) {
  const Icon = tone === 'ok' ? Check : tone === 'warn' ? AlertTriangle : X;
  return (
    <span
      className={cn(
        'flex items-start gap-2 text-[13px] font-medium',
        tone === 'ok' ? 'text-success' : tone === 'warn' ? 'text-warn-strong' : 'text-danger',
      )}
    >
      <Icon size={15} className="mt-0.5 flex-none" />
      {text}
    </span>
  );
}
