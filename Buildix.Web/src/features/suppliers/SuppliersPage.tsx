import { useState } from 'react';
import { useNavigate, useParams } from 'react-router-dom';
import { useQuery, useMutation, useQueryClient } from '@tanstack/react-query';
import { useTranslation } from 'react-i18next';
import { Plus, Search, ArrowLeft, Pencil, Trash2, FileDown } from 'lucide-react';
import { PageHeader, Button, Card, Spinner } from '@/shared/ui';
import { cn } from '@/shared/lib/cn';
import { formatSum } from '@/shared/lib/format';
import { useDebounce } from '@/shared/hooks/useDebounce';
import { useExport } from '@/shared/hooks/useExport';
import { useAuth } from '@/shared/auth/useAuth';
import { PERMISSIONS } from '@/shared/config/permissions';
import type { ApiError } from '@/shared/api/types';
import { purchasesApi, type Supplier } from '@/features/purchases/api';
import { SupplierFormModal } from '@/features/purchases/SupplierFormModal';

const PAGE_SIZE = 20;

export default function SuppliersPage() {
  const { t, i18n } = useTranslation();
  const { subdomain } = useParams();
  const navigate = useNavigate();
  const { hasPermission } = useAuth();
  const qc = useQueryClient();
  const canManage = hasPermission(PERMISSIONS.suppliers.manage);
  const canDelete = hasPermission(PERMISSIONS.suppliers.delete);
  const exporter = useExport(() => purchasesApi.exportSuppliers(i18n.language), 'suppliers.xlsx');

  const [search, setSearch] = useState('');
  const [page, setPage] = useState(1);
  const [formOpen, setFormOpen] = useState(false);
  const [editing, setEditing] = useState<Supplier | null>(null);
  const debouncedSearch = useDebounce(search);

  const listQuery = useQuery({
    queryKey: ['suppliers', { search: debouncedSearch, page }],
    queryFn: () => purchasesApi.suppliersPaged(page, PAGE_SIZE, debouncedSearch),
  });

  const del = useMutation({
    mutationFn: (s: Supplier) => purchasesApi.deleteSupplier(s.id),
    onSuccess: () => void qc.invalidateQueries({ queryKey: ['suppliers'] }),
  });

  async function askDelete(s: Supplier) {
    // Preflight: the server reports whether receipts / outstanding debt block
    // the delete, so we can warn instead of firing a doomed request.
    try {
      const info = await purchasesApi.supplierDeleteInfo(s.id);
      const msg = info.canDelete
        ? t('suppliers.deleteConfirm', { name: s.name })
        : (info.warningMessage ??
          t('suppliers.deleteBlocked', { count: info.receiptsCount, debt: formatSum(info.outstandingDebt) }));
      if (info.canDelete && window.confirm(msg)) del.mutate(s);
      else if (!info.canDelete) window.alert(msg);
    } catch (e) {
      window.alert((e as ApiError).message ?? t('common.somethingWrong'));
    }
  }

  const openCreate = () => {
    setEditing(null);
    setFormOpen(true);
  };
  const openEdit = (s: Supplier) => {
    setEditing(s);
    setFormOpen(true);
  };

  return (
    <>
      <PageHeader
        title={t('suppliers.title')}
        subtitle={t('suppliers.subtitle')}
        actions={
          <div className="flex items-center gap-3">
            <Button variant="secondary" onClick={() => navigate(`/${subdomain}/purchases`)}>
              <ArrowLeft size={15} />
              {t('nav.purchases')}
            </Button>
            <Button variant="secondary" loading={exporter.downloading} onClick={exporter.download}>
              <FileDown size={15} />
              {t('common.export')}
            </Button>
            {canManage && (
              <Button onClick={openCreate}>
                <Plus size={15} strokeWidth={2.4} />
                {t('suppliers.add')}
              </Button>
            )}
          </div>
        }
      />

      <div className="flex flex-1 flex-col gap-[18px] p-8">
        <div className="relative max-w-md">
          <Search size={17} className="absolute left-3.5 top-1/2 -translate-y-1/2 text-muted-2" />
          <input
            value={search}
            onChange={(e) => {
              setSearch(e.target.value);
              setPage(1);
            }}
            placeholder={t('suppliers.searchPlaceholder')}
            className="h-11 w-full rounded-input border border-input-border bg-surface pl-11 pr-4 text-[14px] outline-none focus:border-primary focus:shadow-focus-ring"
          />
        </div>

        <Card className="overflow-hidden">
          <div className="grid grid-cols-[1fr_160px_160px_100px] items-center gap-3 border-b border-hairline bg-bg/40 px-6 py-3 text-[11.5px] font-semibold tracking-[0.4px] text-muted-2">
            <span>{t('suppliers.cols.name')}</span>
            <span>{t('suppliers.cols.phone')}</span>
            <span className="text-right">{t('suppliers.cols.debt')}</span>
            <span className="text-right">{t('suppliers.cols.receipts')}</span>
          </div>

          {listQuery.isLoading ? (
            <div className="flex items-center justify-center py-20 text-primary">
              <Spinner size={24} />
            </div>
          ) : listQuery.data && listQuery.data.items.length > 0 ? (
            listQuery.data.items.map((s) => (
              <div
                key={s.id}
                className="group grid grid-cols-[1fr_160px_160px_100px] items-center gap-3 border-b border-hairline px-6 py-3.5 text-[13px] last:border-0 hover:bg-bg/40"
              >
                <div className="flex min-w-0 items-center gap-3">
                  <div className="flex h-8 w-8 flex-none items-center justify-center rounded-pill bg-primary/10 text-[11px] font-semibold uppercase text-primary">
                    {s.name.slice(0, 2)}
                  </div>
                  <span className="truncate font-medium">{s.name}</span>
                </div>
                <span className="text-muted-2 nums">{s.phone ?? '—'}</span>
                <span className={cn('text-right font-semibold nums', s.outstandingDebt > 0 && 'text-warn-text')}>
                  {formatSum(s.outstandingDebt)}
                </span>
                <div className="flex items-center justify-end gap-1">
                  <span className="text-muted-2 nums">{s.receiptCount}</span>
                  {(canManage || canDelete) && (
                    <div className="ml-2 flex items-center gap-1 opacity-0 transition-opacity group-hover:opacity-100">
                      {canManage && (
                        <button
                          type="button"
                          onClick={() => openEdit(s)}
                          className="flex h-7 w-7 items-center justify-center rounded-md text-muted-2 hover:text-primary"
                          aria-label={t('common.save')}
                        >
                          <Pencil size={14} />
                        </button>
                      )}
                      {canDelete && (
                        <button
                          type="button"
                          onClick={() => askDelete(s)}
                          className="flex h-7 w-7 items-center justify-center rounded-md text-muted-2 hover:text-danger"
                          aria-label={t('warehouse.form.delete')}
                        >
                          <Trash2 size={14} />
                        </button>
                      )}
                    </div>
                  )}
                </div>
              </div>
            ))
          ) : (
            <div className="py-20 text-center text-[14px] text-muted-2">{t('suppliers.empty')}</div>
          )}
        </Card>

        {listQuery.data && listQuery.data.totalPages > 1 && (
          <div className="flex items-center justify-end gap-1.5">
            <button
              type="button"
              disabled={page <= 1}
              onClick={() => setPage((p) => p - 1)}
              className="h-8 rounded-md border border-input-border bg-surface px-3 text-[13px] disabled:opacity-40"
            >
              ‹
            </button>
            <span className="px-2 text-[13px] text-muted nums">
              {page} / {listQuery.data.totalPages}
            </span>
            <button
              type="button"
              disabled={page >= listQuery.data.totalPages}
              onClick={() => setPage((p) => p + 1)}
              className="h-8 rounded-md border border-input-border bg-surface px-3 text-[13px] disabled:opacity-40"
            >
              ›
            </button>
          </div>
        )}
      </div>

      <SupplierFormModal open={formOpen} editing={editing} onClose={() => setFormOpen(false)} />
    </>
  );
}
