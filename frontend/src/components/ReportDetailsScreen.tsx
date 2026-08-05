import { useEffect, useMemo, useState } from 'react';
import { useLanguage } from '../context/LanguageContext';
import { CheckCircle2 } from 'lucide-react';
import { getMorningRoundById, getMorningRoundTemplate } from '../services/morningRoundsService';
import { getMorningRoundV2ReportById } from '../services/morningRoundV2Service';
import { getMaintenanceById } from '../services/maintenanceService';
import { getTreatmentById } from '../services/treatmentsService';
import { getMachines } from '../services/machinesService';
import { getMaintenanceTypes, type MaintenanceTypeDto } from '../services/maintenanceTypeService';
import { getHierarchy, type HierarchyArray } from '../services/hierarchyService';
import { formatDisplayDate, formatDisplayDateTime } from '../utils/formatDate';
import { formatTechnician } from '../utils/formatTechnician';
import { AppHeader } from './AppHeader';
import { MorningRoundV2GeneralChecklist } from './morningRound/MorningRoundV2GeneralChecklist';
import { MorningRoundV2HierarchyView } from './morningRound/MorningRoundV2HierarchyView';
import { ReportLocationHeader } from './reports/ReportLocationHeader';
import { filterVisibleMorningRoundV2Arrays } from '../morningRoundV2Config';
import { buildInitialGeneralChecklistState, loadMorningRoundV2GeneralChecklist } from '../morningRoundV2GeneralChecklist';
import { groupMorningRoundV2Arrays, insertMorningRoundV2ConveyorsArray } from '../morningRoundV2Grouping';
import {
  buildConveyorsHierarchyArray,
  buildInitialConveyorChecklistState,
  loadMorningRoundV2ConveyorChecklist,
} from '../morningRoundV2ConveyorChecklist';
import {
  arrayLocationLabel,
  checklistItemLocationLabel,
  machineLocationLabel,
  reportDateLocationLabel,
} from '../utils/reportLocationLabels';
import { resolveMaintenanceReportArrayLabel } from '../utils/resolveMaintenanceReportArray';
import type { MorningRoundDto, MorningRoundTemplateItemDto } from '../types/morningRound';
import type { MorningRoundV2Report } from '../types/morningRoundV2Report';
import type { MaintenanceEntryDto } from '../types/maintenance';
import type { TreatmentDto } from '../types/treatment';
import type { MachineDto } from '../services/machinesService';
import { safeImageSrc } from '../utils/safeUrl';

interface ReportDetailsScreenProps {
  reportId: string;
  reportType: 'morning' | 'morning-v2' | 'maintenance' | 'treatments';
  onBack: () => void;
}

interface ChecklistRow {
  id: string; // template item id
  translationKey: string;
  note?: string;
}

function normalizeMorningV2Status(status: string | null | undefined): 'ok' | 'fail' | null {
  if (status === 'ok' || status === 'fail') return status;
  return null;
}

