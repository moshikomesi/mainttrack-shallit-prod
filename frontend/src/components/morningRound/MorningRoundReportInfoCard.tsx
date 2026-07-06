import type { Language } from '../../i18n/translations';
import { formatDisplayDate, formatDisplayDateTime } from '../../utils/formatDate';

type Props = {
  language: Language;
  t: (key: string) => string;
  reportDate: string;
  performedBy: string;
  submittedAt: string;
  isLoading?: boolean;
  loadError?: string | null;
};

export function MorningRoundReportInfoCard({
  language,
  t,
  reportDate,
  performedBy,
  submittedAt,
  isLoading,
  loadError,
}: Props) {
  return (
    <div className="bg-white border border-neutral-200 rounded-lg p-4">
      <h2 className="text-sm font-semibold text-neutral-900 mb-3">
        {t('details.reportInfo')}
      </h2>
      {isLoading && (
        <div className="text-sm text-neutral-500">{t('common.loading')}</div>
      )}
      {loadError && !isLoading && (
        <div className="text-sm text-red-600">{loadError}</div>
      )}
      {!isLoading && !loadError && (
        <div className="space-y-2 text-sm">
          <div className="flex justify-between">
            <span className="text-neutral-600">{t('morning.date')}:</span>
            <span className="font-medium text-neutral-900">
              {formatDisplayDate(language, reportDate, 'long')}
            </span>
          </div>
          <div className="flex justify-between">
            <span className="text-neutral-600">{t('morning.performedBy')}:</span>
            <span className="font-medium text-neutral-900">
              {performedBy || '—'}
            </span>
          </div>
          <div className="flex justify-between">
            <span className="text-neutral-600">{t('details.submittedOn')}:</span>
            <span className="font-medium text-neutral-900">
              {formatDisplayDateTime(language, submittedAt)}
            </span>
          </div>
        </div>
      )}
    </div>
  );
}
