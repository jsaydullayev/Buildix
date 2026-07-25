import { useEffect, useState } from 'react';
import { useSearchParams } from 'react-router-dom';
import { useQuery, keepPreviousData } from '@tanstack/react-query';
import { useTranslation } from 'react-i18next';
import { Undo2, Search } from 'lucide-react';
import { PageHeader, Button, Card, Badge, Spinner, Modal } from '@/shared/ui';
import { cn } from '@/shared/lib/cn';
import { formatSum, formatQty, formatShortDate, formatTime } from '@/shared/lib/format';
import { useDebounce } from '@/shared/hooks/useDebounce';
import { useAuth } from '@/shared/auth/useAuth';
import { PERMISSIONS } from '@/shared/config/permissions';
import { returnsApi, type SaleReturn } from './api';
import { NewReturnModal } from './NewReturnModal';

const REASONS = ['all', 'Defect', 'NotFit', 'SellerError'] as const;
type ReasonFilter = (typeof REASONS)[number];
const REASON_KEY: Record<string, string> = { Defect: 'defect', NotFit: 'notFit', SellerError: 'sellerError', Other: 'other' };
const REASON_TONE: Record<string, 'danger' | 'warn' | 'info' | 'neutral'> = {
  Defect: 'danger',
  NotFit: 'info',
  SellerError: 'warn',
  Other: 'neutral',
};
const REFUND_KEY: Record<string, string> = { Cash: 'cash', Terminal: 'card', Transfer: 'transfer' };
const GRID = 'grid-cols-[0.9fr_0.7fr_1.8fr_1fr_0.9fr_0.9fr]';

/**
 * Возвраты — first-class return documents (В-##). Each row is one return: which
 * receipt, what came back, why, how the money went out, and the refunded sum.
 */
