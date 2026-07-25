import { useState } from 'react';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { useTranslation } from 'react-i18next';
import { Plus, Pencil, Trash2, Check, X } from 'lucide-react';
import { Modal, Button, Spinner, useConfirm } from '@/shared/ui';
import { cn } from '@/shared/lib/cn';
import type { ApiError } from '@/shared/api/types';
import { categoriesApi, type ProductCategory } from './api';

/**
 * Inline category manager: a flat list where each row can be renamed in place
 * or deleted, plus an add row. Kept as one modal (rather than nested dialogs)
 * because categories are just a name — no need for a full form per item.
 */
export function CategoriesModal({ open, onClose }: { open: boolean; onClose: () => void }) {
  const { t } = useTranslation();
  const confirm = useConfirm();
  const qc = useQueryClient();
  const [newName, setNewName] = useState('');
  const [editId, setEditId] = useState<number | null>(null);
  const [editName, setEditName] = useState('');
  const [error, setError] = useState<string | null>(null);

  const listQuery = useQuery({ queryKey: ['categories'], queryFn: categoriesApi.list, enabled: open });
  const cats = listQuery.data ?? [];

  const invalidate = () => {
    void qc.invalidateQueries({ queryKey: ['categories'] });
    void qc.invalidateQueries({ queryKey: ['products'] });
  };
  const onErr = (e: unknown) => setError((e as ApiError).message ?? t('common.somethingWrong'));

  const create = useMutation({
    mutationFn: (name: string) => categoriesApi.create({ name }),
    onSuccess: () => {
      setNewName('');
      setError(null);
      invalidate();
    },
    onError: onErr,
  });

  const rename = useMutation({
    mutationFn: (c: ProductCategory) =>
      categoriesApi.update(c.id, { name: editName.trim(), description: c.description ?? null, icon: c.icon ?? null, isActive: c.isActive ?? true }),
    onSuccess: () => {
      setEditId(null);
      setError(null);
      invalidate();
    },
    onError: onErr,
  });

  const del = useMutation({
    mutationFn: (id: number) => categoriesApi.remove(id),
    onSuccess: () => {
      setError(null);
      invalidate();
    },
    onError: onErr,
  });

  const askDelete = async (c: ProductCategory) => {
    if (await confirm({ title: t('categories.deleteConfirm', { name: c.name }), tone: 'danger', confirmLabel: t('common.delete') }))
      del.mutate(c.id);
  };

  const inputCls =
    'h-9 flex-1 rounded-input border border-input-border bg-surface px-3 text-[14px] outline-none focus:border-primary';

  return (
    <Modal open={open} onClose={onClose} title={t('categories.title')} footer={
      <Button variant="secondary" onClick={onClose}>{t('common.close')}</Button>
    }>
      <div className="flex flex-col gap-3">
        {/* Add row */}
        <div className="flex items-center gap-2">
          <input
            value={newName}
            onChange={(e) => setNewName(e.target.value)}
            onKeyDown={(e) => e.key === 'Enter' && newName.trim() && create.mutate(newName.trim())}
            placeholder={t('categories.addPlaceholder')}
            className={inputCls}
          />
          <Button size="sm" disabled={!newName.trim() || create.isPending} onClick={() => create.mutate(newName.trim())}>
            <Plus size={15} />
            {t('categories.add')}
          </Button>
        </div>

        {error && <div className="rounded-input bg-danger-soft px-3 py-2 text-[12.5px] text-danger">{error}</div>}

        {/* List */}
        {listQuery.isLoading ? (
          <div className="flex justify-center py-8 text-primary">
            <Spinner size={20} />
          </div>
        ) : cats.length === 0 ? (
          <p className="py-6 text-center text-[13px] text-muted-2">{t('categories.empty')}</p>
        ) : (
          <div className="flex max-h-[340px] flex-col divide-y divide-hairline overflow-y-auto rounded-input border border-hairline">
            {cats.map((c) => (
              <div key={c.id} className="flex items-center gap-2 px-3 py-2">
                {editId === c.id ? (
                  <>
                    <input
                      value={editName}
                      onChange={(e) => setEditName(e.target.value)}
                      onKeyDown={(e) => e.key === 'Enter' && editName.trim() && rename.mutate(c)}
                      autoFocus
                      className={inputCls}
                    />
                    <IconBtn icon={<Check size={15} />} onClick={() => editName.trim() && rename.mutate(c)} primary />
                    <IconBtn icon={<X size={15} />} onClick={() => setEditId(null)} />
                  </>
                ) : (
                  <>
                    <span className="flex-1 truncate text-[13.5px] font-medium">{c.name}</span>
                    {typeof c.productCount === 'number' && (
                      <span className="text-[11.5px] text-muted-2 nums">{c.productCount}</span>
                    )}
                    <IconBtn
                      icon={<Pencil size={14} />}
                      onClick={() => {
                        setEditId(c.id);
                        setEditName(c.name);
                      }}
                    />
                    <IconBtn icon={<Trash2 size={14} />} onClick={() => askDelete(c)} danger />
                  </>
                )}
              </div>
            ))}
          </div>
        )}
      </div>
    </Modal>
  );
}

function IconBtn({
  icon,
  onClick,
  primary,
  danger,
}: {
  icon: React.ReactNode;
  onClick: () => void;
  primary?: boolean;
  danger?: boolean;
}) {
  return (
    <button
      type="button"
      onClick={onClick}
      className={cn(
        'flex h-8 w-8 flex-none items-center justify-center rounded-md text-muted-2 transition-colors',
        primary && 'text-primary hover:bg-primary-soft',
        danger && 'hover:text-danger',
        !primary && !danger && 'hover:text-primary',
      )}
    >
      {icon}
    </button>
  );
}
