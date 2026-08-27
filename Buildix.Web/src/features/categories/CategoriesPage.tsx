import { useMemo, useState } from 'react';
import { useMutation, useQuery, useQueryClient, keepPreviousData } from '@tanstack/react-query';
import { useTranslation } from 'react-i18next';
import { startOfWeek, startOfMonth, startOfQuarter } from 'date-fns';
import { Plus, Pencil, Trash2, Check, X } from 'lucide-react';
import { PageHeader, Button, Card, StatCard, Spinner, useConfirm } from '@/shared/ui';
import { cn } from '@/shared/lib/cn';
import { formatSum } from '@/shared/lib/format';
import { useAuth } from '@/shared/auth/useAuth';
import { PERMISSIONS } from '@/shared/config/permissions';
import type { ApiError } from '@/shared/api/types';
import { categoriesApi, type ProductCategory } from '@/features/warehouse/api';
import { categorySalesApi } from './api';

type Period = 'week' | 'month' | 'quarter';
const PERIODS: Period[] = ['week', 'month', 'quarter'];

function periodRange(period: Period): { start: string; end: string } {
  const now = new Date();
  const start =
    period === 'week'
      ? startOfWeek(now, { weekStartsOn: 1 })
      : period === 'month'
        ? startOfMonth(now)
        : startOfQuarter(now);
  return { start: start.toISOString(), end: now.toISOString() };
}

/**
 * Kategoriyalar bo'limi.
 *
 * <p><b>Nega alohida bo'lim.</b> Kategoriya qo'shish/tahrirlash kodi
 * allaqachon yozilgan edi, lekin u hech qayerdan ochilmasdi — ya'ni
 * amalda kategoriyani boshqarib bo'lmasdi. Endi u o'z sahifasida.</p>
 *
 * <p><b>Nega bu yerda sotuv raqamlari ham bor.</b> Kategoriyaning butun
 * ma'nosi «qaysi yo'nalish qancha pul keltiryapti» degan savolda. Ro'yxatni
 * raqamsiz ko'rsatish uni oddiy lug'atga aylantirardi va egasi javobni
 * baribir Hisobotlardan qidirishga majbur bo'lardi.</p>
 */
