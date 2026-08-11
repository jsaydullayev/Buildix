import { useMemo, useState } from 'react';
import { useQuery } from '@tanstack/react-query';
import { useTranslation } from 'react-i18next';
import { Plus } from 'lucide-react';
import { PageHeader, Button, Card, Badge, Spinner } from '@/shared/ui';
import { cn } from '@/shared/lib/cn';
import { formatSum, formatTime, formatFullDate } from '@/shared/lib/format';
import { useAuth } from '@/shared/auth/useAuth';
import { PERMISSIONS } from '@/shared/config/permissions';
import { shiftsApi } from '@/features/shifts/api';
import { cashLedgerApi, type CashMovement } from './api';
import { NewCashOperationModal } from './NewCashOperationModal';

type Filter = 'all' | 'income' | 'expense';

/** Movement type → { label key, badge tone }. */
const TYPE_META: Record<string, { key: string; tone: 'success' | 'info' | 'warn' | 'danger' | 'neutral' }> = {
  Sale: { key: 'sale', tone: 'success' },
  DebtPayment: { key: 'debtPayment', tone: 'info' },
  Deposit: { key: 'deposit', tone: 'info' },
  Expense: { key: 'expense', tone: 'danger' },
  Collection: { key: 'collection', tone: 'warn' },
  Opening: { key: 'opening', tone: 'neutral' },
  // Katalogda yo'q tovar qo'shni do'kondan olinganda, puli kassadan beriladi.
  ExternalPurchase: { key: 'externalPurchase', tone: 'warn' },
};

/**
 * Касса — the day's cash ledger. Balance is the authoritative till figure; the
 * list is the typed movement log (sales & debt payments land automatically,
 * deposits/withdrawals from the cash-register flow). Filter by income/outflow.
 */
export default function CashPage() {
  const { t, i18n } = useTranslation();
  const { hasPermission } = useAuth();
  const canManage = hasPermission(PERMISSIONS.cashregister.manage);
  const [filter, setFilter] = useState<Filter>('all');
  const [opOpen, setOpOpen] = useState(false);

  const ledgerQuery = useQuery({ queryKey: ['cash-ledger'], queryFn: () => cashLedgerApi.ledger() });
  const data = ledgerQuery.data;
  // Current open shift powers the «смена №… открыта» caption + opening-cash line.
  const shiftQuery = useQuery({ queryKey: ['shift-current'], queryFn: shiftsApi.current });
  const shift = shiftQuery.data;

  const subtitle = shift
    ? `${formatFullDate(new Date(), i18n.language)} · ${t('cash.shiftOpen', { n: shift.shiftNumber })} · ${t('cash.subtitle')}`
    : `${formatFullDate(new Date(), i18n.language)} · ${t('cash.subtitle')}`;

  const rows = useMemo(() => {
    const items = data?.items ?? [];
    if (filter === 'income') return items.filter((m) => m.amount > 0);
    if (filter === 'expense') return items.filter((m) => m.amount < 0);
    return items;
  }, [data, filter]);

  const FILTERS: Filter[] = ['all', 'income', 'expense'];

  return (
    <>
      <PageHeader
        title={t('cash.title')}
        subtitle={subtitle}
        actions={
          canManage && (
            <Button onClick={() => setOpOpen(true)}>
              <Plus size={15} strokeWidth={2.4} />
              {t('cash.newOp.button')}
            </Button>
          )
        }
      />

      <div className="flex flex-1 flex-col gap-[18px] p-4 sm:p-6 lg:p-8">
        {/* KPI */}
        <div className="grid grid-cols-1 sm:grid-cols-3 gap-4">
          <div className="rounded-card bg-sidebar p-5 text-white">
            <div className="text-[12.5px] text-white/60">{t('cash.balance')}</div>
            <div className="mt-1.5 text-[26px] font-bold tracking-[-0.4px] nums">
              {formatSum(data?.balance ?? 0)} <span className="text-[13px] font-medium text-white/50">{t('common.currency')}</span>
            </div>
            {shift && (
              <div className="mt-1.5 text-[11.5px] text-white/50 nums">
                {t('cash.openingWas', { value: formatSum(shift.openingCash) })}
              </div>
            )}
          </div>
          <KpiCard
            label={t('cash.incomeToday')}
            value={data?.incomeToday ?? 0}
            count={data?.incomeCount ?? 0}
            tone="income"
          />
          <KpiCard
            label={t('cash.expenseToday')}
            value={data?.expenseToday ?? 0}
            count={data?.expenseCount ?? 0}
            tone="expense"
          />
        </div>

        {/* Filter */}
        <div className="inline-flex rounded-input bg-hairline p-1">
          {FILTERS.map((f) => (
            <button
              key={f}
              type="button"
              onClick={() => setFilter(f)}
              className={cn(
                'rounded-md px-4 py-1.5 text-[13px] font-medium transition-colors',
                filter === f ? 'bg-surface text-text shadow-card' : 'text-muted hover:text-text',
              )}
            >
              {t(`cash.filter.${f}`)}
            </button>
          ))}
        </div>

        {/* Table */}
        <Card className="overflow-hidden">
          <div className="grid grid-cols-[0.7fr_0.9fr_2.4fr_1.1fr_1fr] items-center gap-4 border-b border-hairline bg-bg/40 px-6 py-3 text-[11.5px] font-semibold tracking-[0.4px] text-muted-2">
            <span>{t('cash.cols.time')}</span>
            <span>{t('cash.cols.type')}</span>
            <span>{t('cash.cols.description')}</span>
            <span>{t('cash.cols.who')}</span>
            <span className="text-right">{t('cash.cols.amount')}</span>
          </div>

          {ledgerQuery.isLoading ? (
            <div className="flex items-center justify-center py-20 text-primary">
              <Spinner size={24} />
            </div>
          ) : rows.length > 0 ? (
            rows.map((m) => <CashRow key={m.id} m={m} />)
          ) : (
            <div className="py-20 text-center text-[14px] text-muted-2">{t('cash.empty')}</div>
          )}
        </Card>

        <p className="text-[12px] text-muted-2">{t('cash.hint')}</p>
      </div>

      <NewCashOperationModal open={opOpen} onClose={() => setOpOpen(false)} balance={data?.balance ?? 0} />
    </>
  );
}

