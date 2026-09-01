import { useEffect, useState, type ReactNode } from 'react';
import { useParams } from 'react-router-dom';
import { useQuery, useMutation, useQueryClient } from '@tanstack/react-query';
import { useTranslation } from 'react-i18next';
import { Check } from 'lucide-react';
import { PageHeader, Button, Card, Toggle, Spinner, LanguageSwitch, useConfirm } from '@/shared/ui';
import { cn } from '@/shared/lib/cn';
import { formatShortDate, formatTime } from '@/shared/lib/format';
import { useSyncFreshness } from '@/shared/sync/useSyncFreshness';
import { SUPPORTED_LANGUAGES, LANGUAGE_LABELS, type AppLanguage } from '@/shared/i18n';
import { accountApi } from '@/features/account/api';
import { settingsApi, type MarketSettings } from './api';
import { DesktopDownload } from '@/features/desktop/DesktopDownload';

export default function SettingsPage() {
  const { t } = useTranslation();
  const qc = useQueryClient();
  const [form, setForm] = useState<MarketSettings | null>(null);
  const [savedFlash, setSavedFlash] = useState(false);

  const query = useQuery({ queryKey: ['market-settings'], queryFn: settingsApi.get });

  useEffect(() => {
    if (query.data) setForm(query.data);
  }, [query.data]);

  const mutation = useMutation({
    mutationFn: (body: MarketSettings) => settingsApi.update(body),
    onSuccess: (data) => {
      qc.setQueryData(['market-settings'], data);
      setForm(data);
      setSavedFlash(true);
      setTimeout(() => setSavedFlash(false), 2500);
    },
  });

  // Interfeys tili — do'kon sozlamasi emas, foydalanuvchining SHAXSIY sozlamasi,
  // shuning uchun sahifaning umumiy "Saqlash" tugmasiga bog'lanmagan: bosilishi
  // bilan UI o'zgaradi va hisobga yoziladi (boshqa brauzerda ham eslab qolinsin).
  const langMutation = useMutation({
    mutationFn: (language: AppLanguage) => accountApi.updateProfile({ language }),
  });

  const set = <K extends keyof MarketSettings>(key: K, value: MarketSettings[K]) =>
    setForm((f) => (f ? { ...f, [key]: value } : f));

  if (query.isLoading || !form) {
    return (
      <>
        <PageHeader title={t('settings.title')} subtitle={t('settings.subtitle')} />
        <div className="flex flex-1 items-center justify-center text-primary">
          <Spinner size={26} />
        </div>
      </>
    );
  }

  return (
    <>
      <PageHeader
        title={t('settings.title')}
        subtitle={t('settings.subtitle')}
        actions={
          <>
            {savedFlash && (
              <span className="flex items-center gap-1.5 text-[13px] font-medium text-success">
                <Check size={15} /> {t('settings.saved')}
              </span>
            )}
            <Button loading={mutation.isPending} onClick={() => mutation.mutate(form)}>
              {t('common.save')}
            </Button>
          </>
        }
      />

      {/*
        Kartalar USTUNLARGA joylashadi, katakchalarga emas.
        Ilgari bu `grid-cols-2` edi: qator balandligi eng baland karta
        bo'yicha olinar va uning yonidagi past karta ostida katta bo'sh joy
        qolardi — bitta qatorli «Interfeys tili» kartasi yonidagi «Do'kon»
        kartasi shunga misol edi. Ustunlarda esa har bir karta oldingisining
        ostiga TEGIB turadi, bo'sh joy umuman qolmaydi va sahifa sezilarli
        qisqaradi.
      */}
      <div className="flex-1 columns-1 gap-[18px] p-4 sm:p-6 lg:columns-2 lg:p-8">
        {/* Магазин */}
        <Section title={t('settings.store.title')}>
          <TextRow label={t('settings.store.phone')} value={form.phone ?? ''} onChange={(v) => set('phone', v)} />
          <TextRow label={t('settings.store.address')} value={form.address ?? ''} onChange={(v) => set('address', v)} />
        </Section>

        {/* Касса и смены */}
        <Section title={t('settings.cash.title')}>
          <ToggleRow
            label={t('settings.cash.onlyShift')}
            hint={t('settings.cash.onlyShiftHint')}
            checked={form.salesOnlyWhenShiftOpen}
            onChange={(v) => set('salesOnlyWhenShiftOpen', v)}
          />
          {/* «Ombor va narxlar» kartasidan KO'CHDI. U kartada ishlaydigan
              yagona sozlama shu edi va bitta almashtirgich uchun alohida
              karta ochish sahifani bekorga cho'zardi. Mazmunan ham shu
              yerga tegishli: bu kassirning sotuv qoidasi. */}
          <ToggleRow
            label={t('settings.stock.belowCost')}
            hint={t('settings.stock.belowCostHint')}
            checked={form.blockSaleBelowCost}
            onChange={(v) => set('blockSaleBelowCost', v)}
          />
          <ToggleRow
            label={t('settings.cash.approval')}
            hint={t('settings.cash.approvalHint')}
            checked={form.cashWithdrawalNeedsApproval}
            onChange={(v) => set('cashWithdrawalNeedsApproval', v)}
          />
          <ToggleRow
            label={t('settings.cash.debtRegulars')}
            hint={t('settings.cash.debtRegularsHint')}
            checked={form.debtOnlyForRegulars}
            onChange={(v) => set('debtOnlyForRegulars', v)}
          />
          <NumberRow
            label={t('settings.cash.debtLimit')}
            hint={t('settings.cash.debtLimitHint')}
            value={form.defaultDebtLimit}
            onChange={(v) => set('defaultDebtLimit', v)}
            suffix={t('common.currency')}
            wide
          />
          <NumberRow
            label={t('settings.cash.discrepancy')}
            value={form.allowedCashDiscrepancy}
            onChange={(v) => set('allowedCashDiscrepancy', v)}
            suffix={t('common.currency')}
            wide
          />
          {/* Посещаемость — davomat rejasi (Смены → Посещаемость tab) */}
          <div className="border-t border-hairline pt-4">
            <div className="text-[14px] font-medium">{t('settings.attendance.title')}</div>
            <p className="mb-3 mt-0.5 text-[12px] text-muted-2">{t('settings.attendance.hint')}</p>
            <div className="grid grid-cols-1 sm:grid-cols-3 gap-3">
              <TimeField label={t('settings.attendance.start')} value={form.workDayStart} onChange={(v) => set('workDayStart', v)} />
              <TimeField label={t('settings.attendance.end')} value={form.workDayEnd} onChange={(v) => set('workDayEnd', v)} />
              <TimeField label={t('settings.attendance.late')} value={form.lateThreshold} onChange={(v) => set('lateThreshold', v)} />
            </div>
          </div>
        </Section>

        {/* Чек — rulon eni va avtomatik chop etish */}
        <Section title={t('settings.receipt.title')} subtitle={t('settings.receipt.subtitle')}>
          <div className="flex items-center justify-between gap-4">
            <div className="min-w-0">
              <div className="text-[14px] font-medium">{t('settings.receipt.width')}</div>
              <div className="mt-0.5 text-[12.5px] text-muted-2">{t('settings.receipt.widthHint')}</div>
            </div>
            {/* Faqat ikki qiymat: bular termal rulonlarning standart
                o'lchamlari va boshqasini kiritish faqat xato bo'ladi —
                noto'g'ri en bilan chek qog'ozga sig'masdi. */}
            <div className="flex flex-none gap-2">
              {[58, 80].map((mm) => (
                <button
                  key={mm}
                  type="button"
                  onClick={() => set('receiptWidthMm', mm)}
                  className={cn(
                    'rounded-btn border px-4 py-2 text-[13.5px] font-medium transition-colors nums',
                    form.receiptWidthMm === mm
                      ? 'border-primary bg-primary/5 text-primary'
                      : 'border-input-border text-muted hover:border-primary hover:text-primary',
                  )}
                >
                  {mm} {t('settings.receipt.mm')}
                </button>
              ))}
            </div>
          </div>
          <ToggleRow
            label={t('settings.receipt.autoPrint')}
            hint={t('settings.receipt.autoPrintHint')}
            checked={form.autoPrintReceipt}
            onChange={(v) => set('autoPrintReceipt', v)}
          />
          <TextRow
            label={t('settings.receipt.header')}
            value={form.receiptHeader ?? ''}
            onChange={(v) => set('receiptHeader', v)}
          />
          <TextRow
            label={t('settings.receipt.footer')}
            value={form.receiptFooter ?? ''}
            onChange={(v) => set('receiptFooter', v)}
          />
        </Section>

        {/* Уведомления */}
        <Section title={t('settings.notify.title')} subtitle={t('settings.notify.subtitle')}>
          <ToggleRow label={t('settings.notify.daySummary')} checked={form.notifyDaySummary} onChange={(v) => set('notifyDaySummary', v)} />
          <ToggleRow label={t('settings.notify.overdue')} checked={form.notifyOverdueDebts} onChange={(v) => set('notifyOverdueDebts', v)} />
          <ToggleRow label={t('settings.notify.withdrawals')} checked={form.notifyWithdrawalRequests} onChange={(v) => set('notifyWithdrawalRequests', v)} />
          {/* Telegram bog'lash bu yerdan Аккаунт'ga ko'chdi: bot endi har bir
              xodimni o'z ID si bo'yicha taniydi, market darajasida emas. */}
          <p className="pt-2 text-[12.5px] text-muted-2">{t('settings.notify.telegramHint')}</p>
        </Section>

        {/* Система — tillar, audit, avto-logout */}
        <Section title={t('settings.system.title')} subtitle={t('settings.system.subtitle')}>
          {/*
            Ikkala til SHU YERDA, yonma-yon. Ilgari ular ikki xil kartada
            turardi va ekranda bir xil ko'rinardi — bir xil uchta tugma,
            bir-biridan uzoqda. Ega qaysi biri nimani o'zgartirishini
            faqat izohni o'qib bilardi; yonma-yon turganda esa farq
            ko'rinib turadi.

            Sozlamalar EMAS: interfeys tili — hisobning shaxsiy sozlamasi
            va bosilishi bilan yoziladi, «Saqlash» tugmasiga bog'liq emas.
          */}
          <div className="flex items-center justify-between gap-4">
            <div className="min-w-0">
              <div className="text-[14px] font-medium">{t('settings.language.title')}</div>
              <div className="mt-0.5 text-[12.5px] text-muted-2">
                {langMutation.isError ? (
                  <span className="text-danger">{t('settings.language.saveError')}</span>
                ) : (
                  t('settings.language.hint')
                )}
              </div>
            </div>
            <LanguageSwitch onChange={(lang) => langMutation.mutate(lang)} />
          </div>
          <div className="flex items-center justify-between gap-4">
            <div className="min-w-0">
              <div className="text-[14px] font-medium">{t('settings.system.defaultLanguage')}</div>
              <div className="mt-0.5 text-[12.5px] text-muted-2">{t('settings.system.defaultLanguageHint')}</div>
            </div>
            <div className="inline-flex rounded-pill bg-hairline p-1">
              {SUPPORTED_LANGUAGES.map((lang) => (
                <button
                  key={lang}
                  type="button"
                  onClick={() => set('defaultLanguage', lang)}
                  className={cn(
                    'rounded-pill px-4 py-[7px] text-[13.5px] transition-colors',
                    form.defaultLanguage === lang
                      ? 'bg-surface font-semibold text-text shadow-card'
                      : 'font-medium text-muted hover:text-text',
                  )}
                >
                  {LANGUAGE_LABELS[lang]}
                </button>
              ))}
            </div>
          </div>
          <NumberRow
            label={t('settings.system.inactivityLogout')}
            hint={t('settings.system.inactivityLogoutHint')}
            value={form.inactivityLogoutMinutes}
            onChange={(v) => set('inactivityLogoutMinutes', v)}
            suffix={t('settings.system.minutes')}
          />
          <ToggleRow
            label={t('settings.system.audit')}
            hint={t('settings.system.auditHint')}
            checked={form.auditEnabled}
            onChange={(v) => set('auditEnabled', v)}
          />
        </Section>

        {/* Bulutga bog'langan kompyuter — uni bekor qilish yo'li */}
        <TerminalsSection />

        {/* Do'kon dasturi — internetsiz ishlaydigan kassa */}
        <DesktopSection />
      </div>
    </>
  );
}

