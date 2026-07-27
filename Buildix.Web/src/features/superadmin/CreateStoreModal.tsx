import { useEffect, useMemo, useState } from 'react';
import { useMutation, useQuery } from '@tanstack/react-query';
import { useTranslation } from 'react-i18next';
import { Check, X as XIcon, RefreshCw } from 'lucide-react';
import { Modal, Button, Input, Spinner } from '@/shared/ui';
import { useDebounce } from '@/shared/hooks/useDebounce';
import type { ApiError } from '@/shared/api/types';
import { superAdminApi, type CreateStoreResult, type SaRequestRow } from './api';

/** Chalkashadigan belgilarsiz (0/O, 1/l/I) — parol telefonda og'zaki aytiladi. */
const PASS_ALPHABET = 'abcdefghjkmnpqrstuvwxyzABCDEFGHJKMNPQRSTUVWXYZ23456789';

/**
 * Do'konning TO'LIQ havolasi.
 *
 * <p>Operator bu manzilni egasiga o'zi beradi (telefonda yoki xabarda), shuning
 * uchun sof `/sub-path` yetarli emas — domeni bilan birga kerak. Domen brauzer
 * manzilidan olinadi: konsol qaysi hostda ochilgan bo'lsa, do'kon ham o'sha
 * hostda joylashadi (SPA ham, API ham bitta origin — nginx layout).</p>
 */
function storeUrl(subdomain: string): string {
  return `${window.location.origin}/${subdomain}`;
}

function generatePassword(length = 10): string {
  const bytes = new Uint32Array(length);
  crypto.getRandomValues(bytes);
  return Array.from(bytes, (b) => PASS_ALPHABET[b % PASS_ALPHABET.length]).join('');
}

/**
 * «Создать магазин».
 *
 * <p>Maketda bu bitta tugma, lekin backend do'kon yaratish uchun login, parol va
 * do'kon nomini talab qiladi — shuning uchun modal. Sub-path qo'lda yozilmaydi:
 * u do'kon nomidan yasaladi va shu yerda jonli ko'rsatiladi
 * (<code>check-availability</code> serverning o'zi yozadigan qiymatni
 * qaytaradi, ya'ni ko'rilgan narsa saqlanadi).</p>
 *
 * <p>Parol modaldan chiqmaydi: SMS bilan yuborilmaydi (TZ BE-S7), operator uni
 * shu oynadan nusxalab, egasiga o'zi beradi. Shu sababli oyna yopilgach parol
 * yana ko'rsatilmaydi va natija ekranida ataylab bir marta chiqadi.</p>
 */
