import type { ComponentType } from 'react';
import { useTranslation } from 'react-i18next';
import { PageHeader } from '@/shared/ui';

/**
 * Placeholder for a screen whose route exists but whose build-out lands in a
 * later phase (A1 stands up the routes; A2/A3/A5 fill them in). It renders the
 * real title/subtitle so the sidebar link is never a dead 404, with an honest
 * "in progress" note instead of a fake empty table.
 */
export function ComingSoonPage({
  titleKey,
  icon: Icon,
}: {
  titleKey: string;
  icon: ComponentType<{ size?: number | string; className?: string }>;
}) {
  const { t } = useTranslation();
  return (
    <>
      <PageHeader title={t(titleKey as never)} subtitle={t('common.comingSoonSubtitle')} />
      <div className="flex flex-1 flex-col items-center justify-center gap-4 p-8 text-muted-2">
        <div className="flex h-16 w-16 items-center justify-center rounded-2xl bg-hairline text-muted">
          <Icon size={30} />
        </div>
        <p className="text-[14px]">{t('common.comingSoon')}</p>
      </div>
    </>
  );
}
