import { useEffect, useMemo, useRef, useState, type CSSProperties } from 'react';
import { useLanguage } from '../context/LanguageContext';
import { CheckCircle2, ChevronLeft, ChevronRight, X } from 'lucide-react';
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
import {
  groupMorningRoundV2Arrays,
  insertMorningRoundV2ConveyorsArray,
  MORNING_ROUND_V2_CONVEYORS_KEY,
} from '../morningRoundV2Grouping';
import {
  buildConveyorsHierarchyArray,
  buildInitialConveyorChecklistState,
  loadMorningRoundV2ConveyorChecklist,
} from '../morningRoundV2ConveyorChecklist';
import { normalizeMorningRoundV2Status } from './MorningRoundV2MachineStatus';
import {
  arrayLocationLabel,
  checklistItemLocationLabel,
  machineLocationLabel,
  reportDateLocationLabel,
} from '../utils/reportLocationLabels';
import { resolveMaintenanceReportArrayLabel } from '../utils/resolveMaintenanceReportArray';
import { resolveCatalogDisplayValue } from '../utils/resolveCatalogDisplayValue';
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

function findReportMachineStatus(
  report: MorningRoundV2Report,
  nameKey: string
): 'ok' | 'fail' | null {
  for (const array of report.arrays) {
    for (const machine of array.machines) {
      if (machine.nameKey === nameKey) {
        return normalizeMorningRoundV2Status(machine.status);
      }
    }
  }
  return null;
}

/** Inline styles: project CSS is a curated Tailwind subset without absolute/overlay utilities. */
const imageFrameStyle: CSSProperties = { position: 'relative' };

const carouselNavButtonStyle = (side: 'left' | 'right'): CSSProperties => ({
  position: 'absolute',
  top: '50%',
  [side]: 8,
  transform: 'translateY(-50%)',
  zIndex: 2,
  padding: 8,
  border: 'none',
  borderRadius: 9999,
  backgroundColor: 'rgba(0,0,0,0.45)',
  color: '#ffffff',
  cursor: 'pointer',
  display: 'inline-flex',
  alignItems: 'center',
  justifyContent: 'center',
});

const carouselCounterStyle: CSSProperties = {
  position: 'absolute',
  bottom: 8,
  left: '50%',
  transform: 'translateX(-50%)',
  zIndex: 2,
  padding: '2px 8px',
  borderRadius: 9999,
  backgroundColor: 'rgba(0,0,0,0.5)',
  color: '#ffffff',
  fontSize: 12,
};

const viewerOverlayStyle: CSSProperties = {
  position: 'fixed',
  inset: 0,
  zIndex: 100,
  backgroundColor: 'rgba(0,0,0,0.95)',
  display: 'flex',
  alignItems: 'center',
  justifyContent: 'center',
};

const viewerCloseButtonStyle: CSSProperties = {
  position: 'absolute',
  top: 16,
  right: 16,
  zIndex: 10,
  padding: 8,
  border: 'none',
  borderRadius: 9999,
  backgroundColor: 'rgba(255,255,255,0.15)',
  color: '#ffffff',
  cursor: 'pointer',
  display: 'inline-flex',
  alignItems: 'center',
  justifyContent: 'center',
};

const viewerNavButtonStyle = (side: 'left' | 'right'): CSSProperties => ({
  position: 'absolute',
  top: '50%',
  [side]: 12,
  transform: 'translateY(-50%)',
  zIndex: 10,
  padding: 8,
  border: 'none',
  borderRadius: 9999,
  backgroundColor: 'rgba(255,255,255,0.15)',
  color: '#ffffff',
  cursor: 'pointer',
  display: 'inline-flex',
  alignItems: 'center',
  justifyContent: 'center',
});

const viewerCounterStyle: CSSProperties = {
  position: 'absolute',
  bottom: 16,
  left: '50%',
  transform: 'translateX(-50%)',
  color: '#ffffff',
  fontSize: 14,
};

