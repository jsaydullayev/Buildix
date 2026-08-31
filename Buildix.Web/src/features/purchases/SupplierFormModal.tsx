import { useEffect, useState } from 'react';
import { useMutation, useQueryClient } from '@tanstack/react-query';
import { useTranslation } from 'react-i18next';
import { Modal, Button, Field } from '@/shared/ui';
import { cn } from '@/shared/lib/cn';
import type { ApiError } from '@/shared/api/types';
import { purchasesApi, type Supplier } from './api';

/**
 * Create or edit a supplier. `editing === null` means create; passing a
 * supplier prefills the form and switches to the update endpoint.
 */
export function SupplierFormModal({
  open,
  editing,
  onClose,
}: {
  open: boolean;
  editing: Supplier | null;
  onClose: () => void;
}) {
  const { t } = useTranslation();
  const qc = useQueryClient();
  const [name, setName] = useState('');
  const [phone, setPhone] = useState('');
  const [contact, setContact] = useState('');
  const [deliveryTerm, setDeliveryTerm] = useState('');
  const [address, setAddress] = useState('');
  const [comment, setComment] = useState('');
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    if (open) {
      setName(editing?.name ?? '');
      setPhone(editing?.phone ?? '');
      setContact(editing?.contactPerson ?? '');
      setDeliveryTerm(editing?.deliveryTerm ?? '');
      setAddress(editing?.address ?? '');
      setComment(editing?.comment ?? '');
      setError(null);
    }
  }, [open, editing]);

  const save = useMutation({
    mutationFn: () => {
      const body = {
        name: name.trim(),
        phone: phone.trim() || null,
        contactPerson: contact.trim() || null,
        deliveryTerm: deliveryTerm.trim() || null,
        address: address.trim() || null,
        comment: comment.trim() || null,
      };
      return editing ? purchasesApi.updateSupplier(editing.id, body) : purchasesApi.createSupplier(body);
    },
    onSuccess: () => {
      void qc.invalidateQueries({ queryKey: ['suppliers'] });
      onClose();
    },
    onError: (e) => setError((e as unknown as ApiError).message ?? t('common.somethingWrong')),
  });

  const inputCls =
    'h-11 rounded-input border border-input-border bg-surface px-3.5 text-[14px] outline-none focus:border-primary focus:shadow-focus-ring';

  return (
    <Modal
      open={open}
      onClose={onClose}
      title={editing ? t('suppliers.form.editTitle') : t('suppliers.form.addTitle')}
      footer={
        <>
          <Button variant="secondary" onClick={onClose}>
            {t('common.cancel')}
          </Button>
          <Button disabled={!name.trim() || save.isPending} loading={save.isPending} onClick={() => save.mutate()}>
            {t('common.save')}
          </Button>
        </>
      }
    >
      <div className="flex flex-col gap-4">
        <Field label={t('suppliers.form.name')}>
          <input value={name} onChange={(e) => setName(e.target.value)} autoFocus className={cn(inputCls, 'w-full')} />
        </Field>
        <div className="grid grid-cols-2 gap-3">
          <Field label={t('suppliers.form.phone')}>
            <input value={phone} onChange={(e) => setPhone(e.target.value)} className={cn(inputCls, 'w-full nums')} />
          </Field>
          <Field label={t('suppliers.form.contact')}>
            <input value={contact} onChange={(e) => setContact(e.target.value)} className={cn(inputCls, 'w-full')} />
          </Field>
        </div>
        <div className="grid grid-cols-2 gap-3">
          <Field label={t('suppliers.form.deliveryTerm')}>
            <input value={deliveryTerm} onChange={(e) => setDeliveryTerm(e.target.value)} className={cn(inputCls, 'w-full')} />
          </Field>
          <Field label={t('suppliers.form.address')}>
            <input value={address} onChange={(e) => setAddress(e.target.value)} className={cn(inputCls, 'w-full')} />
          </Field>
        </div>
        <Field label={t('suppliers.form.comment')}>
          <input value={comment} onChange={(e) => setComment(e.target.value)} className={cn(inputCls, 'w-full')} />
        </Field>
        {error && <div className="rounded-input bg-danger-soft px-3 py-2 text-[12.5px] text-danger">{error}</div>}
      </div>
    </Modal>
  );
}

