import { useEffect, useState } from 'react';
import { useParams } from 'react-router-dom';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { useTranslation } from 'react-i18next';
import { Check } from 'lucide-react';
import { PageHeader, Card, Button, Input, Toggle, Spinner, Badge } from '@/shared/ui';
import type { ApiError } from '@/shared/api/types';
import { superAdminApi, type SaSettings } from './api';

export default function SuperSettingsPage() {
  const { segment = '' } = useParams();
  const { t } = useTranslation();
  const qc = useQueryClient();

  const [form, setForm] = useState<SaSettings | null>(null);
  const [saved, setSaved] = useState(false);

  const query = useQuery({
    queryKey: ['sa-settings', segment],
    queryFn: () => superAdminApi.settings(segment),
  });

  useEffect(() => {
    if (query.data) setForm(query.data);
  }, [query.data]);

  const save = useMutation({
    mutationFn: () => superAdminApi.updateSettings(segment, form!),
    onSuccess: (data) => {
      setForm(data);
      setSaved(true);
      setTimeout(() => setSaved(false), 2500);
      // Narx va tarif limitlari boshqa ekranlarda ham ko'rinadi.
      void qc.invalidateQueries({ queryKey: ['sa-plans', segment] });
      void qc.invalidateQueries({ queryKey: ['sa-billing', segment] });
      void qc.invalidateQueries({ queryKey: ['sa-dashboard', segment] });
    },
  });

  const err = save.error
    ? ((save.error as unknown as ApiError).message ?? t('common.somethingWrong'))
    : null;

  const patch = (p: Partial<SaSettings>) => {
    setForm((f) => (f ? { ...f, ...p } : f));
    setSaved(false);
  };

  if (query.isLoading || !form) {
    return (
      <div className="flex flex-1 items-center justify-center text-primary">
        <Spinner size={26} />
      </div>
    );
  }

  return (
    <>
      <PageHeader
        title={t('sa.settings.title')}
        subtitle={t('sa.settings.subtitle')}
        actions={
          saved ? (
            <span className="flex items-center gap-1.5 text-[13px] font-medium text-success">
              <Check size={15} /> {t('sa.settings.saved')}
            </span>
          ) : undefined
        }
      />

      <div className="grid grid-cols-1 gap-5 p-4 sm:p-6 lg:grid-cols-2 lg:p-8">
        {/* Тарифы */}
        <Card className="p-4 sm:p-6">
          <h2 className="text-[15px] font-semibold">{t('sa.settings.plansTitle')}</h2>
          <p className="mb-4 mt-0.5 text-[12.5px] text-muted-2">{t('sa.settings.plansHint')}</p>
          <div className="flex flex-col gap-3">
            {form.plans.map((p) => (
              // Telefonda nishon + tavsif + narx maydoni bir qatorga sig'maydi:
              // narx maydoni pastga tushib, to'liq enni egallaydi.
              <div key={p.code} className="flex flex-col gap-2 sm:flex-row sm:items-center sm:gap-3">
                <div className="flex min-w-0 items-center gap-3">
                  <Badge tone="info">{t(`sa.billing.plans.${p.code}` as never)}</Badge>
                  <span className="min-w-0 flex-1 text-[12.5px] text-muted">
                    {t('sa.billing.planLimits', {
                      points: p.maxPoints,
                      users: p.maxUsers === 0 ? t('sa.billing.unlimited') : p.maxUsers,
                    })}
                  </span>
                </div>
                <input
                  className="nums h-11 w-full flex-none rounded-input border-[1.5px] border-input-border px-3 text-right text-[15px] outline-none focus:border-primary focus:shadow-focus-ring sm:w-[150px]"
                  inputMode="numeric"
                  value={p.priceUzs === 0 ? '' : p.priceUzs}
                  onChange={(e) =>
                    patch({
                      plans: form.plans.map((x) =>
                        x.code === p.code
                          ? { ...x, priceUzs: Number(e.target.value.replace(/\D/g, '')) || 0 }
                          : x,
                      ),
                    })
                  }
                />
              </div>
            ))}
          </div>
        </Card>

        {/* Telegram bildirishnomalari (SMS o'rniga) */}
        <Card className="p-4 sm:p-6">
          <h2 className="text-[15px] font-semibold">{t('sa.settings.notifyTitle')}</h2>
          <p className="mb-4 mt-0.5 text-[12.5px] text-muted-2">{t('sa.settings.notifyHint')}</p>
          <div className="flex flex-col gap-3.5">
            <ToggleRow
              title={t('sa.settings.notifyExpiring')}
              sub={t('sa.settings.notifyExpiringSub', { count: form.expiryReminderDays })}
              checked={form.notifyExpiring}
              onChange={(v) => patch({ notifyExpiring: v })}
            />
            <div className="flex items-center justify-between gap-4">
              <span className="text-[13.5px]">{t('sa.settings.expiryReminderDays')}</span>
              <input
                className="nums h-11 w-[110px] flex-none rounded-input border-[1.5px] border-input-border px-3 text-right text-[15px] outline-none focus:border-primary focus:shadow-focus-ring"
                inputMode="numeric"
                value={form.expiryReminderDays === 0 ? '' : form.expiryReminderDays}
                onChange={(e) =>
                  patch({ expiryReminderDays: Number(e.target.value.replace(/\D/g, '')) || 0 })
                }
              />
            </div>
            <ToggleRow
              title={t('sa.settings.notifyBlocked')}
              sub={t('sa.settings.notifyBlockedSub')}
              checked={form.notifyBlocked}
              onChange={(v) => patch({ notifyBlocked: v })}
            />
          </div>
        </Card>

        {/* Правила блокировки */}
        <Card className="p-4 sm:p-6">
          <h2 className="text-[15px] font-semibold">{t('sa.settings.blockTitle')}</h2>
          <p className="mb-4 mt-0.5 text-[12.5px] text-muted-2">{t('sa.settings.blockHint')}</p>

          <div className="mb-4 flex items-center justify-between gap-4">
            <span className="text-[13.5px]">{t('sa.settings.graceDays')}</span>
            <input
              className="nums h-11 w-[110px] flex-none rounded-input border-[1.5px] border-input-border px-3 text-right text-[15px] outline-none focus:border-primary focus:shadow-focus-ring"
              inputMode="numeric"
              value={form.graceDays === 0 ? '' : form.graceDays}
              onChange={(e) => patch({ graceDays: Number(e.target.value.replace(/\D/g, '')) || 0 })}
            />
          </div>

          <div className="flex flex-col gap-3.5">
            <ToggleRow
              title={t('sa.settings.warnOnOverdue')}
              sub={t('sa.settings.warnOnOverdueSub')}
              checked={form.warnOnOverdue}
              onChange={(v) => patch({ warnOnOverdue: v })}
            />
            <ToggleRow
              title={t('sa.settings.restrictAfterGrace')}
              sub={t('sa.settings.restrictAfterGraceSub')}
              checked={form.restrictAfterGrace}
              onChange={(v) => patch({ restrictAfterGrace: v })}
            />
            <div className="flex items-center justify-between gap-4">
              <div className="min-w-0">
                <div className="text-[13.5px]">{t('sa.settings.fullBlockAfterDays')}</div>
                <div className="text-[11.5px] text-muted-2">
                  {t('sa.settings.fullBlockAfterDaysSub')}
                </div>
              </div>
              <input
                className="nums h-11 w-[110px] flex-none rounded-input border-[1.5px] border-input-border px-3 text-right text-[15px] outline-none focus:border-primary focus:shadow-focus-ring"
                inputMode="numeric"
                value={form.fullBlockAfterDays === 0 ? '' : form.fullBlockAfterDays}
                onChange={(e) =>
                  patch({ fullBlockAfterDays: Number(e.target.value.replace(/\D/g, '')) || 0 })
                }
              />
            </div>
          </div>
        </Card>

        {/* Контакты поддержки */}
        <Card className="p-4 sm:p-6">
          <h2 className="text-[15px] font-semibold">{t('sa.settings.supportTitle')}</h2>
          <p className="mb-4 mt-0.5 text-[12.5px] text-muted-2">{t('sa.settings.supportHint')}</p>
          <div className="flex flex-col gap-4">
            <Input
              label={t('sa.settings.supportPhone')}
              value={form.supportPhone ?? ''}
              onChange={(e) => patch({ supportPhone: e.target.value })}
              placeholder="+998 71 200 70 07"
            />
            <Input
              label={t('sa.settings.supportTelegram')}
              value={form.supportTelegram ?? ''}
              onChange={(e) => patch({ supportTelegram: e.target.value })}
              placeholder="@buildix_support"
            />
            <Input
              label={t('sa.settings.supportEmail')}
              value={form.supportEmail ?? ''}
              onChange={(e) => patch({ supportEmail: e.target.value })}
              placeholder="admin@buildix.uz"
            />
          </div>
        </Card>

        <div className="col-span-2 flex flex-col items-center gap-2">
          {err && <span className="text-[12.5px] text-danger">{err}</span>}
          <Button size="lg" onClick={() => save.mutate()} loading={save.isPending}>
            {t('common.save')}
          </Button>
          <p className="text-[11.5px] text-muted-2">{t('sa.settings.applyHint')}</p>
        </div>
      </div>
    </>
  );
}

function ToggleRow({
  title,
  sub,
  checked,
  onChange,
}: {
  title: string;
  sub: string;
  checked: boolean;
  onChange: (v: boolean) => void;
}) {
  return (
    <div className="flex items-center justify-between gap-4">
      <div className="min-w-0">
        <div className="text-[13.5px]">{title}</div>
        <div className="text-[11.5px] text-muted-2">{sub}</div>
      </div>
      <Toggle checked={checked} onChange={onChange} />
    </div>
  );
}