const viewerImageStyle: CSSProperties = {
  maxWidth: '95vw',
  maxHeight: '88vh',
  objectFit: 'contain',
};

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
  const [viewerImageIndex, setViewerImageIndex] = useState<number | null>(null);
  const [carouselIndex, setCarouselIndex] = useState(0);
  const viewerTriggerRef = useRef<HTMLElement | null>(null);
  const touchStartXRef = useRef<number | null>(null);

  const closeImageViewer = () => {
    setViewerImageIndex(null);
    const trigger = viewerTriggerRef.current;
    viewerTriggerRef.current = null;
    queueMicrotask(() => trigger?.focus());
  };

  const openImageViewer = (index: number, trigger: HTMLElement) => {
    viewerTriggerRef.current = trigger;
    setViewerImageIndex(index);
  };

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

  const morningV2GeneralChecklist = useMemo(() => {
    if (!morningV2Report) {
      return buildInitialGeneralChecklistState();
    }

    // Prefer route reportId so viewer matches the opened report even if DTO id casing differs.
    const stored = loadMorningRoundV2GeneralChecklist(reportId || morningV2Report.reportId);
    const metalDetectorStatus = findReportMachineStatus(
      morningV2Report,
      'machine.packing.metalDetector'
    );

    return stored.map((item) => {
      if (item.status) return item;
      // Legacy general item "metal detector check" may only exist on the packing machine in the report.
      if (item.id === 'general-check-35' && metalDetectorStatus) {
        return { ...item, status: metalDetectorStatus };
      }
      return item;
    });
  }, [morningV2Report, reportId]);

  const morningV2ConveyorChecklist = useMemo(() => {
    if (!morningV2Report) {
      return buildInitialConveyorChecklistState();
    }

    const stored = loadMorningRoundV2ConveyorChecklist(reportId || morningV2Report.reportId);
    // Presentation conveyors checklist mirrors machine.conveyors.general when present on the report.
    const reportConveyorStatus = findReportMachineStatus(
      morningV2Report,
      'machine.conveyors.general'
    );

    return stored.map((item) => {
      if (item.status) return item;
      if (item.id === 'conveyor-check-18' && reportConveyorStatus) {
        return { ...item, status: reportConveyorStatus };
      }
      return item;
    });
  }, [morningV2Report, reportId]);

  const morningV2HierarchyArrays = useMemo(() => {
    if (!morningV2Report) {
      return [];
    }

    // Avoid a duplicate "Conveyors" accordion when the report also contains array.conveyors.
    const baseArrays = visibleMorningV2Arrays
      .filter((array) => array.nameKey !== MORNING_ROUND_V2_CONVEYORS_KEY)
      .map((array) => ({
        arrayId: array.arrayId,
        nameKey: array.nameKey,
        machines: array.machines.map((machine) => ({
          id: machine.machineId,
          nameKey: machine.nameKey,
          status: normalizeMorningRoundV2Status(machine.status),
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
    // First line is the component (catalog key, legacy localized label, or free-text).
    // Remaining lines are optional fault details — a single-line description is valid
    // when only a catalog component was stored (no fault text).
    const component = (parts[0] ?? '').trim();
    if (!component) return null;
    const details = parts.slice(1).join('\n').trim();
    return { component, details };
  }, [maintenanceReport?.description, maintenanceReport?.maintenanceTypeCode]);

  /** At most 1 primary + 2 additional = 3 images total. */
  const maintenanceImages = useMemo(() => {
    if (!maintenanceReport) return [];
    return [maintenanceReport.imageUrl, ...(maintenanceReport.additionalImages ?? []).map((image) => image.imageUrl)]
      .map((url) => safeImageSrc(url))
      .filter((url): url is string => Boolean(url))
      .slice(0, 3);
  }, [maintenanceReport]);

  const showImageCarousel = maintenanceImages.length > 1;

  const goToPreviousImage = () => {
    if (maintenanceImages.length < 2) return;
    setCarouselIndex(
      (current) => (current - 1 + maintenanceImages.length) % maintenanceImages.length
    );
  };

  const goToNextImage = () => {
    if (maintenanceImages.length < 2) return;
    setCarouselIndex((current) => (current + 1) % maintenanceImages.length);
  };

  useEffect(() => {
    setViewerImageIndex(null);
    setCarouselIndex(0);
    viewerTriggerRef.current = null;
    touchStartXRef.current = null;
  }, [reportId, reportType]);

  useEffect(() => {
    if (carouselIndex < maintenanceImages.length) return;
    setCarouselIndex(0);
  }, [carouselIndex, maintenanceImages.length]);

  useEffect(() => {
    if (viewerImageIndex == null) return;
    const handleKeyDown = (event: KeyboardEvent) => {
      if (event.key === 'Escape') {
        closeImageViewer();
      } else if (event.key === 'ArrowLeft' && maintenanceImages.length > 1) {
        setViewerImageIndex((current) => {
          if (current == null) return null;
          const next = (current - 1 + maintenanceImages.length) % maintenanceImages.length;
          setCarouselIndex(next);
          return next;
        });
      } else if (event.key === 'ArrowRight' && maintenanceImages.length > 1) {
        setViewerImageIndex((current) => {
          if (current == null) return null;
          const next = (current + 1) % maintenanceImages.length;
          setCarouselIndex(next);
          return next;
        });
      }
    };
    window.addEventListener('keydown', handleKeyDown);
    return () => window.removeEventListener('keydown', handleKeyDown);
  }, [maintenanceImages.length, viewerImageIndex]);

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
        const machineName = machine
          ? resolveCatalogDisplayValue(machine.name, t)
          : maintenanceReport.machineId;
        segments.push(machineLocationLabel(t, machineName));
      }
      return segments;
    }

    if (reportType === 'treatments') {
      segments.push(t('treatments.title'));
      if (treatmentReport) {
        segments.push(reportDateLocationLabel(t, language, treatmentReport.treatmentDate));
        const machineName = treatmentMachine
          ? resolveCatalogDisplayValue(treatmentMachine.name, t)
          : treatmentReport.machineName
            ? resolveCatalogDisplayValue(treatmentReport.machineName, t)
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
                            {machine
                              ? resolveCatalogDisplayValue(machine.name, t)
                              : maintenanceReport.machineId}
                          </span>
                        </div>
                        <div>
                          <span className="text-neutral-600">{t('reports.maintenanceFilters.component')}: </span>
                          <span className="font-medium text-neutral-900">
                            {resolveCatalogDisplayValue(maintenanceV2Parts.component, t)}
                          </span>
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
                            {machine
                              ? resolveCatalogDisplayValue(machine.name, t)
                              : maintenanceReport.machineId}
                          </span>
                        </div>
                        {maintenanceReport.maintenanceTypeCode && (
                          <div>
                            <span className="text-neutral-600">{t('log.maintenanceTypeLabel')}: </span>
                            <span className="font-medium text-neutral-900">
                              {resolveCatalogDisplayValue(
                                `maintenanceType.${maintenanceReport.maintenanceTypeCode}`,
                                t
                              )}
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
                    {maintenanceImages.length > 0 && (
                      <div className="mt-3 space-y-2">
                        {showImageCarousel && (
                          <p className="text-xs font-medium text-neutral-600">
                            {t('log.imageGallery')}
                          </p>
                        )}
                        <div
                          style={imageFrameStyle}
                          onTouchStart={(event) => {
                            if (!showImageCarousel) return;
                            touchStartXRef.current = event.changedTouches[0]?.clientX ?? null;
                          }}
                          onTouchEnd={(event) => {
                            if (!showImageCarousel || touchStartXRef.current == null) return;
                            const endX = event.changedTouches[0]?.clientX;
                            if (endX == null) {
                              touchStartXRef.current = null;
                              return;
                            }
                            const deltaX = endX - touchStartXRef.current;
                            touchStartXRef.current = null;
                            if (Math.abs(deltaX) < 40) return;
                            if (deltaX > 0) goToPreviousImage();
                            else goToNextImage();
                          }}
                        >
                          <button
                            type="button"
                            onClick={(event) =>
                              openImageViewer(
                                showImageCarousel ? carouselIndex : 0,
                                event.currentTarget
                              )
                            }
                            aria-label={
                              showImageCarousel
                                ? `${t('log.openImage')} ${carouselIndex + 1}`
                                : t('log.openImage')
                            }
                            className="block w-full rounded-lg focus:outline-none focus:ring-2 focus:ring-neutral-800"
                          >
                            <img
                              src={
                                showImageCarousel
                                  ? maintenanceImages[carouselIndex]
                                  : maintenanceImages[0]
                              }
                              alt={
                                showImageCarousel
                                  ? `${t('log.maintenancePhotoAlt')} ${carouselIndex + 1}`
                                  : t('log.maintenancePhotoAlt')
                              }
                              className="w-full rounded-lg border border-neutral-300"
                            />
                          </button>
                          {showImageCarousel && (
                            <>
                              <button
                                type="button"
                                onClick={goToPreviousImage}
                                aria-label={t('log.previousImage')}
                                style={carouselNavButtonStyle('left')}
                              >
                                <ChevronLeft className="w-6 h-6" aria-hidden />
                              </button>
                              <button
                                type="button"
                                onClick={goToNextImage}
                                aria-label={t('log.nextImage')}
                                style={carouselNavButtonStyle('right')}
                              >
                                <ChevronRight className="w-6 h-6" aria-hidden />
                              </button>
                              <div style={carouselCounterStyle}>
                                {carouselIndex + 1} / {maintenanceImages.length}
                              </div>
                            </>
                          )}
                        </div>
                      </div>
                    )}
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
                        ? resolveCatalogDisplayValue(treatmentMachine.name, t)
                        : treatmentReport.machineName
                          ? resolveCatalogDisplayValue(treatmentReport.machineName, t)
                          : t('common.notProvided')}
                    </span>
                  </div>
                  <div className="flex justify-between">
                    <span className="text-neutral-600">{t('treatment.type')}:</span>
                    <span className="font-medium text-neutral-900">
                      {treatmentMaintenanceTypeCode
                        ? resolveCatalogDisplayValue(
                            `maintenanceType.${treatmentMaintenanceTypeCode}`,
                            t
                          )
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
      {viewerImageIndex != null && maintenanceImages[viewerImageIndex] && (
        <div
          style={viewerOverlayStyle}
          role="dialog"
          aria-modal="true"
          aria-label={t('log.imageViewer')}
          onClick={closeImageViewer}
        >
          <button
            type="button"
            onClick={closeImageViewer}
            aria-label={t('log.closeViewer')}
            style={viewerCloseButtonStyle}
          >
            <X className="w-7 h-7" aria-hidden />
          </button>
          <img
            src={maintenanceImages[viewerImageIndex]}
            alt={
              showImageCarousel
                ? `${t('log.maintenancePhotoAlt')} ${viewerImageIndex + 1}`
                : t('log.maintenancePhotoAlt')
            }
            style={viewerImageStyle}
            onClick={(event) => event.stopPropagation()}
          />
          {showImageCarousel && (
            <>
              <button
                type="button"
                onClick={(event) => {
                  event.stopPropagation();
                  const next =
                    (viewerImageIndex - 1 + maintenanceImages.length) %
                    maintenanceImages.length;
                  setViewerImageIndex(next);
                  setCarouselIndex(next);
                }}
                aria-label={t('log.previousImage')}
                style={viewerNavButtonStyle('left')}
              >
                <ChevronLeft className="w-8 h-8" aria-hidden />
              </button>
              <button
                type="button"
                onClick={(event) => {
                  event.stopPropagation();
                  const next = (viewerImageIndex + 1) % maintenanceImages.length;
                  setViewerImageIndex(next);
                  setCarouselIndex(next);
                }}
                aria-label={t('log.nextImage')}
                style={viewerNavButtonStyle('right')}
              >
                <ChevronRight className="w-8 h-8" aria-hidden />
              </button>
              <div style={viewerCounterStyle}>
                {viewerImageIndex + 1} / {maintenanceImages.length}
              </div>
            </>
          )}
        </div>
      )}
    </div>
  );
}
