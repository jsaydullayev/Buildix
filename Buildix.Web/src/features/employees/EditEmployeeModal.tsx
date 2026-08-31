import { useEffect, useState } from 'react';
import { useTranslation } from 'react-i18next';
import { useMutation, useQueryClient } from '@tanstack/react-query';
import { Modal, Button, PasswordInput, Field } from '@/shared/ui';
import { cn } from '@/shared/lib/cn';
import { employeesApi, type Employee } from './api';
import type { ApiError } from '@/shared/api/types';

const ROLES = ['Admin', 'Seller'] as const;

/** Edit an existing employee: name, role, active state, optional password reset. */
export function EditEmployeeModal({ employee, onClose }: { employee: Employee | null; onClose: () => void }) {
  const { t } = useTranslation();
  const qc = useQueryClient();
  const [fullName, setFullName] = useState('');
  const [password, setPassword] = useState('');
  const [role, setRole] = useState<string>('Seller');
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    if (employee) {
      setFullName(employee.fullName);
      setPassword('');
      setRole(employee.role);
      setError(null);
    }
  }, [employee]);

  const save = useMutation({
    mutationFn: () =>
      employeesApi.update({
        id: employee!.id,
        fullName: fullName.trim(),
        // Empty = keep current password; the backend only re-hashes when set.
        password: password.trim() ? password : null,
        role,
        isActive: employee!.isActive,
      }),
    onSuccess: () => {
      void qc.invalidateQueries({ queryKey: ['employees'] });
      onClose();
    },
    onError: (e) => setError((e as unknown as ApiError).message ?? t('common.somethingWrong')),
  });

  const valid = fullName.trim().length >= 2;
  const inputCls =
    'h-11 rounded-input border border-input-border bg-surface px-3.5 text-[14px] outline-none focus:border-primary focus:shadow-focus-ring';

  return (
    <Modal
      open={!!employee}
      onClose={onClose}
      title={t('employees.editTitle')}
      subtitle={employee ? `@${employee.username}` : undefined}
      footer={
        <>
          <Button variant="secondary" onClick={onClose}>
            {t('common.cancel')}
          </Button>
          <Button disabled={!valid || save.isPending} loading={save.isPending} onClick={() => save.mutate()}>
            {t('common.save')}
          </Button>
        </>
      }
    >
      <div className="flex flex-col gap-4">
        <Field label={t('employees.form.fullName')}>
          <input className={cn(inputCls, 'w-full')} value={fullName} onChange={(e) => setFullName(e.target.value)} />
        </Field>
        <Field label={t('employees.resetPassword')}>
          <PasswordInput
            className={cn(inputCls, 'w-full')}
            value={password}
            onChange={(e) => setPassword(e.target.value)}
            placeholder={t('employees.resetPasswordHint')}
            autoComplete="new-password"
          />
        </Field>
        <Field label={t('employees.form.role')}>
          <div className="flex gap-2">
            {ROLES.map((r) => (
              <button
                key={r}
                type="button"
                onClick={() => setRole(r)}
                className={cn(
                  'h-11 flex-1 rounded-input border text-[13px] font-medium transition-colors',
                  role === r
                    ? 'border-primary bg-primary-soft text-primary-hover'
                    : 'border-input-border bg-surface text-muted hover:text-text',
                )}
              >
                {t(`employees.roles.${r.toLowerCase()}` as never)}
              </button>
            ))}
          </div>
        </Field>
        {error && <p className="text-[12.5px] text-danger">{error}</p>}
      </div>
    </Modal>
  );
}

