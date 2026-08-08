import { useEffect, useState } from 'react';
import { useTranslation } from 'react-i18next';
import { PackagePlus } from 'lucide-react';
import { Modal, Button } from '@/shared/ui';

/**
 * Miqdor kasr bo'lishi mumkin (2.5 m, 0.5 t) — server ustuni decimal(18,3),
 * shuning uchun uch xonagacha yaxlitlanadi.
 */
function parseQty(raw: string): number | null {
  const text = raw.replace(',', '.').trim();
  // Bo'sh maydon — «javob yo'q», nol emas.
  if (text === '') return null;
  const n = Number(text);
  if (!Number.isFinite(n) || n < 0) return null;
  return Math.round(n * 1000) / 1000;
}

/**
 * Katalogda yo'q tovarni chekka qo'shish — mijoz so'ragan narsa bizda
 * bo'lmasa, uni qo'shni do'kondan olib berish qurilish bozorida odatiy hol.
 *
 * <p>Kassir qobig'ida ham, admin kassasida ham AYNAN shu oyna ishlatiladi.
 * Ilgari u faqat kassirda bor edi va egasi o'zi sotayotganda bunday tovarni
 * chekka qo'sha olmasdi.</p>
 */
export function ExternalItemModal({
  open,
  onClose,
  pending,
  error,
  onSubmit,
}: {
  open: boolean;
  onClose: () => void;
  pending: boolean;
  error: string | null;
  onSubmit: (p: { name: string; salePrice: number; costPrice: number; quantity: number }) => void;
}) {
  const { t } = useTranslation();
  const [name, setName] = useState('');
  const [price, setPrice] = useState('');
  const [cost, setCost] = useState('');
  const [qty, setQty] = useState('1');

  useEffect(() => {
    if (open) {
      setName('');
      setPrice('');
      setCost('');
      setQty('1');
    }
  }, [open]);

  const priceNum = Number(price.replace(',', '.')) || 0;
  const costNum = Number(cost.replace(',', '.')) || 0;
  const qtyNum = parseQty(qty) ?? 0;
  // The server refuses cost >= price on an external line (it would book a loss
  // or a zero-margin sale as if it were normal), so say so before the round-trip.
  const costTooHigh = costNum > 0 && costNum >= priceNum;
  const valid = name.trim().length > 0 && priceNum > 0 && qtyNum > 0 && !costTooHigh;

  const inputCls =
    'h-11 w-full rounded-input border border-input-border bg-surface px-4 text-[15px] outline-none focus:border-primary focus:shadow-focus-ring';

  return (
    <Modal
      open={open}
      onClose={onClose}
      title={t('seller.pos.external.title')}
      subtitle={t('seller.pos.external.subtitle')}
      footer={
        <>
          <Button variant="secondary" onClick={onClose}>
            {t('common.cancel')}
          </Button>
          <Button
            disabled={!valid}
            loading={pending}
            onClick={() => onSubmit({ name: name.trim(), salePrice: priceNum, costPrice: costNum, quantity: qtyNum })}
          >
            <PackagePlus size={15} />
            {t('seller.pos.external.add')}
          </Button>
        </>
      }
    >
      <div className="flex flex-col gap-4">
        <div className="flex flex-col gap-1.5">
          <label className="text-[13px] font-medium text-label">{t('seller.pos.external.name')}</label>
          <input autoFocus value={name} onChange={(e) => setName(e.target.value)} className={inputCls} />
        </div>
        <div className="grid grid-cols-2 gap-3">
          <div className="flex flex-col gap-1.5">
            <label className="text-[13px] font-medium text-label">{t('seller.pos.external.price')}</label>
            <input
              inputMode="decimal"
              placeholder="0"
              value={price}
              onChange={(e) => setPrice(e.target.value)}
              className={`${inputCls} nums`}
            />
          </div>
          <div className="flex flex-col gap-1.5">
            <label className="text-[13px] font-medium text-label">{t('seller.pos.qty')}</label>
            {/* Fokusda «1» belgilanadi — yozish uni almashtiradi. */}
            <input
              inputMode="decimal"
              value={qty}
              onFocus={(e) => e.currentTarget.select()}
              onChange={(e) => setQty(e.target.value)}
              className={`${inputCls} nums`}
            />
          </div>
        </div>
        <div className="flex flex-col gap-1.5">
          <label className="text-[13px] font-medium text-label">{t('seller.pos.external.cost')}</label>
          <input
            inputMode="decimal"
            placeholder="0"
            value={cost}
            onChange={(e) => setCost(e.target.value)}
            className={`${inputCls} nums`}
          />
          <p className="text-[11.5px] text-muted-2">{t('seller.pos.external.costHint')}</p>
        </div>
        {costTooHigh && <p className="text-[12.5px] text-danger">{t('seller.pos.external.costTooHigh')}</p>}
        {error && <p className="text-[12.5px] text-danger">{error}</p>}
      </div>
    </Modal>
  );
}
