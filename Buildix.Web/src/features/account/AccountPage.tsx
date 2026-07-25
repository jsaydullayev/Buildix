import { useEffect, useState } from 'react';
import { useQuery, useMutation, useQueryClient } from '@tanstack/react-query';
import { useTranslation } from 'react-i18next';
import { Monitor, Smartphone, Check, CreditCard, PackageX, Clock } from 'lucide-react';
import type { LucideIcon } from 'lucide-react';
import { PageHeader, Button, Card, Badge, Spinner, StatCard, Toggle } from '@/shared/ui';
import { cn } from '@/shared/lib/cn';
import { formatRelative, formatShortDate, formatTime, formatSum } from '@/shared/lib/format';
import { useAuth } from '@/shared/auth/useAuth';
import { useSessionStore } from '@/shared/auth/sessionStore';
import { shiftsApi } from '@/features/shifts/api';
import { accountApi, type Session } from './api';

function initials(name: string) {
  const p = name.trim().split(/\s+/);
  return (p[0]?.[0] ?? '') + (p[1]?.[0] ?? '');
}

export default function AccountPage() {
  const { t, i18n } = useTranslation();
  const { session } = useAuth();
  const qc = useQueryClient();

  const [fullName, setFullName] = useState(session?.fullName ?? '');
  const [phone, setPhone] = useState('');
  const [telegram, setTelegram] = useState('');
  const [current, setCurrent] = useState('');
  const [next, setNext] = useState('');
  const [repeat, setRepeat] = useState('');
  const [profileSaved, setProfileSaved] = useState(false);
  const [pwSaved, setPwSaved] = useState(false);
  const [pwError, setPwError] = useState<string | null>(null);
  // Per-user Telegram toggles — seeded from the profile, saved on each flip.
  const [notify, setNotify] = useState({ debt: true, stock: true, shift: true });

  const isSeller = session?.role === 'Seller';
  const profileQuery = useQuery({ queryKey: ['my-profile'], queryFn: accountApi.profile });
  const sessionsQuery = useQuery({ queryKey: ['sessions'], queryFn: accountApi.sessions });
  const historyQuery = useQuery({ queryKey: ['login-history'], queryFn: accountApi.loginHistory });
  // «Мои результаты · <oy>» — kassirning shu oylik shaxsiy natijasi (self-service).
  const resultsQuery = useQuery({
    queryKey: ['my-shifts', 'month'],
    queryFn: () => shiftsApi.myHistory('month'),
    enabled: isSeller,
  });

  // Seed the editable fields once the profile loads.
  useEffect(() => {
    if (profileQuery.data) {
      setFullName(profileQuery.data.fullName);
      setPhone(profileQuery.data.phone ?? '');
      setTelegram(profileQuery.data.telegram ?? '');
      setNotify({
        debt: profileQuery.data.notifyDebt,
        stock: profileQuery.data.notifyStock,
        shift: profileQuery.data.notifyShift,
      });
    }
  }, [profileQuery.data]);

  const profileMutation = useMutation({
    mutationFn: () => accountApi.updateProfile({ fullName, phone, telegram }),
    onSuccess: () => {
      const s = useSessionStore.getState().session;
      if (s) useSessionStore.getState().setSession({ ...s, fullName });
      void qc.invalidateQueries({ queryKey: ['my-profile'] });
      setProfileSaved(true);
      setTimeout(() => setProfileSaved(false), 2500);
    },
  });

  const pwMutation = useMutation({
    mutationFn: () => accountApi.updateProfile({ currentPassword: current, newPassword: next }),
    onSuccess: () => {
      setCurrent('');
      setNext('');
      setRepeat('');
      setPwSaved(true);
      setTimeout(() => setPwSaved(false), 2500);
    },
    onError: () => setPwError(t('auth.errors.generic')),
  });

  const revokeMutation = useMutation({
    mutationFn: () => accountApi.revokeOthers(session?.refreshToken ?? ''),
    onSuccess: () => void qc.invalidateQueries({ queryKey: ['sessions'] }),
  });

  const revokeOneMutation = useMutation({
    mutationFn: (id: string) => accountApi.revokeSession(id),
    onSuccess: () => void qc.invalidateQueries({ queryKey: ['sessions'] }),
  });

  // Toggle a single Telegram-notification preference — optimistic, saved at once.
  const notifyMutation = useMutation({
    mutationFn: (body: { notifyDebt?: boolean; notifyStock?: boolean; notifyShift?: boolean }) =>
      accountApi.updateProfile(body),
    onSuccess: () => void qc.invalidateQueries({ queryKey: ['my-profile'] }),
  });
  const flipNotify = (key: 'debt' | 'stock' | 'shift', value: boolean) => {
    setNotify((n) => ({ ...n, [key]: value }));
    const field = key === 'debt' ? 'notifyDebt' : key === 'stock' ? 'notifyStock' : 'notifyShift';
    notifyMutation.mutate({ [field]: value });
  };

  const submitPassword = () => {
    setPwError(null);
    if (next !== repeat) {
      setPwError(t('account.password.mismatch'));
      return;
    }
    pwMutation.mutate();
  };

  const inputCls =
    'h-12 rounded-input border border-input-border bg-surface px-4 text-[15px] outline-none focus:border-primary focus:shadow-focus-ring';
  const sessions = sessionsQuery.data ?? [];
  const hasOthers = sessions.some((s) => !s.isCurrent);

  return (
    <>
      <PageHeader
        title={t('account.title')}
        subtitle={t('account.subtitle')}
        actions={
          <>
            {profileSaved && (
              <span className="flex items-center gap-1.5 text-[13px] font-medium text-success">
                <Check size={15} /> {t('settings.saved')}
              </span>
            )}
            <Button loading={profileMutation.isPending} onClick={() => profileMutation.mutate()}>
              {t('common.save')}
            </Button>
          </>
        }
      />

      <div className="grid flex-1 grid-cols-2 items-start gap-[18px] p-8">
        {/* Left column */}
        <div className="flex flex-col gap-[18px]">
          {/* «Мои результаты · <oy>» — kassir shaxsiy oylik natijasi */}
          {isSeller && (
            <Card className="p-6">
              <h2 className="mb-4 text-[16px] font-semibold">
                {t('account.myResults.title', {
                  month: new Intl.DateTimeFormat(i18n.language, { month: 'long' }).format(new Date()),
                })}
              </h2>
              {resultsQuery.isLoading ? (
                <div className="flex justify-center py-6 text-primary">
                  <Spinner size={20} />
                </div>
              ) : (
                <div className="grid grid-cols-2 gap-3">
                  <StatCard label={t('account.myResults.revenue')} value={formatSum(resultsQuery.data?.totalRevenue ?? 0)} suffix={t('common.currency')} />
                  <StatCard label={t('account.myResults.checks')} value={resultsQuery.data?.totalChecks ?? 0} />
                  <StatCard label={t('account.myResults.avgCheck')} value={formatSum(resultsQuery.data?.avgCheck ?? 0)} suffix={t('common.currency')} />
                  <StatCard label={t('account.myResults.shifts')} value={resultsQuery.data?.items.length ?? 0} />
                </div>
              )}
            </Card>
          )}

          {/* Profile */}
          <Card className="p-6">
            <div className="mb-5 flex items-center gap-4">
              <div className="flex h-16 w-16 items-center justify-center rounded-pill bg-primary text-[22px] font-semibold uppercase text-white">
                {session ? initials(session.fullName) : '—'}
              </div>
              <div>
                <div className="text-[18px] font-semibold">{session?.fullName}</div>
                <Badge tone="info" className="mt-1">
                  {session?.role}
                </Badge>
              </div>
            </div>
            <div className="flex flex-col gap-4">
              <div className="flex flex-col gap-1.5">
                <label className="text-[13px] font-medium text-label">{t('account.fullName')}</label>
                <input
                  className={cn(inputCls, isSeller && 'bg-bg text-muted')}
                  value={fullName}
                  onChange={(e) => setFullName(e.target.value)}
                  disabled={isSeller}
                />
                {isSeller && <span className="text-[11.5px] text-muted-2">{t('account.nameLocked')}</span>}
              </div>
              <div className="grid grid-cols-2 gap-4">
                <div className="flex flex-col gap-1.5">
                  <label className="text-[13px] font-medium text-label">{t('account.phone')}</label>
                  <input className={inputCls} value={phone} onChange={(e) => setPhone(e.target.value)} placeholder="+998 90 123 45 67" />
                </div>
                <div className="flex flex-col gap-1.5">
                  <label className="text-[13px] font-medium text-label">{t('account.telegram')}</label>
                  <input className={inputCls} value={telegram} onChange={(e) => setTelegram(e.target.value)} placeholder="@username" />
                </div>
              </div>
              <div className="flex flex-col gap-1.5">
                <label className="text-[13px] font-medium text-label">{t('account.login')}</label>
                <input className={cn(inputCls, 'bg-bg text-muted')} value={session?.username ?? ''} disabled />
                <span className="text-[11.5px] text-muted-2">{t('account.loginLocked')}</span>
              </div>
            </div>
          </Card>

          {/* Password */}
          <Card className="p-6">
            <div className="mb-5">
              <h2 className="text-[16px] font-semibold">{t('account.password.title')}</h2>
              <p className="mt-0.5 text-[12.5px] text-muted-2">{t('account.password.hint')}</p>
            </div>
            <div className="flex flex-col gap-4">
              <input className={inputCls} type="password" placeholder={t('account.password.current')} value={current} onChange={(e) => setCurrent(e.target.value)} autoComplete="current-password" />
              <div className="grid grid-cols-2 gap-4">
                <input className={inputCls} type="password" placeholder={t('account.password.new')} value={next} onChange={(e) => setNext(e.target.value)} autoComplete="new-password" />
                <input className={inputCls} type="password" placeholder={t('account.password.repeat')} value={repeat} onChange={(e) => setRepeat(e.target.value)} autoComplete="new-password" />
              </div>
              {pwError && <span className="text-[12.5px] text-danger">{pwError}</span>}
              {pwSaved && <span className="text-[12.5px] text-success">{t('account.password.changed')}</span>}
              <Button
                variant="secondary"
                className="self-start"
                disabled={!current || !next || !repeat}
                loading={pwMutation.isPending}
                onClick={submitPassword}
              >
                {t('account.password.change')}
              </Button>
            </div>
          </Card>
        </div>

        {/* Notifications + sessions + login history */}
        <div className="flex flex-col gap-[18px]">
        {/* Telegram notification preferences (BE-9) */}
        <Card className="p-6">
          <div className="mb-4">
            <h2 className="text-[16px] font-semibold">{t('account.notify.title')}</h2>
            <p className="mt-0.5 text-[12.5px] text-muted-2">{t('account.notify.subtitle')}</p>
          </div>
          <div className="flex flex-col">
            <NotifyRow
              icon={CreditCard}
              label={t('account.notify.debt')}
              checked={notify.debt}
              onChange={(v) => flipNotify('debt', v)}
            />
            <NotifyRow
              icon={PackageX}
              label={t('account.notify.stock')}
              checked={notify.stock}
              onChange={(v) => flipNotify('stock', v)}
            />
            <NotifyRow
              icon={Clock}
              label={t('account.notify.shift')}
              checked={notify.shift}
              onChange={(v) => flipNotify('shift', v)}
            />
          </div>
          {!telegram.trim() && <p className="mt-3 text-[11.5px] text-warn-strong">{t('account.notify.noTelegram')}</p>}
        </Card>

        <Card className="p-6">
          <div className="mb-5">
            <h2 className="text-[16px] font-semibold">{t('account.sessions.title')}</h2>
            <p className="mt-0.5 text-[12.5px] text-muted-2">{t('account.sessions.subtitle')}</p>
          </div>
          {sessionsQuery.isLoading ? (
            <div className="flex justify-center py-10 text-primary">
              <Spinner size={22} />
            </div>
          ) : sessions.length === 0 ? (
            <p className="py-8 text-center text-[13px] text-muted-2">{t('account.sessions.empty')}</p>
          ) : (
            <div className="flex flex-col gap-3">
              {sessions.map((s) => (
                <SessionRow
                  key={s.id}
                  session={s}
                  lang={i18n.language}
                  onRevoke={() => revokeOneMutation.mutate(s.id)}
                  revoking={revokeOneMutation.isPending && revokeOneMutation.variables === s.id}
                />
              ))}
            </div>
          )}
          {hasOthers && (
            <Button
              variant="danger"
              fullWidth
              className="mt-5"
              loading={revokeMutation.isPending}
              onClick={() => revokeMutation.mutate()}
            >
              {t('account.sessions.revokeOthers')}
            </Button>
          )}
        </Card>

        {/* Login history */}
        <Card className="p-6">
          <div className="mb-4">
            <h2 className="text-[16px] font-semibold">{t('account.history.title')}</h2>
            <p className="mt-0.5 text-[12.5px] text-muted-2">{t('account.history.subtitle')}</p>
          </div>
          {historyQuery.isLoading ? (
            <div className="flex justify-center py-8 text-primary">
              <Spinner size={20} />
            </div>
          ) : (historyQuery.data ?? []).length === 0 ? (
            <p className="py-6 text-center text-[13px] text-muted-2">{t('account.history.empty')}</p>
          ) : (
            <div className="flex flex-col">
              {(historyQuery.data ?? []).map((h) => (
                <div
                  key={h.id}
                  className="flex items-center justify-between gap-3 border-b border-hairline py-2.5 text-[13px] last:border-0"
                >
                  <span className="w-[128px] flex-none text-muted-2 nums">
                    {formatShortDate(h.atUtc, i18n.language)} {formatTime(h.atUtc)}
                  </span>
                  <span className="flex-1 truncate text-muted">{h.device ?? '—'}</span>
                  <Badge tone={h.success ? 'success' : 'danger'}>
                    {h.success ? t('account.history.success') : t('account.history.failed')}
                  </Badge>
                </div>
              ))}
            </div>
          )}
        </Card>
        </div>
      </div>
    </>
  );
}

