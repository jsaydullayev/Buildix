import { useTranslation } from 'react-i18next';
import { CloudOff, Link2Off } from 'lucide-react';
import { useSyncFreshness } from './useSyncFreshness';

/**
 * Ekrandagi ma'lumot eskirganini aytadi.
 *
 * <p><b>Nega faqat muammo bo'lganda ko'rinadi.</b> Doimiy turadigan
 * «hammasi joyida» yozuvi bir kunda ko'zga ilinmay qoladi va aynan kerak
 * bo'lganda ham o'qilmaydi. Shuning uchun bu chiziq faqat aytadigan gapi
 * bo'lganda chiqadi: ma'lumot eskirgan yoki do'kon umuman bog'lanmagan.
 * Chiziq yo'q bo'lsa — raqamlar jonli.</p>
 *
 * <p><b>Ikki holat farqlanadi.</b> «Bog'lanmagan» — o'rnatish tugallanmagan
 * va uni kutib o'tirishning ma'nosi yo'q, odam aralashuvi kerak.
 * «Eskirgan» — do'kon ishlayapti, lekin aloqada emas: ma'lumot aloqa
 * tiklanganda o'zi yetib keladi.</p>
 */
export function SyncFreshnessBanner() {
  const { t } = useTranslation();
  const { data } = useSyncFreshness();

  // Yuklanayotganda ham, xato bo'lganda ham hech narsa ko'rsatilmaydi:
  // bu belgining o'zi shovqin manbaiga aylanmasligi kerak.
  if (!data) return null;
  if (data.isPaired && data.isFresh) return null;

  const unpaired = !data.isPaired;

  // «2 soat», «14 daqiqa» — aniq soniyalar emas: egasiga «7243 soniya» hech
  // narsa aytmaydi, unga kerakli narsa — son qanchalik eskirgani.
  const seconds = data.secondsSinceSync;
  let age: string;
  if (seconds === null) {
    age = t('sync.ageUnknown');
  } else if (seconds < 3600) {
    age = t('sync.ageMinutes', { count: Math.max(1, Math.floor(seconds / 60)) });
  } else if (seconds < 86_400) {
    age = t('sync.ageHours', { count: Math.floor(seconds / 3600) });
  } else {
    age = t('sync.ageDays', { count: Math.floor(seconds / 86_400) });
  }

  return (
    <div
      role="status"
      className={[
        'flex flex-wrap items-center gap-2 border-b px-3 py-2 text-[13px] sm:px-4',
        unpaired
          ? 'border-danger/25 bg-danger-soft text-danger'
          : 'border-warn-amber/30 bg-warn-soft text-warn-text',
      ].join(' ')}
    >
      {unpaired ? (
        <Link2Off size={15} className="flex-none" />
      ) : (
        <CloudOff size={15} className="flex-none" />
      )}
      <span className="min-w-0">{unpaired ? t('sync.unpaired') : t('sync.stale', { age })}</span>
    </div>
  );
}
