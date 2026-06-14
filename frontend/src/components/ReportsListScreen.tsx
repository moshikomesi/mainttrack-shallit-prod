import { useCallback, useEffect, useState } from 'react';
import { useLanguage } from '../context/LanguageContext';
import { Calendar, Loader2, RefreshCw } from 'lucide-react';
import { AnnualPlanReportScreen } from './AnnualPlanReportScreen';
import { MaintenanceTasksReportScreen } from './reports/MaintenanceTasksReportScreen';
import { getMorningRounds } from '../services/morningRoundsService';
import { getMaintenance } from '../services/maintenanceService';
import { getTreatments } from '../services/treatmentService';
import { useInfiniteScroll } from '../hooks/useInfiniteScroll';
import { AppHeader } from './AppHeader';
import type { MorningRoundDto } from '../types/morningRound';
import type { MaintenanceEntryDto } from '../types/maintenance';
import type { TreatmentDto } from '../types/treatment';
import { formatDisplayDate, formatDisplayDateTime } from '../utils/formatDate';
import { formatTechnician } from '../utils/formatTechnician';
import type { ReportListItem, ReportsListScreenProps } from '../types/reports';

export function ReportsListScreen({ onSelectReport }: ReportsListScreenProps) {
  const { t, language } = useLanguage();
  const [selectedType, setSelectedType] =
    useState<'morning' | 'maintenance' | 'maintenance-tasks' | 'treatments' | 'annual-plans' | null>(null);
  const [filterDate, setFilterDate] = useState('');
  const [morningReports, setMorningReports] = useState<ReportListItem[]>([]);
  const [isLoadingMorning, setIsLoadingMorning] = useState(false);
  const [loadError, setLoadError] = useState<string | null>(null);
  const [search, setSearch] = useState('');
  const [debouncedSearch, setDebouncedSearch] = useState('');
  const [annualPlanYear, setAnnualPlanYear] = useState('');
  const [annualPlanType, setAnnualPlanType] = useState<'Preventive' | 'Summer' | ''>('');

  const currentYear = new Date().getFullYear();
  const annualPlanYears = Array.from({ length: 7 }, (_, i) => String(currentYear - 3 + i));

  useEffect(() => {
    const handler = setTimeout(() => {
      setDebouncedSearch(search);
    }, 500);

    return () => {
      clearTimeout(handler);
    };
  }, [search]);

  useEffect(() => {
    if (selectedType !== 'morning') return;

    let cancelled = false;
    const load = async () => {
      try {
        setIsLoadingMorning(true);
        setLoadError(null);
        const list = await getMorningRounds();
        if (cancelled) return;
        const mapped: ReportListItem[] = list.map((r: MorningRoundDto) => ({
          id: r.id,
          date: r.reportDate,
          submittedBy: r.performedByName,
          submittedAt: r.performedAt,
          type: 'morning',
        }));
        setMorningReports(mapped);
      } catch (err) {
        console.error(err);
        if (!cancelled) {
          setLoadError(t('messages.failedToLoadMorningRoundReports'));
        }
      } finally {
        if (!cancelled) {
          setIsLoadingMorning(false);
        }
      }
    };

    load();
    return () => {
      cancelled = true;
    };
  }, [selectedType]);

  const fetchMaintenancePage = useCallback(
    async (pageNumber: number, pageSize: number) => {
      const list = await getMaintenance({
        pageNumber,
        pageSize,
        search: debouncedSearch.length >= 2 ? debouncedSearch : undefined,
      });
      const items: ReportListItem[] = (list as MaintenanceEntryDto[]).map((m) => ({
        id: m.id,
        date: m.date,
        submittedBy: m.employeeName,
        submittedAt: m.createdAt,
        type: 'maintenance',
      }));
      return { items };
    },
    [debouncedSearch]
  );

  const fetchTreatmentsPage = useCallback(
    async (pageNumber: number, pageSize: number) => {
      const list = await getTreatments({
        pageNumber,
        pageSize,
        search: debouncedSearch.length >= 2 ? debouncedSearch : undefined,
      });
      const items: ReportListItem[] = (list as TreatmentDto[]).map((treatment) => ({
        id: treatment.id,
        date: treatment.treatmentDate,
        submittedBy: treatment.technician,
        submittedAt: treatment.treatmentDate,
        type: 'treatments',
      }));
      return { items };
    },
    [debouncedSearch]
  );

  const maintenanceScroll = useInfiniteScroll<ReportListItem>({
    fetchPage: fetchMaintenancePage,
    pageSize: 20,
    resetKey: debouncedSearch,
    enabled: selectedType === 'maintenance',
  });

  const treatmentsScroll = useInfiniteScroll<ReportListItem>({
    fetchPage: fetchTreatmentsPage,
    pageSize: 20,
    resetKey: debouncedSearch,
    enabled: selectedType === 'treatments',
  });

  const reports =
    selectedType === 'morning'
      ? morningReports
      : selectedType === 'maintenance'
        ? maintenanceScroll.items
        : selectedType === 'treatments'
          ? treatmentsScroll.items
          : [];

  const filteredReports = filterDate 
    ? reports.filter(r => r.date === filterDate)
    : reports;

  return (
    <div className="min-h-screen bg-neutral-50">
      <AppHeader title={t('reports.title')} showBack={true} showHome={true} />

      <div className="p-4">
        {!selectedType ? (
          /* Report Type Selection */
          <div className="space-y-3">
            <h2 className="text-sm font-medium text-neutral-600 mb-3">
            </h2>
            <button
              onClick={() => setSelectedType('morning')}
              className="w-full bg-white border border-neutral-200 rounded-lg p-5 text-start hover:bg-neutral-50 active:bg-neutral-100 transition-colors"
            >
              <div className="text-base font-semibold text-neutral-900 mb-1">
                {t('reports.morningRound')}
              </div>
              <div className="text-sm text-neutral-500">
              </div>
            </button>
            <button
              onClick={() => setSelectedType('maintenance')}
              className="w-full bg-white border border-neutral-200 rounded-lg p-5 text-start hover:bg-neutral-50 active:bg-neutral-100 transition-colors"
            >
              <div className="text-base font-semibold text-neutral-900 mb-1">
                {t('reports.maintenanceLog')}
              </div>
                <div className="text-sm text-neutral-500">
                </div>
            </button>
            <button
              onClick={() => setSelectedType('maintenance-tasks')}
              className="w-full bg-white border border-neutral-200 rounded-lg p-5 text-start hover:bg-neutral-50 active:bg-neutral-100 transition-colors"
            >
              <div className="text-base font-semibold text-neutral-900 mb-1">
                {t('reports.tasks.menuTitle')}
              </div>
            </button>
            <button
              onClick={() => setSelectedType('annual-plans')}
              className="w-full bg-white border border-neutral-200 rounded-lg p-5 text-start hover:bg-neutral-50 active:bg-neutral-100 transition-colors"
            >
              <div className="text-base font-semibold text-neutral-900 mb-1">
                {t('home.annualPlans')}
              </div>
            </button>
            <button
              onClick={() => setSelectedType('treatments')}
              className="w-full bg-white border border-neutral-200 rounded-lg p-5 text-start hover:bg-neutral-50 active:bg-neutral-100 transition-colors"
            >
              <div className="text-base font-semibold text-neutral-900 mb-1">
                {t('treatments.title')}
              </div>
              <div className="text-sm text-neutral-500">
              </div>
            </button>
          </div>
        ) : selectedType === 'maintenance-tasks' ? (
          <MaintenanceTasksReportScreen />
        ) : selectedType === 'annual-plans' ? (
          <div className="space-y-4">
            <div className="bg-white border border-neutral-200 rounded-lg p-3 space-y-3">
              <div>
                <label className="block text-sm font-medium text-neutral-700 mb-1">
                  {t('annual.year')}
                </label>
                <select
                  value={annualPlanYear}
                  onChange={(e) => setAnnualPlanYear(e.target.value)}
                  className="w-full px-3 py-2 bg-white border border-neutral-300 rounded text-sm text-neutral-900 focus:outline-none focus:ring-2 focus:ring-neutral-800"
                >
                  <option value="">{t('reports.year')}</option>
                  {annualPlanYears.map((y) => (
                    <option key={y} value={y}
                    className="w-full px-3 py-2 bg-white border border-neutral-300 rounded text-sm text-neutral-900 focus:outline-none focus:ring-2 focus:ring-neutral-800">
                      {y}
                    </option>
                  ))}
                </select>
              </div>
              <div>
                <label className="block text-sm font-medium text-neutral-700 mb-1">
                  {t('annual.planType')}
                </label>
                <div className="grid grid-cols-2 gap-2">
                  <button
                    onClick={() => setAnnualPlanType('Preventive')}
                    className={`px-3 py-2 rounded-lg text-sm font-medium transition-colors ${
                      annualPlanType === 'Preventive'
                        ? 'bg-neutral-800 text-white'
                        : 'bg-neutral-50 text-neutral-700 hover:bg-neutral-100'
                    }`}
                  >
                    {t('annual.preventive')}
                  </button>
                  <button
                    onClick={() => setAnnualPlanType('Summer')}
                    className={`px-3 py-2 rounded-lg text-sm font-medium transition-colors ${
                      annualPlanType === 'Summer'
                        ? 'bg-neutral-800 text-white'
                        : 'bg-neutral-50 text-neutral-700 hover:bg-neutral-100'
                    }`}
                  >
                    {t('annual.summer')}
                  </button>
                </div>
              </div>
            </div>

            {annualPlanYear && annualPlanType && (
              <AnnualPlanReportScreen
                year={Number(annualPlanYear)}
                type={annualPlanType}
              />
            )}
          </div>
        ) : (
          /* Report List */
          <div className="space-y-4">
            {/* Date & Search Filters */}
            <div className="bg-white border border-neutral-200 rounded-lg p-3 space-y-3">
              <div>
                <label className="flex items-center gap-2 text-sm font-medium text-neutral-700 mb-2">
                  <Calendar className="w-4 h-4" />
                  {t('reports.filterByDate')}
                </label>
                <input
                  type="date"
                  value={filterDate}
                  onChange={(e) => setFilterDate(e.target.value)}
                  className="w-full px-3 py-2 bg-white border border-neutral-300 rounded text-sm text-neutral-900 focus:outline-none focus:ring-2 focus:ring-neutral-800"
                />
                {filterDate && (
                  <button
                    onClick={() => setFilterDate('')}
                    className="mt-2 text-xs text-neutral-600 hover:text-neutral-900"
                  >
                    {t('common.clearFilter')}
                  </button>
                )}
              </div>

              {(selectedType === 'maintenance' || selectedType === 'treatments') && (
                <div>
                  <label className="block text-sm font-medium text-neutral-700 mb-1">
                    {t('common.description')}
                  </label>
                  <input
                    type="text"
                    value={search}
                    onChange={(e) => setSearch(e.target.value)}
                    placeholder={t('common.description')}
                    className="w-full px-3 py-2 bg-white border border-neutral-300 rounded text-sm text-neutral-900 placeholder:text-neutral-400 focus:outline-none focus:ring-2 focus:ring-neutral-800"
                  />
                </div>
              )}
            </div>

            {/* Reports */}
            <div className="flex flex-col gap-4 py-4">
              {filteredReports.map((report) => (
                <button
                  key={report.id}
                  onClick={() => onSelectReport(report.id, report.type)}
                  className="w-full bg-white border border-neutral-200 rounded-lg p-4 text-start hover:bg-neutral-50 active:bg-neutral-100 transition-colors touch-manipulation"
                >
                  <div className="flex items-start justify-between mb-2">
                    <div className="text-sm font-semibold text-neutral-900">
                      {formatDisplayDate(language, report.date)}
                    </div>
                    <div className="px-2 py-0.5 bg-teal-100 text-teal-800 rounded text-xs font-medium">
                      {t('common.submitted')}
                    </div>
                  </div>
                  <div className="text-sm text-neutral-600 mb-1">
                    {selectedType === 'treatments' ? t('common.technician') : t('reports.submittedBy')}:{' '}
                    <span className="font-medium text-neutral-900">
                      {selectedType === 'treatments'
                        ? formatTechnician(t, report.submittedBy)
                        : report.submittedBy}
                    </span>
                  </div>
                  {selectedType === 'morning' && (
                    <div className="text-xs text-neutral-500">
                      {t('details.submittedOn')}: {formatDisplayDateTime(language, report.submittedAt)}
                    </div>
                  )}
                </button>
              ))}

              {/* Infinite scroll sentinel and loading / error for maintenance and treatments */}
              {(selectedType === 'maintenance' || selectedType === 'treatments') && (
                <>
                  <div
                    ref={(selectedType === 'maintenance' ? maintenanceScroll : treatmentsScroll).loadMoreRef}
                    className="h-4 min-h-4"
                    aria-hidden
                  />
                  {(selectedType === 'maintenance' ? maintenanceScroll : treatmentsScroll).loading && (
                    <div
                      className="flex justify-center py-4"
                      role="status"
                      aria-label={t('common.loadingMore')}
                    >
                      <Loader2 className="w-8 h-8 text-neutral-400 animate-spin" />
                    </div>
                  )}
                  {(selectedType === 'maintenance' ? maintenanceScroll : treatmentsScroll).error && (
                    <div className="bg-white border border-neutral-200 rounded-lg p-4 flex flex-col items-center gap-3">
                      <p className="text-sm text-neutral-600 text-center">
                        {t('common.failedToLoad')}
                      </p>
                      <button
                        type="button"
                        onClick={() => (selectedType === 'maintenance' ? maintenanceScroll : treatmentsScroll).retry()}
                        className="inline-flex items-center gap-2 px-4 py-2 bg-neutral-800 text-white text-sm font-medium rounded-lg hover:bg-neutral-900 active:bg-neutral-950 transition-colors touch-manipulation"
                      >
                        <RefreshCw className="w-4 h-4" />
                        {t('common.retry')}
                      </button>
                    </div>
                  )}
                </>
              )}

              {filteredReports.length === 0 &&
                (selectedType === 'morning'
                  ? !isLoadingMorning
                  : selectedType === 'maintenance'
                    ? !maintenanceScroll.loading
                    : selectedType === 'treatments'
                      ? !treatmentsScroll.loading
                      : true) && (
                  <div className="bg-white border border-neutral-200 rounded-lg p-8 text-center text-neutral-500">
                    {selectedType === 'morning'
                      ? loadError ?? t('reports.noReportsFound')
                      : t('reports.noReportsFound')}
                  </div>
                )}
            </div>
          </div>
        )}
      </div>
    </div>
  );
}