function KpiCard({
  label,
  value,
  count,
  tone,
}: {
  label: string;
  value: number;
  count: number;
  tone: 'income' | 'expense';
}) {
  const { t } = useTranslation();
  const positive = tone === 'income';
  return (
    <Card className="p-4 sm:p-5">
      <div className="text-[12.5px] text-muted-2">{label}</div>
      <div className={cn('mt-1.5 text-[24px] font-bold tracking-[-0.3px] nums', positive ? 'text-success' : 'text-danger')}>
        {positive ? '+ ' : '− '}
        {formatSum(Math.abs(value))} <span className="text-[13px] font-medium text-muted-2">{t('common.currency')}</span>
      </div>
      <div className="mt-0.5 text-[11.5px] text-muted-2">{t('cash.opsCount', { count })}</div>
    </Card>
  );
}

function CashRow({ m }: { m: CashMovement }) {
  const { t } = useTranslation();
  const meta = TYPE_META[m.type] ?? { key: 'expense', tone: 'neutral' as const };
  const positive = m.amount > 0;

  // Описание: reference for sales/debt, category/comment for expenses.
  const description = (() => {
    if (m.type === 'Sale') return `${t('cash.desc.receipt', { n: m.refNumber })} · ${t('pos.payment.cash')}`;
    if (m.type === 'DebtPayment') return `${t('cash.desc.receipt', { n: m.refNumber })}${m.comment ? ` · ${m.comment}` : ''}`;
    return m.category ?? m.comment ?? '—';
  })();

  return (
    <div className="grid grid-cols-[0.7fr_0.9fr_2.4fr_1.1fr_1fr] items-center gap-4 border-b border-hairline px-6 py-3 text-[13px] last:border-0 hover:bg-bg/40">
      <span className="text-muted-2 nums">{formatTime(m.createdAt)}</span>
      <span>
        <Badge tone={meta.tone}>{t(`cash.type.${meta.key}` as never)}</Badge>
      </span>
      <span className="truncate text-muted">{description}</span>
      <span className="truncate text-muted-2">{m.userName ?? '—'}</span>
      <span className={cn('text-right font-semibold nums', positive ? 'text-success' : 'text-danger')}>
        {positive ? '+ ' : '− '}
        {formatSum(Math.abs(m.amount))}
      </span>
    </div>
  );
}
