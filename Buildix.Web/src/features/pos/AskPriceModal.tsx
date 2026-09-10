import { useEffect, useState } from 'react';
import { useTranslation } from 'react-i18next';
import { Plus } from 'lucide-react';
import { Modal, Button } from '@/shared/ui';
import { unitLabel } from '@/shared/lib/units';

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
 * Narxi sotuvchidan yashirilgan tovarni chekka qo'shish — narxni kassir
 * SHU YERDA kiritadi.
 *
 * <p><b>Nega tovar bosilishi bilan darhol savatga tushmaydi.</b> Ilgari
 * yashirish faqat katalog kartochkasida edi: tovar savatga tushishi bilan
 * uning haqiqiy narxi qatorda, qator summasida va umumiy summada ochiq
 * ko'rinardi. Miqdor bitta bo'lganda qator summasining o'zi narx bo'ladi,
 * ya'ni qatorni to'sish ham yordam bermasdi — narx savatga UMUMAN
 * tushmasligi kerak.</p>
 *
 * <p>Shuning uchun oqim teskari qilindi: avval narx so'raladi, keyin tovar
 * qo'shiladi. Savatga kassir kiritgan raqam tushadi va do'kon narxi
 * ekranga hech qachon chiqmaydi. Bu tovar izohidagi niyatning o'zi —
 * «kassir narxni qo'lda kiritadi».</p>
 *
 * <p>Maydon ataylab KATTA: bu ekrandagi asosiy amal va kassir uni navbat
 * oldida, tez topishi kerak. Oldingi kichkina qalamcha (12×12 nuqta)
 * shunchaki ko'rinmasdi.</p>
 */
export function AskPriceModal({
  open,
  product,
  onClose,
  onSubmit,
}: {
  open: boolean;
  /** Qo'shilayotgan tovar; oyna yopiq bo'lsa <c>null</c>. */
  product: { name: string; unit: number; unitName: string } | null;
  onClose: () => void;
  onSubmit: (price: number, quantity: number) => void;
}) {
  const { t } = useTranslation();
  const [price, setPrice] = useState('');
  const [qty, setQty] = useState('1');

  useEffect(() => {
    if (open) {
      setPrice('');
      setQty('1');
    }
  }, [open]);

  const priceNum = Number(price.replace(',', '.')) || 0;
  const qtyNum = parseQty(qty) ?? 0;
  const valid = priceNum > 0 && qtyNum > 0;

  const inputCls =
    'w-full rounded-input border border-input-border bg-surface px-4 outline-none focus:border-primary focus:shadow-focus-ring';

  const unit = product ? unitLabel(t, product.unit, product.unitName) : '';

  return (
    <Modal
      open={open}
      onClose={onClose}
      title={t('seller.pos.askPrice.title')}
      subtitle={product?.name}
      footer={
        <>
          <Button variant="secondary" onClick={onClose}>
            {t('common.cancel')}
          </Button>
          <Button disabled={!valid} onClick={() => onSubmit(priceNum, qtyNum)}>
            <Plus size={15} />
            {t('seller.pos.askPrice.add')}
          </Button>
        </>
      }
    >
      <div className="flex flex-col gap-4">
        <div className="flex flex-col gap-1.5">
          <label className="text-[13px] font-medium text-label">
            {t('seller.pos.askPrice.price')}
            {unit && <span className="ml-1 font-normal text-muted-2">({t('common.currency')}/{unit})</span>}
          </label>
          {/* Chekdagi eng muhim raqam — shuning uchun eng katta maydon. */}
          <input
            autoFocus
            inputMode="decimal"
            placeholder="0"
            value={price}
            onChange={(e) => setPrice(e.target.value)}
            onKeyDown={(e) => {
              if (e.key === 'Enter' && valid) onSubmit(priceNum, qtyNum);
            }}
            className={`${inputCls} h-14 text-right text-[22px] font-semibold nums`}
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
            onKeyDown={(e) => {
              if (e.key === 'Enter' && valid) onSubmit(priceNum, qtyNum);
            }}
            className={`${inputCls} h-11 text-[15px] nums`}
          />
        </div>

        <p className="text-[11.5px] text-muted-2">{t('seller.pos.askPrice.hint')}</p>
      </div>
    </Modal>
  );
}
