import { type FormEvent, useState } from 'react';
import { Link } from 'react-router-dom';
import { useTranslation } from 'react-i18next';
import { useMutation } from '@tanstack/react-query';
import {
  BarChart3,
  Boxes,
  CheckCircle2,
  LogIn,
  type LucideIcon,
  Receipt,
  ShieldCheck,
  Truck,
  UsersRound,
  Wallet,
} from 'lucide-react';
import { apiClient } from '@/shared/api/client';
import { formatSum } from '@/shared/lib/format';
import { cn } from '@/shared/lib/cn';
import { BrandLogo, BrandMark, Button, Input, LanguageSwitch } from '@/shared/ui';

/** Subtle blueprint-grid + radial-fade backdrop used behind the hero and CTA. */
function GridBackdrop({ fade }: { fade: string }) {
  return (
    <>
      <div
        aria-hidden
        className="pointer-events-none absolute inset-0"
        style={{
          backgroundImage:
            'repeating-linear-gradient(0deg,rgba(37,99,235,.045) 0 1px,transparent 1px 48px),' +
            'repeating-linear-gradient(90deg,rgba(37,99,235,.045) 0 1px,transparent 1px 48px)',
        }}
      />
      <div
        aria-hidden
        className="pointer-events-none absolute inset-0"
        style={{ background: `radial-gradient(ellipse 800px 480px at 50% 30%, transparent 30%, ${fade} 95%)` }}
      />
    </>
  );
}

function Eyebrow({ children }: { children: React.ReactNode }) {
  return (
    <div className="text-[11px] font-semibold uppercase tracking-[2.2px] text-primary">{children}</div>
  );
}

interface ModuleDef {
  key: 'sales' | 'warehouse' | 'debts' | 'purchases' | 'employees' | 'reports';
  Icon: LucideIcon;
}

const MODULES: ModuleDef[] = [
  { key: 'sales', Icon: Receipt },
  { key: 'warehouse', Icon: Boxes },
  { key: 'debts', Icon: Wallet },
  { key: 'purchases', Icon: Truck },
  { key: 'employees', Icon: UsersRound },
  { key: 'reports', Icon: BarChart3 },
];

/** Decorative demo rows for the POS preview card (illustrative sample data). */
const DEMO_ROWS: { name: string; qty: string; total: number }[] = [
  { name: 'Cement M500, 50 kg', qty: '10', total: 850_000 },
  { name: 'Ceramic brick', qty: '500', total: 700_000 },
];
const DEMO_TOTAL = DEMO_ROWS.reduce((sum, r) => sum + r.total, 0);

