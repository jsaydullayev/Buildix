import { useEffect, useState } from 'react';
import { useQuery, useMutation, useQueryClient, keepPreviousData } from '@tanstack/react-query';
import { useTranslation } from 'react-i18next';
import { Search, Plus, UserPlus } from 'lucide-react';
import { PageHeader, Card, Badge, Spinner, Button, Modal } from '@/shared/ui';
import { cn } from '@/shared/lib/cn';
import { formatSum, formatShortDate } from '@/shared/lib/format';
import { useDebounce } from '@/shared/hooks/useDebounce';
import { useAuth } from '@/shared/auth/useAuth';
import { PERMISSIONS } from '@/shared/config/permissions';
import type { ApiError } from '@/shared/api/types';
import { customersApi, type Customer } from '@/features/customers/api';

const PAGE_SIZE = 50;
const GRID = 'grid-cols-[2fr_1.2fr_1.3fr_1fr_0.9fr]';
const FILTERS = ['all', 'withDebt', 'regular'] as const;
type Filter = (typeof FILTERS)[number];

function initials(name: string): string {
  const p = name.trim().split(/\s+/);
  return ((p[0]?.[0] ?? '') + (p[1]?.[0] ?? '')).toUpperCase();
}

/** Seller client directory: view customers + debt, add a new one (name/phone/type). */
export default function SellerClientsPage() {
  const { t, i18n } = useTranslation();
  const { hasPermission } = useAuth();
  const canManage = hasPermission(PERMISSIONS.customers.manage);
  const [search, setSearch] = useState('');
  const [filter, setFilter] = useState<Filter>('all');
  const [addOpen, setAddOpen] = useState(false);
  const debouncedSearch = useDebounce(search);

  const listQuery = useQuery({
    queryKey: ['seller-clients', { search: debouncedSearch, filter }],
    queryFn: () =>
      customersApi.listPaged({
        page: 1,
        size: PAGE_SIZE,
        search: debouncedSearch,
        withDebt: filter === 'withDebt' || undefined,
        isRegular: filter === 'regular' || undefined,
      }),
    placeholderData: keepPreviousData,
  });

  const rows = listQuery.data?.items ?? [];

  return (
    <>
      <PageHeader
        title={t('seller.clients.title')}
        subtitle={t('seller.clients.subtitle', { count: listQuery.data?.total ?? 0 })}
        actions={
          canManage && (
            <Button onClick={() => setAddOpen(true)}>
              <Plus size={15} strokeWidth={2.4} />
              {t('seller.clients.add')}
            </Button>
          )
        }
      />

      <div className="mx-auto flex w-full max-w-[1240px] flex-1 flex-col gap-[18px] p-8">
        <div className="flex flex-wrap items-center gap-3">
          <div className="relative max-w-[420px] flex-1">
            <Search size={17} className="absolute left-3.5 top-1/2 -translate-y-1/2 text-muted-2" />
            <input
              value={search}
              onChange={(e) => setSearch(e.target.value)}
              placeholder={t('seller.clients.searchPlaceholder')}
              className="h-11 w-full rounded-input border border-input-border bg-surface pl-11 pr-4 text-[14px] outline-none focus:border-primary focus:shadow-focus-ring"
            />
          </div>
          <div className="flex items-center gap-1.5">
            {FILTERS.map((f) => (
              <button
                key={f}
                type="button"
                onClick={() => setFilter(f)}
                className={cn(
                  'h-11 rounded-input border px-4 text-[13px] font-medium transition-colors',
                  filter === f
                    ? 'border-primary bg-primary text-white'
                    : 'border-input-border bg-surface text-muted hover:text-text',
                )}
              >
                {t(`seller.clients.filters.${f}`)}
              </button>
            ))}
          </div>
        </div>

        <Card className="overflow-hidden">
          <div className={cn('grid items-center gap-4 border-b border-hairline bg-bg/40 px-6 py-3 text-[11.5px] font-semibold tracking-[0.4px] text-muted-2', GRID)}>
            <span>{t('seller.clients.cols.client')}</span>
            <span>{t('seller.clients.cols.phone')}</span>
            <span>{t('seller.clients.cols.monthPurchases')}</span>
            <span>{t('seller.clients.cols.lastPurchase')}</span>
            <span className="text-right">{t('seller.clients.cols.debt')}</span>
          </div>

          {listQuery.isLoading ? (
            <div className="flex items-center justify-center py-20 text-primary">
              <Spinner size={24} />
            </div>
          ) : rows.length > 0 ? (
            rows.map((c) => <Row key={c.id} customer={c} lang={i18n.language} />)
          ) : (
            <div className="py-20 text-center text-[14px] text-muted-2">{t('seller.clients.empty')}</div>
          )}
        </Card>
      </div>

      <AddClientModal open={addOpen} onClose={() => setAddOpen(false)} />
    </>
  );
}

