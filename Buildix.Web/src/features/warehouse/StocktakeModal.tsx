import { useEffect, useMemo, useState } from 'react';
import { useQuery, useMutation, useQueryClient } from '@tanstack/react-query';
import { useTranslation } from 'react-i18next';
import { Search, Check } from 'lucide-react';
import { Modal, Button, Spinner } from '@/shared/ui';
import { cn } from '@/shared/lib/cn';
import { formatQty } from '@/shared/lib/format';
import { unitLabel } from '@/shared/lib/units';
import { productsApi, type StocktakeResult } from './api';

export function StocktakeModal({ open, onClose }: { open: boolean; onClose: () => void }) {
  const { t } = useTranslation();
  const qc = useQueryClient();
  const [search, setSearch] = useState('');
  const [counts, setCounts] = useState<Record<string, string>>({});
  const [result, setResult] = useState<StocktakeResult | null>(null);

  const productsQuery = useQuery({
    queryKey: ['products-all'],
    queryFn: productsApi.listAll,
    enabled: open,
  });

  useEffect(() => {
    if (open) {
      setSearch('');
      setCounts({});
      setResult(null);
    }
  }, [open]);

  const filtered = useMemo(() => {
    const list = productsQuery.data ?? [];
    const term = search.trim().toLowerCase();
    if (!term) return list;
    return list.filter(
      (p) => p.name.toLowerCase().includes(term) || (p.sku ?? '').toLowerCase().includes(term),
    );
  }, [productsQuery.data, search]);

  const items = useMemo(
    () =>
      Object.entries(counts)
        .filter(([, v]) => v.trim() !== '' && !Number.isNaN(Number(v)))
        .map(([productId, v]) => ({ productId, countedQty: Number(v) })),
    [counts],
  );

  const mutation = useMutation({
    mutationFn: () => productsApi.stocktake(items),
    onSuccess: (res) => {
      setResult(res);
      void qc.invalidateQueries({ queryKey: ['products'] });
      void qc.invalidateQueries({ queryKey: ['products-all'] });
    },
  });

  return (
    <Modal
      open={open}
      onClose={onClose}
      width="xl"
      title={t('warehouse.stocktakeModal.title')}
      subtitle={result ? undefined : t('warehouse.stocktakeModal.subtitle')}
      footer={
        result ? (
          <Button onClick={onClose}>{t('warehouse.stocktakeModal.done')}</Button>
        ) : (
          <>
            <Button variant="secondary" onClick={onClose}>
              {t('common.cancel')}
            </Button>
            <Button disabled={items.length === 0} loading={mutation.isPending} onClick={() => mutation.mutate()}>
              {t('warehouse.stocktakeModal.submit')}
            </Button>
          </>
        )
      }
    >
      {result ? (
        <ResultView result={result} />
      ) : productsQuery.isLoading ? (
        <div className="flex justify-center py-16 text-primary">
          <Spinner size={24} />
        </div>
      ) : (
        <div className="flex flex-col gap-3">
          <div className="relative">
            <Search size={17} className="absolute left-3.5 top-1/2 -translate-y-1/2 text-muted-2" />
            <input
              value={search}
              onChange={(e) => setSearch(e.target.value)}
              placeholder={t('warehouse.stocktakeModal.searchPlaceholder')}
              className="h-11 w-full rounded-input border border-input-border bg-surface pl-11 pr-4 text-[14px] outline-none focus:border-primary focus:shadow-focus-ring"
            />
          </div>

          <div className="grid grid-cols-[1fr_100px_120px_110px] items-center gap-3 border-b border-hairline px-1 pb-2 text-[11.5px] font-semibold tracking-[0.4px] text-muted-2">
            <span>{t('warehouse.cols.product')}</span>
            <span className="text-right">{t('warehouse.stocktakeModal.system')}</span>
            <span className="text-right">{t('warehouse.stocktakeModal.counted')}</span>
            <span className="text-right">{t('warehouse.stocktakeModal.variance')}</span>
          </div>

          <div className="max-h-[46vh] overflow-y-auto">
            {filtered.map((p) => {
              const raw = counts[p.id];
              const hasVal = raw !== undefined && raw.trim() !== '' && !Number.isNaN(Number(raw));
              const variance = hasVal ? Number(raw) - p.quantity : null;
              return (
                <div
                  key={p.id}
                  className="grid grid-cols-[1fr_100px_120px_110px] items-center gap-3 border-b border-hairline px-1 py-2.5 text-[13px] last:border-0"
                >
                  <div className="min-w-0">
                    <div className="truncate font-medium">{p.name}</div>
                    {p.sku && <div className="truncate text-[11.5px] text-muted-2">{p.sku}</div>}
                  </div>
                  <div className="text-right text-muted nums">
                    {formatQty(p.quantity)} {unitLabel(t, p.unit, p.unitName)}
                  </div>
                  <input
                    type="number"
                    step="any"
                    value={raw ?? ''}
                    onChange={(e) => setCounts((c) => ({ ...c, [p.id]: e.target.value }))}
                    placeholder={formatQty(p.quantity)}
                    className="h-9 w-full rounded-input border border-input-border bg-surface px-2.5 text-right text-[13px] outline-none focus:border-primary nums"
                  />
                  <div
                    className={cn(
                      'text-right font-semibold nums',
                      variance === null || variance === 0
                        ? 'text-muted-2'
                        : variance > 0
                          ? 'text-success'
                          : 'text-danger',
                    )}
                  >
                    {variance === null ? '—' : variance === 0 ? '0' : `${variance > 0 ? '+' : ''}${formatQty(variance)}`}
                  </div>
                </div>
              );
            })}
          </div>
        </div>
      )}
    </Modal>
  );
}

function ResultView({ result }: { result: StocktakeResult }) {
  const { t } = useTranslation();
  return (
    <div className="flex flex-col gap-4">
      <div className="flex items-center gap-2.5 rounded-input bg-success-soft px-4 py-3 text-[14px] font-medium text-success-text">
        <Check size={18} />
        {t('warehouse.stocktakeModal.adjusted', { count: result.adjustedCount })}
      </div>
      {result.lines.length === 0 ? (
        <p className="py-4 text-center text-[13.5px] text-muted-2">
          {t('warehouse.stocktakeModal.noChanges')}
        </p>
      ) : (
        <div className="max-h-[46vh] overflow-y-auto">
          {result.lines.map((l) => (
            <div
              key={l.productId}
              className="flex items-center justify-between gap-3 border-b border-hairline py-2.5 text-[13px] last:border-0"
            >
              <span className="truncate font-medium">{l.name}</span>
              <div className="flex items-center gap-3 nums">
                <span className="text-muted-2">
                  {formatQty(l.before)} → {formatQty(l.counted)}
                </span>
                <span className={cn('w-16 text-right font-semibold', l.variance > 0 ? 'text-success' : 'text-danger')}>
                  {l.variance > 0 ? '+' : ''}
                  {formatQty(l.variance)}
                </span>
              </div>
            </div>
          ))}
        </div>
      )}
    </div>
  );
}