export default function LandingPage() {
  const { t } = useTranslation();

  const [name, setName] = useState('');
  const [phone, setPhone] = useState('');

  const lead = useMutation({
    mutationFn: async (payload: { fullName: string; phone: string }) => {
      await apiClient.post('/RegistrationRequests', payload);
    },
  });

  const heroBullets = [
    t('landing.hero.bullets.b1'),
    t('landing.hero.bullets.b2'),
    t('landing.hero.bullets.b3'),
  ];

  function handleSubmit(e: FormEvent) {
    e.preventDefault();
    if (!name.trim() || !phone.trim() || lead.isPending) return;
    lead.mutate({ fullName: name.trim(), phone: phone.trim() });
  }

  const submitted = lead.isSuccess;

  return (
    <div className="min-h-screen bg-surface font-body text-text">
      {/* Header */}
      <header className="flex items-center justify-between border-b border-hairline px-4 py-5 sm:px-6 md:px-16">
        {/* Telefonda faqat belgi. To'liq logotip + til almashtirgich + «Kirish»
            375px ga sig'masdi (ichki eni 397px), natijada «Kirish» tugmasi
            ekrandan chiqib ketardi. Belgining o'zi ham brendni tanitadi. */}
        <BrandMark className="h-8 w-8 sm:hidden" />
        <BrandLogo className="hidden sm:inline-flex" />
        <div className="flex items-center gap-3">
          <LanguageSwitch />
          {/* Kirish — ildizdagi `/login`. Do'kon xodimi o'z do'koniga, SuperAdmin
              esa konsolga avtomatik yo'naltiriladi (LoginPage). */}
          <Link to="/login">
            <Button variant="secondary" size="sm">
              <LogIn size={15} />
              {t('landing.nav.login')}
            </Button>
          </Link>
        </div>
      </header>

      {/* Hero */}
      <section className="relative overflow-hidden">
        <GridBackdrop fade="#ffffff" />
        <div className="relative mx-auto flex max-w-3xl flex-col items-center px-6 pb-20 pt-16 text-center md:pb-24 md:pt-24">
          <Eyebrow>{t('landing.hero.eyebrow')}</Eyebrow>
          <h1 className="mt-4 font-brand text-[34px] font-semibold leading-[1.15] tracking-[-0.5px] md:text-[44px]">
            {t('landing.hero.title')}
          </h1>
          <p className="mt-5 max-w-xl text-[16px] leading-relaxed text-muted md:text-[16.5px]">
            {t('landing.hero.subtitle')}
          </p>
          <div className="mt-9 flex flex-wrap items-center justify-center gap-x-8 gap-y-3 text-[14.5px] text-label">
            {heroBullets.map((b) => (
              <span key={b} className="flex items-center gap-2.5">
                <CheckCircle2 size={20} className="shrink-0 text-primary" />
                {b}
              </span>
            ))}
          </div>
          <a href="#lead" className="mt-10">
            <Button size="lg">{t('landing.cta.submit')}</Button>
          </a>
        </div>
      </section>

      {/* Modules */}
      <section className="border-t border-hairline bg-bg px-6 py-16 md:px-16 md:py-20">
        <div className="mx-auto max-w-6xl">
          <div className="mb-12 text-center">
            <Eyebrow>{t('landing.modules.eyebrow')}</Eyebrow>
            <h2 className="mt-3 font-brand text-[26px] font-semibold tracking-[-0.3px] md:text-[30px]">
              {t('landing.modules.title')}
            </h2>
          </div>
          <div className="grid gap-[18px] sm:grid-cols-2 lg:grid-cols-3">
            {MODULES.map(({ key, Icon }) => (
              <div
                key={key}
                className="rounded-card border border-border bg-surface p-[26px] transition-shadow hover:shadow-card"
              >
                <div className="mb-[18px] flex h-10 w-10 items-center justify-center rounded-[10px] bg-primary-soft">
                  <Icon size={20} className="text-primary" strokeWidth={1.8} />
                </div>
                <h3 className="mb-1.5 text-[16.5px] font-semibold">
                  {t(`landing.modules.${key}.title` as never)}
                </h3>
                <p className="text-[13.5px] leading-[1.55] text-muted">
                  {t(`landing.modules.${key}.desc` as never)}
                </p>
              </div>
            ))}
          </div>
        </div>
      </section>

      {/* In action — POS preview band */}
      <section className="px-6 py-16 md:px-16 md:py-20">
        {/* min-w-0: grid elementining standart `min-width: auto` qiymati uni
            ichidagi eng keng bolaga qarab cho'zadi — pastdagi chek maketi
            336px, natijada telefonda butun sahifa 410px bo'lib, har bir
            bo'limning o'ng tomonida bo'sh chiziq qolardi. Dizaynga ta'sir
            qilmaydi, faqat cho'zilishni to'xtatadi. */}
        <div className="mx-auto grid max-w-6xl items-center gap-12 [&>*]:min-w-0 lg:grid-cols-2 lg:gap-16">
          <div>
            <Eyebrow>{t('landing.modules.sales.title')}</Eyebrow>
            <h2 className="mt-3 font-brand text-[24px] font-semibold tracking-[-0.2px] md:text-[26px]">
              {t('landing.hero.bullets.b1')}
            </h2>
            <p className="mt-3 max-w-md text-[14.5px] leading-relaxed text-muted">
              {t('landing.modules.sales.desc')}
            </p>
            <div className="mt-6 flex flex-col gap-3">
              {heroBullets.map((b) => (
                <span key={b} className="flex items-center gap-2.5 text-[14px] text-label">
                  <CheckCircle2 size={18} className="shrink-0 text-primary" />
                  {b}
                </span>
              ))}
            </div>
          </div>

          {/* Mock POS card */}
          <div className="rounded-2xl border border-border bg-bg p-6 md:p-8">
            <div className="overflow-hidden rounded-xl bg-surface shadow-[0_14px_40px_rgba(15,23,42,.09)]">
              <div className="flex items-center justify-between border-b border-hairline px-5 py-3.5">
                <span className="text-[13.5px] font-semibold">{t('pos.title')}</span>
                <span className="rounded-pill bg-primary-soft px-3 py-1 text-[11.5px] font-semibold text-primary-hover">
                  ≈ 15s
                </span>
              </div>
              <div className="px-5 py-1.5">
                {DEMO_ROWS.map((row, i) => (
                  <div
                    key={row.name}
                    className={cn(
                      'grid grid-cols-[1fr_64px_100px] items-center gap-3 py-2.5 text-[13px]',
                      i < DEMO_ROWS.length - 1 && 'border-b border-hairline',
                    )}
                  >
                    <span className="font-medium">{row.name}</span>
                    <span className="text-muted">{row.qty}</span>
                    <span className="nums text-right font-semibold">{formatSum(row.total)}</span>
                  </div>
                ))}
              </div>
              <div className="flex items-center justify-between border-t border-hairline bg-bg px-5 py-3 text-[13.5px]">
                <span className="text-muted">{t('pos.total')}</span>
                <span className="nums text-[16px] font-bold">{formatSum(DEMO_TOTAL)}</span>
              </div>
              <div className="flex items-center gap-2 px-5 pb-4 pt-3.5">
                <span className="rounded-btn border border-primary/30 bg-primary-soft px-3.5 py-2 text-[12.5px] font-semibold text-primary-hover">
                  {t('pos.payment.cash')}
                </span>
                <span className="rounded-btn border border-border px-3.5 py-2 text-[12.5px] font-medium text-muted">
                  {t('pos.payment.card')}
                </span>
                <span className="rounded-btn border border-border px-3.5 py-2 text-[12.5px] font-medium text-muted">
                  {t('pos.payment.debt')}
                </span>
                <Button size="sm" className="ml-auto pointer-events-none">
                  {t('pos.checkout')}
                </Button>
              </div>
            </div>
          </div>
        </div>
      </section>

      {/* Lead form */}
      <section
        id="lead"
        className="relative overflow-hidden border-t border-hairline bg-bg px-6 py-16 text-center md:px-16 md:py-20"
      >
        <GridBackdrop fade="#f6f8fb" />
        <div className="relative mx-auto max-w-2xl">
          <Eyebrow>{t('landing.cta.eyebrow')}</Eyebrow>
          <h2 className="mt-3 font-brand text-[24px] font-semibold tracking-[-0.3px] md:text-[28px]">
            {t('landing.cta.title')}
          </h2>
          <p className="mx-auto mt-3 max-w-md text-[14.5px] leading-relaxed text-muted">
            {t('landing.cta.subtitle')}
          </p>

          {submitted ? (
            <div className="mx-auto mt-8 flex max-w-md items-center justify-center gap-2.5 rounded-card border border-success-border bg-success-soft px-5 py-4 text-[14.5px] font-medium text-success-text">
              <CheckCircle2 size={20} className="shrink-0" />
              {t('landing.cta.success')}
            </div>
          ) : (
            <form
              onSubmit={handleSubmit}
              className="mx-auto mt-8 flex max-w-xl flex-col gap-3 sm:flex-row"
            >
              <Input
                type="text"
                value={name}
                onChange={(e) => setName(e.target.value)}
                placeholder={t('landing.cta.namePlaceholder')}
                aria-label={t('landing.cta.nameLabel')}
                autoComplete="name"
                className="flex-1"
                required
              />
              <Input
                type="tel"
                value={phone}
                onChange={(e) => setPhone(e.target.value)}
                placeholder={t('landing.cta.phonePlaceholder')}
                aria-label={t('landing.cta.phoneLabel')}
                autoComplete="tel"
                className="flex-1"
                required
              />
              <Button
                type="submit"
                size="lg"
                loading={lead.isPending}
                disabled={!name.trim() || !phone.trim()}
                className="shrink-0"
              >
                {t('landing.cta.submit')}
              </Button>
            </form>
          )}

          {lead.isError && !submitted && (
            <p className="mt-3 text-[13px] text-danger">{lead.error.message}</p>
          )}

          <div className="mt-5 inline-flex items-center gap-2 text-[12.5px] text-muted-2">
            <ShieldCheck size={14} className="shrink-0" />
            {t('landing.cta.privacy')}
          </div>
        </div>
      </section>

      {/* Footer */}
      <footer className="flex flex-col items-center justify-between gap-4 border-t border-hairline px-6 py-6 text-[12.5px] text-muted-2 md:flex-row md:px-16">
        <div className="flex items-center gap-2.5">
          <span className="flex h-5 w-5 items-center justify-center rounded-[5px] bg-primary font-brand text-[10px] font-bold text-white">
            B
          </span>
          <span>© 2026 Buildix · Strotech</span>
        </div>
        <a href="#" className="text-muted-2 transition-colors hover:text-primary">
          {t('landing.footer.privacy')}
        </a>
        <span>{t('landing.footer.tagline')}</span>
      </footer>
    </div>
  );
}
