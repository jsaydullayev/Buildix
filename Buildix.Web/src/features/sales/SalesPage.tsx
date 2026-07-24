import { useMemo, useState } from 'react';
import { useNavigate, useParams } from 'react-router-dom';
import { useQuery, keepPreviousData } from '@tanstack/react-query';
import { useTranslation } from 'react-i18next';
import { startOfDay, subDays } from 'date-fns';
import { Plus, Search, FileDown } from 'lucide-react';
import { PageHeader, Button, Card, StatCard, Badge, Spinner } from '@/shared/ui';
import { cn } from '@/shared/lib/cn';
import { formatSum, formatTime } from '@/shared/lib/format';
import { downloadBlob } from '@/shared/lib/download';
import { useDebounce } from '@/shared/hooks/useDebounce';
import { useAuth } from '@/shared/auth/useAuth';
import { PERMISSIONS } from '@/shared/config/permissions';
import { employeesApi } from '@/features/employees/api';
import { salesApi, type Sale } from './api';
import { SaleDetailModal } from './SaleDetailModal';

const PAGE_SIZE = 20;
type Period = 'today' | 'yesterday' | 'all';
const PAYMENT_FILTERS = ['all', 'cash', 'card', 'debt'] as const;
type PayFilter = (typeof PAYMENT_FILTERS)[number];

const PAY_PARAM: Record<PayFilter, string | null> = {
  all: null,
  cash: 'Cash',
  card: 'Terminal',
  debt: 'Debt',
};

function periodRange(period: Period): { from?: string; to?: string } {
  const now = new Date();
  // «Все» — vaqt chegarasisiz (butun tarix).
  if (period === 'all') return {};
  // «Вчера» — kechagi kunning boshidan bugungi kun boshigacha.
  if (period === 'yesterday')
    return { from: startOfDay(subDays(now, 1)).toISOString(), to: startOfDay(now).toISOString() };
  // «Сегодня».
  return { from: startOfDay(now).toISOString(), to: now.toISOString() };
}

