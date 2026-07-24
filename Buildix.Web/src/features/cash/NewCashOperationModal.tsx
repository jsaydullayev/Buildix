import { useState } from 'react';
import { useMutation, useQueryClient } from '@tanstack/react-query';
import { useTranslation } from 'react-i18next';
import { Modal, Button } from '@/shared/ui';
import { cn } from '@/shared/lib/cn';
import { formatSum } from '@/shared/lib/format';
import { cashLedgerApi } from './api';

type OpType = 'expense' | 'collection' | 'deposit';
const OP_TYPES: OpType[] = ['expense', 'collection', 'deposit'];
const EXPENSE_CATEGORIES = ['household', 'delivery', 'advance', 'other'] as const;
type Category = (typeof EXPENSE_CATEGORIES)[number];

/**
 * «Новая операция» — a manual cash movement from the Касса screen. Sales and
 * debt payments land automatically; here the owner records an expense (with a
 * category), a bank collection (Инкассация), or a change deposit (Внесение).
 * Outflows can't exceed the current balance — the server rejects an overdraft
 * and we also guard the button.
 */
export function NewCashOperationModal({
  open,
  onClose,
  balance,
}: {
  open: boolean;
  onClose: () => void;
  balance: number;
}) {
  const { t } = useTranslation();
  const qc = useQueryClient();
  const [type, setType] = useState<OpType>('expense');
  const [category, setCategory] = useState<Category>('household');
  const [amount, setAmount] = useState('');
  const [comment, setComment] = useState('');
  const [error, setError] = useState<string | null>(null);

  const amountNum = Number(amount) || 0;
  const isOutflow = type !== 'deposit';
  const overdraft = isOutflow && amountNum > balance;

  const reset = () => {
    setType('expense');
    setCategory('household');
    setAmount('');
    setComment('');
    setError(null);
  };
  const close = () => {
    reset();
    onClose();
  };

  const submit = useMutation({
    mutationFn: () => {
      if (type === 'deposit') {
        return cashLedgerApi.deposit(amountNum, comment.trim() || null);
      }
      // expense → category; collection → isCollection
      return cashLedgerApi.withdraw({
        amount: amountNum,
        comment: comment.trim(),
        category: type === 'expense' ? t(`cash.category.${category}`) : undefined,
        isCollection: type === 'collection',
      });
    },
    onSuccess: () => {
      void qc.invalidateQueries({ queryKey: ['cash-ledger'] });
      close();
    },
    onError: (e) => setError((e as { message?: string }).message ?? t('common.somethingWrong')),
  });

  const inputCls =
    'h-11 w-full rounded-input border border-input-border bg-surface px-3.5 text-[14px] outline-none focus:border-primary focus:shadow-focus-ring';

  return (
    <Modal
      open={open}
      onClose={close}
      title={t('cash.newOp.title')}
      footer={
        <>
          <Button variant="secondary" onClick={close}>
            {t('common.cancel')}
          </Button>
          <Button
            loading={submit.isPending}
            disabled={amountNum <= 0 || overdraft}
            onClick={() => submit.mutate()}
          >
            {t('cash.newOp.confirm')}
          </Button>
        </>
      }
    >
      <div className="flex flex-col gap-4">
        {/* Type */}
        <div className="grid grid-cols-3 gap-2">
          {OP_TYPES.map((tp) => (
            <button
              key={tp}
              type="button"
              onClick={() => setType(tp)}
              className={cn(
                'h-10 rounded-input border text-[13px] font-medium transition-colors',
                type === tp
                  ? 'border-primary bg-primary-soft text-primary-hover'
                  : 'border-input-border bg-surface text-muted hover:text-text',
              )}
            >
              {t(`cash.type.${tp === 'expense' ? 'expense' : tp === 'collection' ? 'collection' : 'deposit'}`)}
            </button>
          ))}
        </div>

        {/* Category (expense only) */}
        {type === 'expense' && (
          <div>
            <label className="mb-1.5 block text-[13px] font-medium text-label">{t('cash.newOp.category')}</label>
            <div className="grid grid-cols-2 gap-2">
              {EXPENSE_CATEGORIES.map((c) => (
                <button
                  key={c}
                  type="button"
                  onClick={() => setCategory(c)}
                  className={cn(
                    'h-10 rounded-input border text-[13px] transition-colors',
                    category === c
                      ? 'border-primary bg-primary-soft text-primary-hover'
                      : 'border-input-border bg-surface text-muted hover:text-text',
                  )}
                >
                  {t(`cash.category.${c}`)}
                </button>
              ))}
            </div>
          </div>
        )}

        {/* Amount */}
        <div>
          <label className="mb-1.5 block text-[13px] font-medium text-label">{t('cash.newOp.amount')}</label>
          <input
            type="number"
            step="any"
            min="0"
            placeholder="0"
            value={amount}
            onChange={(e) => setAmount(e.target.value)}
            className={cn(inputCls, overdraft && 'border-danger')}
            autoFocus
          />
          {isOutflow && (
            <p className={cn('mt-1 text-[11.5px]', overdraft ? 'text-danger' : 'text-muted-2')}>
              {overdraft
                ? t('cash.newOp.overdraft')
                : t('cash.newOp.available', { value: formatSum(balance) })}
            </p>
          )}
        </div>

        {/* Comment */}
        <div>
          <label className="mb-1.5 block text-[13px] font-medium text-label">{t('cash.newOp.comment')}</label>
          <input
            value={comment}
            onChange={(e) => setComment(e.target.value)}
            placeholder={t('cash.newOp.commentHint')}
            className={inputCls}
          />
        </div>

        {error && <p className="text-[12.5px] text-danger">{error}</p>}
      </div>
    </Modal>
  );
}