/**
 * Do'konning bulutga bog'langan kompyuteri va uni UZISH tugmasi.
 *
 * <p><b>Nega kerak edi.</b> Bitta do'konga bir vaqtda faqat bitta kompyuter
 * bog'lanadi. Server kompyuter almashtirilsa, yangisini bog'lash oynasi
 * «avval eskisini bekor qiling» deb rad etardi — lekin bu amalni faqat
 * SuperAdmin bajara olardi va unga ham interfeys yo'q edi. Ya'ni xabar
 * mavjud bo'lmagan panelga yo'naltirar, egasi esa qo'llab-quvvatlashsiz
 * kompyuterini almashtira olmasdi.</p>
 *
 * <p><b>Nega faqat bulutda.</b> <code>ShopTerminals</code> jadvali do'kon
 * nusxasiga hech qachon tortilmaydi — do'kon kompyuterida u har doim bo'sh.
 * U yerda kartani ko'rsatish «hech narsa bog'lanmagan» degan yolg'on
 * xulosaga olib kelardi.</p>
 */
function TerminalsSection() {
  const { t, i18n } = useTranslation();
  const qc = useQueryClient();
  const confirm = useConfirm();
  const freshness = useSyncFreshness();

  const onShopMachine = freshness.data?.isShopMachine ?? false;

  const query = useQuery({
    queryKey: ['shop-terminals'],
    queryFn: settingsApi.terminals,
    enabled: !onShopMachine,
  });

  const revoke = useMutation({
    mutationFn: (id: string) => settingsApi.revokeTerminal(id),
    onSuccess: () => qc.invalidateQueries({ queryKey: ['shop-terminals'] }),
  });

  if (onShopMachine || freshness.isLoading) return null;

  const terminals = query.data ?? [];
  const when = (iso: string) => `${formatShortDate(iso, i18n.language)} ${formatTime(iso)}`;

  async function askRevoke(id: string, name: string) {
    const ok = await confirm({
      title: t('settings.terminals.confirmTitle'),
      message: t('settings.terminals.confirmBody', { name }),
      confirmLabel: t('settings.terminals.revoke'),
      tone: 'danger',
    });
    if (ok) revoke.mutate(id);
  }

  return (
    <Section title={t('settings.terminals.title')} subtitle={t('settings.terminals.subtitle')}>
      {query.isLoading ? (
        <div className="flex justify-center py-2 text-primary">
          <Spinner size={20} />
        </div>
      ) : terminals.length === 0 ? (
        <p className="text-[13px] text-muted">{t('settings.terminals.none')}</p>
      ) : (
        terminals.map((terminal) => (
          <div
            key={terminal.id}
            className={cn(
              'flex items-start justify-between gap-3 rounded-input border border-hairline px-3 py-2.5',
              terminal.revokedAtUtc && 'opacity-55',
            )}
          >
            <div className="min-w-0">
              <div className="flex items-center gap-2">
                <span className="truncate text-[14px] font-medium">{terminal.name}</span>
                {terminal.revokedAtUtc && (
                  <span className="flex-none rounded-pill bg-danger-soft px-2 py-0.5 text-[11.5px] text-danger">
                    {t('settings.terminals.revoked')}
                  </span>
                )}
              </div>
              <p className="mt-0.5 text-[12px] text-muted-2">
                {t('settings.terminals.pairedAt')}: {when(terminal.pairedAt)}
                {' · '}
                {t('settings.terminals.lastSeen')}:{' '}
                {terminal.lastSeenAtUtc ? when(terminal.lastSeenAtUtc) : t('settings.terminals.never')}
                {terminal.lastIpAddress ? ` · ${terminal.lastIpAddress}` : ''}
              </p>
            </div>
            {!terminal.revokedAtUtc && (
              <Button
                variant="secondary"
                size="sm"
                className="flex-none"
                loading={revoke.isPending}
                onClick={() => void askRevoke(terminal.id, terminal.name)}
              >
                {t('settings.terminals.revoke')}
              </Button>
            )}
          </div>
        ))
      )}
      <p className="text-[12px] text-muted-2">{t('settings.terminals.hint')}</p>
    </Section>
  );
}

