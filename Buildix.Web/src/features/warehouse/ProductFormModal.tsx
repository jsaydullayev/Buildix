import { useEffect, useState } from 'react';
import { useForm } from 'react-hook-form';
import { zodResolver } from '@hookform/resolvers/zod';
import { z } from 'zod';
import { useTranslation } from 'react-i18next';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { Sparkles, Printer } from 'lucide-react';
import { Modal, Button, Input, useConfirm } from '@/shared/ui';
import { PrintLabelsModal } from './PrintLabelsModal';
import { cn } from '@/shared/lib/cn';
import { unitLabel } from '@/shared/lib/units';
import { useAuth } from '@/shared/auth/useAuth';
import { ROLES, PERMISSIONS } from '@/shared/config/permissions';
import {
  productsApi,
  type Product,
  type ProductCategory,
  type CreateProductBody,
} from './api';

/**
 * A money/quantity field. The input is left BLANK when the value is 0 so the
 * user sees a faint "0" placeholder instead of a literal 0 they must delete
 * before typing. An empty field means 0, so '' is coerced back to 0 here.
 */
const numField = () =>
  z.preprocess((v) => (v === '' || v === null || v === undefined ? 0 : v), z.coerce.number().min(0));

const schema = z.object({
  name: z.string().min(1),
  sku: z.string().max(50).optional(),
  barcode: z.string().max(64).optional(),
  description: z.string().max(1000).optional(),
  categoryId: z.preprocess(
    (v) => (v === '' || v === undefined || v === null ? null : Number(v)),
    z.number().int().nullable(),
  ),
  unit: z.coerce.number().int().min(1),
  salePrice: numField(),
  minSalePrice: numField(),
  costPrice: numField(),
  quantity: numField(),
  minThreshold: numField(),
  hidePriceFromSellers: z.boolean(),
});
type FormValues = z.infer<typeof schema>;

/** Blank (placeholder-showing) value for a numeric field. Typed as number so it
 *  fits FormValues; the schema turns it back into 0 on submit. */
const BLANK = '' as unknown as number;
/** Show a real stored value, but blank a 0 so the placeholder shows instead. */
const numOrBlank = (n: number): number => (n === 0 ? BLANK : n);

