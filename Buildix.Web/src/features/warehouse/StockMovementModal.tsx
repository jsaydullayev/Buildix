import { useState } from 'react';
import { useQuery, useMutation, useQueryClient } from '@tanstack/react-query';
import { useTranslation } from 'react-i18next';
import { ArrowDownLeft, ArrowUpRight, SlidersHorizontal } from 'lucide-react';
import type { TFunction } from 'i18next';
import { Modal, Button, Spinner } from '@/shared/ui';
import { cn } from '@/shared/lib/cn';
import { formatQty, formatShortDate, formatTime } from '@/shared/lib/format';
import { unitLabel } from '@/shared/lib/units';
import { useAuth } from '@/shared/auth/useAuth';
import { ROLES, PERMISSIONS } from '@/shared/config/permissions';
import { productsApi, type Product, type StockMovement } from './api';

/** Localised label + reference prefix per movement type. */
function movementLabel(t: TFunction, m: StockMovement): string {
  const key = `warehouse.movement.type.${m.type.charAt(0).toLowerCase()}${m.type.slice(1)}`;
  const base = String(t(key, { defaultValue: m.type }));
  if (m.refNumber == null) return base;
  const prefix = m.type === 'Purchase' ? 'З' : m.type === 'Sale' || m.type === 'SaleReversal' ? 'Ч' : '';
  return prefix ? `${base} · ${prefix}-${m.refNumber}` : base;
}

/**
 * Склад "Движение товара" — the append-only stock ledger for one product
 * (Приход / Продажа / Корректировка with the resulting quantity at each step),
 * plus an inline Корректировка (inventory) that sets the physical count. The
 * variance is written back into the ledger as a Correction movement.
 */
export function StockMovementModal({
  product,
  open,
  onClose,
}: {
  product: Product | null;
  open: boolean;
  onClose: () => void;
}) {
  const { t, i18n } = useTranslation();
  const qc = useQueryClient();
  const { hasRole, hasPermission } = useAuth();
  // Инвентаризация writes stock — Owner/SuperAdmin only, same gate as the bulk stocktake.
  const canCorrect = hasRole(ROLES.Owner, ROLES.SuperAdmin) && hasPermission(PERMISSIONS.products.edit);

  const [counted, setCounted] = useState('');
  const [error, setError] = useState<string | null>(null);

  const movementsQuery = useQuery({
    queryKey: ['product-movements', product?.id],
    queryFn: () => productsApi.movements(product!.id),
    enabled: open && !!product,
  });

  const correct = useMutation({
    mutationFn: (qty: number) => productsApi.stocktake([{ productId: product!.id, countedQty: qty }]),
    onSuccess: () => {
      setCounted('');
      setError(null);
      void qc.invalidateQueries({ queryKey: ['product-movements', product?.id] });
      void qc.invalidateQueries({ queryKey: ['products'] });
    },
    onError: () => setError(t('common.somethingWrong')),
  });

  if (!product) return null;
  const unit = unitLabel(t, product.unit, product.unitName);

  return (
    <Modal
      open={open}
      onClose={onClose}
      width="lg"
      title={product.name}
      subtitle={`${t('warehouse.cols.stock')}: ${formatQty(product.quantity)} ${unit}  ·  ${t('warehouse.form.minThreshold')}: ${formatQty(product.minThreshold)} ${unit}`}
    >
      <div className="flex flex-col gap-5">
        {/* Ledger */}
        <div>
          <h3 className="mb-2 text-[13px] font-semibold">{t('warehouse.movement.title')}</h3>
          {movementsQuery.isLoading ? (
            <div className="flex items-center justify-center py-10 text-primary">
              <Spinner size={22} />
            </div>
          ) : movementsQuery.data && movementsQuery.data.length > 0 ? (
            /*
              Ro'yxat O'Z ichida aylanadi.

              Tovar harakati vaqt o'tgan sari o'sib boradi va bir necha
              o'nlab qatorga yetadi. Ilgari ular oynani cho'zib yuborardi va
              pastdagi «Tuzatish» ekrandan chiqib ketardi — omborchi uni
              topish uchun butun tarixni aylantirib o'tishga majbur edi. Endi
              tarix shu quti ichida qoladi, tuzatish esa doim ko'rinib
              turadi.

              Balandlik ekranga nisbatan: kichik monitorda ham, kattasida
              ham ro'yxat oynaning yarmidan oshmaydi. Qatorlar oz bo'lsa
              quti o'zi kichrayadi — bo'sh joy qolmaydi.
            */
            <div className="max-h-[45vh] overflow-y-auto overscroll-contain pr-1">
              {movementsQuery.data.map((m) => (
                <MovementRow key={m.id} m={m} unit={unit} label={movementLabel(t, m)} lang={i18n.language} />
              ))}
            </div>
          ) : (
            <p className="py-8 text-center text-[13px] text-muted-2">{t('warehouse.movement.empty')}</p>
          )}
        </div>

        {/* Correction */}
        {canCorrect && (
          <div className="rounded-input border border-hairline bg-bg/50 p-4">
            <div className="mb-2 flex items-center gap-2 text-[13px] font-semibold">
              <SlidersHorizontal size={15} className="text-muted" />
              {t('warehouse.movement.correctionTitle')}
            </div>
            <div className="flex items-center gap-2">
              <input
                type="number"
                step="any"
                min="0"
                placeholder={t('warehouse.movement.actualQty')}
                value={counted}
                onChange={(e) => setCounted(e.target.value)}
                className="h-10 flex-1 rounded-input border border-input-border bg-surface px-3.5 text-[14px] outline-none focus:border-primary nums"
              />
              <Button
                loading={correct.isPending}
                disabled={counted === '' || Number(counted) === product.quantity}
                onClick={() => correct.mutate(Math.max(0, Number(counted) || 0))}
              >
                {t('warehouse.movement.apply')}
              </Button>
            </div>
            {error ? (
              <p className="mt-2 text-[12px] text-danger">{error}</p>
            ) : (
              <p className="mt-2 text-[11.5px] text-muted-2">{t('warehouse.movement.correctionHint')}</p>
            )}
          </div>
        )}
      </div>
    </Modal>
  );
}

function MovementRow({
  m,
  unit,
  label,
  lang,
}: {
  m: StockMovement;
  unit: string;
  label: string;
  lang: string;
}) {
  const positive = m.delta > 0;
  return (
    <div className="flex items-center gap-3 border-b border-hairline py-2.5 last:border-0">
      <div
        className={cn(
          'flex h-7 w-7 flex-none items-center justify-center rounded-lg',
          positive ? 'bg-success-soft text-success' : 'bg-danger-soft text-danger',
        )}
      >
        {positive ? <ArrowDownLeft size={15} /> : <ArrowUpRight size={15} />}
      </div>
      <div className="min-w-0 flex-1">
        <div className="truncate text-[13px] font-medium">{label}</div>
        <div className="text-[11.5px] text-muted-2 nums">
          {formatShortDate(m.createdAt, lang)}, {formatTime(m.createdAt)}
          {m.comment ? ` · ${m.comment}` : ''}
        </div>
      </div>
      <div className="flex-none text-right">
        <div className={cn('text-[13px] font-semibold nums', positive ? 'text-success' : 'text-danger')}>
          {positive ? '+' : ''}
          {formatQty(m.delta)} {unit}
        </div>
        <div className="text-[11px] text-muted-2 nums">
          → {formatQty(m.resultingQty)} {unit}
        </div>
      </div>
    </div>
  );
}
