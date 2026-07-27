import { useEffect } from 'react';
import { Navigate, useNavigate, useParams } from 'react-router-dom';
import { useForm } from 'react-hook-form';
import { zodResolver } from '@hookform/resolvers/zod';
import { z } from 'zod';
import { useTranslation } from 'react-i18next';
import { BrandLogo, Button, Input } from '@/shared/ui';
import { useAuth, useLogin, useLogout } from '@/shared/auth/useAuth';
import { ROLES } from '@/shared/config/permissions';
import type { ApiError } from '@/shared/api/types';

const schema = z.object({
  username: z.string().min(3),
  password: z.string().min(1),
});
type FormValues = z.infer<typeof schema>;

/**
 * Konsolga kirish. Do'kon login sahifasidan ikki farqi bor:
 *
 * 1. <b>Slug yuborilmaydi.</b> SuperAdmin hech qaysi marketga tegishli emas;
 *    backend slug'siz loginni market-agnostik qilib qabul qiladi va obuna
 *    eshigidan o'tkazmaydi (AuthService.Login).
 * 2. <b>Rol tekshiriladi.</b> To'g'ri login/parol bilan kirgan do'kon xodimi
 *    ham token oladi — lekin bu yerda unga o'rin yo'q: sessiya darhol
 *    yopiladi. Aks holda konsol qobig'i uning uchun ochilib, har bir so'rov
 *    403 bilan qaytadigan "buzuq panel" ko'rinardi.
 */
export function SuperLoginPage() {
  const { segment } = useParams();
  const { t } = useTranslation();
  const navigate = useNavigate();
  const { session, isAuthenticated, hasRole } = useAuth();
  const login = useLogin();
  const logout = useLogout();

  const {
    register,
    handleSubmit,
    setError,
    formState: { errors, isSubmitting },
  } = useForm<FormValues>({ resolver: zodResolver(schema) });

  useEffect(() => {
    if (login.isSuccess && session?.role === ROLES.SuperAdmin) {
      navigate(`/_sa/${segment}/dashboard`, { replace: true });
    }
  }, [login.isSuccess, session?.role, navigate, segment]);

  if (isAuthenticated && hasRole(ROLES.SuperAdmin)) {
    return <Navigate to={`/_sa/${segment}/dashboard`} replace />;
  }

  const onSubmit = handleSubmit(async (values) => {
    try {
      const data = await login.mutateAsync({ ...values, subdomain: null });
      if (data.role !== ROLES.SuperAdmin) {
        await logout();
        setError('password', { message: t('sa.auth.notSuperAdmin') });
      }
    } catch (err) {
      const apiErr = err as ApiError;
      const message =
        apiErr.status === 401
          ? t('auth.errors.invalidCredentials')
          : apiErr.status === 429
            ? t('auth.errors.tooManyAttempts')
            : (apiErr.message ?? t('auth.errors.generic'));
      setError('password', { message });
    }
  });

  return (
    <div
      data-theme="super"
      className="flex min-h-screen flex-col justify-between bg-surface px-[60px] pb-8 pt-10 text-text"
    >
      <div className="flex items-center gap-2">
        <BrandLogo />
        <span className="rounded-pill border border-primary/30 bg-primary-soft px-2.5 py-[3px] text-[10px] font-bold tracking-[0.5px] text-primary">
          SUPER
        </span>
      </div>

      <div className="mx-auto w-full max-w-auth">
        <h1 className="mb-2.5 text-center text-[33px] font-semibold tracking-[-0.4px]">
          {t('sa.auth.title')}
        </h1>
        <p className="mb-8 text-center text-[15px] leading-[1.55] text-muted">
          {t('sa.auth.subtitle')}
        </p>

        <form onSubmit={onSubmit} className="flex flex-col gap-[18px]" noValidate>
          <Input
            label={t('auth.usernameLabel')}
            placeholder={t('auth.usernamePlaceholder')}
            autoComplete="username"
            autoFocus
            error={errors.username ? t('auth.errors.invalidCredentials') : undefined}
            {...register('username')}
          />
          <Input
            type="password"
            label={t('auth.passwordLabel')}
            placeholder={t('auth.passwordPlaceholder')}
            autoComplete="current-password"
            error={errors.password?.message}
            {...register('password')}
          />
          <Button type="submit" size="lg" fullWidth loading={isSubmitting} className="mt-1.5">
            {isSubmitting ? t('auth.submitting') : t('auth.submit')}
          </Button>
        </form>
      </div>

      <div className="self-center text-[12px] text-input-border">{t('brand.footer')}</div>
    </div>
  );
}
