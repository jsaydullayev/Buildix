import { useEffect } from 'react';
import { Link, Navigate, useNavigate, useParams } from 'react-router-dom';
import { useForm } from 'react-hook-form';
import { zodResolver } from '@hookform/resolvers/zod';
import { z } from 'zod';
import { useTranslation } from 'react-i18next';
import { useQuery } from '@tanstack/react-query';
import { Phone, Mail, Send } from 'lucide-react';
import { BrandLogo, Button, Input, LanguageSwitch } from '@/shared/ui';
import { useAuth, useLogin } from '@/shared/auth/useAuth';
import { useFirstAccessiblePath } from '@/shared/auth/useFirstAccessiblePath';
import { publicMarketApi } from '@/shared/api/auth';
import type { ApiError } from '@/shared/api/types';

const schema = z.object({
  username: z.string().min(3),
  password: z.string().min(1),
});
type FormValues = z.infer<typeof schema>;

export function LoginPage() {
  const { subdomain } = useParams();
  const { t } = useTranslation();
  const navigate = useNavigate();
  const { session, isAuthenticated } = useAuth();
  const login = useLogin();
  const home = useFirstAccessiblePath();

  // Market state for this slug (name + subscription hint). Non-blocking.
  const marketQuery = useQuery({
    queryKey: ['public-market', subdomain],
    queryFn: () => publicMarketApi.getState(subdomain!),
    enabled: !!subdomain,
    retry: false,
  });

  const {
    register,
    handleSubmit,
    setError,
    formState: { errors, isSubmitting },
  } = useForm<FormValues>({ resolver: zodResolver(schema) });

  useEffect(() => {
    if (login.isSuccess && subdomain) navigate(home, { replace: true });
  }, [login.isSuccess, subdomain, navigate, home]);

  // Already signed in for this tenant → skip the form.
  if (isAuthenticated && session?.subdomain && session.subdomain === subdomain) {
    return <Navigate to={home} replace />;
  }

  const onSubmit = handleSubmit(async (values) => {
    try {
      await login.mutateAsync({ ...values, subdomain: subdomain ?? null });
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
    <div className="flex min-h-screen flex-col justify-between bg-surface px-[60px] pb-8 pt-10 text-text">
      {/* Header */}
      <div className="flex items-center justify-between">
        <Link to="/">
          <BrandLogo />
        </Link>
        <LanguageSwitch />
      </div>

      {/* Center form */}
      <div className="mx-auto w-full max-w-auth">
        {marketQuery.data?.marketName && (
          <div className="mb-4 text-center text-[13px] font-semibold uppercase tracking-wide text-primary">
            {marketQuery.data.marketName}
          </div>
        )}
        <h1 className="mb-2.5 text-center text-[33px] font-semibold tracking-[-0.4px]">
          {t('auth.title')}
        </h1>
        <p className="mb-8 text-center text-[15px] leading-[1.55] text-muted">{t('auth.subtitle')}</p>

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
            labelAddon={
              <a href="#" className="text-[13px] text-primary hover:text-primary-hover">
                {t('auth.forgotPassword')}
              </a>
            }
            error={errors.password?.message}
            {...register('password')}
          />
          <Button type="submit" size="lg" fullWidth loading={isSubmitting} className="mt-1.5">
            {isSubmitting ? t('auth.submitting') : t('auth.submit')}
          </Button>
        </form>

        {/* Contact admin */}
        <div className="mt-7 border-t border-hairline pt-[22px]">
          <div className="mb-3 text-center text-[12px] font-semibold tracking-[1.2px] text-muted-2">
            {t('auth.contactAdmin')}
          </div>
          <div className="flex flex-col items-center gap-[9px] text-[13.5px] text-label">
            <a href="tel:+998901234567" className="flex items-center gap-2.5 hover:text-primary">
              <Phone size={15} className="text-primary" /> +998 90 123 45 67
            </a>
            <a href="mailto:admin@buildix.uz" className="flex items-center gap-2.5 hover:text-primary">
              <Mail size={15} className="text-primary" /> admin@buildix.uz
            </a>
            <a href="https://t.me/buildix_admin" className="flex items-center gap-2.5 hover:text-primary">
              <Send size={15} className="text-primary" /> @buildix_admin
            </a>
          </div>
        </div>
      </div>

      {/* Footer */}
      <div className="self-center text-[12px] text-input-border">{t('brand.footer')}</div>
    </div>
  );
}
