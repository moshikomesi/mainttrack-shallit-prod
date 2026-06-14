import { useEffect, useState } from 'react';
import { useLanguage } from '../context/LanguageContext';
import { CheckCircle2 } from 'lucide-react';
import { getMorningRoundById, getMorningRoundTemplate } from '../services/morningRoundsService';
import { getMaintenanceById } from '../services/maintenanceService';
import { getTreatmentById } from '../services/treatmentsService';
import { getMachines } from '../services/machinesService';
import { getMaintenanceTypes, type MaintenanceTypeDto } from '../services/maintenanceTypeService';
import { formatDisplayDate, formatDisplayDateTime } from '../utils/formatDate';
import { formatTechnician } from '../utils/formatTechnician';
import { AppHeader } from './AppHeader';
import type { MorningRoundDto, MorningRoundTemplateItemDto } from '../types/morningRound';
import type { MaintenanceEntryDto } from '../types/maintenance';
import type { TreatmentDto } from '../types/treatment';
import type { MachineDto } from '../services/machinesService';
import { safeImageSrc } from '../utils/safeUrl';

interface ReportDetailsScreenProps {
  reportId: string;
  reportType: 'morning' | 'maintenance' | 'treatments';
  onBack: () => void;
}

interface ChecklistRow {
  id: string; // template item id
  translationKey: string;
  note?: string;
}