export default function ReturnsPage() {
  const { t, i18n } = useTranslation();
  const { hasPermission } = useAuth();
  const canCreate = hasPermission(PERMISSIONS.sales.return);

  const [reason, setReason] = useState<ReasonFilter>('all');
  const [search, setSearch] = useState('');
  const [page, setPage] = useState(1);
  const [newOpen, setNewOpen] = useState(false);
  const [detail, setDetail] = useState<SaleReturn | null>(null);
  const debouncedSearch = useDebounce(search);

  // Deep-link from a receipt's "Оформить возврат": ?receipt=Ч-№ opens the modal
  // pre-loaded with that sale. Consume the param once so refresh doesn't re-open.
  const [searchParams, setSearchParams] = useSearchParams();
  const presetReceipt = searchParams.get('receipt');
  useEffect(() => {
    if (presetReceipt) {
      setNewOpen(true);
      searchParams.delete('receipt');
      setSearchParams(searchParams, { replace: true });
    }
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [presetReceipt]);
  const [presetSearch, setPresetSearch] = useState<string | undefined>(presetReceipt ?? undefined);
  useEffect(() => {
    if (presetReceipt) setPresetSearch(presetReceipt);
  }, [presetReceipt]);

  const summaryQuery = useQuery({ queryKey: ['returns-summary'], queryFn: () => returnsApi.summary() });
  const listQuery = useQuery({
    queryKey: ['returns', { page, reason, search: debouncedSearch }],
    queryFn: () => returnsApi.list({ page, size: 30, reason: reason === 'all' ? null : reason, search: debouncedSearch }),
    placeholderData: keepPreviousData,
  });

  const sum = summaryQuery.data;

  return (
    <>
      <PageHeader
        title={t('returns.title')}
        subtitle={t('returns.subtitle', {
          count: sum?.count ?? 0,
          sum: formatSum(sum?.totalAmount ?? 0),
          pct: sum?.revenuePercent ?? 0,
        })}
        actions={
          canCreate && (
            <Button onClick={() => setNewOpen(true)}>
              <Undo2 size={15} strokeWidth={2.4} />
              {t('returns.create')}
            </Button>
          )
        }
      />

      <div className="flex flex-1 flex-col gap-[18px] p-8">
        {/* Toolbar */}
        <div className="flex flex-wrap items-center gap-3">
          <div className="relative min-w-[280px] flex-1">
            <Search size={17} className="absolute left-3.5 top-1/2 -translate-y-1/2 text-muted-2" />
            <input
              value={search}
              onChange={(e) => {
                setSearch(e.target.value);
                setPage(1);
              }}
              placeholder={t('returns.searchPlaceholder')}
              className="h-11 w-full rounded-input border border-input-border bg-surface pl-11 pr-4 text-[14px] outline-none focus:border-primary focus:shadow-focus-ring"
            />
          </div>
          <div className="flex items-center gap-1.5">
            {REASONS.map((r) => (
              <button
                key={r}
                type="button"
                onClick={() => {
                  setReason(r);
                  setPage(1);
                }}
                className={cn(
                  'rounded-input px-3.5 py-2 text-[13px] font-medium transition-colors',
                  reason === r ? 'bg-primary text-white' : 'border border-input-border bg-surface text-muted hover:text-text',
                )}
              >
                {r === 'all' ? t('common.all') : t(`returns.reason.${REASON_KEY[r]}` as never)}
              </button>
            ))}
          </div>
        </div>

        {/* Table */}
        <Card className="overflow-hidden">
          <div className={cn('grid items-center gap-4 border-b border-hairline bg-bg/40 px-6 py-3 text-[11.5px] font-semibold tracking-[0.4px] text-muted-2', GRID)}>
            <span>{t('returns.cols.return')}</span>
            <span>{t('returns.cols.receipt')}</span>
            <span>{t('returns.cols.items')}</span>
            <span>{t('returns.cols.reason')}</span>
            <span>{t('returns.cols.money')}</span>
            <span className="text-right">{t('returns.cols.sum')}</span>
          </div>

          {listQuery.isLoading ? (
            <div className="flex items-center justify-center py-20 text-primary">
              <Spinner size={24} />
            </div>
          ) : listQuery.data && listQuery.data.items.length > 0 ? (
            listQuery.data.items.map((r) => <ReturnRow key={r.id} r={r} lang={i18n.language} onOpen={() => setDetail(r)} />)
          ) : (
            <div className="py-20 text-center text-[14px] text-muted-2">{t('returns.empty')}</div>
          )}
        </Card>

        <p className="text-[12px] text-muted-2">{t('returns.hint')}</p>

        {listQuery.data && listQuery.data.totalPages > 1 && (
          <div className="flex items-center justify-end gap-1.5">
            {listQuery.isFetching && <Spinner size={14} className="mr-1 text-primary" />}
            <button type="button" disabled={page <= 1} onClick={() => setPage((p) => p - 1)} className="h-8 rounded-md border border-input-border bg-surface px-3 text-[13px] disabled:opacity-40">‹</button>
            <span className="px-2 text-[13px] text-muted nums">{page} / {listQuery.data.totalPages}</span>
            <button type="button" disabled={page >= listQuery.data.totalPages} onClick={() => setPage((p) => p + 1)} className="h-8 rounded-md border border-input-border bg-surface px-3 text-[13px] disabled:opacity-40">›</button>
          </div>
        )}
      </div>

      <NewReturnModal open={newOpen} onClose={() => setNewOpen(false)} presetSearch={presetSearch} />
      <ReturnDetailModal r={detail} onClose={() => setDetail(null)} />
    </>
  );
}

function ReturnDetailModal({ r, onClose }: { r: SaleReturn | null; onClose: () => void }) {
  const { t, i18n } = useTranslation();
  if (!r) return null;
  return (
    <Modal
      open
      onClose={onClose}
      title={`В-${r.number}`}
      subtitle={`${formatShortDate(r.createdAt, i18n.language)}, ${formatTime(r.createdAt)} · Ч-${r.saleNumber}`}
      footer={<Button variant="secondary" onClick={onClose}>{t('common.close')}</Button>}
    >
      <div className="flex flex-col gap-4">
        <div className="flex flex-wrap items-center gap-x-6 gap-y-2 text-[13px]">
          <div className="flex items-center gap-2">
            <span className="text-muted">{t('returns.cols.reason')}</span>
            <Badge tone={REASON_TONE[r.reason] ?? 'neutral'}>{t(`returns.reason.${REASON_KEY[r.reason] ?? 'other'}` as never)}</Badge>
          </div>
          <div className="flex items-center gap-2">
            <span className="text-muted">{t('returns.cols.money')}</span>
            <span className="font-medium">{t(`returns.refund.${REFUND_KEY[r.refundMethod] ?? 'cash'}` as never)}</span>
          </div>
          {r.userName && (
            <div className="flex items-center gap-2">
              <span className="text-muted">{t('cash.cols.who')}</span>
              <span className="font-medium">{r.userName}</span>
            </div>
          )}
        </div>

        <div className="rounded-input border border-hairline">
          <div className="grid grid-cols-[1fr_80px_110px] gap-3 border-b border-hairline bg-bg/40 px-3 py-2 text-[11.5px] font-semibold text-muted-2">
            <span>{t('returns.cols.items')}</span>
            <span className="text-right">{t('sales.detail.qty')}</span>
            <span className="text-right">{t('sales.cols.sum')}</span>
          </div>
          {r.items.map((it, i) => (
            <div key={i} className="grid grid-cols-[1fr_80px_110px] items-center gap-3 border-b border-hairline px-3 py-2 text-[13px] last:border-0">
              <span className="truncate font-medium">{it.productName}</span>
              <span className="text-right text-muted nums">{formatQty(it.quantity)}</span>
              <span className="text-right font-semibold nums">{formatSum(it.lineTotal)}</span>
            </div>
          ))}
        </div>

        {r.comment && <p className="rounded-input bg-bg px-4 py-2.5 text-[13px] text-muted">{r.comment}</p>}

        <div className="flex items-baseline justify-between rounded-input bg-bg px-4 py-3">
          <span className="text-[14px] font-semibold">{t('returns.refundMoney')}</span>
          <span className="text-[18px] font-bold text-danger nums">− {formatSum(r.totalAmount)} <span className="text-[12px] font-normal text-muted-2">{t('common.currency')}</span></span>
        </div>
      </div>
    </Modal>
  );
}

function ReturnRow({ r, lang, onOpen }: { r: SaleReturn; lang: string; onOpen: () => void }) {
  const { t } = useTranslation();
  const itemsText =
    r.items.length === 0
      ? '—'
      : r.items.length === 1
        ? `${r.items[0]!.productName} ×${r.items[0]!.quantity}`
        : `${r.items[0]!.productName} +${r.items.length - 1}`;
  return (
    <button
      type="button"
      onClick={onOpen}
      className={cn('grid w-full items-center gap-4 border-b border-hairline px-6 py-3.5 text-left text-[13px] last:border-0 hover:bg-bg/40', GRID)}
    >
      <div className="min-w-0">
        <div className="font-semibold text-primary nums">В-{r.number}</div>
        <div className="text-[11.5px] text-muted-2 nums">{formatShortDate(r.createdAt, lang)}, {formatTime(r.createdAt)}</div>
      </div>
      <span className="text-muted-2 nums">Ч-{r.saleNumber}</span>
      <span className="truncate text-muted">{itemsText}</span>
      <span>
        <Badge tone={REASON_TONE[r.reason] ?? 'neutral'}>{t(`returns.reason.${REASON_KEY[r.reason] ?? 'other'}` as never)}</Badge>
      </span>
      <span className="text-muted-2">{t(`returns.refund.${REFUND_KEY[r.refundMethod] ?? 'cash'}` as never)}</span>
      <span className="text-right font-semibold text-danger nums">− {formatSum(r.totalAmount)}</span>
    </button>
  );
}