function Row({ customer: c, lang }: { customer: Customer; lang: string }) {
  const { t } = useTranslation();
  return (
    <div className={cn('grid items-center gap-4 border-b border-hairline px-6 py-3.5 text-[13px] last:border-0 hover:bg-bg/40', GRID)}>
      <div className="flex min-w-0 items-center gap-2.5">
        <span className="flex h-8 w-8 flex-none items-center justify-center rounded-pill bg-primary-soft text-[11px] font-semibold text-primary">
          {initials(c.fullName ?? c.phone)}
        </span>
        <span className="flex min-w-0 items-center gap-1.5">
          <span className="truncate font-medium">{c.fullName ?? c.phone}</span>
          <Badge tone="neutral">{t(`debts.type.${c.customerType === 'Legal' ? 'legal' : 'individual'}`)}</Badge>
          {c.isRegular && <Badge tone="info">{t('seller.clients.regular')}</Badge>}
        </span>
      </div>
      <span className="text-muted-2 nums">{c.phone}</span>
      <span className="nums">
        {c.monthPurchaseCount > 0 ? (
          <>
            <span className="font-medium">{t('seller.clients.purchasesCount', { count: c.monthPurchaseCount })}</span>
            <span className="text-muted-2"> · {formatSum(c.monthPurchaseSum)}</span>
          </>
        ) : (
          <span className="text-muted-2">—</span>
        )}
      </span>
      <span className="text-muted-2 nums">{c.lastPurchaseAt ? formatShortDate(c.lastPurchaseAt, lang) : '—'}</span>
      <span className={cn('text-right font-semibold nums', c.totalDebt > 0 ? 'text-warn-text' : 'text-muted-2')}>
        {c.totalDebt > 0 ? formatSum(c.totalDebt) : '—'}
      </span>
    </div>
  );
}

function AddClientModal({ open, onClose }: { open: boolean; onClose: () => void }) {
  const { t } = useTranslation();
  const qc = useQueryClient();
  const [fullName, setFullName] = useState('');
  const [phone, setPhone] = useState('');
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    if (open) {
      setFullName('');
      setPhone('');
      setError(null);
    }
  }, [open]);

  const mutation = useMutation({
    // Mijoz turi tanlagichi olib tashlangan — hamma yangi mijoz Individual
    // (backend standarti); tur biznes oqimida ishlatilmaydi.
    mutationFn: () => customersApi.create({ fullName: fullName || null, phone }),
    onSuccess: () => {
      void qc.invalidateQueries({ queryKey: ['seller-clients'] });
      onClose();
    },
    onError: (e) => setError((e as unknown as ApiError).message ?? t('common.somethingWrong')),
  });

  const valid = phone.trim().length >= 5;
  const inputCls =
    'h-12 rounded-input border border-input-border bg-surface px-4 text-[15px] outline-none focus:border-primary focus:shadow-focus-ring';

  return (
    <Modal
      open={open}
      onClose={onClose}
      title={t('seller.clients.add')}
      footer={
        <>
          <Button variant="secondary" onClick={onClose}>
            {t('common.cancel')}
          </Button>
          <Button disabled={!valid} loading={mutation.isPending} onClick={() => mutation.mutate()}>
            <UserPlus size={15} />
            {t('common.save')}
          </Button>
        </>
      }
    >
      <div className="flex flex-col gap-4">
        <p className="text-[13px] text-muted">{t('seller.clients.addHint')}</p>
        <div className="flex flex-col gap-1.5">
          <label className="text-[13px] font-medium text-label">{t('seller.clients.form.name')}</label>
          <input value={fullName} onChange={(e) => setFullName(e.target.value)} className={inputCls} autoFocus />
        </div>
        <div className="flex flex-col gap-1.5">
          <label className="text-[13px] font-medium text-label">{t('seller.clients.form.phone')}</label>
          <input
            value={phone}
            onChange={(e) => setPhone(e.target.value)}
            placeholder="+998 __ ___ __ __"
            className={`${inputCls} nums`}
          />
        </div>
        {error && <p className="text-[12.5px] text-danger">{error}</p>}
      </div>
    </Modal>
  );
}
