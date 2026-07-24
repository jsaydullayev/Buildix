import { useEffect, useMemo, useState } from 'react';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { useTranslation } from 'react-i18next';
import { RotateCcw } from 'lucide-react';
import { Modal, Button, Spinner, Toggle } from '@/shared/ui';
import { cn } from '@/shared/lib/cn';
import { formatSum } from '@/shared/lib/format';
import type { ApiError } from '@/shared/api/types';
import { employeesApi, type Employee, type UserPermissions } from './api';

/** Access presets (Стажёр/Продавец/Старший) applied over the real permission keys + limits. */
const PRESETS: Record<string, { add: string[]; disc: number | null; debt: number | null }> = {
  trainee: { add: ['dashboard.access', 'sales.access', 'products.access'], disc: null, debt: 0 },
  seller: {
    add: ['dashboard.access', 'sales.access', 'sales.create', 'sales.invoice', 'products.access', 'customers.access', 'customers.manage', 'debts.access', 'debts.manage', 'zakup.access', 'notifications.access'],
    disc: 3,
    debt: 5_000_000,
  },
  senior: {
    add: ['dashboard.access', 'sales.access', 'sales.create', 'sales.edit', 'sales.delete', 'sales.invoice', 'products.access', 'customers.access', 'customers.manage', 'debts.access', 'debts.manage', 'debts.dueDate', 'zakup.access', 'notifications.access'],
    disc: 10,
    debt: null,
  },
};
const DISC_OPTIONS: (number | null)[] = [3, 5, 10, null];
const DEBT_OPTIONS: (number | null)[] = [5_000_000, 20_000_000, null];

/** Order in which permission groups are shown; keys not listed fall to the end. */
const GROUP_ORDER = [
  'dashboard',
  'sales',
  'products',
  'categories',
  'zakup',
  'suppliers',
  'customers',
  'debts',
  'cashregister',
  'reports',
  'users',
  'notifications',
  'data',
];

/** "products.create" → group "products", so we can bucket the catalogue. */
function groupOf(key: string): string {
  return key.split('.')[0] ?? key;
}

