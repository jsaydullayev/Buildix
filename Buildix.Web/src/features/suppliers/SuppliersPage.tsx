import { useEffect, useMemo, useState } from 'react';
import { useNavigate, useParams } from 'react-router-dom';
import { useQuery, useMutation, useQueryClient } from '@tanstack/react-query';
import { useTranslation } from 'react-i18next';
import { Plus, Search, Building2, Pencil, Trash2 } from 'lucide-react';
import { PageHeader, Button, Card, Badge, Spinner } from '@/shared/ui';
import { cn } from '@/shared/lib/cn';
import { formatSum, formatShortDate } from '@/shared/lib/format';
import { useDebounce } from '@/shared/hooks/useDebounce';
import { useAuth } from '@/shared/auth/useAuth';
import { PERMISSIONS } from '@/shared/config/permissions';
import type { ApiError } from '@/shared/api/types';
import { purchasesApi, type Supplier, type ZakupReceipt } from '@/features/purchases/api';
import { SupplierFormModal } from '@/features/purchases/SupplierFormModal';
import { PaySupplierDebtModal } from './PaySupplierDebtModal';

const PAGE_SIZE = 50;

/**
 * Поставщики — master-detail. Left: searchable supplier cards with their debt.
 * Right (sticky): the selected supplier's contact, delivery term, outstanding
 * debt (with a FIFO «Погасить долг»), and recent receipts.
 */