export function ProductFormModal({
  open,
  onClose,
  product,
  categories,
}: {
  open: boolean;
  onClose: () => void;
  product: Product | null;
  categories: ProductCategory[];
}) {
  const { t } = useTranslation();
  const confirm = useConfirm();
  const qc = useQueryClient();
  const { hasPermission, hasRole } = useAuth();
  const canViewCost = hasPermission(PERMISSIONS.data.costPrice);
  const canEditStock = hasRole(ROLES.Owner, ROLES.SuperAdmin);
  const isEdit = !!product;

  const unitsQuery = useQuery({ queryKey: ['units'], queryFn: productsApi.units, staleTime: Infinity });

  // Kod maydonga tushadi, bazaga esa forma saqlanganda yoziladi — «Bekor»
  // bosilsa hech nima o'zgarmaydi va yangi tovarda ham ishlaydi.
  const suggest = useMutation({
    mutationFn: productsApi.suggestBarcode,
    onSuccess: (code) => setValue('barcode', code, { shouldDirty: true }),
  });

  const [printing, setPrinting] = useState(false);

  const {
    register,
    handleSubmit,
    reset,
    watch,
    setValue,
    formState: { errors, isSubmitting },
  } = useForm<FormValues>({
    resolver: zodResolver(schema),
    defaultValues: {
      name: '',
      sku: '',
      barcode: '',
      description: '',
      categoryId: null,
      unit: 1,
      salePrice: BLANK,
      minSalePrice: BLANK,
      costPrice: BLANK,
      quantity: BLANK,
      minThreshold: 5,
      hidePriceFromSellers: false,
    },
  });

  useEffect(() => {
    if (!open) return;
    reset(
      product
        ? {
            name: product.name,
            sku: product.sku ?? '',
            barcode: product.barcode ?? '',
            description: product.description ?? '',
            categoryId: product.categoryId,
            unit: product.unit,
            salePrice: numOrBlank(product.salePrice),
            minSalePrice: numOrBlank(product.minSalePrice),
            costPrice: numOrBlank(product.costPrice),
            quantity: numOrBlank(product.quantity),
            minThreshold: numOrBlank(product.minThreshold),
            hidePriceFromSellers: product.hidePriceFromSellers,
          }
        : {
            name: '',
            sku: '',
            barcode: '',
            description: '',
            categoryId: null,
            unit: 1,
            salePrice: BLANK,
            minSalePrice: BLANK,
            costPrice: BLANK,
            quantity: BLANK,
            minThreshold: 5,
            hidePriceFromSellers: false,
          },
    );
  }, [open, product, reset]);

  const mutation = useMutation({
    mutationFn: (values: FormValues) => {
      const body: CreateProductBody = {
        name: values.name,
        sku: values.sku?.trim() ? values.sku.trim() : null,
        // Bo'sh satr emas, null yuboriladi: server null ni «tozalash» deb
        // tushunadi va kod boshqa tovarga bo'shaydi. Bo'sh satr esa unikal
        // indeksga tushib, ikkinchi kodsiz tovarni saqlashga to'sqinlik qilardi.
        barcode: values.barcode?.trim() ? values.barcode.trim() : null,
        description: values.description?.trim() ? values.description.trim() : null,
        categoryId: values.categoryId,
        unit: values.unit,
        salePrice: values.salePrice,
        minSalePrice: values.minSalePrice,
        costPrice: values.costPrice,
        quantity: values.quantity,
        minThreshold: values.minThreshold,
        isTemporary: false,
        hidePriceFromSellers: values.hidePriceFromSellers,
      };
      return isEdit
        ? productsApi.update(product.id, { ...body, quantity: canEditStock ? values.quantity : null })
        : productsApi.create(body);
    },
    onSuccess: () => {
      void qc.invalidateQueries({ queryKey: ['products'] });
      void qc.invalidateQueries({ queryKey: ['products-all'] });
      onClose();
    },
  });

  const deleteMutation = useMutation({
    mutationFn: () => productsApi.remove(product!.id),
    onSuccess: () => {
      void qc.invalidateQueries({ queryKey: ['products'] });
      void qc.invalidateQueries({ queryKey: ['products-all'] });
      onClose();
    },
  });

  const belowCost = canViewCost && watch('salePrice') > 0 && watch('salePrice') < watch('costPrice');

  // Kodning kelib chiqishini ko'rsatamiz: ikkalasi bitta maydonda yashaydi va
  // omborchi qaysi biri ekanini bilishi kerak. 20-29 — do'kon ichki diapazoni,
  // ya'ni bu kodni tizimning o'zi chiqargan va yorliqni ham o'zi bosadi.
  const barcodeValue = watch('barcode');
  const barcodeHint = (() => {
    const code = barcodeValue?.trim() ?? '';
    if (code.length === 0) return t('warehouse.form.barcodeHint');
    // 13 xonali raqam — zavod kodi. Uning 20-29 bilan boshlanadigan qismi
    // xalqaro miqyosda do'kon ichki ehtiyoji uchun ajratilgan, ya'ni bu kodni
    // tizimning o'zi chiqargan.
    if (/^\d{13}$/.test(code)) {
      const prefix = Number(code.slice(0, 2));
      return prefix >= 20 && prefix <= 29
        ? t('warehouse.form.barcodeInternal')
        : t('warehouse.form.barcodeFactory');
    }
    // Qolgani — do'konning o'z kodi («1», «A-3»). U Code 128 bilan bosiladi.
    return t('warehouse.form.barcodeShop');
  })();

  const inputCls =
    'h-11 rounded-input border border-input-border bg-surface px-3.5 text-[14px] outline-none focus:border-primary focus:shadow-focus-ring';

  return (
    <Modal
      open={open}
      onClose={onClose}
      title={isEdit ? t('warehouse.form.editTitle') : t('warehouse.form.addTitle')}
      width="lg"
      footer={
        <>
          {isEdit && hasPermission(PERMISSIONS.products.delete) && (
            <Button
              variant="danger"
              className="mr-auto"
              loading={deleteMutation.isPending}
              onClick={async () => {
                if (await confirm({ title: t('warehouse.form.deleteConfirm'), tone: 'danger', confirmLabel: t('warehouse.form.delete') }))
                  deleteMutation.mutate();
              }}
            >
              {t('warehouse.form.delete')}
            </Button>
          )}
          {/* Faqat saqlangan tovarda: chop etish uchun serverdagi id kerak.
              Yangi tovarda avval saqlanadi, keyin qaytib kelib bosiladi. */}
          {isEdit && hasPermission(PERMISSIONS.products.edit) && (
            <Button variant="secondary" onClick={() => setPrinting(true)}>
              <Printer size={15} />
              {t('labels.fromProduct')}
            </Button>
          )}
          <Button variant="secondary" onClick={onClose}>
            {t('common.cancel')}
          </Button>
          <Button
            loading={isSubmitting || mutation.isPending}
            onClick={handleSubmit((v) => mutation.mutate(v))}
          >
            {t('common.save')}
          </Button>
        </>
      }
    >
      <form onSubmit={handleSubmit((v) => mutation.mutate(v))} className="grid grid-cols-2 gap-4">
        <div className="col-span-2">
          <Input
            label={t('warehouse.form.name')}
            error={errors.name ? t('warehouse.form.name') : undefined}
            {...register('name')}
          />
        </div>

        <Field label={t('warehouse.form.sku')}>
          <input className={inputCls} {...register('sku')} />
        </Field>
        {/* Skaner klaviatura kabi ishlaydi — maydonga fokus berib kodni
            o'qitsa, u shu yerga tushadi. inputMode="numeric" telefonda raqamli
            klaviaturani ochadi. */}
        <Field label={t('warehouse.form.barcode')} hint={barcodeHint}>
          <div className="flex gap-2">
            <input
              className={cn(inputCls, 'flex-1')}
              inputMode="numeric"
              autoComplete="off"
              placeholder={t('warehouse.form.barcodePlaceholder')}
              // Apparat skaner klaviatura kabi ishlaydi: raqamlarni terib,
              // oxirida Enter yuboradi. Enter esa formani YUBORADI — ya'ni kod
              // tushishi bilan tovar saqlanib ketardi, yangi tovarda esa hali
              // to'ldirilmagan maydonlar bilan validatsiya xatosi chiqardi.
              // Bu yerda Enter kodning tugagani, saqlash buyrug'i emas.
              onKeyDown={(e) => {
                if (e.key === 'Enter') e.preventDefault();
              }}
              {...register('barcode')}
            />
            <Button
              type="button"
              variant="secondary"
              size="sm"
              className="flex-none"
              loading={suggest.isPending}
              onClick={async () => {
                // Kod bor bo'lsa tasdiq so'raymiz: eski kod bilan chop etilgan
                // va tovarlarga yopishtirilgan yorliqlar ishlamay qoladi.
                if (barcodeValue?.trim() && !(await confirm({
                  title: t('warehouse.form.barcodeReplaceConfirm'),
                  confirmLabel: t('warehouse.form.barcodeGenerate'),
                }))) return;
                suggest.mutate();
              }}
            >
              <Sparkles size={14} />
              {barcodeValue?.trim() ? t('warehouse.form.barcodeRegenerate') : t('warehouse.form.barcodeGenerate')}
            </Button>
          </div>
        </Field>
        <Field label={t('warehouse.form.category')}>
          <select className={inputCls} {...register('categoryId')}>
            <option value="">{t('warehouse.form.noCategory')}</option>
            {categories.map((c) => (
              <option key={c.id} value={c.id}>
                {c.name}
              </option>
            ))}
          </select>
        </Field>

        <Field label={t('warehouse.form.unit')}>
          <select className={inputCls} {...register('unit')}>
            {/* Ilgari bu yerda qat'iy `u.nameRu` turardi — o'zbek yoki ingliz
                interfeysda ham ro'yxat ruscha chiqardi. Nom endi tanlangan
                tildan olinadi (server uchala variantni ham yuboradi). */}
            {(unitsQuery.data ?? []).map((u) => (
              <option key={u.value} value={u.value}>
                {unitLabel(t, u.value, u.nameRu)}
              </option>
            ))}
          </select>
        </Field>
        <Field label={t('warehouse.form.minThreshold')}>
          <input type="number" step="any" placeholder="0" className={inputCls} {...register('minThreshold')} />
        </Field>

        <Field label={t('warehouse.form.salePrice')}>
          <input type="number" step="any" placeholder="0" className={cn(inputCls, belowCost && 'border-danger')} {...register('salePrice')} />
        </Field>
        <Field label={t('warehouse.form.minSalePrice')}>
          <input type="number" step="any" placeholder="0" className={inputCls} {...register('minSalePrice')} />
        </Field>

        {canViewCost && (
          <Field label={t('warehouse.form.costPrice')}>
            <input type="number" step="any" placeholder="0" className={inputCls} {...register('costPrice')} />
          </Field>
        )}
        {(!isEdit || canEditStock) && (
          <Field label={t('warehouse.form.quantity')}>
            <input type="number" step="any" placeholder="0" className={inputCls} {...register('quantity')} />
          </Field>
        )}

        {belowCost && (
          <div className="col-span-2 -mt-1 text-[12.5px] text-danger">{t('warehouse.form.belowCost')}</div>
        )}

        <div className="col-span-2">
          <label className="mb-1.5 block text-[13px] font-medium text-label">
            {t('warehouse.form.description')}
          </label>
          <textarea
            rows={2}
            placeholder={t('warehouse.form.descriptionHint')}
            className={cn(inputCls, 'h-auto w-full resize-none py-2.5')}
            {...register('description')}
          />
        </div>

        <label className="col-span-2 flex cursor-pointer select-none items-center gap-2.5 text-[13.5px]">
          <input type="checkbox" className="h-4 w-4 accent-primary" {...register('hidePriceFromSellers')} />
          {t('warehouse.form.hidePrice')}
        </label>
      </form>

      {/* Chop etish oynasi SAQLANGAN holatdan chiziladi (product), formadagi
          tahrirlanayotgan qiymatdan emas: hali saqlanmagan kod bilan yorliq
          bosilsa, u bazadagi tovarga mos kelmasdi. */}
      {product && (
        <PrintLabelsModal
          open={printing}
          onClose={() => setPrinting(false)}
          targets={[{ id: product.id, name: product.name, sku: product.sku, barcode: product.barcode }]}
        />
      )}
    </Modal>
  );
}

function Field({
  label,
  hint,
  children,
}: {
  label: string;
  /** Maydon ostidagi kichik izoh — nima uchun kerakligi aniq bo'lmagan joylarda. */
  hint?: string;
  children: React.ReactNode;
}) {
  return (
    <div className="flex flex-col gap-1.5">
      <label className="text-[13px] font-medium text-label">{label}</label>
      {children}
      {hint && <span className="text-[11.5px] leading-snug text-muted-2">{hint}</span>}
    </div>
  );
}