export function PermissionsModal({ employee, onClose }: { employee: Employee | null; onClose: () => void }) {
  const { t } = useTranslation();
  const qc = useQueryClient();
  const [selected, setSelected] = useState<Set<string>>(new Set());
  const [maxDisc, setMaxDisc] = useState<number | null>(null);
  const [maxDebt, setMaxDebt] = useState<number | null>(null);
  const [error, setError] = useState<string | null>(null);

  const query = useQuery({
    queryKey: ['user-permissions', employee?.id],
    queryFn: () => employeesApi.permissions(employee!.id),
    enabled: !!employee,
  });
  const data = query.data;

  useEffect(() => {
    if (data) {
      setSelected(new Set(data.effectivePermissions));
      setMaxDisc(data.maxDiscountPercent);
      setMaxDebt(data.maxDebtPerCheck);
    }
  }, [data]);

  // Apply a preset: only keys present in the catalogue, plus its limits.
  const applyPreset = (name: keyof typeof PRESETS, d: UserPermissions) => {
    const p = PRESETS[name]!;
    const catalog = new Set(d.catalog);
    setSelected(new Set(p.add.filter((k) => catalog.has(k))));
    setMaxDisc(p.disc);
    setMaxDebt(p.debt);
  };

  // Bucket the full catalogue into ordered groups so related toggles sit together.
  const groups = useMemo(() => {
    const byGroup = new Map<string, string[]>();
    for (const key of data?.catalog ?? []) {
      const g = groupOf(key);
      if (!byGroup.has(g)) byGroup.set(g, []);
      byGroup.get(g)!.push(key);
    }
    return [...byGroup.entries()].sort(
      (a, b) => (GROUP_ORDER.indexOf(a[0]) + 1 || 99) - (GROUP_ORDER.indexOf(b[0]) + 1 || 99),
    );
  }, [data]);

  const toggle = (key: string, on: boolean) =>
    setSelected((prev) => {
      const next = new Set(prev);
      if (on) next.add(key);
      else next.delete(key);
      return next;
    });

  const save = useMutation({
    mutationFn: (permissions: string[]) =>
      employeesApi.updatePermissions(employee!.id, permissions, { maxDiscountPercent: maxDisc, maxDebtPerCheck: maxDebt }),
    onSuccess: () => {
      void qc.invalidateQueries({ queryKey: ['employees'] });
      void qc.invalidateQueries({ queryKey: ['user-permissions', employee?.id] });
      onClose();
    },
    onError: (e) => setError((e as unknown as ApiError).message ?? t('common.somethingWrong')),
  });

  // Perm label: prefer a localised name, fall back to the raw key so a newly
  // added permission still renders (just untranslated) instead of blank.
  const permLabel = (key: string) => t(`permissions.perm.${key}` as never, { defaultValue: key });
  const groupLabel = (g: string) => t(`permissions.groups.${g}` as never, { defaultValue: g });

  return (
    <Modal
      open={!!employee}
      onClose={onClose}
      width="lg"
      title={t('permissions.title')}
      subtitle={employee ? `${employee.fullName} · @${employee.username}` : undefined}
      footer={
        <>
          <Button variant="secondary" onClick={onClose}>
            {t('common.cancel')}
          </Button>
          <Button loading={save.isPending} onClick={() => save.mutate([...selected])}>
            {t('common.save')}
          </Button>
        </>
      }
    >
      {query.isLoading || !data ? (
        <div className="flex justify-center py-16 text-primary">
          <Spinner size={24} />
        </div>
      ) : (
        <div className="flex flex-col gap-4">
          {/* Reset-to-default banner. Sending an empty set reverts the user to
              its role default; we surface it as a one-click action + a note. */}
          <div className="flex items-center justify-between gap-3 rounded-input bg-bg px-4 py-2.5">
            <span className="text-[12.5px] text-muted">
              {data.isCustomized ? t('permissions.customized') : t('permissions.roleDefault')}
            </span>
            <button
              type="button"
              onClick={() => {
                setSelected(new Set(data.roleDefaults));
                setMaxDisc(null);
                setMaxDebt(null);
              }}
              className="flex items-center gap-1.5 text-[12.5px] font-medium text-primary hover:text-primary-hover"
            >
              <RotateCcw size={13} />
              {t('permissions.reset')}
            </button>
          </div>

          {/* Access preset (Стажёр/Продавец/Старший) */}
          <div>
            <h3 className="mb-1.5 text-[12px] font-semibold uppercase tracking-[0.4px] text-muted-2">
              {t('permissions.profile')}
            </h3>
            <div className="grid grid-cols-3 gap-2">
              {(['trainee', 'seller', 'senior'] as const).map((p) => (
                <button
                  key={p}
                  type="button"
                  onClick={() => applyPreset(p, data)}
                  className="h-10 rounded-input border border-input-border bg-surface text-[13px] font-medium text-muted transition-colors hover:border-primary hover:text-primary"
                >
                  {t(`permissions.preset.${p}`)}
                </button>
              ))}
            </div>
          </div>

          {/* Limits */}
          <div className="flex flex-col gap-3 rounded-input border border-hairline p-3.5">
            <LimitRow
              label={t('permissions.discountLimit')}
              options={DISC_OPTIONS}
              value={maxDisc}
              onChange={setMaxDisc}
              render={(v) => (v === null ? t('permissions.noLimit') : `${v}%`)}
            />
            <LimitRow
              label={t('permissions.debtLimit')}
              options={DEBT_OPTIONS}
              value={maxDebt}
              onChange={setMaxDebt}
              render={(v) => (v === null ? t('permissions.noLimit') : formatSum(v))}
            />
          </div>

          {groups.map(([g, keys]) => (
            <div key={g}>
              <h3 className="mb-1.5 text-[12px] font-semibold uppercase tracking-[0.4px] text-muted-2">
                {groupLabel(g)}
              </h3>
              <div className="flex flex-col divide-y divide-hairline rounded-input border border-hairline">
                {keys.map((key) => {
                  const isDefault = data.roleDefaults.includes(key);
                  return (
                    <div key={key} className="flex items-center justify-between gap-3 px-3.5 py-2.5">
                      <div className="min-w-0">
                        <div className="text-[13.5px] font-medium">{permLabel(key)}</div>
                        <div className="text-[11px] text-muted-2 nums">{key}</div>
                      </div>
                      <div className="flex items-center gap-2">
                        {isDefault && (
                          <span className="text-[10.5px] font-medium uppercase tracking-[0.3px] text-muted-2">
                            {t('permissions.byRole')}
                          </span>
                        )}
                        <Toggle checked={selected.has(key)} onChange={(on) => toggle(key, on)} />
                      </div>
                    </div>
                  );
                })}
              </div>
            </div>
          ))}

          <p className="text-[12px] text-muted-2">{t('permissions.effectNote')}</p>
          {error && <div className="rounded-input bg-danger-soft px-3 py-2 text-[12.5px] text-danger">{error}</div>}
        </div>
      )}
    </Modal>
  );
}

function LimitRow({
  label,
  options,
  value,
  onChange,
  render,
}: {
  label: string;
  options: (number | null)[];
  value: number | null;
  onChange: (v: number | null) => void;
  render: (v: number | null) => string;
}) {
  return (
    <div className="flex flex-wrap items-center justify-between gap-2">
      <span className="text-[13px] font-medium text-label">{label}</span>
      <div className="inline-flex rounded-input bg-hairline p-1">
        {options.map((opt) => (
          <button
            key={String(opt)}
            type="button"
            onClick={() => onChange(opt)}
            className={cn(
              'rounded-md px-3 py-1 text-[12.5px] font-medium transition-colors nums',
              value === opt ? 'bg-surface text-text shadow-card' : 'text-muted hover:text-text',
            )}
          >
            {render(opt)}
          </button>
        ))}
      </div>
    </div>
  );
}
