import { cn } from '@/shared/lib/cn';

/**
 * Filtr tugmasi — «Hammasi», «Bugun», kategoriya nomi va shu kabilar.
 *
 * <p>Uchta ro'yxat sahifasida (tovarlar, sotuvlar, sotuvchi tovarlari)
 * bir xil nusxada yozilgan edi. Faol holatning rangi bitta joyda
 * turishi kerak: aks holda bir sahifada ko'k, boshqasida boshqacha
 * bo'lib qolishi vaqt masalasi.</p>
 */
export function Chip({
  label,
  active,
  onClick,
}: {
  label: string;
  active: boolean;
  onClick: () => void;
}) {
  return (
    <button
      type="button"
      onClick={onClick}
      className={cn(
        'flex-none whitespace-nowrap rounded-input px-3.5 py-2 text-[13px] font-medium transition-colors',
        active ? 'bg-primary text-white' : 'border border-input-border bg-surface text-muted hover:text-text',
      )}
    >
      {label}
    </button>
  );
}