/**
 * Do'kon dasturini yuklab olish.
 *
 * <p><b>Nega aynan sozlamalarda.</b> Dasturni o'rnatish — do'kon egasining
 * bir martalik ishi, kundalik amal emas. Uni menyuga alohida chiqarish
 * kassirlarga hech qachon kerak bo'lmaydigan bandni har kuni ko'rsatardi.</p>
 *
 * <p>Manzil serverdan so'raladi va sahifaga qattiq yozilmaydi: paket turgan
 * papka ataylab sir (deploy/README.md → «Desktop yangilanishlari»).</p>
 *
 * <p>Ko'rinishning O'ZI <c>features/desktop</c> da — u do'konning o'z
 * yuklab olish sahifasida ham ishlatiladi va ikki nusxa bo'lsa biri
 * eskirib qolardi.</p>
 */
function DesktopSection() {
  const { t } = useTranslation();
  const { subdomain } = useParams();

  return (
    <Section title={t('desktop.title')} subtitle={t('desktop.subtitle')}>
      <DesktopDownload subdomain={subdomain!} />
    </Section>
  );
}

function Section({ title, subtitle, children }: { title: string; subtitle?: string; children: ReactNode }) {
  return (
    // `break-inside-avoid` — karta ustunlar orasida IKKIGA bo'linmasin;
    // pastki chekka esa ustunlar ichidagi oraliqni beradi (ustun maketida
    // `gap` faqat ustunlar ORASIGA qo'llanadi).
    <Card className="mb-[18px] break-inside-avoid p-4 sm:p-6">
      <div className="mb-5">
        <h2 className="text-[16px] font-semibold">{title}</h2>
        {subtitle && <p className="mt-0.5 text-[12.5px] text-muted-2">{subtitle}</p>}
      </div>
      <div className="flex flex-col gap-4">{children}</div>
    </Card>
  );
}

