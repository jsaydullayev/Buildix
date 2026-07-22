import { useTranslation } from 'react-i18next';
import { Hammer } from 'lucide-react';
import { PageHeader } from '@/shared/ui';

/** Temporary module scaffold — keeps routing/nav coherent until the real
 *  screen is implemented in a later phase. */
export function PagePlaceholder({ title, subtitle }: { title: string; subtitle?: string }) {
  const { t } = useTranslation();
  return (
    <>
      <PageHeader title={title} subtitle={subtitle} />
      <div className="flex flex-1 flex-col items-center justify-center gap-3 p-8 text-muted-2">
        <div className="flex h-14 w-14 items-center justify-center rounded-2xl bg-hairline text-muted">
          <Hammer size={24} />
        </div>
        <p className="text-[14px]">{t('placeholder.soon')}</p>
      </div>
    </>
  );
}
