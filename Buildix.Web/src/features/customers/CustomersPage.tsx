import { useState } from 'react';
import { useQuery, useMutation, useQueryClient, keepPreviousData } from '@tanstack/react-query';
import { useTranslation } from 'react-i18next';
import { Plus, Search, Pencil, Trash2, FileDown } from 'lucide-react';
import { PageHeader, Button, Card, Badge, Spinner } from '@/shared/ui';
import { cn } from '@/shared/lib/cn';
import { formatSum } from '@/shared/lib/format';
import { useDebounce } from '@/shared/hooks/useDebounce';
import { useExport } from '@/shared/hooks/useExport';
import { useAuth } from '@/shared/auth/useAuth';
import { PERMISSIONS } from '@/shared/config/permissions';
import type { ApiError } from '@/shared/api/types';
import { customersApi, type Customer } from './api';
import { CustomerFormModal } from './CustomerFormModal';
import { CustomerDetailModal } from './CustomerDetailModal';

const PAGE_SIZE = 20;
const GRID = 'grid-cols-[1.6fr_1fr_130px_120px_90px]';

export default function CustomersPage() {
  const { t, i18n } = useTranslation();
  const { hasPermission } = useAuth();
  const canManage = hasPermission(PERMISSIONS.customers.manage);
  const canDelete = hasPermission(PERMISSIONS.customers.delete);
  const canExport = hasPermission(PERMISSIONS.customers.export);
  const exporter = useExport(() => customersApi.exportExcel(i18n.language), 'customers.xlsx');
  const qc = useQueryClient();

  const [search, setSearch] = useState('');
  const [page, setPage] = useState(1);
  const [formOpen, setFormOpen] = useState(false);
  const [editing, setEditing] = useState<Customer | null>(null);
  const [detail, setDetail] = useState<Customer | null>(null);
  const debouncedSearch = useDebounce(search);

  const listQuery = useQuery({
    queryKey: ['customers', { search: debouncedSearch, page }],
    queryFn: () => customersApi.listPaged({ page, size: PAGE_SIZE, search: debouncedSearch }),
    placeholderData: keepPreviousData,
  });

  const del = useMutation({
    mutationFn: (c: Customer) => customersApi.remove(c.id),
    onSuccess: () => void qc.invalidateQueries({ queryKey: ['customers'] }),
  });

  async function askDelete(c: Customer) {
    try {
      const info = await customersApi.deleteInfo(c.id);
      const blocked = !info.canDelete
        ? (info.warningMessage ??
          t('customers.deleteBlocked', { debts: info.debtsCount, debt: formatSum(info.totalDebt) }))
        : null;
      if (blocked) {
        window.alert(blocked);
        return;
      }
      if (window.confirm(t('customers.deleteConfirm', { name: c.fullName ?? c.phone }))) del.mutate(c);
    } catch (e) {
      window.alert((e as ApiError).message ?? t('common.somethingWrong'));
    }
  }

  const openCreate = () => {
    setEditing(null);
    setFormOpen(true);
  };
  const openEdit = (c: Customer) => {
    setEditing(c);
    setFormOpen(true);
  };

  const rows = listQuery.data?.items ?? [];

  return (
    <>
      <PageHeader
        title={t('customers.title')}
        subtitle={t('customers.subtitle', { count: listQuery.data?.total ?? 0 })}
        actions={
          <div className="flex items-center gap-3">
            {canExport && (
              <Button variant="secondary" loading={exporter.downloading} onClick={exporter.download}>
                <FileDown size={15} />
                {t('common.export')}
              </Button>
            )}
            {canManage && (
              <Button onClick={openCreate}>
                <Plus size={15} strokeWidth={2.4} />
                {t('customers.add')}
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
            placeholder={t('customers.searchPlaceholder')}
            className="h-11 w-full rounded-input border border-input-border bg-surface pl-11 pr-4 text-[14px] outline-none focus:border-primary focus:shadow-focus-ring"
          />
        </div>

        <Card className="overflow-hidden">
          <div className={cn('grid items-center gap-3 border-b border-hairline bg-bg/40 px-6 py-3 text-[11.5px] font-semibold tracking-[0.4px] text-muted-2', GRID)}>
            <span>{t('customers.cols.name')}</span>
            <span>{t('customers.cols.phone')}</span>
            <span className="text-right">{t('customers.cols.debt')}</span>
            <span>{t('customers.cols.type')}</span>
            <span />
          </div>

          {listQuery.isLoading ? (
            <div className="flex items-center justify-center py-20 text-primary">
              <Spinner size={24} />
            </div>
          ) : rows.length > 0 ? (
            rows.map((c) => (
              <div
                key={c.id}
                className={cn('group grid items-center gap-3 border-b border-hairline px-6 py-3 text-[13px] last:border-0 hover:bg-bg/40', GRID)}
              >
                <button type="button" onClick={() => setDetail(c)} className="flex min-w-0 items-center gap-3 text-left">
                  <div className="flex h-8 w-8 flex-none items-center justify-center rounded-pill bg-primary/10 text-[11px] font-semibold uppercase text-primary">
                    {(c.fullName ?? c.phone).slice(0, 2)}
                  </div>
                  <span className="flex min-w-0 items-center gap-1.5">
                    <span className="truncate font-medium">{c.fullName ?? t('sales.walkIn')}</span>
                    {c.isRegular && <Badge tone="success">{t('seller.clients.regular')}</Badge>}
                  </span>
                </button>
                <span className="text-muted-2 nums">{c.phone}</span>
                <span className={cn('text-right font-semibold nums', c.totalDebt > 0 && 'text-warn-text')}>
                  {formatSum(c.totalDebt)}
                </span>
                <span className="text-muted">{t(`customers.types.${c.customerType.toLowerCase()}` as never)}</span>
                <div className="flex items-center justify-end gap-1 opacity-0 transition-opacity group-hover:opacity-100">
                  {canManage && (
                    <button
                      type="button"
                      onClick={() => openEdit(c)}
                      className="flex h-7 w-7 items-center justify-center rounded-md text-muted-2 hover:text-primary"
                      aria-label={t('common.save')}
                    >
                      <Pencil size={14} />
                    </button>
                  )}
                  {canDelete && (
                    <button
                      type="button"
                      onClick={() => askDelete(c)}
                      className="flex h-7 w-7 items-center justify-center rounded-md text-muted-2 hover:text-danger"
                      aria-label={t('warehouse.form.delete')}
                    >
                      <Trash2 size={14} />
                    </button>
                  )}
                </div>
              </div>
            ))
          ) : (
            <div className="py-20 text-center text-[14px] text-muted-2">{t('customers.empty')}</div>
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

      <CustomerFormModal open={formOpen} editing={editing} onClose={() => setFormOpen(false)} />
      <CustomerDetailModal customer={detail} onClose={() => setDetail(null)} />
    </>
  );
}