export function ReportDetailsScreen({ reportId, reportType }: ReportDetailsScreenProps) {
  const { t, language } = useLanguage();

  const [morningReport, setMorningReport] = useState<MorningRoundDto | null>(null);
  const [morningChecklist, setMorningChecklist] = useState<ChecklistRow[]>([]);
  const [isLoadingMorning, setIsLoadingMorning] = useState(false);
  const [loadErrorMorning, setLoadErrorMorning] = useState<string | null>(null);
  const [maintenanceReport, setMaintenanceReport] = useState<MaintenanceEntryDto | null>(null);
  const [isLoadingMaintenance, setIsLoadingMaintenance] = useState(false);
  const [loadErrorMaintenance, setLoadErrorMaintenance] = useState<string | null>(null);
  const [treatmentReport, setTreatmentReport] = useState<TreatmentDto | null>(null);
  const [isLoadingTreatment, setIsLoadingTreatment] = useState(false);
  const [loadErrorTreatment, setLoadErrorTreatment] = useState<string | null>(null);
  const [machines, setMachines] = useState<MachineDto[]>([]);
  const [maintenanceTypes, setMaintenanceTypes] = useState<MaintenanceTypeDto[]>([]);

  useEffect(() => {
    if (reportType !== 'morning') return;

    let cancelled = false;
    const load = async () => {
      try {
        setIsLoadingMorning(true);
        setLoadErrorMorning(null);
        const [template, dto] = await Promise.all([
          getMorningRoundTemplate(),
          getMorningRoundById(reportId),
        ]);
        if (cancelled) return;
        setMorningReport(dto);

        const ordered: MorningRoundTemplateItemDto[] = [...template].sort(
          (a, b) => a.order - b.order
        );
        const rows: ChecklistRow[] = ordered.map((item) => {
          const note = dto.notes?.[item.id];
          return {
            id: item.id,
            translationKey: item.translationKey,
            note,
          };
        });
        setMorningChecklist(rows);
      } catch (err) {
        console.error(err);
        if (!cancelled) {
          setLoadErrorMorning(t('messages.failedToLoadMorningRoundReport'));
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
  }, [reportId, reportType, t]);

  useEffect(() => {
    if (reportType !== 'maintenance') return;

    let cancelled = false;
    const load = async () => {
      try {
        setIsLoadingMaintenance(true);
        setLoadErrorMaintenance(null);
        const dto = await getMaintenanceById(reportId);
        if (cancelled) return;
        setMaintenanceReport(dto);
      } catch (err) {
        console.error(err);
        if (!cancelled) {
          setLoadErrorMaintenance(t('messages.failedToLoadMaintenanceReport'));
        }
      } finally {
        if (!cancelled) {
          setIsLoadingMaintenance(false);
        }
      }
    };

    load();
    return () => {
      cancelled = true;
    };
  }, [reportId, reportType]);

  useEffect(() => {
    if (reportType !== 'treatments') return;

    let cancelled = false;
    const load = async () => {
      try {
        setIsLoadingTreatment(true);
        setLoadErrorTreatment(null);
        const dto = await getTreatmentById(reportId);
        if (cancelled) return;
        setTreatmentReport(dto);
      } catch (err) {
        console.error(err);
        if (!cancelled) {
          setLoadErrorTreatment(t('messages.failedToLoadTreatmentReport'));
        }
      } finally {
        if (!cancelled) {
          setIsLoadingTreatment(false);
        }
      }
    };

    load();
    return () => {
      cancelled = true;
    };
  }, [reportId, reportType]);

  useEffect(() => {
    const loadLookupData = async () => {
      try {
        const [machinesData, maintenanceTypesData] = await Promise.all([
          getMachines(),
          getMaintenanceTypes(),
        ]);
        setMachines(Array.isArray(machinesData) ? machinesData : []);
        setMaintenanceTypes(Array.isArray(maintenanceTypesData) ? maintenanceTypesData : []);
      } catch (err) {
        console.error(err);
      }
    };

    loadLookupData();
  }, []);

  const machine = machines.find((m) => m.id === maintenanceReport?.machineId);
  const treatmentMachine = machines.find((m) => m.id === treatmentReport?.machineId);
  const treatmentMaintenanceTypeCode =
    treatmentReport?.maintenanceTypeName ??
    maintenanceTypes.find((mt) => mt.id === treatmentReport?.maintenanceTypeId)?.code;

  return (
    <div className="min-h-screen bg-neutral-50 pb-6">
      <AppHeader title={t('details.title')} showBack={true} showHome={true} />

      <div className="p-4 space-y-4">
        {reportType === 'morning' ? (
          /* Morning Round Report Details */
          <>
            {/* Report Info */}
            <div className="bg-white border border-neutral-200 rounded-lg p-4">
              <h2 className="text-sm font-semibold text-neutral-900 mb-3">
                {t('details.reportInfo')}
              </h2>
              {isLoadingMorning && (
                <div className="text-sm text-neutral-500">{t('common.loading')}</div>
              )}
              {loadErrorMorning && !isLoadingMorning && (
                <div className="text-sm text-red-600">{loadErrorMorning}</div>
              )}
              {morningReport && !isLoadingMorning && !loadErrorMorning && (
                <div className="space-y-2 text-sm">
                  <div className="flex justify-between">
                    <span className="text-neutral-600">{t('morning.date')}:</span>
                    <span className="font-medium text-neutral-900">
                      {formatDisplayDate(language, morningReport.reportDate, 'long')}
                    </span>
                  </div>
                  <div className="flex justify-between">
                    <span className="text-neutral-600">{t('morning.performedBy')}:</span>
                    <span className="font-medium text-neutral-900">
                      {morningReport.performedByName}
                    </span>
                  </div>
                  <div className="flex justify-between">
                    <span className="text-neutral-600">{t('details.submittedOn')}:</span>
                    <span className="font-medium text-neutral-900">
                      {formatDisplayDateTime(language, morningReport.performedAt)}
                    </span>
                  </div>
                </div>
              )}
            </div>

            {/* Checklist Results */}
            <div className="bg-white border border-neutral-200 rounded-lg p-4">
              <h2 className="text-sm font-semibold text-neutral-900 mb-3">
                {t('morning.checklist')}
              </h2>
              <div className="space-y-3">
                {morningChecklist.map((item, index) => (
                  <div
                    key={item.id}
                    className="pb-3 border-b border-neutral-200 last:border-0 last:pb-0"
                  >
                    <div className="flex items-start gap-3 mb-1">
                      <CheckCircle2
                        className={`w-5 h-5 flex-shrink-0 ${
                          item.note ? 'text-teal-600' : 'text-neutral-300'
                        }`}
                      />
                      <div className="flex-1">
                        <span className="text-sm text-neutral-900">
                          {index + 1}. {t(item.translationKey)}
                        </span>
                        {item.note && (
                          <div className="mt-1 text-sm text-neutral-600 bg-amber-50 border border-amber-200 rounded px-2 py-1">
                            {item.note}
                          </div>
                        )}
                      </div>
                    </div>
                  </div>
                ))}
              </div>
            </div>
          </>
        ) : reportType === 'maintenance' ? (
          /* Maintenance Log Report Details */
          <>
            {/* Report Info */}
            <div className="bg-white border border-neutral-200 rounded-lg p-4">
              <h2 className="text-sm font-semibold text-neutral-900 mb-3">
                {t('details.reportInfo')}
              </h2>
              {isLoadingMaintenance && (
                <div className="text-sm text-neutral-500">{t('common.loading')}</div>
              )}
              {loadErrorMaintenance && !isLoadingMaintenance && (
                <div className="text-sm text-red-600">{loadErrorMaintenance}</div>
              )}
              {maintenanceReport && !isLoadingMaintenance && !loadErrorMaintenance && (
                <div className="space-y-2 text-sm">
                  <div className="flex justify-between">
                    <span className="text-neutral-600">{t('log.date')}:</span>
                    <span className="font-medium text-neutral-900">
                      {formatDisplayDate(language, maintenanceReport.date, 'long')}
                    </span>
                  </div>
                  <div className="flex justify-between">
                    <span className="text-neutral-600">{t('reports.submittedBy')}:</span>
                    <span className="font-medium text-neutral-900">
                      {maintenanceReport.employeeName}
                    </span>
                  </div>
                  <div className="flex justify-between">
                    <span className="text-neutral-600">{t('details.submittedOn')}:</span>
                    <span className="font-medium text-neutral-900">
                      {formatDisplayDateTime(language, maintenanceReport.createdAt)}
                    </span>
                  </div>
                </div>
              )}
            </div>

            {/* Maintenance Entry Details */}
            {maintenanceReport && !isLoadingMaintenance && !loadErrorMaintenance && (
              <>
                <div className="bg-white border border-neutral-200 rounded-lg p-4 space-y-2">
                  <div className="space-y-1.5 text-sm">
                    <div>
                      <span className="text-neutral-600">{t('log.machine')}: </span>
                      <span className="font-medium text-neutral-900">
                        {machine ? t(machine.name) : maintenanceReport.machineId}
                      </span>
                    </div>
                    {maintenanceReport.maintenanceTypeCode && (
                      <div>
                        <span className="text-neutral-600">{t('log.maintenanceTypeLabel')}: </span>
                        <span className="font-medium text-neutral-900">
                          {t(`maintenanceType.${maintenanceReport.maintenanceTypeCode}`)}
                        </span>
                      </div>
                    )}
                    {maintenanceReport.description.trim().length > 0 && (
                      <div>
                        <span className="text-neutral-600">{t('log.fault')}: </span>
                        <span className="text-neutral-900">{maintenanceReport.description}</span>
                      </div>
                    )}
                    {maintenanceReport.sparePartsUsed && (
                      <div>
                        <span className="text-neutral-600">{t('log.spareParts')}: </span>
                        <span className="text-neutral-900">
                          {maintenanceReport.sparePartsUsed}
                        </span>
                      </div>
                    )}
                    <div>
                      <span className="text-neutral-600">{t('reports.submittedBy')}: </span>
                      <span className="font-medium text-neutral-900">
                        {maintenanceReport.employeeName}
                      </span>
                    </div>
                    <div>
                      <span className="text-neutral-600">{t('log.clearance')}: </span>
                      <span className="text-neutral-900">
                        {maintenanceReport.isSafeToOperate
                          ? t('common.complete')
                          : t('common.pending')}
                      </span>
                    </div>
                    {(() => {
                      const imageSrc = safeImageSrc(maintenanceReport.imageUrl);
                      if (!imageSrc) return null;
                      return (
                        <div className="mt-3">
                          <img
                            src={imageSrc}
                            alt={t('log.maintenancePhotoAlt')}
                            className="w-full rounded-lg border border-neutral-300"
                          />
                        </div>
                      );
                    })()}
                  </div>
                </div>

                {/* Declaration */}
                <div className="bg-neutral-100 border border-neutral-300 rounded-lg p-4">
                  <p className="text-sm text-neutral-900 font-medium mb-2">
                    {t('log.declaration')}
                  </p>
                  <p className="text-sm text-neutral-700">
                    <span className="font-semibold">{t('log.name')}: </span>
                    {maintenanceReport.employeeName}
                  </p>
                </div>
              </>
            )}
          </>
        ) : (
          /* Treatment Report Details */
          <>
            {/* Report Info */}
            <div className="bg-white border border-neutral-200 rounded-lg p-4">
              <h2 className="text-sm font-semibold text-neutral-900 mb-3">
                {t('details.reportInfo')}
              </h2>
              {isLoadingTreatment && (
                <div className="text-sm text-neutral-500">{t('common.loading')}</div>
              )}
              {loadErrorTreatment && !isLoadingTreatment && (
                <div className="text-sm text-red-600">{loadErrorTreatment}</div>
              )}
              {treatmentReport && !isLoadingTreatment && !loadErrorTreatment && (
                <div className="space-y-2 text-sm">
                  <div className="flex justify-between">
                    <span className="text-neutral-600">{t('common.date')}:</span>
                    <span className="font-medium text-neutral-900">
                      {formatDisplayDate(language, treatmentReport.treatmentDate, 'long')}
                    </span>
                  </div>
                  <div className="flex justify-between">
                    <span className="text-neutral-600">{t('log.machine')}:</span>
                    <span className="font-medium text-neutral-900">
                      {treatmentMachine
                        ? t(treatmentMachine.name)
                        : treatmentReport.machineName
                          ? t(treatmentReport.machineName)
                          : t('common.notProvided')}
                    </span>
                  </div>
                  <div className="flex justify-between">
                    <span className="text-neutral-600">{t('treatment.type')}:</span>
                    <span className="font-medium text-neutral-900">
                      {treatmentMaintenanceTypeCode
                        ? t(`maintenanceType.${treatmentMaintenanceTypeCode}`)
                        : t('common.notProvided')}
                    </span>
                  </div>
                  <div className="flex justify-between">
                    <span className="text-neutral-600">{t('common.technician')}:</span>
                    <span className="font-medium text-neutral-900">
                      {treatmentReport.technician
                        ? formatTechnician(t, treatmentReport.technician)
                        : t('common.notProvided')}
                    </span>
                  </div>
                </div>
              )}
            </div>

            {/* Treatment Details */}
            {treatmentReport && !isLoadingTreatment && !loadErrorTreatment && (
              <div className="bg-white border border-neutral-200 rounded-lg p-4 space-y-2">
                <div className="space-y-1.5 text-sm">
                  <div>
                    <span className="text-neutral-600">{t('treatments.description')}: </span>
                    <span className="text-neutral-900">{treatmentReport.description}</span>
                  </div>
                  {treatmentReport.nextDueDate && (
                    <div>
                      <span className="text-neutral-600">{t('treatments.nextScheduled')}: </span>
                      <span className="font-medium text-neutral-900">
                        {formatDisplayDate(language, treatmentReport.nextDueDate, 'long')}
                      </span>
                    </div>
                  )}
                </div>
              </div>
            )}
          </>
        )}
      </div>
    </div>
  );
}
