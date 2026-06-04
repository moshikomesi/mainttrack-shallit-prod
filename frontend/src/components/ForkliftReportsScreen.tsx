import { useEffect, useState } from 'react';
import { useLanguage } from '../context/LanguageContext';
import { getForkliftReportsOverview } from '../services/forkliftReportsService';
import { getForklifts } from '../services/forkliftsService';
import type { Forklift } from '../types/forklift';
import type { ForkliftReportListItem, ExpiringInspection } from '../types/forkliftReports';
import { ForkliftReportDetailsScreen } from './ForkliftReportDetailsScreen';
import { AppHeader } from './AppHeader';

interface ForkliftReportsScreenProps {
  onBack: () => void;
}

export function ForkliftReportsScreen({ onBack }: ForkliftReportsScreenProps) {
  const { t, isRTL } = useLanguage();
  void onBack; // Back is handled by AppHeader (navigate(-1)).
  const [reports, setReports] = useState<ForkliftReportListItem[]>([]);
  const [expiringInspections, setExpiringInspections] = useState<ExpiringInspection[]>([]);
  const [loading, setLoading] = useState(true);
  const [forkliftNumber, _setForkliftNumber] = useState('');
  const [forklifts, setForklifts] = useState<Forklift[]>([]);
  const [selectedForkliftId, setSelectedForkliftId] = useState<string | undefined>();
  const [fromDate, setFromDate] = useState('');
  const [toDate, setToDate] = useState('');
  const [type, _setType] = useState<'all' | 'treatments' | 'faults'>('all');
  const [inspectionExpiringSoon, setInspectionExpiringSoon] = useState(false);
  const [search, setSearch] = useState('');
  const [debouncedSearch, setDebouncedSearch] = useState('');
  const [selectedReportId, setSelectedReportId] = useState<string | null>(null);

  useEffect(() => {
    const handler = setTimeout(() => {
      setDebouncedSearch(search);
    }, 500);

    return () => {
      clearTimeout(handler);
    };
  }, [search]);

  useEffect(() => {
    async function loadForklifts() {
      try {
        const data = await getForklifts();
        setForklifts(data);
      } catch (err) {
        console.error('Failed loading forklifts', err);
      }
    }
    loadForklifts();
  }, []);

  useEffect(() => {
    async function load() {
      setLoading(true);
      try {
        const data = await getForkliftReportsOverview({
          forkliftNumber: forkliftNumber || undefined,
          fromDate: fromDate || undefined,
          toDate: toDate || undefined,
          type: type === 'all' ? undefined : type,
          inspectionExpiringSoon,
          search: debouncedSearch || undefined,
        });
        setReports(data.reports);
        setExpiringInspections(data.expiringInspections);
      } catch (err) {
        console.error('Failed loading forklift reports', err);
        setReports([]);
        setExpiringInspections([]);
      } finally {
        setLoading(false);
      }
    }
    load();
  }, [forkliftNumber, fromDate, toDate, type, inspectionExpiringSoon, debouncedSearch]);

  const formatDate = (dateStr: string) => {
    const date = new Date(dateStr);
    const locale = isRTL ? 'he-IL' : 'en-US';
    return date.toLocaleDateString(locale, {
      weekday: 'short',
      year: 'numeric',
      month: 'short',
      day: 'numeric',
    });
  };

  if (selectedReportId) {
    return (
      <ForkliftReportDetailsScreen
        reportId={selectedReportId}
        onBack={() => setSelectedReportId(null)}
      />
    );
  }

  const filteredReports = selectedForkliftId
    ? reports.filter((report) => report.forkliftId === selectedForkliftId)
    : reports;

  return (
    <div className="min-h-screen bg-neutral-50 pb-24">
      <AppHeader title={t('forkliftReports.title')} showBack={true} showHome={true} />

      <div className="p-4 space-y-4">
        {loading ? (
          <div className="bg-white border border-neutral-200 rounded-lg p-8 text-center text-neutral-500">
            {t('forkliftReports.loading')}
          </div>
        ) : (
          <>
            {/* 1. Expiring inspections warning */}
            {expiringInspections.length > 0 && (
              <div className="bg-white border border-neutral-200 rounded-lg p-4 space-y-3">
                <h2 className="text-sm font-semibold text-neutral-900 flex items-center gap-2">
                  <span className="text-amber-600">⚠</span>
                  {t('forkliftReports.expiringInspection')}
                </h2>
                <ul className="space-y-2">
                  {expiringInspections.map((item) => (
                    <li
                      key={item.forkliftId}
                      className="text-sm text-neutral-700"
                    >
                      {item.licenseNumber} – {item.daysRemaining} {t('forkliftReports.days')}
                    </li>
                  ))}
                </ul>
              </div>
            )}

            {/* Filters */}
            <div className="bg-white border border-neutral-200 rounded-lg p-4 space-y-3">
              <select
                value={selectedForkliftId ?? ''}
                onChange={(e) => setSelectedForkliftId(e.target.value || undefined)}
                className="w-full px-3 py-2 bg-white border border-neutral-300 rounded-lg text-neutral-900 focus:outline-none focus:ring-2 focus:ring-neutral-800"
              >
                <option value="">{t('forkliftReports.filterForklift')}</option>
                {forklifts.map((forklift) => (
                  <option key={forklift.id} value={forklift.id}>
                    {forklift.licenseNumber}
                  </option>
                ))}
              </select>
              <input
                placeholder={t('forkliftReports.description')}
                value={search}
                onChange={(e) => setSearch(e.target.value)}
                className="w-full border border-neutral-200 rounded-lg p-2 text-neutral-900 focus:outline-none focus:ring-2 focus:ring-neutral-800"
              />
              <div className="grid grid-cols-2 gap-2">
                <input
                  type="date"
                  value={fromDate}
                  onChange={(e) => setFromDate(e.target.value)}
                  className="w-full px-3 py-2 bg-white border border-neutral-300 rounded-lg text-neutral-900 focus:outline-none focus:ring-2 focus:ring-neutral-800"
                />
                <input
                  type="date"
                  value={toDate}
                  onChange={(e) => setToDate(e.target.value)}
                  className="w-full px-3 py-2 bg-white border border-neutral-300 rounded-lg text-neutral-900 focus:outline-none focus:ring-2 focus:ring-neutral-800"
                />
              </div>
              <label className="flex items-center gap-2 text-sm text-neutral-700 cursor-pointer">
                <input
                  type="checkbox"
                  checked={inspectionExpiringSoon}
                  onChange={(e) => setInspectionExpiringSoon(e.target.checked)}
                  className="rounded border-neutral-300"
                />
                {t('forkliftReports.expiringInspection')}
              </label>
            </div>

            {/* 2. Reports list */}
            <div className="space-y-3">
              <h2 className="text-sm font-medium text-neutral-600">
                {t('forkliftReports.reports')}
              </h2>
              {filteredReports.length === 0 ? (
                <div className="bg-white border border-neutral-200 rounded-lg p-8 text-center text-neutral-500">
                  {t('forkliftReports.noReports')}
                </div>
              ) : (
                filteredReports.map((report) => (
                  <div
                    key={report.reportId}
                    className="w-full bg-white border border-neutral-200 rounded-lg p-4 text-start"
                    onClick={() => setSelectedReportId(report.reportId)}
                  >
                    <div className="text-sm font-semibold text-neutral-900 mb-2">
                      {formatDate(report.reportDate)}
                    </div>
                    <div className="text-sm text-neutral-600 mb-1">
                      {t('forkliftReports.forklift')}: <span className="font-medium text-neutral-900">{report.forkliftNumber}</span>
                    </div>
                    <div className="text-sm text-neutral-600 mb-1">
                      {t('forkliftReports.date')}: <span className="font-medium text-neutral-900">{report.reportDate}</span>
                    </div>
                    <div className="text-sm text-neutral-600 mb-1">
                      {t('forkliftReports.treatments')}: <span className="font-medium text-neutral-900">{report.treatmentsCount}</span>
                    </div>
                    <div className="text-sm text-neutral-600">
                      {t('forkliftReports.faults')}: <span className="font-medium text-neutral-900">{report.faultsCount}</span>
                    </div>
                  </div>
                ))
              )}
            </div>
          </>
        )}
      </div>
    </div>
  );
}