function ToggleRow({
  label,
  hint,
  checked,
  onChange,
}: {
  label: string;
  hint?: string;
  checked: boolean;
  onChange: (v: boolean) => void;
}) {
  return (
    <div className="flex items-center justify-between gap-4">
      <div className="min-w-0">
        <div className="text-[14px] font-medium">{label}</div>
        {hint && <div className="mt-0.5 text-[12.5px] text-muted-2">{hint}</div>}
      </div>
      <Toggle checked={checked} onChange={onChange} />
    </div>
  );
}

function TextRow({ label, value, onChange }: { label: string; value: string; onChange: (v: string) => void }) {
  return (
    <div className="flex flex-col gap-1.5">
      <label className="text-[13px] font-medium text-label">{label}</label>
      <input
        value={value}
        onChange={(e) => onChange(e.target.value)}
        className="h-11 rounded-input border border-input-border bg-surface px-3.5 text-[14px] outline-none focus:border-primary focus:shadow-focus-ring"
      />
    </div>
  );
}

function NumberRow({
  label,
  hint,
  value,
  onChange,
  suffix,
  wide,
}: {
  label: string;
  hint?: string;
  value: number;
  onChange: (v: number) => void;
  suffix?: string;
  wide?: boolean;
}) {
  // Mirror the numeric value as text so 0 can render as an empty field (with a
  // faint "0" placeholder) instead of a literal 0 the user must delete first.
  // Keeping the raw text also preserves in-progress input like "0." or "1.5".
  const [text, setText] = useState(value === 0 ? '' : String(value));

  useEffect(() => {
    // Only resync from the outside when it really differs from what's typed —
    // otherwise a re-render would wipe a partially typed decimal.
    setText((prev) => (Number(prev || 0) === value ? prev : value === 0 ? '' : String(value)));
  }, [value]);

  return (
    <div className="flex items-center justify-between gap-4">
      <div className="min-w-0">
        <div className="text-[14px] font-medium">{label}</div>
        {hint && <div className="mt-0.5 text-[12.5px] text-muted-2">{hint}</div>}
      </div>
      <div className="flex items-center gap-2">
        <input
          type="number"
          step="any"
          placeholder="0"
          value={text}
          onChange={(e) => {
            setText(e.target.value);
            onChange(Number(e.target.value) || 0);
          }}
          className={`h-10 rounded-input border border-input-border bg-surface px-3 text-right text-[14px] outline-none focus:border-primary focus:shadow-focus-ring nums ${wide ? 'w-[150px]' : 'w-[90px]'}`}
        />
        {suffix && <span className="text-[13px] text-muted-2">{suffix}</span>}
      </div>
    </div>
  );
}

/** Kichik "HH:mm" vaqt maydoni (davomat rejasi sozlamalari uchun). */
function TimeField({ label, value, onChange }: { label: string; value: string; onChange: (v: string) => void }) {
  return (
    <label className="flex flex-col gap-1.5">
      <span className="text-[12.5px] font-medium text-label">{label}</span>
      <input
        type="time"
        value={value}
        onChange={(e) => onChange(e.target.value)}
        className="h-10 rounded-input border border-input-border bg-surface px-3 text-[14px] outline-none focus:border-primary focus:shadow-focus-ring nums"
      />
    </label>
  );
}
