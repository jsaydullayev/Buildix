import type { TFunction } from 'i18next';

/**
 * UnitType (Buildix.Domain/Enums/UnitType.cs) → i18n key.
 *
 * The API also sends a `unit`/`unitName` string, but it is a fixed Uzbek
 * abbreviation ("dona", "kg") baked into Product.GetUnitName(), which reads
 * wrong in the Russian and English UI. Everything user-facing should localise
 * from the numeric value instead.
 */
const UNIT_KEYS: Record<number, string> = {
  1: 'piece',
  2: 'kilogram',
  3: 'meter',
  4: 'bag',
  5: 'ton',
  6: 'sheet',
  7: 'bucket',
  8: 'roll',
  9: 'box',
  10: 'pack',
};

/**
 * Localised short unit label. Falls back to the server-provided string (and
 * then to an empty string) when the numeric value is missing — external sale
 * lines carry no unit, and rows written before `unitValue` existed send 0.
 */
export function unitLabel(
  t: TFunction,
  unitValue: number | null | undefined,
  fallback?: string | null,
): string {
  const key = unitValue ? UNIT_KEYS[unitValue] : undefined;
  return key ? (t(`units.${key}` as never) as string) : (fallback ?? '');
}