export default function SalesPage() {
  const { t, i18n } = useTranslation();
  const { subdomain } = useParams();
  const navigate = useNavigate();
  const { hasPermission } = useAuth();
  const canCreate = hasPermission(PERMISSIONS.sales.create);
  const canExport = hasPermission(PERMISSIONS.sales.export);
  // Sotuvchi bo'yicha filtr — barcha xodimlar ro'yxati kerak (users.access).
  const canFilterSeller = hasPermission(PERMISSIONS.users.access);

  const [period, setPeriod] = useState<Period>('today');
  const [pay, setPay] = useState<PayFilter>('all');
  const [sellerId, setSellerId] = useState<string>('');
  const [search, setSearch] = useState('');
  const [page, setPage] = useState(1);
  const [downloading, setDownloading] = useState(false);
  // Ochilgan chek — ro'yxatdagi qatorning o'zi (items/payments allaqachon
  // yuklangan), shuning uchun modal qo'shimcha so'rovsiz darhol chiziladi.
  const [openSale, setOpenSale] = useState<Sale | null>(null);
  const debouncedSearch = useDebounce(search);
  const range = useMemo(() => periodRange(period), [period]);

  const summaryQuery = useQuery({ queryKey: ['today-sales'], queryFn: salesApi.todaySummary });
  const sellersQuery = useQuery({
    queryKey: ['sellers-filter'],
    queryFn: () => employeesApi.list(),
    enabled: canFilterSeller,
  });

  const listQuery = useQuery({
    queryKey: ['sales', { page, search: debouncedSearch, pay, period, sellerId }],
    queryFn: () =>
      salesApi.listPaged({
        page,
        size: PAGE_SIZE,
        search: debouncedSearch,
        paymentType: PAY_PARAM[pay],
        from: range.from,
        to: range.to,
        sellerId: sellerId || null,
      }),
    placeholderData: keepPreviousData,
  });

  const s = summaryQuery.data;
  const avg = s && s.totalSales > 0 ? Math.round(s.totalAmount / s.totalSales) : 0;
  const resetPage = () => setPage(1);

  async function handleExport() {
    if (downloading) return;
    setDownloading(true);
    try {
      const blob = await salesApi.exportExcel(i18n.language);
      downloadBlob(blob, 'sales.xlsx');
    } catch {
      // best-effort; ignore
    } finally {
      setDownloading(false);
    }
  }

  return (
    <>
      <PageHeader
        title={t('sales.title')}
        actions={
          <div className="flex items-center gap-3">
            {canExport && (
              <Button variant="secondary" loading={downloading} onClick={handleExport}>
                <FileDown size={15} />
                {t('common.export')}
              </Button>
            )}
            {canCreate && (
              <Button onClick={() => navigate(`/${subdomain}/pos`)}>
                <Plus size={15} strokeWidth={2.4} />
                {t('sales.newSale')}
              </Button>
            )}
          </div>
        }
      />

      <div className="flex flex-1 flex-col gap-[18px] p-8">
        {/* Stat cards (today) */}
        <div className="grid grid-cols-4 gap-4">
          <StatCard
            label={t('sales.stats.sum')}
            value={formatSum(s?.totalAmount ?? 0)}
            suffix={t('common.currency')}
            hint={t('sales.stats.today')}
          />
          <StatCard label={t('sales.stats.checks')} value={s?.totalSales ?? 0} hint={t('sales.stats.today')} />
          <StatCard label={t('sales.stats.avg')} value={formatSum(avg)} suffix={t('common.currency')} />
          <StatCard
            label={t('sales.stats.debt')}
            value={formatSum(s?.debtAmount ?? 0)}
            suffix={t('common.currency')}
            tone={s && s.debtAmount > 0 ? 'warn' : 'default'}
          />
        </div>

        {/* Toolbar */}
        <div className="flex flex-wrap items-center gap-3">
          <div className="relative min-w-[280px] flex-1">
            <Search size={17} className="absolute left-3.5 top-1/2 -translate-y-1/2 text-muted-2" />
            <input
              value={search}
              onChange={(e) => {
                setSearch(e.target.value);
                resetPage();
              }}
              placeholder={t('sales.searchPlaceholder')}
              className="h-11 w-full rounded-input border border-input-border bg-surface pl-11 pr-4 text-[14px] outline-none focus:border-primary focus:shadow-focus-ring"
            />
          </div>

          <div className="flex items-center gap-1.5">
            {PAYMENT_FILTERS.map((p) => (
              <Chip
                key={p}
                label={t(`sales.payment.${p}`)}
                active={pay === p}
                onClick={() => {
                  setPay(p);
                  resetPage();
                }}
              />
            ))}
          </div>

          {canFilterSeller && (sellersQuery.data ?? []).length > 0 && (
            <div className="flex flex-wrap items-center gap-1.5">
              <Chip
                label={t('sales.allSellers')}
                active={sellerId === ''}
                onClick={() => {
                  setSellerId('');
                  resetPage();
                }}
              />
              {(sellersQuery.data ?? []).map((u) => (
                <Chip
                  key={u.id}
                  label={u.fullName}
                  active={sellerId === u.id}
                  onClick={() => {
                    setSellerId(u.id);
                    resetPage();
                  }}
                />
              ))}
            </div>
          )}

          <div className="ml-auto inline-flex rounded-input bg-hairline p-1">
            {(['today', 'yesterday', 'all'] as Period[]).map((p) => (
              <button
                key={p}
                type="button"
                onClick={() => {
                  setPeriod(p);
                  resetPage();
                }}
                className={cn(
                  'rounded-md px-4 py-1.5 text-[13px] font-medium transition-colors',
                  period === p ? 'bg-surface text-text shadow-card' : 'text-muted hover:text-text',
                )}
              >
                {t(`sales.period.${p}`)}
              </button>
            ))}
          </div>
        </div>

        {/* Table */}
        <Card className="overflow-hidden">
          <div className="grid grid-cols-sales items-center gap-4 border-b border-hairline bg-bg/40 px-6 py-3 text-[11.5px] font-semibold tracking-[0.4px] text-muted-2">
            <span>{t('sales.cols.number')}</span>
            <span>{t('sales.cols.time')}</span>
            <span>{t('sales.cols.seller')}</span>
            <span>{t('sales.cols.items')}</span>
            <span>{t('sales.cols.customer')}</span>
            <span>{t('sales.cols.pay')}</span>
            <span className="text-right">{t('sales.cols.sum')}</span>
          </div>

          {listQuery.isLoading ? (
            <div className="flex items-center justify-center py-20 text-primary">
              <Spinner size={24} />
            </div>
          ) : listQuery.data && listQuery.data.items.length > 0 ? (
            listQuery.data.items.map((sale) => (
              <SaleRow key={sale.id} sale={sale} onOpen={() => setOpenSale(sale)} />
            ))
          ) : (
            <div className="py-20 text-center text-[14px] text-muted-2">{t('sales.empty')}</div>
          )}
        </Card>

        {listQuery.data && listQuery.data.total > 0 && (
          <div className="flex items-center justify-between">
            <span className="text-[12.5px] text-muted-2">
              {t('sales.showing', { shown: listQuery.data.items.length, total: listQuery.data.total })}
            </span>
            <Pager
              page={page}
              totalPages={listQuery.data.totalPages}
              onChange={setPage}
              busy={listQuery.isFetching}
            />
          </div>
        )}
      </div>

      <SaleDetailModal sale={openSale} onClose={() => setOpenSale(null)} />
    </>
  );
}

