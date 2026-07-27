import { useEffect, useState } from 'react';
import { useMutation } from '@tanstack/react-query';
import { useTranslation } from 'react-i18next';
import { RefreshCw, ShieldAlert } from 'lucide-react';
import { Modal, Button, Input } from '@/shared/ui';
import type { ApiError } from '@/shared/api/types';
import { superAdminApi, type SaUserRow } from './api';

/** Chalkashadigan belgilarsiz — parol og'zaki aytiladi (0/O, 1/l/I yo'q). */
const ALPHABET = 'abcdefghjkmnpqrstuvwxyzABCDEFGHJKMNPQRSTUVWXYZ23456789';

function generate(length = 10): string {
  const bytes = new Uint32Array(length);
  crypto.getRandomValues(bytes);
  return Array.from(bytes, (b) => ALPHABET[b % ALPHABET.length]).join('');
}

/**
 * «Сменить пароль».
 *
 * <p>Parol SMS bilan yuborilmaydi (TZ BE-S7): SuperAdmin uni shu oynadan
 * nusxalab, foydalanuvchiga shaxsan beradi. Shuning uchun oyna yopilgach
 * parol qayta ko'rsatilmaydi — faqat yana tiklash mumkin.</p>
 *
 * <p>Amal foydalanuvchining BARCHA sessiyalarini uzadi, ya'ni bu «parolni
 * almashtirish» emas, «kirishni qaytarib olish». Ogohlantirish shuning uchun
 * tugma yonida turadi.</p>
 */
export function ResetPasswordModal({
  segment,
  user,
  onClose,
}: {
  segment: string;
  user: SaUserRow | null;
  onClose: () => void;
}) {
  const { t } = useTranslation();
  const [password, setPassword] = useState('');
  const [done, setDone] = useState(false);

  useEffect(() => {
    if (user) {
      setPassword(generate());
      setDone(false);
    }
  }, [user]);

  const save = useMutation({
    mutationFn: () => superAdminApi.resetPassword(segment, user!.id, password),
    onSuccess: () => setDone(true),
  });

  const err = save.error
    ? ((save.error as unknown as ApiError).message ?? t('common.somethingWrong'))
    : null;

  return (
    <Modal
      open={!!user}
      onClose={onClose}
      title={t('sa.users.resetTitle')}
      subtitle={user ? `${user.fullName} · ${user.username}` : undefined}
      footer={
        done ? (
          <Button onClick={onClose}>{t('common.close')}</Button>
        ) : (
          <>
            <Button variant="ghost" onClick={onClose}>
              {t('common.cancel')}
            </Button>
            <Button
              onClick={() => save.mutate()}
              loading={save.isPending}
              disabled={password.length < 8}
            >
              {t('sa.users.resetConfirm')}
            </Button>
          </>
        )
      }
    >
      <div className="flex flex-col gap-4 py-1">
        <Input
          label={t('sa.create.password')}
          value={password}
          onChange={(e) => setPassword(e.target.value)}
          disabled={done}
          className="font-mono"
          autoComplete="off"
          labelAddon={
            !done && (
              <button
                type="button"
                onClick={() => setPassword(generate())}
                className="flex items-center gap-1 text-[12px] text-primary hover:text-primary-hover"
              >
                <RefreshCw size={12} /> {t('sa.create.generate')}
              </button>
            )
          }
        />

        {done ? (
          <div className="rounded-card border border-success-border bg-success-soft px-4 py-3 text-[13px]">
            <div className="mb-1 font-semibold text-success-text">{t('sa.users.resetDone')}</div>
            <p className="text-muted">{t('sa.users.resetDoneBody')}</p>
          </div>
        ) : (
          <p className="flex items-start gap-2 rounded-card bg-warn-soft px-4 py-3 text-[12.5px] leading-relaxed text-warn-text">
            <ShieldAlert size={14} className="mt-0.5 flex-none" />
            {t('sa.users.resetWarning')}
          </p>
        )}

        {err && <span className="text-[12.5px] text-danger">{err}</span>}
      </div>
    </Modal>
  );
}