export default function SuppliersPage() {
  const { t, i18n } = useTranslation();
  const { subdomain } = useParams();
  const navigate = useNavigate();
  const { hasPermission } = useAuth();
  const qc = useQueryClient();
  const canManage = hasPermission(PERMISSIONS.suppliers.manage);
  const canDelete = hasPermission(PERMISSIONS.suppliers.delete);
  const canPurchase = hasPermission(PERMISSIONS.zakup.create);

  const [search, setSearch] = useState('');
  const [selectedId, setSelectedId] = useState<string | null>(null);
  const [formOpen, setFormOpen] = useState(false);
  const [editing, setEditing] = useState<Supplier | null>(null);
  const [payOpen, setPayOpen] = useState(false);
  const debouncedSearch = useDebounce(search);

  const listQuery = useQuery({
    queryKey: ['suppliers', { search: debouncedSearch }],
    queryFn: () => purchasesApi.suppliersPaged(1, PAGE_SIZE, debouncedSearch),
  });
  const suppliers = useMemo(() => listQuery.data?.items ?? [], [listQuery.data]);

  // Keep a valid selection: default to the first, and follow list changes.
  useEffect(() => {
    if (suppliers.length === 0) {
      setSelectedId(null);
    } else if (!selectedId || !suppliers.some((s) => s.id === selectedId)) {
      setSelectedId(suppliers[0]!.id);
    }
  }, [suppliers, selectedId]);

  const selected = useMemo(() => suppliers.find((s) => s.id === selectedId) ?? null, [suppliers, selectedId]);

  const del = useMutation({
    mutationFn: (s: Supplier) => purchasesApi.deleteSupplier(s.id),
    onSuccess: () => void qc.invalidateQueries({ queryKey: ['suppliers'] }),
  });

  async function askDelete(s: Supplier) {
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

  const totalDebt = suppliers.reduce((s, x) => s + x.outstandingDebt, 0);

  return (
    <>
      <PageHeader
        title={t('suppliers.title')}
        subtitle={t('suppliers.subtitleDebt', { count: suppliers.length, debt: formatSum(totalDebt) })}
        actions={
          canManage && (
            <Button
              onClick={() => {
                setEditing(null);
                setFormOpen(true);
              }}
            >
              <Plus size={15} strokeWidth={2.4} />
              {t('suppliers.add')}
            </Button>
          )
        }
      />

      <div className="flex flex-1 gap-[18px] p-8">
        {/* Master */}
        <div className="flex min-w-0 flex-1 flex-col gap-3">
          <div className="relative">
            <Search size={17} className="absolute left-3.5 top-1/2 -translate-y-1/2 text-muted-2" />
            <input
              value={search}
              onChange={(e) => setSearch(e.target.value)}
              placeholder={t('suppliers.searchPlaceholder')}
              className="h-11 w-full rounded-input border border-input-border bg-surface pl-11 pr-4 text-[14px] outline-none focus:border-primary focus:shadow-focus-ring"
            />
          </div>

          {listQuery.isLoading ? (
            <div className="flex items-center justify-center py-20 text-primary">
              <Spinner size={24} />
            </div>
          ) : suppliers.length === 0 ? (
            <Card className="py-20 text-center text-[14px] text-muted-2">{t('suppliers.empty')}</Card>
          ) : (
            <div className="flex flex-col gap-2.5">
              {suppliers.map((s) => (
                <SupplierCard
                  key={s.id}
                  supplier={s}
                  active={s.id === selectedId}
                  onClick={() => setSelectedId(s.id)}
                />
              ))}
            </div>
          )}
        </div>

        {/* Detail */}
        <div className="w-[380px] flex-none">
          {selected && (
            <Card className="sticky top-8 p-5">
              <div className="mb-4 flex items-start justify-between gap-3">
                <div className="min-w-0">
                  <h2 className="truncate text-[16px] font-semibold">{selected.name}</h2>
                  {selected.comment && (
                    <p className="mt-0.5 truncate text-[12.5px] text-muted-2">{selected.comment}</p>
                  )}
                </div>
                {canPurchase && (
                  <Button size="sm" onClick={() => navigate(`/${subdomain}/purchases`)}>
                    {t('purchases.newPurchase')}
                  </Button>
                )}
              </div>

              <dl className="flex flex-col gap-2.5 text-[13px]">
                <DetailRow label={t('suppliers.cols.phone')} value={selected.phone ?? '—'} />
                <DetailRow label={t('suppliers.form.contact')} value={selected.contactPerson ?? '—'} />
                <DetailRow label={t('suppliers.form.deliveryTerm')} value={selected.deliveryTerm ?? '—'} />
                <div className="flex items-center justify-between border-t border-hairline pt-2.5">
                  <dt className="text-muted">{t('suppliers.debtLabel')}</dt>
                  <dd className={cn('font-semibold nums', selected.outstandingDebt > 0 ? 'text-danger' : 'text-success')}>
                    {selected.outstandingDebt > 0 ? formatSum(selected.outstandingDebt) : t('suppliers.noDebt')}
                  </dd>
                </div>
              </dl>

              {canPurchase && selected.outstandingDebt > 0 && (
                <Button variant="secondary" fullWidth className="mt-3" onClick={() => setPayOpen(true)}>
                  {t('suppliers.payDebt')}
                </Button>
              )}

              {(canManage || canDelete) && (
                <div className="mt-3 flex items-center gap-2">
                  {canManage && (
                    <Button
                      variant="ghost"
                      size="sm"
                      onClick={() => {
                        setEditing(selected);
                        setFormOpen(true);
                      }}
                    >
                      <Pencil size={13} />
                      {t('common.save')}
                    </Button>
                  )}
                  {canDelete && (
                    <Button variant="ghost" size="sm" className="text-danger" onClick={() => askDelete(selected)}>
                      <Trash2 size={13} />
                      {t('warehouse.form.delete')}
                    </Button>
                  )}
                </div>
              )}

              <RecentReceipts supplierId={selected.id} lang={i18n.language} />
            </Card>
          )}
        </div>
      </div>

      <SupplierFormModal open={formOpen} editing={editing} onClose={() => setFormOpen(false)} />
      {selected && (
        <PaySupplierDebtModal
          open={payOpen}
          supplier={selected}
          onClose={() => setPayOpen(false)}
        />
      )}
    </>
  );
}

function SupplierCard({
  supplier: s,
  active,
  onClick,
}: {
  supplier: Supplier;
  active: boolean;
  onClick: () => void;
}) {
  const { t } = useTranslation();
  return (
    <button
      type="button"
      onClick={onClick}
      className={cn(
        'flex items-center gap-3 rounded-card border bg-surface px-4 py-3 text-left transition-colors',
        active ? 'border-primary shadow-focus-ring' : 'border-border hover:border-input-border',
      )}
    >
      <div className="flex h-9 w-9 flex-none items-center justify-center rounded-lg bg-primary/10 text-primary">
        <Building2 size={17} />
      </div>
      <div className="min-w-0 flex-1">
        <div className="truncate text-[13.5px] font-semibold">{s.name}</div>
        <div className="truncate text-[11.5px] text-muted-2">
          {s.comment ? `${s.comment} · ` : ''}
          {t('purchases.itemsCount', { count: s.receiptCount })}
        </div>
      </div>
      <div className="flex-none text-right">
        {s.outstandingDebt > 0 ? (
          <>
            <div className="text-[13px] font-semibold text-danger nums">{formatSum(s.outstandingDebt)}</div>
            <div className="text-[10.5px] text-muted-2">{t('suppliers.weOwe')}</div>
          </>
        ) : (
          <Badge tone="success">{t('suppliers.noDebt')}</Badge>
        )}
      </div>
    </button>
  );
}

function DetailRow({ label, value }: { label: string; value: string }) {
  return (
    <div className="flex items-center justify-between gap-3">
      <dt className="text-muted">{label}</dt>
      <dd className="truncate font-medium nums">{value}</dd>
    </div>
  );
}

const DELIVERY_TONE: Record<string, 'success' | 'info'> = { Accepted: 'success', InTransit: 'info' };

function RecentReceipts({ supplierId, lang }: { supplierId: string; lang: string }) {
  const { t } = useTranslation();
  const query = useQuery({
    queryKey: ['supplier-receipts', supplierId],
    queryFn: () => purchasesApi.receiptsPaged(1, 5, supplierId),
  });
  const items = query.data?.items ?? [];

  return (
    <div className="mt-5 border-t border-hairline pt-4">
      <h3 className="mb-2.5 text-[13px] font-semibold">{t('suppliers.recentReceipts')}</h3>
      {query.isLoading ? (
        <div className="flex justify-center py-4 text-primary">
          <Spinner size={18} />
        </div>
      ) : items.length === 0 ? (
        <p className="py-3 text-center text-[12.5px] text-muted-2">{t('purchases.empty')}</p>
      ) : (
        <div className="flex flex-col gap-2">
          {items.map((r: ZakupReceipt) => (
            <div key={r.id} className="flex items-center justify-between gap-3 text-[12.5px]">
              <div className="min-w-0">
                <div className="font-semibold text-primary nums">
                  №{r.receiptNumber} · {formatShortDate(r.createdAt, lang)}
                </div>
                <div className="text-muted-2">{t('purchases.itemsCount', { count: r.itemCount })}</div>
              </div>
              <div className="flex-none text-right">
                <div className="font-semibold nums">{formatSum(r.totalAmount)}</div>
                <Badge tone={DELIVERY_TONE[r.deliveryStatus] ?? 'neutral'}>
                  {t(`purchases.delivery.${r.deliveryStatus === 'InTransit' ? 'inTransit' : 'accepted'}`)}
                </Badge>
              </div>
            </div>
          ))}
        </div>
      )}
    </div>
  );
}