export function CreateStoreModal({
  segment,
  request,
  standalone = false,
  onClose,
  onCreated,
}: {
  segment: string;
  /** Ariza asosida yaratish. `null` + `standalone` — arizasiz yaratish. */
  request: SaRequestRow | null;
  /** «Do'konlar» sahifasidan chaqirilgan: ega ismi/telefoni qo'lda so'raladi. */
  standalone?: boolean;
  onClose: () => void;
  onCreated: () => void;
}) {
  const { t } = useTranslation();
  const [username, setUsername] = useState('');
  const [password, setPassword] = useState(() => generatePassword());
  const [marketName, setMarketName] = useState('');
  const [months, setMonths] = useState(1);
  const [error, setError] = useState<string | null>(null);
  const [result, setResult] = useState<CreateStoreResult | null>(null);
  // Arizasiz rejimda ega ma'lumotlari arizadan kelmaydi.
  const [ownerName, setOwnerName] = useState('');
  const [ownerPhone, setOwnerPhone] = useState('');

  const open = !!request || standalone;

  // Har ochilishda toza holat — oldingi arizaning qiymatlari qolib ketmasin.
  useEffect(() => {
    if (open) {
      setUsername('');
      setPassword(generatePassword());
      setMarketName('');
      setMonths(1);
      setOwnerName('');
      setOwnerPhone('');
      setError(null);
      setResult(null);
    }
  }, [open, request]);

  const debouncedUsername = useDebounce(username, 350);
  const debouncedMarketName = useDebounce(marketName, 350);

  const availability = useQuery({
    queryKey: ['sa-availability', debouncedUsername, debouncedMarketName],
    queryFn: () =>
      superAdminApi.checkAvailability(segment, {
        username: debouncedUsername.length >= 3 ? debouncedUsername : undefined,
        marketName: debouncedMarketName.length >= 3 ? debouncedMarketName : undefined,
      }),
    enabled: open && (debouncedUsername.length >= 3 || debouncedMarketName.length >= 3),
  });

  const expiresAt = useMemo(() => {
    const d = new Date();
    d.setMonth(d.getMonth() + months);
    return d;
  }, [months]);

  const create = useMutation({
    mutationFn: () => {
      const base = {
        username: username.trim().toLowerCase(),
        password,
        marketName: marketName.trim(),
        expiresAt: expiresAt.toISOString(),
      };
      // Ariza bo'lsa — uni yopib do'kon ochiladi; bo'lmasa to'g'ridan-to'g'ri
      // ega+do'kon yaratiladi. Ikkalasi ham bir xil natija qaytaradi.
      return request
        ? superAdminApi.approveRequest(segment, request.id, base)
        : superAdminApi.createStore(segment, {
            ...base,
            fullName: ownerName.trim(),
            phone: ownerPhone.trim(),
          });
    },
    onSuccess: (data) => {
      setResult(data);
      onCreated();
    },
    onError: (e) => setError((e as unknown as ApiError).message ?? t('common.somethingWrong')),
  });

  const usernameFree = availability.data?.usernameAvailable;
  const marketNameFree = availability.data?.marketNameAvailable;
  // Arizasiz rejimda ega ismi ham majburiy (backend ham talab qiladi);
  // telefon — 9-15 raqam, do'kon egasiga aloqa uchun yagona kanal.
  const ownerOk = !request
    ? ownerName.trim().length >= 2 && /^\+?[0-9]{9,15}$/.test(ownerPhone.replace(/\s/g, ''))
    : true;
  const canSubmit =
    username.trim().length >= 3 &&
    password.length >= 8 &&
    marketName.trim().length >= 3 &&
    ownerOk &&
    usernameFree !== false &&
    marketNameFree !== false &&
    !create.isPending;

  const mark = (state: boolean | null | undefined) =>
    state === true ? (
      <span className="flex items-center gap-1 text-[12px] text-success-text">
        <Check size={13} /> {t('sa.create.free')}
      </span>
    ) : state === false ? (
      <span className="flex items-center gap-1 text-[12px] text-danger">
        <XIcon size={13} /> {t('sa.create.taken')}
      </span>
    ) : null;

  return (
    <Modal
      open={open}
      onClose={onClose}
      title={t('sa.create.title')}
      subtitle={request ? `${request.fullName} · ${request.phone}` : t('sa.create.standaloneHint')}
      width="lg"
      footer={
        result ? (
          <Button onClick={onClose}>{t('common.close')}</Button>
        ) : (
          <>
            <Button variant="ghost" onClick={onClose}>
              {t('common.cancel')}
            </Button>
            <Button onClick={() => create.mutate()} disabled={!canSubmit} loading={create.isPending}>
              {t('sa.create.submit')}
            </Button>
          </>
        )
      }
    >
      {result ? (
        // Bir martalik ko'rsatish: parol hech qayerda saqlanmaydi va qayta
        // ochib bo'lmaydi — operator hozir nusxalab olishi kerak.
        <div className="flex flex-col gap-3 py-1">
          <div className="rounded-card border border-success-border bg-success-soft px-4 py-3 text-[13px]">
            <div className="mb-1 font-semibold text-success-text">{t('sa.create.doneTitle')}</div>
            <p className="text-muted">{t('sa.create.doneBody')}</p>
          </div>
          <dl className="grid grid-cols-[130px_1fr] gap-y-2 text-[13.5px]">
            <dt className="text-muted">{t('sa.create.storeName')}</dt>
            <dd className="font-semibold">{result.marketName}</dd>
            <dt className="text-muted">{t('sa.create.address')}</dt>
            <dd className="select-all break-all font-mono text-[13px]">
              {/* `subdomain` tipda nullable: eski javoblarda u yo'q edi. Bunday
                  holatda «/null» chiqarmaymiz — do'kon nomidan ko'rinadi. */}
              {result.subdomain ? storeUrl(result.subdomain) : '—'}
            </dd>
            <dt className="text-muted">{t('sa.create.username')}</dt>
            <dd className="font-semibold">{result.username}</dd>
            <dt className="text-muted">{t('sa.create.password')}</dt>
            <dd className="font-mono text-[14px] font-semibold">{password}</dd>
          </dl>
        </div>
      ) : (
        <div className="flex flex-col gap-4 py-1">
          {/* Arizasiz yaratishda ega ma'lumotlari qo'lda: ariza rejimida ular
              arizadan keladi va sarlavhada ko'rsatiladi. */}
          {!request && (
            <div className="grid grid-cols-2 gap-4">
              <Input
                label={t('sa.create.ownerName')}
                placeholder="Sardor Toshmatov"
                value={ownerName}
                onChange={(e) => setOwnerName(e.target.value)}
                autoFocus
              />
              <Input
                label={t('sa.create.ownerPhone')}
                placeholder="+998 90 123 45 67"
                value={ownerPhone}
                onChange={(e) => setOwnerPhone(e.target.value)}
                className="nums"
              />
            </div>
          )}
          <Input
            label={t('sa.create.storeName')}
            placeholder="«Тош Кон Строй Маркет»"
            value={marketName}
            onChange={(e) => setMarketName(e.target.value)}
            labelAddon={mark(marketNameFree)}
            autoFocus={!!request}
          />

          {/* Sub-path — ko'rsatiladi, tahrirlanmaydi. Mijoz aynan shu manzilga
              kiradi, shuning uchun operator uni yaratishdan OLDIN ko'rsin. */}
          <div className="flex flex-col gap-1.5">
            <span className="text-[14.5px] font-medium text-label">{t('sa.create.address')}</span>
            <div className="flex h-12 items-center gap-2 rounded-input border-[1.5px] border-dashed border-input-border bg-bg px-[18px] text-[15px]">
              {availability.isFetching ? (
                <Spinner size={15} />
              ) : (
                <span className="truncate font-mono text-text">
                  {availability.data?.suggestedSubdomain
                    ? storeUrl(availability.data.suggestedSubdomain)
                    : '…'}
                </span>
              )}
            </div>
            <span className="text-[11.5px] text-muted-2">{t('sa.create.addressHint')}</span>
          </div>

          <div className="grid grid-cols-2 gap-4">
            <Input
              label={t('sa.create.username')}
              placeholder="sardor.t"
              value={username}
              onChange={(e) => setUsername(e.target.value.replace(/\s/g, ''))}
              labelAddon={mark(usernameFree)}
              autoComplete="off"
            />
            <Input
              label={t('sa.create.password')}
              value={password}
              onChange={(e) => setPassword(e.target.value)}
              autoComplete="off"
              className="font-mono"
              labelAddon={
                <button
                  type="button"
                  onClick={() => setPassword(generatePassword())}
                  className="flex items-center gap-1 text-[12px] text-primary hover:text-primary-hover"
                >
                  <RefreshCw size={12} /> {t('sa.create.generate')}
                </button>
              }
            />
          </div>

          <div className="flex flex-col gap-1.5">
            <span className="text-[14.5px] font-medium text-label">{t('sa.create.period')}</span>
            <div className="flex items-center gap-2">
              {[1, 3, 6, 12].map((m) => (
                <button
                  key={m}
                  type="button"
                  onClick={() => setMonths(m)}
                  className={
                    m === months
                      ? 'rounded-btn border border-primary bg-primary px-4 py-2 text-[13px] font-semibold text-white'
                      : 'rounded-btn border border-border bg-surface px-4 py-2 text-[13px] text-muted hover:border-primary hover:text-primary'
                  }
                >
                  {t('sa.create.months', { count: m })}
                </button>
              ))}
              <span className="ml-2 text-[13px] text-muted">
                {t('sa.create.paidUntil')}{' '}
                <b className="text-text">{expiresAt.toLocaleDateString()}</b>
              </span>
            </div>
          </div>

          <p className="rounded-card bg-warn-soft px-4 py-2.5 text-[12.5px] leading-relaxed text-warn-text">
            {t('sa.create.passwordWarning')}
          </p>

          {error && <span className="text-[12.5px] text-danger">{error}</span>}
        </div>
      )}
    </Modal>
  );
}
