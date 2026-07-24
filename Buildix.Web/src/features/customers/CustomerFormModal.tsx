import { useEffect, useState } from 'react';
import { useMutation, useQueryClient } from '@tanstack/react-query';
import { useTranslation } from 'react-i18next';
import { Modal, Button, Toggle } from '@/shared/ui';
import { cn } from '@/shared/lib/cn';
import type { ApiError } from '@/shared/api/types';
import { customersApi, type Customer } from './api';

const TYPES = ['Individual', 'Legal'] as const;

/**
 * Create or edit a customer. `editing === null` → create (with optional opening
 * debt); passing a customer switches to update (opening debt is create-only, so
 * it is hidden then).
 */
export function CustomerFormModal({
  open,
  editing,
  onClose,
}: {
  open: boolean;
  editing: Customer | null;
  onClose: () => void;
}) {
  const { t } = useTranslation();
  const qc = useQueryClient();
  const [phone, setPhone] = useState('');
  const [fullName, setFullName] = useState('');
  const [type, setType] = useState<string>('Individual');
  const [isRegular, setIsRegular] = useState(false);
  const [debtLimit, setDebtLimit] = useState('');
  const [initialDebt, setInitialDebt] = useState('');
  const [comment, setComment] = useState('');
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    if (open) {
      setPhone(editing?.phone ?? '');
      setFullName(editing?.fullName ?? '');
      setType(editing?.customerType ?? 'Individual');
      setIsRegular(editing?.isRegular ?? false);
      setDebtLimit(editing?.debtLimit ? String(editing.debtLimit) : '');
      setInitialDebt('');
      setComment(editing?.comment ?? '');
      setError(null);
    }
  }, [open, editing]);

  const save = useMutation({
    mutationFn: () => {
      // 0 debt limit = unlimited; empty field also means "no explicit limit".
      const limit = debtLimit.trim() ? Math.max(0, Number(debtLimit) || 0) : null;
      if (editing) {
        return customersApi.update({
          id: editing.id,
          phone: phone.trim(),
          fullName: fullName.trim() || null,
          customerType: type,
          isRegular,
          debtLimit: limit,
        });
      }
      return customersApi.create({
        phone: phone.trim(),
        fullName: fullName.trim() || null,
        comment: comment.trim() || null,
        initialDebt: initialDebt.trim() ? Math.max(0, Number(initialDebt) || 0) : null,
        customerType: type,
        isRegular,
        debtLimit: limit,
      });
    },
    onSuccess: () => {
      void qc.invalidateQueries({ queryKey: ['customers'] });
      void qc.invalidateQueries({ queryKey: ['debt-summary'] });
      onClose();
    },
    onError: (e) => setError((e as unknown as ApiError).message ?? t('common.somethingWrong')),
  });

  // Backend requires a 9–15 digit phone; guard here so the button reflects it.
  const phoneValid = /^\+?[0-9]{9,15}$/.test(phone.trim());
  const inputCls =
    'h-11 rounded-input border border-input-border bg-surface px-3.5 text-[14px] outline-none focus:border-primary focus:shadow-focus-ring';

  return (
    <Modal
      open={open}
      onClose={onClose}
      title={editing ? t('customers.form.editTitle') : t('customers.form.addTitle')}
      footer={
        <>
          <Button variant="secondary" onClick={onClose}>
            {t('common.cancel')}
          </Button>
          <Button disabled={!phoneValid || save.isPending} loading={save.isPending} onClick={() => save.mutate()}>
            {t('common.save')}
          </Button>
        </>
      }
    >
      <div className="flex flex-col gap-4">
        <div className="grid grid-cols-2 gap-3">
          <Field label={t('customers.form.phone')}>
            <input
              value={phone}
              onChange={(e) => setPhone(e.target.value)}
              placeholder="+998 90 123 45 67"
              className={cn(inputCls, 'w-full nums')}
            />
          </Field>
          <Field label={t('customers.form.name')}>
            <input value={fullName} onChange={(e) => setFullName(e.target.value)} className={cn(inputCls, 'w-full')} />
          </Field>
        </div>

        <Field label={t('customers.form.type')}>
          <div className="flex gap-2">
            {TYPES.map((ty) => (
              <button
                key={ty}
                type="button"
                onClick={() => setType(ty)}
                className={cn(
                  'h-11 flex-1 rounded-input border text-[13px] font-medium transition-colors',
                  type === ty
                    ? 'border-primary bg-primary-soft text-primary-hover'
                    : 'border-input-border bg-surface text-muted hover:text-text',
                )}
              >
                {t(`customers.types.${ty.toLowerCase()}` as never)}
              </button>
            ))}
          </div>
        </Field>

        <div className="grid grid-cols-2 gap-3">
          <Field label={t('customers.form.debtLimit')}>
            <input
              type="number"
              step="any"
              placeholder={t('customers.form.noLimit')}
              value={debtLimit}
              onChange={(e) => setDebtLimit(e.target.value)}
              className={cn(inputCls, 'w-full text-right nums')}
            />
          </Field>
          {!editing && (
            <Field label={t('customers.form.initialDebt')}>
              <input
                type="number"
                step="any"
                placeholder="0"
                value={initialDebt}
                onChange={(e) => setInitialDebt(e.target.value)}
                className={cn(inputCls, 'w-full text-right nums')}
              />
            </Field>
          )}
        </div>

        <div className="flex items-center justify-between gap-4 rounded-input bg-bg px-4 py-2.5">
          <div className="min-w-0">
            <div className="text-[13.5px] font-medium">{t('customers.form.regular')}</div>
            <div className="text-[12px] text-muted-2">{t('customers.form.regularHint')}</div>
          </div>
          <Toggle checked={isRegular} onChange={setIsRegular} />
        </div>

        {!editing && (
          <Field label={t('customers.form.comment')}>
            <input value={comment} onChange={(e) => setComment(e.target.value)} className={cn(inputCls, 'w-full')} />
          </Field>
        )}

        {error && <div className="rounded-input bg-danger-soft px-3 py-2 text-[12.5px] text-danger">{error}</div>}
      </div>
    </Modal>
  );
}

function Field({ label, children }: { label: string; children: React.ReactNode }) {
  return (
    <div className="flex flex-col gap-1.5">
      <label className="text-[13px] font-medium text-label">{label}</label>
      {children}
    </div>
  );
}
