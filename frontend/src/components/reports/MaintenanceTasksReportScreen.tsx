import { Fragment, useEffect, useMemo, useState } from 'react';
import { Calendar, ChevronDown, ChevronUp, Loader2 } from 'lucide-react';
import { useLanguage } from '../../context/LanguageContext';
import { getMaintenanceTasksReport } from '../../services/maintenanceTasksReportService';
import { ReportLocationHeader } from './ReportLocationHeader';
import { reportDateLocationLabel, taskLocationLabel } from '../../utils/reportLocationLabels';
import type { MaintenanceTaskReportDto } from '../../types/maintenanceTaskReport';
import { safeImageSrc } from '../../utils/safeUrl';

type Props = {
  expandedId?: string | null;
  onExpandedIdChange?: (id: string | null) => void;
};

export function MaintenanceTasksReportScreen({
  expandedId: expandedIdProp,
  onExpandedIdChange,
}: Props = {}) {
  const { t, language } = useLanguage();
  const [tasks, setTasks] = useState<MaintenanceTaskReportDto[]>([]);
  const [internalExpandedId, setInternalExpandedId] = useState<string | null>(null);
  const expandedId = expandedIdProp ?? internalExpandedId;
  const setExpandedId = onExpandedIdChange ?? setInternalExpandedId;
  const [filterDate, setFilterDate] = useState('');
  const [isLoading, setIsLoading] = useState(true);
  const [loadError, setLoadError] = useState<string | null>(null);

  useEffect(() => {
    let cancelled = false;

    const load = async () => {
      try {
        setIsLoading(true);
        setLoadError(null);
        const data = await getMaintenanceTasksReport();
        if (!cancelled) {
          setTasks(data);
        }
      } catch (err) {
        console.error('Failed loading maintenance tasks report', err);
        if (!cancelled) {
          setLoadError(t('messages.failedToLoadMaintenanceTasksReport'));
        }
      } finally {
        if (!cancelled) {
          setIsLoading(false);
        }
      }
    };

    load();

    return () => {
      cancelled = true;
    };
  }, [t]);

  const locale = language === 'he' ? 'he-IL' : language === 'th' ? 'th-TH' : 'en-US';

  const formatDate = (dateStr: string) => {
    const date = new Date(dateStr);
    return date.toLocaleDateString(locale, {
      weekday: 'short',
      year: 'numeric',
      month: 'short',
      day: 'numeric',
      hour: '2-digit',
      minute: '2-digit',
      hour12: false,
    });
  };

  const formatDateTime = (dateStr: string) =>
    new Date(dateStr).toLocaleString(locale, {
      year: 'numeric',
      month: 'short',
      day: 'numeric',
      hour: '2-digit',
      minute: '2-digit',
      hour12: false,
    });

  const filteredTasks = useMemo(() => {
    if (!filterDate) return tasks;
    return tasks.filter((task) => task.taskDate.slice(0, 10) === filterDate);
  }, [filterDate, tasks]);

  const expandedTask = expandedId
    ? filteredTasks.find((task) => task.id === expandedId) ?? null
    : null;

  const locationSegments = useMemo(() => {
    const segments = [t('reports.title'), t('reports.tasks.menuTitle')];
    if (expandedTask) {
      segments.push(reportDateLocationLabel(t, language, expandedTask.taskDate.slice(0, 10)));
      if ((expandedTask.description?.trim() ?? '').length > 0) {
        segments.push(taskLocationLabel(t, expandedTask.description));
      }
    }
    return segments;
  }, [expandedTask, language, t]);

  return (
    <div className="space-y-4">
      <ReportLocationHeader segments={locationSegments} />
      <div className="bg-white border border-neutral-200 rounded-lg p-3">
        <label className="flex items-center gap-2 text-sm font-medium text-neutral-700 mb-2">
          <Calendar className="w-4 h-4" />
          {t('reports.filterByDate')}
        </label>
        <input
          type="date"
          value={filterDate}
          onChange={(event) => setFilterDate(event.target.value)}
          className="w-full px-3 py-2 bg-white border border-neutral-300 rounded text-sm text-neutral-900 focus:outline-none focus:ring-2 focus:ring-neutral-800"
        />
        {filterDate && (
          <button
            type="button"
            onClick={() => setFilterDate('')}
            className="mt-2 text-xs text-neutral-600 hover:text-neutral-900"
          >
            {t('common.clearFilter')}
          </button>
        )}
      </div>

      <div className="bg-white border border-neutral-200 rounded-lg overflow-hidden">
        <div className="px-4 py-3 border-b border-neutral-200">
          <h2 className="text-sm font-semibold text-neutral-900">
            {t('reports.tasks.title')}
          </h2>
        </div>

        {isLoading ? (
          <div className="flex justify-center py-8" role="status" aria-label={t('common.loading')}>
            <Loader2 className="w-8 h-8 text-neutral-400 animate-spin" />
          </div>
        ) : loadError ? (
          <div className="p-8 text-center text-sm text-red-600">{loadError}</div>
        ) : filteredTasks.length === 0 ? (
          <div className="p-8 text-center text-sm text-neutral-500">
            {t('reports.noReportsFound')}
          </div>
        ) : (
          <div className="overflow-x-auto">
            <table className="w-full text-sm">
              <thead className="bg-neutral-50 text-neutral-600">
                <tr>
                  <th className="px-4 py-3 text-start font-medium">{t('reports.tasks.date')}</th>
                  <th className="px-4 py-3 text-start font-medium">{t('reports.tasks.createdBy')}</th>
                  <th className="px-4 py-3 text-start font-medium" aria-label={t('common.details')} />
                </tr>
              </thead>
              <tbody>
                {filteredTasks.map((task) => {
                  const isExpanded = expandedId === task.id;
                  const imageSrc = safeImageSrc(task.imageUrl);
                  return (
                    <Fragment key={task.id}>
                      <tr
                        className="border-t border-neutral-200 hover:bg-neutral-50 cursor-pointer"
                        onClick={() => setExpandedId(isExpanded ? null : task.id)}
                      >
                        <td className="px-4 py-3 font-medium text-neutral-900 whitespace-nowrap">
                          {formatDate(task.taskDate)}
                        </td>
                        <td className="px-4 py-3 text-neutral-700 whitespace-nowrap">
                          {task.createdByUserName}
                        </td>
                        <td className="px-4 py-3 text-neutral-500">
                          {isExpanded ? (
                            <ChevronUp className="w-4 h-4" />
                          ) : (
                            <ChevronDown className="w-4 h-4" />
                          )}
                        </td>
                      </tr>
                      {isExpanded && (
                        <tr className="border-t border-neutral-200 bg-neutral-50">
                          <td colSpan={3} className="px-4 py-4">
                            <div className="space-y-4">
                              {imageSrc && (
                                <img
                                  src={imageSrc}
                                  alt={t('reports.tasks.imageAlt')}
                                  className="w-full max-h-72 object-cover rounded-lg border border-neutral-300"
                                />
                              )}
                              <div className="bg-white border border-neutral-200 rounded-lg p-4 space-y-2">
                                <DetailRow label={t('reports.tasks.date')} value={formatDate(task.taskDate)} />
                                <DetailRow
                                  label={t('reports.tasks.description')}
                                  value={task.description || t('common.notProvided')}
                                />
                                <DetailRow
                                  label={t('reports.tasks.status')}
                                  value={task.isConfirmed ? t('common.yes') : t('common.no')}
                                />
                                <DetailRow label={t('reports.tasks.createdBy')} value={task.createdByUserName} />
                                <DetailRow label={t('reports.tasks.createdAt')} value={formatDateTime(task.createdAt)} />
                              </div>
                            </div>
                          </td>
                        </tr>
                      )}
                    </Fragment>
                  );
                })}
              </tbody>
            </table>
          </div>
        )}
      </div>
    </div>
  );
}

function DetailRow({ label, value }: { label: string; value: string }) {
  return (
    <div className="flex justify-between gap-4 text-sm">
      <span className="text-neutral-600">{label}:</span>
      <span className="font-medium text-neutral-900 text-end">{value}</span>
    </div>
  );
}