export default function CategoriesPage() {
  const { t } = useTranslation();
  const qc = useQueryClient();
  const confirm = useConfirm();
  const { hasPermission } = useAuth();
  const canManage = hasPermission(PERMISSIONS.categories.manage);
  const canSeeSales = hasPermission(PERMISSIONS.sales.access);
  const canProfit = hasPermission(PERMISSIONS.data.profit);

  const [period, setPeriod] = useState<Period>('month');
  const [newName, setNewName] = useState('');
  const [editId, setEditId] = useState<number | null>(null);
  const [editName, setEditName] = useState('');
  const [error, setError] = useState<string | null>(null);

  const range = useMemo(() => periodRange(period), [period]);

  const listQuery = useQuery({ queryKey: ['categories'], queryFn: categoriesApi.list });
  const salesQuery = useQuery({
    queryKey: ['category-sales', range.start, range.end],
    queryFn: () => categorySalesApi.forPeriod(range.start, range.end),
    enabled: canSeeSales,
    placeholderData: keepPreviousData,
  });

  const cats = listQuery.data ?? [];

  /**
   * Kategoriyasiz sotuvlar — server ularni «Boshqa» nomi va manfiy id bilan
   * qaytaradi.
   *
   * <p>U kategoriyalar ro'yxatida YO'Q, chunki bu haqiqiy kategoriya emas.
   * Lekin uni ko'rsatmasak ham ulush hisobiga kiraverardi va ekrandagi
   * foizlar 100 ga yetmasdi — egasi «qolgan pul qayerda?» degan savolga
   * javob topa olmasdi. Shuning uchun u ro'yxat oxirida, tahrirlab
   * bo'lmaydigan qator bo'lib turadi.</p>
   */
  const uncategorized = useMemo(
    () => (salesQuery.data?.categories ?? []).find((c) => c.categoryId < 0) ?? null,
    [salesQuery.data],
  );

  /** Kategoriya id → davr ichidagi sotuv. */
  const salesById = useMemo(() => {
    const map = new Map<number, { sales: number; qty: number; profit: number | null }>();
    for (const row of salesQuery.data?.categories ?? [])
      map.set(row.categoryId, {
        sales: row.totalSales,
        qty: row.totalQuantity,
        profit: row.totalProfit,
      });
    return map;
  }, [salesQuery.data]);

  /**
   * Ulush bo'luvchisi — qatorlarning O'Z yig'indisi.
   *
   * <p>Serverning `totalSales` i chegirma ayirilgan (net), qatorlar esa
   * chegirmasiz (gross). Netga bo'lish foizni 100 dan oshirib yuborardi —
   * Hisobotlar ekranida aynan shu «111%» bo'lib ko'ringan edi.</p>
   */
  const grossTotal = useMemo(
    () => (salesQuery.data?.categories ?? []).reduce((sum, c) => sum + c.totalSales, 0),
    [salesQuery.data],
  );

  const invalidate = () => {
    void qc.invalidateQueries({ queryKey: ['categories'] });
    void qc.invalidateQueries({ queryKey: ['products'] });
  };
  const onErr = (e: unknown) => setError((e as ApiError).message ?? t('common.somethingWrong'));

  const create = useMutation({
    mutationFn: (name: string) => categoriesApi.create({ name }),
    onSuccess: () => {
      setNewName('');
      setError(null);
      invalidate();
    },
    onError: onErr,
  });

  const rename = useMutation({
    mutationFn: (c: ProductCategory) =>
      categoriesApi.update(c.id, {
        name: editName.trim(),
        description: c.description ?? null,
        icon: c.icon ?? null,
        isActive: c.isActive ?? true,
      }),
    onSuccess: () => {
      setEditId(null);
      setError(null);
      invalidate();
    },
    onError: onErr,
  });

  const remove = useMutation({
    mutationFn: (id: number) => categoriesApi.remove(id),
    onSuccess: () => {
      setError(null);
      invalidate();
    },
    onError: onErr,
  });

  async function askRemove(c: ProductCategory) {
    const ok = await confirm({
      title: t('categories.deleteTitle'),
      // Tovarlar YO'QOLMAYDI — ular kategoriyasiz qoladi. Buni aytmasak,
      // egasi tovarlarni ham o'chirib yuborishdan qo'rqib, kategoriyani
      // hech qachon tozalamasdi.
      message: t('categories.deleteHint', { name: c.name, count: c.productCount ?? 0 }),
      confirmLabel: t('common.delete'),
      tone: 'danger',
    });
    if (ok) remove.mutate(c.id);
  }

  const totalProducts = cats.reduce((sum, c) => sum + (c.productCount ?? 0), 0);

  return (
    <>
      <PageHeader
        title={t('categories.title')}
        subtitle={t('categories.subtitle')}
        actions={
          canSeeSales ? (
            <div className="inline-flex rounded-input bg-hairline p-1">
              {PERIODS.map((p) => (
                <button
                  key={p}
                  type="button"
                  onClick={() => setPeriod(p)}
                  className={cn(
                    'rounded-md px-4 py-1.5 text-[13px] font-medium transition-colors',
                    period === p ? 'bg-surface text-text shadow-card' : 'text-muted hover:text-text',
                  )}
                >
                  {t(`reports.period.${p}`)}
                </button>
              ))}
            </div>
          ) : undefined
        }
      />

      <div className="flex min-h-0 flex-1 flex-col gap-[18px] p-4 sm:p-6 lg:p-8">
        <div className="grid grid-cols-1 gap-4 sm:grid-cols-3">
          <StatCard label={t('categories.stats.count')} value={cats.length} />
          <StatCard label={t('categories.stats.products')} value={totalProducts} />
          {canSeeSales && (
            <StatCard
              label={t('categories.stats.sales')}
              value={formatSum(grossTotal)}
              suffix={t('common.currency')}
              hint={t(`reports.period.${period}`)}
            />
          )}
        </div>

        {canManage && (
          <div className="flex flex-wrap items-center gap-2">
            <input
              value={newName}
              onChange={(e) => setNewName(e.target.value)}
              onKeyDown={(e) => {
                if (e.key === 'Enter' && newName.trim()) create.mutate(newName.trim());
              }}
              placeholder={t('categories.newPlaceholder')}
              className="h-10 min-w-[240px] flex-1 rounded-input border border-input-border bg-surface px-3.5 text-[14px] outline-none focus:border-primary"
            />
            <Button
              loading={create.isPending}
              disabled={!newName.trim()}
              onClick={() => create.mutate(newName.trim())}
            >
              <Plus size={15} strokeWidth={2.4} /> {t('categories.add')}
            </Button>
          </div>
        )}

        {error && <p className="text-[13px] text-danger">{error}</p>}

        <Card className="flex min-h-0 flex-1 flex-col overflow-hidden">
          <div className="grid flex-none grid-cols-[2fr_0.8fr_1fr_1.4fr_0.6fr] items-center gap-4 border-b border-hairline bg-bg/40 px-6 py-3 text-[11.5px] font-semibold tracking-[0.4px] text-muted-2">
            <span>{t('categories.cols.name')}</span>
            <span className="text-right">{t('categories.cols.products')}</span>
            <span className="text-right">{t('categories.cols.sales')}</span>
            <span>{t('categories.cols.share')}</span>
            <span />
          </div>

          <div className="min-h-0 flex-1 overflow-auto">
            {listQuery.isLoading ? (
              <div className="flex items-center justify-center py-20 text-primary">
                <Spinner size={24} />
              </div>
            ) : cats.length === 0 ? (
              <div className="py-20 text-center text-[14px] text-muted-2">{t('categories.empty')}</div>
            ) : (
              cats.map((c) => {
                const row = salesById.get(c.id);
                const sales = row?.sales ?? 0;
                const share = grossTotal > 0 ? sales / grossTotal : 0;
                const editing = editId === c.id;

                return (
                  <div
                    key={c.id}
                    className="grid grid-cols-[2fr_0.8fr_1fr_1.4fr_0.6fr] items-center gap-4 border-b border-hairline px-6 py-3 text-[13px] last:border-0 hover:bg-bg/40"
                  >
                    {editing ? (
                      <input
                        autoFocus
                        value={editName}
                        onChange={(e) => setEditName(e.target.value)}
                        onKeyDown={(e) => {
                          if (e.key === 'Enter' && editName.trim()) rename.mutate(c);
                          if (e.key === 'Escape') setEditId(null);
                        }}
                        className="h-9 rounded-input border border-input-border bg-surface px-3 text-[13px] outline-none focus:border-primary"
                      />
                    ) : (
                      <span className="flex min-w-0 items-center gap-2">
                        {c.icon && <span className="flex-none">{c.icon}</span>}
                        <span className="truncate font-medium">{c.name}</span>
                      </span>
                    )}

                    <span className="text-right text-muted nums">{c.productCount ?? 0}</span>

                    <span className="text-right font-semibold nums">
                      {canSeeSales ? formatSum(sales) : '—'}
                    </span>

                    <span className="flex items-center gap-2">
                      {canSeeSales ? (
                        <>
                          <span className="h-1.5 flex-1 rounded-pill bg-hairline">
                            <span
                              className="block h-1.5 rounded-pill bg-primary"
                              // 0–100 ga qisiladi: qaytarish yoki manfiy
                              // summa chiziqni kartadan chiqarib yuborardi.
                              style={{ width: `${Math.min(100, Math.max(0, share * 100))}%` }}
                            />
                          </span>
                          <span className="w-11 flex-none text-right text-[12px] text-muted nums">
                            {(share * 100).toFixed(0)}%
                          </span>
                        </>
                      ) : (
                        <span className="text-muted-2">—</span>
                      )}
                    </span>

                    <span className="flex items-center justify-end gap-1">
                      {canManage &&
                        (editing ? (
                          <>
                            <IconBtn
                              title={t('common.save')}
                              onClick={() => editName.trim() && rename.mutate(c)}
                            >
                              <Check size={15} className="text-success" />
                            </IconBtn>
                            <IconBtn title={t('common.cancel')} onClick={() => setEditId(null)}>
                              <X size={15} />
                            </IconBtn>
                          </>
                        ) : (
                          <>
                            <IconBtn
                              title={t('categories.edit')}
                              onClick={() => {
                                setEditId(c.id);
                                setEditName(c.name);
                              }}
                            >
                              <Pencil size={14} />
                            </IconBtn>
                            <IconBtn title={t('common.delete')} onClick={() => void askRemove(c)}>
                              <Trash2 size={14} className="text-danger" />
                            </IconBtn>
                          </>
                        ))}
                    </span>
                  </div>
                );
              })
            )}

          </div>

          {uncategorized && (
            <div className="grid grid-cols-[2fr_0.8fr_1fr_1.4fr_0.6fr] items-center gap-4 flex-none border-t border-hairline bg-bg/30 px-6 py-3 text-[13px]">
              <span className="truncate font-medium text-muted">
                {t('categories.uncategorized')}
              </span>
              <span className="text-right text-muted-2">—</span>
              <span className="text-right font-semibold nums">
                {formatSum(uncategorized.totalSales)}
              </span>
              <span className="flex items-center gap-2">
                <span className="h-1.5 flex-1 rounded-pill bg-hairline">
                  <span
                    className="block h-1.5 rounded-pill bg-muted-2/50"
                    style={{
                      width: `${Math.min(100, Math.max(0, grossTotal > 0 ? (uncategorized.totalSales / grossTotal) * 100 : 0))}%`,
                    }}
                  />
                </span>
                <span className="w-11 flex-none text-right text-[12px] text-muted nums">
                  {(grossTotal > 0 ? (uncategorized.totalSales / grossTotal) * 100 : 0).toFixed(0)}%
                </span>
              </span>
              <span />
            </div>
            )}
        </Card>

        {canSeeSales && canProfit && salesQuery.data?.totalProfit != null && (
          <p className="text-[12px] text-muted-2">
            {t('categories.profitNote', { value: formatSum(salesQuery.data.totalProfit) })}
          </p>
        )}
      </div>
    </>
  );
}

function IconBtn({
  title,
  onClick,
  children,
}: {
  title: string;
  onClick: () => void;
  children: React.ReactNode;
}) {
  return (
    <button
      type="button"
      title={title}
      onClick={onClick}
      className="flex h-8 w-8 items-center justify-center rounded-md text-muted-2 transition-colors hover:bg-hairline hover:text-text"
    >
      {children}
    </button>
  );
}
