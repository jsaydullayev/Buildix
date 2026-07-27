import { useTranslation } from 'react-i18next';
import { Check, Printer } from 'lucide-react';
import { Modal, Button, Card } from '@/shared/ui';
import { formatSum, formatQty, formatTime } from '@/shared/lib/format';
import type { PosSale } from './api';

/**
 * Sotuv yakunlangandan keyingi oyna: chekning o'zi (do'kon nomi, qatorlar,
 * jami, to'langan, qarz) va ikki amal — <b>chekni chop etish</b> va yopish.
 *
 * <p>Nega ekranning o'rtasida oyna: «Rasmiylashtirish» bosilgach kassa darhol
 * tozalanadi, ya'ni nima sotilgani ekranda qolmaydi. Chek raqami va summa
 * ko'rinmasa, kassir chop etishni ham, mijozga aytishni ham ilgari
 * «Sotuvlar» sahifasiga borib qidirishiga to'g'ri kelardi.</p>
 *
 * <p>Ikkala kassa (Owner/Admin — <code>features/pos</code>, sotuvchi —
 * <code>features/seller</code>) bir xil oynani ko'rsatadi: chek ko'rinishi
 * qobiqqa qarab farq qilmasligi kerak.</p>
 */
export function ReceiptModal({
  sale,
  shiftNumber = 0,
  storeName = null,
  closeLabel,
  onClose,
  onPrint,
}: {
  sale: PosSale | null;
  shiftNumber?: number;
  storeName?: string | null;
  /** Yopish tugmasining yozuvi — qobiqqa qarab «Tugatish» yoki «Yangi sotuv». */
  closeLabel: string;
  onClose: () => void;
  onPrint: (id: string) => void;
}) {
  const { t } = useTranslation();
  return (
    <Modal
      open={!!sale}
      onClose={onClose}
      title={t('pos.done.title')}
      footer={
        <>
          <Button variant="secondary" onClick={() => sale && onPrint(sale.id)}>
            <Printer size={15} />
            {t('pos.done.print')}
          </Button>
          <Button onClick={onClose}>{closeLabel}</Button>
        </>
      }
    >
      {sale && (
        <div className="flex flex-col gap-3">
          <div className="flex items-center justify-center gap-2 text-success">
            <Check size={20} />
            <span className="text-[15px] font-semibold">
              {t('pos.done.receipt')} №{sale.saleNumber}
            </span>
          </div>
          <Card className="p-4 font-mono text-[12.5px]">
            {storeName && (
              <div className="mb-2 border-b border-hairline pb-2 text-center text-[13px] font-semibold">
                {storeName}
              </div>
            )}
            <div className="mb-2 flex justify-between text-muted-2">
              <span>{formatTime(sale.createdAt)}</span>
              {shiftNumber > 0 && (
                <span className="nums">
                  {t('pos.done.shift')} №{shiftNumber}
                </span>
              )}
              <span>{sale.sellerName}</span>
            </div>
            {sale.items.map((it) => (
              <div key={it.id} className="flex justify-between gap-2 py-0.5">
                <span className="min-w-0 truncate">
                  {it.productName} × {formatQty(it.quantity)}
                </span>
                <span className="flex-none nums">{formatSum(it.totalPrice)}</span>
              </div>
            ))}
            <div className="mt-2 flex justify-between border-t border-hairline pt-2 text-[14px] font-bold">
              <span>{t('pos.total')}</span>
              <span className="nums">{formatSum(sale.totalAmount)}</span>
            </div>
            <div className="flex justify-between pt-1 text-muted">
              <span>{t('pos.amount')}</span>
              <span className="nums">{formatSum(sale.paidAmount)}</span>
            </div>
            {sale.remainingAmount > 0 && (
              <div className="flex justify-between text-warn-text">
                <span>{t('pos.payment.debt')}</span>
                <span className="nums">{formatSum(sale.remainingAmount)}</span>
              </div>
            )}
            {sale.customerName && (
              <div className="mt-1 border-t border-hairline pt-1 text-muted">{sale.customerName}</div>
            )}
            <div className="mt-2 border-t border-hairline pt-2 text-center text-[11.5px] text-muted-2">
              {t('pos.done.thanks')}
            </div>
          </Card>
        </div>
      )}
    </Modal>
  );
}