function paymentBadge(sale: Sale) {
  if (sale.status === 'Debt' || sale.remainingAmount > 0) return { key: 'debt', tone: 'warn' as const };
  const types = new Set(sale.payments.map((p) => p.paymentType));
  if (types.size > 1) return { key: 'mixed', tone: 'neutral' as const };
  const type = sale.payments[0]?.paymentType;
  if (type === 'Cash') return { key: 'cash', tone: 'success' as const };
  if (type === 'Terminal' || type === 'Click' || type === 'Transfer') return { key: 'card', tone: 'info' as const };
  return { key: 'cash', tone: 'success' as const };
}

function SaleRow({ sale, onOpen }: { sale: Sale; onOpen: () => void }) {
  const { t } = useTranslation();
  const badge = paymentBadge(sale);
  const itemsText =
    sale.items.length === 0
      ? '—'
      : sale.items.length === 1
        ? sale.items[0]!.productName
        : `${sale.items[0]!.productName} +${sale.items.length - 1}`;

  return (
    // Butun qator bosiladi — chek ichini (tovarlar, to'lovlar, chegirma)
    // ochadigan yagona yo'l shu edi va u yo'q edi.
    <button
      type="button"
      onClick={onOpen}
      className="grid w-full grid-cols-sales items-center gap-4 border-b border-hairline px-6 py-3.5 text-left text-[13px] last:border-0 hover:bg-bg/40"
    >
      <span className="font-semibold text-primary nums">№{sale.saleNumber}</span>
      <span className="text-muted-2 nums">{formatTime(sale.createdAt)}</span>
      <span className="truncate font-medium">{sale.sellerName}</span>
      <span className="truncate text-muted">{itemsText}</span>
      <span className="truncate text-muted">{sale.customerName ?? t('sales.walkIn')}</span>
      <span>
        <Badge tone={badge.tone}>{t(`sales.payment.${badge.key}` as never)}</Badge>
      </span>
      <span className="text-right font-semibold nums">{formatSum(sale.totalAmount)}</span>
    </button>
  );
}

function Chip({ label, active, onClick }: { label: string; active: boolean; onClick: () => void }) {
  return (
    <button
      type="button"
      onClick={onClick}
      className={cn(
        'rounded-input px-3.5 py-2 text-[13px] font-medium transition-colors',
        active ? 'bg-primary text-white' : 'border border-input-border bg-surface text-muted hover:text-text',
      )}
    >
      {label}
    </button>
  );
}

function Pager({
  page,
  totalPages,
  onChange,
  busy,
}: {
  page: number;
  totalPages: number;
  onChange: (p: number) => void;
  busy: boolean;
}) {
  if (totalPages <= 1) return null;
  return (
    <div className="flex items-center gap-1.5">
      {busy && <Spinner size={14} className="mr-1 text-primary" />}
      <button
        type="button"
        disabled={page <= 1}
        onClick={() => onChange(page - 1)}
        className="h-8 rounded-md border border-input-border bg-surface px-3 text-[13px] disabled:opacity-40"
      >
        ‹
      </button>
      <span className="px-2 text-[13px] text-muted nums">
        {page} / {totalPages}
      </span>
      <button
        type="button"
        disabled={page >= totalPages}
        onClick={() => onChange(page + 1)}
        className="h-8 rounded-md border border-input-border bg-surface px-3 text-[13px] disabled:opacity-40"
      >
        ›
      </button>
    </div>
  );
}
