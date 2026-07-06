import { useLanguage } from '../context/LanguageContext';
import { formatFormDate } from '../utils/formatDate';

type Props = {
  date: string;
};

export function ReadOnlyDateBanner({ date }: Props) {
  const { language, t } = useLanguage();

  return (
    <div className="bg-white border border-neutral-200 rounded-lg px-4 py-3 flex items-center justify-between gap-3">
      <span className="text-sm text-neutral-600">{t('morning.date')}</span>
      <span className="text-sm font-medium text-neutral-900 tabular-nums">
        {formatFormDate(language, date)}
      </span>
    </div>
  );
}