export function ReportDetailsScreen({ reportId, reportType, onBack }: ReportDetailsScreenProps) {
  const { t, language } = useLanguage();

  const [morningReport, setMorningReport] = useState<MorningRoundDto | null>(null);
  const [morningChecklist, setMorningChecklist] = useState<ChecklistRow[]>([]);
  const [isLoadingMorning, setIsLoadingMorning] = useState(false);
  const [loadErrorMorning, setLoadErrorMorning] = useState<string | null>(null);
  const [morningV2Report, setMorningV2Report] = useState<MorningRoundV2Report | null>(null);
  const [expandedArrays, setExpandedArrays] = useState<Record<string, boolean>>({});
  const [v2FocusedArrayLabel, setV2FocusedArrayLabel] = useState<string | null>(null);
  const [v2FocusedMachineLabel, setV2FocusedMachineLabel] = useState<string | null>(null);
  const [v1FocusedItemLabel, setV1FocusedItemLabel] = useState<string | null>(null);
  const [isLoadingMorningV2, setIsLoadingMorningV2] = useState(false);
  const [loadErrorMorningV2, setLoadErrorMorningV2] = useState<string | null>(null);
  const [maintenanceReport, setMaintenanceReport] = useState<MaintenanceEntryDto | null>(null);
  const [isLoadingMaintenance, setIsLoadingMaintenance] = useState(false);
  const [loadErrorMaintenance, setLoadErrorMaintenance] = useState<string | null>(null);
  const [hierarchy, setHierarchy] = useState<HierarchyArray[]>([]);
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
    if (reportType !== 'morning-v2') return;

    let cancelled = false;
    const load = async () => {
      try {
        setIsLoadingMorningV2(true);
        setLoadErrorMorningV2(null);
        const data = await getMorningRoundV2ReportById(reportId);
        if (cancelled) return;

        setMorningV2Report(data);
        setExpandedArrays({});
        setV2FocusedArrayLabel(null);
        setV2FocusedMachineLabel(null);
      } catch (err) {
        console.error(err);
        if (!cancelled) {
          setLoadErrorMorningV2(t('messages.failedToLoadMorningRoundV2Report'));
        }
      } finally {
        if (!cancelled) {
          setIsLoadingMorningV2(false);
        }
      }
    };

    load();
    return () => {
      cancelled = true;
    };
  }, [reportId, reportType, t]);

  const visibleMorningV2Arrays = useMemo(
    () => (morningV2Report ? filterVisibleMorningRoundV2Arrays(morningV2Report.arrays) : []),
    [morningV2Report]
  );

  const morningV2GeneralChecklist = useMemo(
    () =>
      morningV2Report
        ? loadMorningRoundV2GeneralChecklist(morningV2Report.reportId)
        : buildInitialGeneralChecklistState(),
    [morningV2Report]
  );

  const morningV2ConveyorChecklist = useMemo(
    () =>
      morningV2Report
        ? loadMorningRoundV2ConveyorChecklist(morningV2Report.reportId)
        : buildInitialConveyorChecklistState(),
    [morningV2Report]
  );

  const morningV2HierarchyArrays = useMemo(() => {
    if (!morningV2Report) {
      return [];
    }

    const baseArrays = visibleMorningV2Arrays.map((array) => ({
      arrayId: array.arrayId,
      nameKey: array.nameKey,
      machines: array.machines.map((machine) => ({
        id: machine.machineId,
        nameKey: machine.nameKey,
        status: normalizeMorningV2Status(machine.status),
        notes: machine.notes ?? '',
      })),
    }));

    const grouped = groupMorningRoundV2Arrays(baseArrays).map((array) => ({
      arrayId: array.arrayId,
      nameKey: array.nameKey,
      machines: array.machines,
      children: array.children?.map((child) => ({
        arrayId: child.arrayId,
        nameKey: child.nameKey,
        machines: child.machines,
      })),
    }));

    const conveyors = buildConveyorsHierarchyArray(morningV2ConveyorChecklist);
    return insertMorningRoundV2ConveyorsArray(grouped, conveyors);
  }, [morningV2Report, visibleMorningV2Arrays, morningV2ConveyorChecklist]);

  const toggleMorningV2Array = (key: string) => {
    setExpandedArrays((prev) => ({ ...prev, [key]: !prev[key] }));
  };

  const handleMorningV2ArrayFocus = (_arrayKey: string, arrayNameKey: string) => {
    setV2FocusedArrayLabel(arrayLocationLabel(t, t(arrayNameKey)));
    setV2FocusedMachineLabel(null);
  };

  const handleMorningV2MachineFocus = (_machineId: string, machineNameKey: string) => {
    setV2FocusedMachineLabel(machineLocationLabel(t, t(machineNameKey)));
  };

  const handleHeaderBack = () => {
    if (reportType === 'morning-v2' && v2FocusedMachineLabel) {
      setV2FocusedMachineLabel(null);
      return;
    }

    if (reportType === 'morning-v2' && v2FocusedArrayLabel) {
      setV2FocusedArrayLabel(null);
      return;
    }

    if (reportType === 'morning' && v1FocusedItemLabel) {
      setV1FocusedItemLabel(null);
      return;
    }

    onBack();
  };

  useEffect(() => {
    if (reportType !== 'maintenance') return;

    let cancelled = false;
    const load = async () => {
      try {
        setIsLoadingMaintenance(true);
        setLoadErrorMaintenance(null);
        const [dto, hierarchyData] = await Promise.all([
          getMaintenanceById(reportId),
          getHierarchy(),
        ]);
        if (cancelled) return;
        setMaintenanceReport(dto);
        setHierarchy(Array.isArray(hierarchyData) ? hierarchyData : []);
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

  const maintenanceArrayLabel = useMemo(() => {
    if (!maintenanceReport?.machineId) return '';
    return resolveMaintenanceReportArrayLabel(
      { arrayId: maintenanceReport.arrayId, machineId: maintenanceReport.machineId },
      hierarchy,
      t
    );
  }, [hierarchy, maintenanceReport?.arrayId, maintenanceReport?.machineId, t]);

  const maintenanceV2Parts = useMemo(() => {
    if (maintenanceReport?.maintenanceTypeCode !== 'other') return null;
    const raw = maintenanceReport?.description ?? '';
    const parts = raw.split('\n');
    if (parts.length < 2) return null;
    const component = (parts[0] ?? '').trim();
    const details = parts.slice(1).join('\n').trim();
    if (!component) return null;
    return { component, details };
  }, [maintenanceReport?.description, maintenanceReport?.maintenanceTypeCode]);

  const locationSegments = useMemo(() => {
    const segments = [t('reports.title')];

    if (reportType === 'morning') {
      segments.push(t('reports.morningRound'), t('reports.morningRoundV1'));
      if (morningReport) {
        segments.push(reportDateLocationLabel(t, language, morningReport.reportDate));
      }
      if (v1FocusedItemLabel) {
        segments.push(v1FocusedItemLabel);
      }
      return segments;
    }

    if (reportType === 'morning-v2') {
      segments.push(t('reports.morningRound'), t('reports.morningRoundV2'));
      if (morningV2Report) {
        segments.push(reportDateLocationLabel(t, language, morningV2Report.date.slice(0, 10)));
      }
      if (v2FocusedArrayLabel) {
        segments.push(v2FocusedArrayLabel);
      }
      if (v2FocusedMachineLabel) {
        segments.push(v2FocusedMachineLabel);
      }
      return segments;
    }

    if (reportType === 'maintenance') {
      segments.push(t('reports.maintenanceLog'));
      if (maintenanceReport) {
        segments.push(reportDateLocationLabel(t, language, maintenanceReport.date));
        const machineName = machine ? t(machine.name) : maintenanceReport.machineId;
        segments.push(machineLocationLabel(t, machineName));
      }
      return segments;
    }

    if (reportType === 'treatments') {
      segments.push(t('treatments.title'));
      if (treatmentReport) {
        segments.push(reportDateLocationLabel(t, language, treatmentReport.treatmentDate));
        const machineName = treatmentMachine
          ? t(treatmentMachine.name)
          : treatmentReport.machineName
            ? t(treatmentReport.machineName)
            : t('common.notProvided');
        segments.push(machineLocationLabel(t, machineName));
      }
      return segments;
    }

    return segments;
  }, [
    reportType,
    t,
    language,
    morningReport,
    morningV2Report,
    v1FocusedItemLabel,
    v2FocusedArrayLabel,
    v2FocusedMachineLabel,
    maintenanceReport,
    machine,
    treatmentReport,
    treatmentMachine,
  ]);

  return (
    <div className="min-h-screen bg-neutral-50 pb-6">
      <AppHeader
        title={t('details.title')}
        showBack={true}
        showHome={true}
        onBack={handleHeaderBack}
      />

      <div className="p-4 space-y-4">
        <ReportLocationHeader segments={locationSegments} />

        {reportType === 'morning' ? (
          /* Morning Round v1 Report Details */
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
                    className="pb-3 border-b border-neutral-200 last:border-0 last:pb-0 cursor-pointer"
                    onClick={() =>
                      setV1FocusedItemLabel(
                        checklistItemLocationLabel(t, t(item.translationKey))
                      )
                    }
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
        ) : reportType === 'morning-v2' ? (
          /* Morning Round v2 Report Details */
          <>
            <div className="bg-white border border-neutral-200 rounded-lg p-4">
              <h2 className="text-sm font-semibold text-neutral-900 mb-3">
                {t('details.reportInfo')}
              </h2>
              {isLoadingMorningV2 && (
                <div className="text-sm text-neutral-500">{t('common.loading')}</div>
              )}
              {loadErrorMorningV2 && !isLoadingMorningV2 && (
                <div className="text-sm text-red-600">{loadErrorMorningV2}</div>
              )}
              {morningV2Report && !isLoadingMorningV2 && !loadErrorMorningV2 && (
                <div className="space-y-2 text-sm">
                  <div className="flex justify-between">
                    <span className="text-neutral-600">{t('morning.date')}:</span>
                    <span className="font-medium text-neutral-900">
                      {formatDisplayDate(language, morningV2Report.date.slice(0, 10), 'long')}
                    </span>
                  </div>
                  <div className="flex justify-between">
                    <span className="text-neutral-600">{t('morning.performedBy')}:</span>
                    <span className="font-medium text-neutral-900">
                      {morningV2Report.submittedBy.fullName}
                    </span>
                  </div>
                  <div className="flex justify-between">
                    <span className="text-neutral-600">{t('details.submittedOn')}:</span>
                    <span className="font-medium text-neutral-900">
                      {formatDisplayDateTime(language, morningV2Report.submittedAt)}
                    </span>
                  </div>
                </div>
              )}
            </div>

            {morningV2Report && !isLoadingMorningV2 && !loadErrorMorningV2 && (
              <>
                <MorningRoundV2HierarchyView
                  arrays={morningV2HierarchyArrays}
                  expandedArrays={expandedArrays}
                  onToggleArray={toggleMorningV2Array}
                  t={t}
                  readOnly
                  onArrayFocus={handleMorningV2ArrayFocus}
                  onMachineFocus={handleMorningV2MachineFocus}
                />

                <MorningRoundV2GeneralChecklist
                  items={morningV2GeneralChecklist}
                  t={t}
                  readOnly
                />
              </>
            )}
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
                    {maintenanceV2Parts ? (
                      <>
                        <div>
                          <span className="text-neutral-600">{t('reports.maintenanceFilters.array')}: </span>
                          <span className="font-medium text-neutral-900">{maintenanceArrayLabel}</span>
                        </div>
                        <div>
                          <span className="text-neutral-600">{t('log.machine')}: </span>
                          <span className="font-medium text-neutral-900">
                            {machine ? t(machine.name) : maintenanceReport.machineId}
                          </span>
                        </div>
                        <div>
                          <span className="text-neutral-600">{t('reports.maintenanceFilters.component')}: </span>
                          <span className="font-medium text-neutral-900">{maintenanceV2Parts.component}</span>
                        </div>
                        {maintenanceV2Parts.details.length > 0 && (
                          <div>
                            <span className="text-neutral-600">{t('log.fault')}: </span>
                            <span className="text-neutral-900">{maintenanceV2Parts.details}</span>
                          </div>
                        )}
                      </>
                    ) : (
                      <>
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
                        {(maintenanceReport.description?.trim() ?? '').length > 0 && (
                          <div>
                            <span className="text-neutral-600">{t('log.fault')}: </span>
                            <span className="text-neutral-900">{maintenanceReport.description}</span>
                          </div>
                        )}
                      </>
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
