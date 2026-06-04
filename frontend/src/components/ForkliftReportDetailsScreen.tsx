import { useEffect, useState } from 'react';
import { useLanguage } from '../context/LanguageContext';
import { getForkliftReportById } from '../services/forkliftReportsService';
import type { ForkliftReportDetails } from '../types/forkliftReports';
import { AppHeader } from './AppHeader';

interface ForkliftReportDetailsScreenProps {
  reportId: string;
  onBack: () => void;
}

type DetailsTab = 'all' | 'treatments' | 'faults';

export function ForkliftReportDetailsScreen({
  reportId,
  onBack,
}: ForkliftReportDetailsScreenProps) {
  const { t } = useLanguage();
  void onBack; // Back is handled by AppHeader (navigate(-1)).
  const [report, setReport] = useState<ForkliftReportDetails | null>(null);
  const [loading, setLoading] = useState(true);
  const [tab, setTab] = useState<DetailsTab>('all');

  useEffect(() => {
    async function load() {
      setLoading(true);
      try {
        const data = await getForkliftReportById(reportId);
        setReport(data);
      } catch (err) {
        console.error('Failed loading forklift report details', err);
        setReport(null);
      } finally {
        setLoading(false);
      }
    }
    load();
  }, [reportId]);

  return (
    <div className="min-h-screen bg-neutral-50 pb-24">
      <AppHeader title={t('details.title')} showBack={true} showHome={true} />

      <div className="p-4 space-y-4">
        {loading || !report ? (
          <div className="bg-white border border-neutral-200 rounded-lg p-8 text-center text-neutral-500">
            {t('forkliftReports.loading')}
          </div>
        ) : (
          <>
            {/* Tabs */}
            <div className="bg-white border border-neutral-200 rounded-lg p-2 flex justify-between text-sm text-start">
              <button
                type="button"
                className={`flex-1 px-3 py-1 rounded ${
                  tab === 'all'
                    ? 'bg-neutral-900 text-white'
                    : 'text-neutral-700'
                }`}
                onClick={() => setTab('all')}
              >
                {t('forkliftReports.all')}
              </button>
              <button
                type="button"
                className={`flex-1 px-3 py-1 rounded ${
                  tab === 'treatments'
                    ? 'bg-neutral-900 text-white'
                    : 'text-neutral-700'
                }`}
                onClick={() => setTab('treatments')}
              >
                {t('forkliftReports.treatments')}
              </button>
              <button
                type="button"
                className={`flex-1 px-3 py-1 rounded ${
                  tab === 'faults'
                    ? 'bg-neutral-900 text-white'
                    : 'text-neutral-700'
                }`}
                onClick={() => setTab('faults')}
              >
                {t('forkliftReports.faults')}
              </button>
            </div>

            {/* Treatments */}
            {(tab === 'all' || tab === 'treatments') && (
              <div className="bg-white border border-neutral-200 rounded-lg p-4 space-y-2">
                <h2 className="text-sm font-semibold text-neutral-900">
                  {t('forkliftReports.treatments')}
                </h2>
                {report.treatments.length === 0 ? (
                  <p className="text-sm text-neutral-500">
                    {t('forkliftReports.noReports')}
                  </p>
                ) : (
                  report.treatments.map((treatment) => (
                    <div
                      key={treatment.id}
                      className="border border-neutral-200 rounded-lg p-3 text-sm text-neutral-700 text-start"
                    >
                      <div className="font-medium text-neutral-900 mb-1">
                        {t('common.date')}: {treatment.date}
                      </div>
                      <div className="mb-1">
                        {t('common.description')}: {treatment.description}
                      </div>
                      <div>
                        {t('common.technician')}: {treatment.technician}
                      </div>
                    </div>
                  ))
                )}
              </div>
            )}

            {/* Faults */}
            {(tab === 'all' || tab === 'faults') && (
              <div className="bg-white border border-neutral-200 rounded-lg p-4 space-y-2">
                <h2 className="text-sm font-semibold text-neutral-900">
                  {t('forkliftReports.faults')}
                </h2>
                {report.faults.length === 0 ? (
                  <p className="text-sm text-neutral-500">
                    {t('forkliftReports.noReports')}
                  </p>
                ) : (
                  report.faults.map((fault) => (
                    <div
                      key={fault.id}
                      className="border border-neutral-200 rounded-lg p-3 text-sm text-neutral-700 text-start"
                    >
                      <div className="font-medium text-neutral-900 mb-1">
                        {t('forklift.faultType')}: {fault.faultType}
                      </div>
                      <div className="mb-1">
                        {t('common.description')}: {fault.description}
                      </div>
                      <div>
                        {t('forklift.repairCost')}: {fault.repairCost}
                      </div>
                    </div>
                  ))
                )}
              </div>
            )}
          </>
        )}
      </div>
    </div>
  );
}

