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
import { consoleApi, publicMarketApi } from '@/shared/api/auth';
import type { ApiError } from '@/shared/api/types';
import { ROLES } from '@/shared/config/permissions';

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

  // Qo'llab-quvvatlash kontaktlari — platforma sozlamalaridan. Bloklanmaydi:
  // so'rov yiqilsa, blok umuman ko'rsatilmaydi va login shundoq ishlayveradi.
  const supportQuery = useQuery({
    queryKey: ['public-support'],
    queryFn: publicMarketApi.support,
    retry: false,
    staleTime: 5 * 60_000,
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

  // Allaqachon SuperAdmin sifatida kirgan bo'lsa, formani qayta ko'rsatmaymiz:
  // uning uyi — konsol. Segment sessiyada saqlanmaydi (u sir), shuning uchun
  // har safar serverdan so'raladi.
  useEffect(() => {
    if (!isAuthenticated || subdomain || session?.role !== ROLES.SuperAdmin) return;
    let cancelled = false;
    consoleApi
      .segment()
      .then((segment) => {
        if (!cancelled) navigate(`/_sa/${segment}/dashboard`, { replace: true });
      })
      // Segment sozlanmagan — formada qolamiz, xatoni login urinishi ko'rsatadi.
      .catch(() => undefined);
    return () => {
      cancelled = true;
    };
  }, [isAuthenticated, subdomain, session?.role, navigate]);

  // Already signed in for this tenant → skip the form.
  if (isAuthenticated && session?.subdomain && session.subdomain === subdomain) {
    return <Navigate to={home} replace />;
  }

  // Ildizdagi login (`/login`), lekin sessiya allaqachon bor — do'koniga
  // qaytaramiz. Ilgari bu holat formani QAYTA ko'rsatardi: foydalanuvchi
  // hali kirgan bo'lsa ham parol so'ralardi va bu «tizim o'zi chiqarib
  // yubordi» bo'lib ko'rinardi. Do'kon dasturi aynan shu manzilni ochadi,
  // ya'ni har ochilishda kassir qaytadan kirishga majbur bo'lardi.
  //
  // `home` bu yerda ishlamaydi — u manzildagi sub-yo'lga tayanadi va u
  // hozir yo'q; do'kon ildiziga yuboramiz, u yoqda IndexRedirect o'zi
  // kerakli sahifani tanlaydi.
  if (isAuthenticated && !subdomain && session?.subdomain && session.role !== ROLES.SuperAdmin) {
    return <Navigate to={`/${session.subdomain}`} replace />;
  }

  const onSubmit = handleSubmit(async (values) => {
    try {
      const data = await login.mutateAsync({ ...values, subdomain: subdomain ?? null });

      // SuperAdmin hech qaysi do'konga tegishli emas — uning uyi konsol.
      // Yashirin segmentni QO'LDA yozish shart emas: u autentifikatsiyadan
      // keyin server tomonidan beriladi (segment pre-auth qatlami bo'lib
      // qolaveradi — skaner uni bilmaydi).
      if (data.role === ROLES.SuperAdmin) {
        try {
          const segment = await consoleApi.segment();
          navigate(`/_sa/${segment}/dashboard`, { replace: true });
        } catch {
          // Segment sozlanmagan bo'lsa konsol ochilmaydi — buni jimgina
          // yutib yubormaymiz, aks holda operator «login ishlamadi» deb
          // o'ylardi.
          setError('password', { message: t('auth.errors.consoleNotConfigured') });
        }
        return;
      }

      // Slug'siz (ildizdagi) login — do'kon xodimi ham shu yerdan kirishi
      // mumkin: uni o'z do'koniga yuboramiz, aks holda muvaffaqiyatli login
      // hech qayerga olib bormay, forma joyida turib qolardi.
      if (!subdomain) {
        // Do'kon nomi bo'sh bo'lsa boradigan manzil YO'Q. Ilgari bu shart
        // jimgina yolg'on bo'lib, funksiya hech narsa qilmasdan tugardi:
        // server 200 qaytargan, sessiya saqlangan, lekin ekranda kirish
        // formasi turaverardi. Do'kon dasturida aynan shu holat yuz berdi
        // va tashqaridan u «Kirish tugmasi ishlamayapti» bo'lib ko'rindi —
        // jurnalda esa muvaffaqiyatli kirish yozilgan edi.
        if (!data.subdomain) {
          setError('password', { message: t('auth.errors.marketMissing') });
          return;
        }
        navigate(`/${data.subdomain}`, { replace: true });
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
          {/* Kontaktlar platforma sozlamalaridan (SuperAdmin → Настройки).
              Ilgari ular shu yerga yozib qo'yilgan edi — operator sozlamani
              o'zgartirsa, kirish sahifasi eskisini ko'rsatib turaverardi. */}
          <div className="flex flex-col items-center gap-[9px] text-[13.5px] text-label">
            {supportQuery.data?.phone && (
              <a
                href={`tel:${supportQuery.data.phone.replace(/\s/g, '')}`}
                className="flex items-center gap-2.5 hover:text-primary"
              >
                <Phone size={15} className="text-primary" /> {supportQuery.data.phone}
              </a>
            )}
            {supportQuery.data?.email && (
              <a
                href={`mailto:${supportQuery.data.email}`}
                className="flex items-center gap-2.5 hover:text-primary"
              >
                <Mail size={15} className="text-primary" /> {supportQuery.data.email}
              </a>
            )}
            {supportQuery.data?.telegram && (
              <a
                href={`https://t.me/${supportQuery.data.telegram.replace(/^@/, '')}`}
                className="flex items-center gap-2.5 hover:text-primary"
              >
                <Send size={15} className="text-primary" /> {supportQuery.data.telegram}
              </a>
            )}
          </div>
        </div>
      </div>

      {/* Footer */}
      <div className="self-center text-[12px] text-input-border">{t('brand.footer')}</div>
    </div>
  );
}
