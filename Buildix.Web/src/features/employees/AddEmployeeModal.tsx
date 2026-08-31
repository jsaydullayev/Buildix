import { useEffect, useState } from 'react';
import { useTranslation } from 'react-i18next';
import { useMutation, useQueryClient } from '@tanstack/react-query';
import { Modal, Button, PasswordInput, Field } from '@/shared/ui';
import { cn } from '@/shared/lib/cn';
import { employeesApi } from './api';
import type { ApiError } from '@/shared/api/types';

const ROLES = ['Admin', 'Seller'] as const;

export function AddEmployeeModal({ open, onClose }: { open: boolean; onClose: () => void }) {
  const { t } = useTranslation();
  const qc = useQueryClient();
  const [fullName, setFullName] = useState('');
  const [username, setUsername] = useState('');
  const [password, setPassword] = useState('');
  const [role, setRole] = useState<string>('Seller');
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    if (open) {
      setFullName('');
      setUsername('');
      setPassword('');
      setRole('Seller');
      setError(null);
    }
  }, [open]);

  const mutation = useMutation({
    mutationFn: () => employeesApi.create({ fullName, username, password, role }),
    onSuccess: () => {
      void qc.invalidateQueries({ queryKey: ['employees'] });
      onClose();
    },
    onError: (e) => setError((e as unknown as ApiError).message ?? t('common.somethingWrong')),
  });

  const valid = fullName.trim().length >= 2 && username.trim().length >= 3 && password.length >= 1;
  const inputCls =
    'h-11 rounded-input border border-input-border bg-surface px-3.5 text-[14px] outline-none focus:border-primary focus:shadow-focus-ring';

  return (
    <Modal
      open={open}
      onClose={onClose}
      title={t('employees.form.title')}
      footer={
        <>
          <Button variant="secondary" onClick={onClose}>
            {t('common.cancel')}
          </Button>
          <Button disabled={!valid} loading={mutation.isPending} onClick={() => mutation.mutate()}>
            {t('common.save')}
          </Button>
        </>
      }
    >
      <div className="flex flex-col gap-4">
        <Field label={t('employees.form.fullName')}>
          <input className={inputCls} value={fullName} onChange={(e) => setFullName(e.target.value)} />
        </Field>
        <div className="grid grid-cols-2 gap-4">
          <Field label={t('employees.form.username')}>
            <input className={inputCls} value={username} onChange={(e) => setUsername(e.target.value)} autoComplete="off" />
          </Field>
          <Field label={t('employees.form.password')}>
            <PasswordInput className={inputCls} value={password} onChange={(e) => setPassword(e.target.value)} autoComplete="new-password" />
          </Field>
        </div>
        <Field label={t('employees.form.role')}>
          <div className="flex gap-2">
            {ROLES.map((r) => (
              <button
                key={r}
                type="button"
                onClick={() => setRole(r)}
                className={cn(
                  'h-11 flex-1 rounded-input border text-[13px] font-medium transition-colors',
                  role === r ? 'border-primary bg-primary-soft text-primary-hover' : 'border-input-border bg-surface text-muted hover:text-text',
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

