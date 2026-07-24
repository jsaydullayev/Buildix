import { useEffect, useState, type ReactNode } from 'react';
import { useQuery, useMutation, useQueryClient } from '@tanstack/react-query';
import { useTranslation } from 'react-i18next';
import { Check } from 'lucide-react';
import { PageHeader, Button, Card, Toggle, Spinner, Badge, LanguageSwitch } from '@/shared/ui';
import { cn } from '@/shared/lib/cn';
import { SUPPORTED_LANGUAGES, LANGUAGE_LABELS, type AppLanguage } from '@/shared/i18n';
import { accountApi } from '@/features/account/api';
import { settingsApi, type MarketSettings } from './api';

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

      <div className="grid flex-1 grid-cols-2 items-start gap-[18px] p-8">
        {/* Язык интерфейса */}
        <Section title={t('settings.language.title')} subtitle={t('settings.language.subtitle')}>
          <div className="flex items-center justify-between gap-4">
            <div className="min-w-0">
              <div className="text-[14px] font-medium">{t('settings.language.label')}</div>
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
        </Section>

        {/* Магазин */}
        <Section title={t('settings.store.title')}>
          <TextRow label={t('settings.store.phone')} value={form.phone ?? ''} onChange={(v) => set('phone', v)} />
          <TextRow label={t('settings.store.address')} value={form.address ?? ''} onChange={(v) => set('address', v)} />
          <TextRow label={t('settings.store.hours')} value={form.workingHours ?? ''} onChange={(v) => set('workingHours', v)} />
        </Section>

        {/* Склад и цены */}
        <Section title={t('settings.stock.title')}>
          <ToggleRow
            label={t('settings.stock.minAlert')}
            hint={t('settings.stock.minAlertHint')}
            checked={form.minStockAlertEnabled}
            onChange={(v) => set('minStockAlertEnabled', v)}
          />
          <ToggleRow
            label={t('settings.stock.belowCost')}
            hint={t('settings.stock.belowCostHint')}
            checked={form.blockSaleBelowCost}
            onChange={(v) => set('blockSaleBelowCost', v)}
          />
          <NumberRow
            label={t('settings.stock.markup')}
            hint={t('settings.stock.markupHint')}
            value={form.defaultMarkupPct}
            onChange={(v) => set('defaultMarkupPct', v)}
            suffix="%"
          />
        </Section>

        {/* Касса и смены */}
        <Section title={t('settings.cash.title')}>
          <ToggleRow
            label={t('settings.cash.onlyShift')}
            hint={t('settings.cash.onlyShiftHint')}
            checked={form.salesOnlyWhenShiftOpen}
            onChange={(v) => set('salesOnlyWhenShiftOpen', v)}
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
        </Section>

        {/* Уведомления */}
        <Section title={t('settings.notify.title')} subtitle={t('settings.notify.subtitle')}>
          <ToggleRow label={t('settings.notify.daySummary')} checked={form.notifyDaySummary} onChange={(v) => set('notifyDaySummary', v)} />
          <ToggleRow label={t('settings.notify.overdue')} checked={form.notifyOverdueDebts} onChange={(v) => set('notifyOverdueDebts', v)} />
          <ToggleRow label={t('settings.notify.withdrawals')} checked={form.notifyWithdrawalRequests} onChange={(v) => set('notifyWithdrawalRequests', v)} />
          <div className="flex items-center justify-between gap-4 pt-2">
            <div className="flex items-center gap-2">
              <span className="text-[14px] font-medium">{t('settings.notify.telegram')}</span>
              <Badge tone={form.ownerTelegramLinked ? 'success' : 'neutral'}>
                {form.ownerTelegramLinked ? t('settings.notify.linked') : t('settings.notify.notLinked')}
              </Badge>
            </div>
            <input
              value={form.ownerTelegram ?? ''}
              onChange={(e) => set('ownerTelegram', e.target.value)}
              placeholder="@username"
              className="h-11 w-[220px] rounded-input border border-input-border bg-surface px-3.5 text-[14px] outline-none focus:border-primary focus:shadow-focus-ring"
            />
          </div>
        </Section>

        {/* Система — do'kon standart tili (yangi xodim uchun), audit, avto-logout */}
        <Section title={t('settings.system.title')} subtitle={t('settings.system.subtitle')}>
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
      </div>
    </>
  );
}

function Section({ title, subtitle, children }: { title: string; subtitle?: string; children: ReactNode }) {
  return (
    <Card className="p-6">
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
