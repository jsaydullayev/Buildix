import { useQuery } from '@tanstack/react-query';
import { useTranslation } from 'react-i18next';
import { Modal, Button, Badge, Spinner } from '@/shared/ui';
import { formatSum, formatShortDate } from '@/shared/lib/format';
import { debtsApi } from '@/features/debts/api';
import { customersApi, type Customer } from './api';

const DEBT_TONE: Record<string, 'warn' | 'success' | 'neutral'> = {
  Open: 'warn',
  Closed: 'success',
};

/** Payment type → { label key, badge tone } for the «Последние покупки» list. */
function payMeta(paymentType: string): { key: string; tone: 'success' | 'info' | 'warn' } {
  switch (paymentType) {
    case 'Cash':
      return { key: 'cash', tone: 'success' };
    case 'Card':
    case 'Terminal':
      return { key: 'card', tone: 'info' };
    case 'Click':
      return { key: 'click', tone: 'info' };
    case 'Transfer':
      return { key: 'transfer', tone: 'info' };
    case 'Debt':
    case 'Credit':
      return { key: 'debt', tone: 'warn' };
    default:
      return { key: 'mixed', tone: 'info' };
  }
}

/**
 * Read-only customer card: at-a-glance stats (покупок / купил всего / текущий
 * долг), the «Последние покупки» history (GET /Customers/{id}/purchases) and the
 * list of open/closed debts.
 */
export function CustomerDetailModal({ customer, onClose }: { customer: Customer | null; onClose: () => void }) {
  const { t, i18n } = useTranslation();

  const debtsQuery = useQuery({
    queryKey: ['customer-debts', customer?.id],
    queryFn: () => debtsApi.customerDebts(customer!.id),
    enabled: !!customer,
  });
  const debts = debtsQuery.data ?? [];

  const purchasesQuery = useQuery({
    queryKey: ['customer-purchases', customer?.id],
    queryFn: () => customersApi.purchases(customer!.id),
    enabled: !!customer,
  });
  const purchases = purchasesQuery.data ?? [];

  return (
    <Modal
      open={!!customer}
      onClose={onClose}
      width="lg"
      title={customer?.fullName ?? customer?.phone ?? t('customers.detail.title')}
      subtitle={customer ? customer.phone : undefined}
      footer={
        <Button variant="secondary" onClick={onClose}>
          {t('common.close')}
        </Button>
      }
    >
      {!customer ? null : (
        <div className="flex flex-col gap-5">
          {/* Summary — покупок / купил всего / текущий долг */}
          <div className="grid grid-cols-3 gap-3">
            <Stat label={t('customers.cols.purchases')} value={String(customer.purchaseCount)} />
            <Stat label={t('customers.cols.totalBought')} value={formatSum(customer.totalPurchased)} />
            <Stat label={t('customers.detail.totalDebt')} value={formatSum(customer.totalDebt)} warn={customer.totalDebt > 0} />
          </div>

          {/* Последние покупки */}
          <div>
            <h3 className="mb-2 text-[13px] font-semibold">{t('customers.detail.recentPurchases')}</h3>
            {purchasesQuery.isLoading ? (
              <div className="flex justify-center py-8 text-primary">
                <Spinner size={20} />
              </div>
            ) : purchases.length === 0 ? (
              <p className="py-6 text-center text-[13px] text-muted-2">{t('customers.detail.noPurchases')}</p>
            ) : (
              <div className="flex flex-col divide-y divide-hairline rounded-input border border-hairline">
                {purchases.map((p) => {
                  const meta = payMeta(p.paymentType);
                  return (
                    <div key={p.saleId} className="flex items-center justify-between gap-3 px-4 py-2.5 text-[13px]">
                      <div className="min-w-0">
                        <div className="font-semibold text-primary nums">
                          {t('cash.desc.receipt', { n: p.number })} · {formatShortDate(p.createdAt, i18n.language)}
                        </div>
                        <div className="truncate text-[11.5px] text-muted-2">
                          {p.itemsSummary || t('purchases.itemsCount', { count: p.itemCount })}
                        </div>
                      </div>
                      <div className="flex flex-none items-center gap-3">
                        <span className="font-semibold nums">{formatSum(p.totalAmount)}</span>
                        <Badge tone={meta.tone}>{t(`sales.payment.${meta.key}` as never)}</Badge>
                      </div>
                    </div>
                  );
                })}
              </div>
            )}
          </div>

          {/* Debts */}
          <div>
            <h3 className="mb-2 text-[13px] font-semibold">{t('customers.detail.debts')}</h3>
            {debtsQuery.isLoading ? (
              <div className="flex justify-center py-8 text-primary">
                <Spinner size={20} />
              </div>
            ) : debts.length === 0 ? (
              <p className="py-6 text-center text-[13px] text-muted-2">{t('customers.detail.noDebts')}</p>
            ) : (
              <div className="flex flex-col divide-y divide-hairline rounded-input border border-hairline">
                {debts.map((d) => (
                  <div key={d.id} className="flex items-center justify-between gap-3 px-4 py-2.5 text-[13px]">
                    <div className="min-w-0">
                      <div className="font-medium nums">{formatShortDate(d.createdAt, i18n.language)}</div>
                      {d.dueDate && (
                        <div className="text-[11.5px] text-muted-2">
                          {t('customers.detail.due', { date: formatShortDate(d.dueDate, i18n.language) })}
                        </div>
                      )}
                    </div>
                    <div className="flex items-center gap-3">
                      <Badge tone={DEBT_TONE[d.status] ?? 'neutral'}>
                        {t(`customers.detail.status.${d.status.toLowerCase()}` as never, { defaultValue: d.status })}
                      </Badge>
                      <span className="w-[110px] text-right font-semibold nums">{formatSum(d.remainingDebt)}</span>
                    </div>
                  </div>
                ))}
              </div>
            )}
          </div>

          {customer.comment && (
            <p className="rounded-input bg-bg px-4 py-2.5 text-[13px] text-muted">{customer.comment}</p>
          )}
        </div>
      )}
    </Modal>
  );
}

function Stat({ label, value, warn }: { label: string; value: string; warn?: boolean }) {
  return (
    <div className="rounded-input bg-bg px-4 py-3">
      <div className="text-[11.5px] text-muted-2">{label}</div>
      <div className={`mt-1 text-[15px] font-bold nums ${warn ? 'text-warn-text' : ''}`}>{value}</div>
    </div>
  );
}