function NotifyRow({
  icon: Icon,
  label,
  checked,
  onChange,
}: {
  icon: LucideIcon;
  label: string;
  checked: boolean;
  onChange: (v: boolean) => void;
}) {
  return (
    <div className="flex items-center gap-3 border-b border-hairline py-3 last:border-0">
      <span className="flex h-9 w-9 flex-none items-center justify-center rounded-lg bg-bg text-muted">
        <Icon size={17} />
      </span>
      <span className="flex-1 text-[13.5px] font-medium">{label}</span>
      <Toggle checked={checked} onChange={onChange} />
    </div>
  );
}

function SessionRow({
  session: s,
  lang,
  onRevoke,
  revoking,
}: {
  session: Session;
  lang: string;
  onRevoke: () => void;
  revoking: boolean;
}) {
  const { t } = useTranslation();
  const isMobile = /iphone|ipad|android/i.test(s.device ?? '');
  return (
    <div className="flex items-center gap-3 rounded-input border border-hairline px-4 py-3">
      <div className="flex h-10 w-10 flex-none items-center justify-center rounded-lg bg-hairline text-muted">
        {isMobile ? <Smartphone size={18} /> : <Monitor size={18} />}
      </div>
      <div className="min-w-0 flex-1">
        <div className="flex items-center gap-2">
          <span className="truncate text-[13.5px] font-semibold">{s.device ?? '—'}</span>
          {s.isCurrent && <Badge tone="success">{t('account.sessions.thisDevice')}</Badge>}
        </div>
        <div className="truncate text-[12px] text-muted-2">
          {s.ipAddress ?? '—'} ·{' '}
          {s.lastUsedAt ? formatRelative(s.lastUsedAt, lang) : t('account.sessions.now')}
        </div>
      </div>
      {!s.isCurrent && (
        <button
          type="button"
          onClick={onRevoke}
          disabled={revoking}
          className="flex-none rounded-input border border-input-border px-3 py-1.5 text-[12.5px] font-medium text-muted transition-colors hover:border-danger hover:text-danger disabled:opacity-50"
        >
          {t('account.sessions.revoke')}
        </button>
      )}
    </div>
  );
}
