import { useCallback, useEffect, useMemo, useRef, useState } from 'react';
import { useLocation } from 'react-router-dom';
import { useLanguage } from '../context/LanguageContext';
import { Calendar, ClipboardCheck, ClipboardList, Droplets, FileText, Loader2, RefreshCw, Truck } from 'lucide-react';
import { AnnualPlanReportScreen } from './AnnualPlanReportScreen';
import { ForkliftReportsScreen } from './ForkliftReportsScreen';
import { MaintenanceTasksReportScreen } from './reports/MaintenanceTasksReportScreen';
import { ReportLocationHeader } from './reports/ReportLocationHeader';
import { getMorningRounds } from '../services/morningRoundsService';
import { listMorningRoundV2Reports } from '../services/morningRoundV2Service';
import { getMaintenance } from '../services/maintenanceService';
import { getHierarchy, type HierarchyArray } from '../services/hierarchyService';
import { getMachineComponents, type MachineComponentOption } from '../services/machineComponentService';
import { getTreatments } from '../services/treatmentService';
import { useInfiniteScroll } from '../hooks/useInfiniteScroll';
import { AppHeader } from './AppHeader';
import { canSeeMorningRoundV2 } from '../auth/roles';
import type { MorningRoundDto } from '../types/morningRound';
import type { MaintenanceEntryDto } from '../types/maintenance';
import type { TreatmentDto } from '../types/treatment';
import { formatDisplayDate, formatDisplayDateTime, formatFormDate } from '../utils/formatDate';
import { formatTechnician } from '../utils/formatTechnician';
import { resolveCatalogDisplayValue, resolveCatalogKey } from '../utils/resolveCatalogDisplayValue';
import type { MorningRoundReportVariant, ReportListItem, ReportsListReturnContext, ReportsListScreenProps } from '../types/reports';

function interpolate(template: string, values: Record<string, string>): string {
  return Object.entries(values).reduce(
    (result, [key, value]) => result.replace(`{${key}}`, value),
    template
  );
}

function parseMaintenanceV2Description(description: string): { component: string; details: string } | null {
  const text = description ?? '';
  const parts = text.split('\n');
  // First line is the component; remaining lines are optional fault details.
  const component = (parts[0] ?? '').trim();
  if (!component) return null;
  const details = parts.slice(1).join('\n').trim();
  return { component, details };
}

export function ReportsListScreen({ onSelectReport, onBack, userRoleId }: ReportsListScreenProps) {
  const { t, language } = useLanguage();
  const location = useLocation();
  const restoredContextRef = useRef(false);
  const [selectedType, setSelectedType] =
    useState<'morning' | 'maintenance' | 'maintenance-tasks' | 'treatments' | 'annual-plans' | 'forklift' | null>(null);
  const [morningVariant, setMorningVariant] = useState<MorningRoundReportVariant | null>(null);
  const [filterDate, setFilterDate] = useState('');
  const [morningReports, setMorningReports] = useState<ReportListItem[]>([]);
  const [isLoadingMorning, setIsLoadingMorning] = useState(false);
  const [loadError, setLoadError] = useState<string | null>(null);
  const [search, setSearch] = useState('');
  const [debouncedSearch, setDebouncedSearch] = useState('');
  const [annualPlanYear, setAnnualPlanYear] = useState('');
  const [annualPlanType, setAnnualPlanType] = useState<'Preventive' | 'Summer' | ''>('');
  const [tasksExpandedId, setTasksExpandedId] = useState<string | null>(null);

  const [hierarchy, setHierarchy] = useState<HierarchyArray[]>([]);
  const [isLoadingHierarchy, setIsLoadingHierarchy] = useState(false);
  const [selectedArrayId, setSelectedArrayId] = useState('');
  const [selectedMachineId, setSelectedMachineId] = useState('');
  const [selectedComponentKey, setSelectedComponentKey] = useState('');
  const [componentOptions, setComponentOptions] = useState<MachineComponentOption[]>([]);
  const [isLoadingComponents, setIsLoadingComponents] = useState(false);

  useEffect(() => {
    if (restoredContextRef.current) return;

    const ctx = (
      location.state as { reportsListContext?: ReportsListReturnContext } | null
    )?.reportsListContext;
    if (!ctx) return;

    restoredContextRef.current = true;
    setSelectedType(ctx.selectedType);
    if (ctx.selectedType === 'morning') {
      setMorningVariant(ctx.morningVariant ?? null);
    }
  }, [location.state]);

  useEffect(() => {
    if (selectedType !== 'maintenance-tasks') {
      setTasksExpandedId(null);
    }
  }, [selectedType]);

  useEffect(() => {
    if (selectedType !== 'maintenance') return;
    let cancelled = false;

    const load = async () => {
      try {
        setIsLoadingHierarchy(true);
        const data = await getHierarchy();
        if (cancelled) return;
        setHierarchy(Array.isArray(data) ? data : []);
      } catch (err) {
        console.error(err);
        if (!cancelled) setHierarchy([]);
      } finally {
        if (!cancelled) setIsLoadingHierarchy(false);
      }
    };

    load();
    return () => {
      cancelled = true;
    };
  }, [selectedType]);

  useEffect(() => {
    if (selectedType !== 'maintenance') return;
    if (!selectedMachineId) {
      setComponentOptions([]);
      return;
    }

    let cancelled = false;
    const load = async () => {
      try {
        setIsLoadingComponents(true);
        const list = await getMachineComponents(selectedMachineId);
        if (cancelled) return;
        setComponentOptions(Array.isArray(list) ? list : []);
      } catch (err) {
        console.error(err);
        if (!cancelled) setComponentOptions([]);
      } finally {
        if (!cancelled) setIsLoadingComponents(false);
      }
    };

    load();
    return () => {
      cancelled = true;
    };
  }, [selectedMachineId, selectedType]);

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
    if (selectedType !== 'morning' || !morningVariant) return;

    let cancelled = false;
    const load = async () => {
      try {
        setIsLoadingMorning(true);
        setLoadError(null);

        if (morningVariant === 'v1') {
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
        } else {
          const list = await listMorningRoundV2Reports();
          if (cancelled) return;
          const mapped: ReportListItem[] = (Array.isArray(list) ? list : []).map((r) => ({
            id: r.reportId,
            date: r.date.slice(0, 10),
            submittedBy: r.submittedBy.fullName,
            submittedAt: r.submittedAt,
            type: 'morning-v2',
          }));
          setMorningReports(mapped);
        }
      } catch (err) {
        console.error(err);
        if (!cancelled) {
          setLoadError(
            morningVariant === 'v1'
              ? t('messages.failedToLoadMorningRoundReports')
              : t('messages.failedToLoadMorningRoundV2Reports')
          );
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
  }, [selectedType, morningVariant, t]);

  const fetchMaintenancePage = useCallback(
    async (pageNumber: number, pageSize: number) => {
      // DateOnly calendar filter: send the selected YYYY-MM-DD as both bounds.
      // Backend applies inclusive start / half-open next-day end on Entry.Date.
      const list = await getMaintenance({
        pageNumber,
        pageSize,
        machineId: selectedMachineId || undefined,
        fromDate: filterDate || undefined,
        toDate: filterDate || undefined,
        search: debouncedSearch.length >= 2 ? debouncedSearch : undefined,
      });
      const items: ReportListItem[] = (list as MaintenanceEntryDto[]).map((m) => ({
        id: m.id,
        date: m.date,
        submittedBy: m.employeeName,
        submittedAt: m.createdAt,
        type: 'maintenance',
        machineId: m.machineId,
        maintenanceTypeCode: m.maintenanceTypeCode ?? null,
        description: m.description,
      }));
      return { items };
    },
    [debouncedSearch, selectedMachineId, filterDate]
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
    resetKey: `${debouncedSearch}|${selectedArrayId}|${selectedMachineId}|${selectedComponentKey}|${filterDate}`,
    enabled: selectedType === 'maintenance',
  });

  const treatmentsScroll = useInfiniteScroll<ReportListItem>({
    fetchPage: fetchTreatmentsPage,
    pageSize: 20,
    resetKey: debouncedSearch,
    enabled: selectedType === 'treatments',
  });

  const reports =
    selectedType === 'morning' && morningVariant
      ? morningReports
      : selectedType === 'maintenance'
        ? maintenanceScroll.items
        : selectedType === 'treatments'
          ? treatmentsScroll.items
          : [];

  // Morning/treatments still use client-side date filtering (unchanged).
  // Maintenance date filtering is server-side via fromDate/toDate (see fetchMaintenancePage).
  const filteredReports = filterDate
    ? reports.filter((r) => {
        if (selectedType === 'maintenance') return true;
        return r.date === filterDate || r.date.startsWith(`${filterDate}T`) || r.date.startsWith(`${filterDate} `);
      })
    : reports;

  const activeArrays = useMemo(
    () => hierarchy.filter((array) => array.arrayId != null),
    [hierarchy]
  );

  const machinesForSelectedArray = useMemo(() => {
    if (!selectedArrayId) return [];
    const array = activeArrays.find((a) => a.arrayId === selectedArrayId);
    return array?.machines ?? [];
  }, [activeArrays, selectedArrayId]);

  const machineIdsForSelectedArray = useMemo(() => {
    if (!selectedArrayId) return null;
    return new Set(machinesForSelectedArray.map((m) => m.id));
  }, [machinesForSelectedArray, selectedArrayId]);

  const filteredMaintenanceReports = useMemo(() => {
    if (selectedType !== 'maintenance') return filteredReports;
    return filteredReports.filter((r) => {
      if (machineIdsForSelectedArray && (!r.machineId || !machineIdsForSelectedArray.has(r.machineId))) {
        return false;
      }

      if (selectedComponentKey) {
        if (r.maintenanceTypeCode !== 'other' || !r.description) return false;
        const parsed = parseMaintenanceV2Description(r.description);
        if (!parsed) return false;
        // Match by catalog key so filtering works across languages and for
        // legacy rows that stored a localized component label.
        const storedKey = resolveCatalogKey(parsed.component);
        return storedKey === selectedComponentKey || parsed.component === selectedComponentKey;
      }

      return true;
    });
  }, [filteredReports, machineIdsForSelectedArray, selectedComponentKey, selectedType]);

  const showMorningVariantPicker = selectedType === 'morning' && morningVariant === null;
  const showMorningReportList = selectedType === 'morning' && morningVariant !== null;
  const morningRoundV2Enabled = canSeeMorningRoundV2(userRoleId);

  const handleMorningTypeSelect = () => {
    setMorningVariant(null);
    setMorningReports([]);
    setFilterDate('');
    setLoadError(null);
    setSelectedType('morning');
  };

  const handleBackFromMorningList = () => {
    setMorningVariant(null);
    setMorningReports([]);
    setFilterDate('');
    setLoadError(null);
  };

  const buildReturnContext = (): ReportsListReturnContext | null => {
    if (!selectedType || selectedType === 'maintenance-tasks' || selectedType === 'annual-plans') {
      return null;
    }

    return {
      selectedType,
      morningVariant: selectedType === 'morning' ? morningVariant : undefined,
    };
  };

  const handleHeaderBack = () => {
    if (selectedType === 'maintenance-tasks' && tasksExpandedId) {
      setTasksExpandedId(null);
      return;
    }

    if (selectedType === 'annual-plans' && annualPlanType) {
      setAnnualPlanType('');
      return;
    }

    if (selectedType === 'annual-plans' && annualPlanYear) {
      setAnnualPlanYear('');
      return;
    }

    if (selectedType === 'morning' && morningVariant) {
      handleBackFromMorningList();
      return;
    }

    if (selectedType) {
      setSelectedType(null);
      setFilterDate('');
      setSearch('');
      return;
    }

    onBack();
  };

  const locationSegments = useMemo(() => {
    const segments = [t('reports.title')];
    if (!selectedType) {
      return segments;
    }

    if (selectedType === 'morning') {
      segments.push(t('reports.morningRound'));
      if (morningVariant === 'v1') {
        segments.push(t('reports.morningRoundV1'));
      } else if (morningVariant === 'v2') {
        segments.push(t('reports.morningRoundV2'));
      }
      return segments;
    }

    if (selectedType === 'maintenance') {
      segments.push(t('reports.maintenanceLog'));
    } else if (selectedType === 'maintenance-tasks') {
      segments.push(t('reports.tasks.menuTitle'));
    } else if (selectedType === 'treatments') {
      segments.push(t('treatments.title'));
    } else if (selectedType === 'annual-plans') {
      segments.push(t('home.annualPlans'));
      if (annualPlanYear) {
        segments.push(annualPlanYear);
      }
      if (annualPlanType === 'Preventive') {
        segments.push(t('annual.preventive'));
      } else if (annualPlanType === 'Summer') {
        segments.push(t('annual.summer'));
      }
    }

    return segments;
  }, [selectedType, morningVariant, annualPlanYear, annualPlanType, t]);

  if (selectedType === 'forklift') {
    return <ForkliftReportsScreen onBack={() => setSelectedType(null)} />;
  }

  return (
    <div className="min-h-screen bg-neutral-50">
      <AppHeader
        title={t('reports.title')}
        showBack={true}
        showHome={true}
        onBack={handleHeaderBack}
      />

      <div className="p-4 space-y-3">
        <ReportLocationHeader segments={locationSegments} />
        {!selectedType ? (
          /* Report Type Selection */
          <div className="space-y-3">
            <h2 className="text-sm font-medium text-neutral-600 mb-3">
            </h2>
            <button
              onClick={handleMorningTypeSelect}
              className="w-full bg-white border border-neutral-200 rounded-lg p-5 flex flex-col items-center justify-center gap-2 text-center hover:bg-neutral-50 active:bg-neutral-100 transition-colors"
            >
              <ClipboardList className="w-6 h-6 text-neutral-700" />
              <div className="text-base font-semibold text-neutral-900">
                {t('reports.morningRound')}
              </div>
            </button>
            <button
              onClick={() => setSelectedType('maintenance')}
              className="w-full bg-white border border-neutral-200 rounded-lg p-5 flex flex-col items-center justify-center gap-2 text-center hover:bg-neutral-50 active:bg-neutral-100 transition-colors"
            >
              <FileText className="w-6 h-6 text-neutral-700" />
              <div className="text-base font-semibold text-neutral-900">
                {t('reports.maintenanceLog')}
              </div>
            </button>
            <button
              onClick={() => setSelectedType('maintenance-tasks')}
              className="w-full bg-white border border-neutral-200 rounded-lg p-5 flex flex-col items-center justify-center gap-2 text-center hover:bg-neutral-50 active:bg-neutral-100 transition-colors"
            >
              <ClipboardCheck className="w-6 h-6 text-neutral-700" />
              <div className="text-base font-semibold text-neutral-900">
                {t('reports.tasks.menuTitle')}
              </div>
            </button>
            <button
              onClick={() => setSelectedType('annual-plans')}
              className="w-full bg-white border border-neutral-200 rounded-lg p-5 flex flex-col items-center justify-center gap-2 text-center hover:bg-neutral-50 active:bg-neutral-100 transition-colors"
            >
              <Calendar className="w-6 h-6 text-neutral-700" />
              <div className="text-base font-semibold text-neutral-900">
                {t('home.annualPlans')}
              </div>
            </button>
            <button
              onClick={() => setSelectedType('treatments')}
              className="w-full bg-white border border-neutral-200 rounded-lg p-5 flex flex-col items-center justify-center gap-2 text-center hover:bg-neutral-50 active:bg-neutral-100 transition-colors"
            >
              <Droplets className="w-6 h-6 text-neutral-700" />
              <div className="text-base font-semibold text-neutral-900">
                {t('treatments.title')}
              </div>
            </button>
            <button
              onClick={() => setSelectedType('forklift')}
              className="w-full bg-white border border-neutral-200 rounded-lg p-5 flex flex-col items-center justify-center gap-2 text-center hover:bg-neutral-50 active:bg-neutral-100 transition-colors"
            >
              <Truck className="w-6 h-6 text-neutral-700" />
              <div className="text-base font-semibold text-neutral-900">
                {t('forkliftReports.title')}
              </div>
            </button>
          </div>
        ) : selectedType === 'maintenance-tasks' ? (
          <MaintenanceTasksReportScreen
            expandedId={tasksExpandedId}
            onExpandedIdChange={setTasksExpandedId}
          />
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
        ) : showMorningVariantPicker ? (
          <div className="space-y-3">
            <h2 className="text-sm font-medium text-neutral-600 mb-1">
              {t('reports.morningRound')}
            </h2>
            <button
              onClick={() => setMorningVariant('v1')}
              className="w-full bg-white border border-neutral-200 rounded-lg p-5 text-start hover:bg-neutral-50 active:bg-neutral-100 transition-colors"
            >
              <div className="text-base font-semibold text-neutral-900">
                {t('reports.morningRoundV1')}
              </div>
            </button>
            {morningRoundV2Enabled && (
              <button
                onClick={() => setMorningVariant('v2')}
                className="w-full bg-white border border-neutral-200 rounded-lg p-5 text-start hover:bg-neutral-50 active:bg-neutral-100 transition-colors"
              >
                <div className="text-base font-semibold text-neutral-900">
                  {t('reports.morningRoundV2')}
                </div>
              </button>
            )}
          </div>
        ) : showMorningReportList || selectedType === 'maintenance' || selectedType === 'treatments' ? (
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
                    type="button"
                    onClick={() => setFilterDate('')}
                    className="mt-2 text-xs text-neutral-600 hover:text-neutral-900"
                  >
                    {t('common.clearFilter')}
                  </button>
                )}
              </div>

              {selectedType === 'maintenance' && (
                <div
                  className="rounded-lg border border-neutral-200 bg-neutral-50 px-3 py-2 text-sm text-neutral-700 space-y-0.5"
                  role="status"
                >
                  <div className="font-medium text-neutral-900">
                    {filterDate
                      ? interpolate(t('reports.filteredDay'), {
                          date: formatFormDate(language, filterDate),
                        })
                      : t('reports.allRecords')}
                  </div>
                  <div className="text-xs text-neutral-600">
                    {t('reports.latestFirst')}
                    {' · '}
                    {interpolate(t('reports.loadedCount'), {
                      count: String(filteredMaintenanceReports.length),
                    })}
                  </div>
                </div>
              )}

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

              {selectedType === 'maintenance' && (
                <>
                  <div>
                    <label className="block text-sm font-medium text-neutral-700 mb-1">
                      {t('reports.maintenanceFilters.array')}
                    </label>
                    <select
                      value={selectedArrayId}
                      disabled={isLoadingHierarchy}
                      onChange={(e) => {
                        const nextArrayId = e.target.value;
                        setSelectedArrayId(nextArrayId);
                        setSelectedMachineId('');
                        setSelectedComponentKey('');
                      }}
                      className="w-full px-3 py-2 bg-white border border-neutral-300 rounded text-sm text-neutral-900 focus:outline-none focus:ring-2 focus:ring-neutral-800"
                    >
                      <option value="">{t('reports.maintenanceFilters.allArrays')}</option>
                      {activeArrays.map((array) => (
                        <option key={array.arrayId!} value={array.arrayId!}>
                          {t(array.nameKey)}
                        </option>
                      ))}
                    </select>
                  </div>

                  <div>
                    <label className="block text-sm font-medium text-neutral-700 mb-1">
                      {t('reports.maintenanceFilters.machine')}
                    </label>
                    <select
                      value={selectedMachineId}
                      disabled={!selectedArrayId}
                      onChange={(e) => {
                        setSelectedMachineId(e.target.value);
                        setSelectedComponentKey('');
                      }}
                      className="w-full px-3 py-2 bg-white border border-neutral-300 rounded text-sm text-neutral-900 focus:outline-none focus:ring-2 focus:ring-neutral-800 disabled:bg-neutral-50 disabled:text-neutral-500"
                    >
                      <option value="">{t('reports.maintenanceFilters.allMachines')}</option>
                      {machinesForSelectedArray.map((machine) => (
                        <option key={machine.id} value={machine.id}>
                          {t(machine.name)}
                        </option>
                      ))}
                    </select>
                  </div>

                  <div>
                    <label className="block text-sm font-medium text-neutral-700 mb-1">
                      {t('reports.maintenanceFilters.component')}
                    </label>
                    <select
                      value={selectedComponentKey}
                      disabled={!selectedMachineId || isLoadingComponents}
                      onChange={(e) => setSelectedComponentKey(e.target.value)}
                      className="w-full px-3 py-2 bg-white border border-neutral-300 rounded text-sm text-neutral-900 focus:outline-none focus:ring-2 focus:ring-neutral-800 disabled:bg-neutral-50 disabled:text-neutral-500"
                    >
                      <option value="">{t('reports.maintenanceFilters.allComponents')}</option>
                      {componentOptions.map((c) => (
                        <option key={c.id} value={c.nameKey}>
                          {t(c.nameKey)}
                        </option>
                      ))}
                    </select>
                  </div>
                </>
              )}
            </div>

            {/* Reports */}
            <div className="flex flex-col gap-4 py-4">
              {(selectedType === 'maintenance' ? filteredMaintenanceReports : filteredReports).map((report) => (
                <button
                  key={report.id}
                  onClick={() => {
                    const returnContext = buildReturnContext();
                    if (!returnContext) return;
                    onSelectReport(report.id, report.type, returnContext);
                  }}
                  className="w-full bg-white border border-neutral-200 rounded-lg p-4 text-start hover:bg-neutral-50 active:bg-neutral-100 transition-colors touch-manipulation"
                >
                  {selectedType === 'maintenance' ? (
                    <div className="space-y-2">
                      <div className="text-base font-semibold text-neutral-900 leading-tight">
                        {formatDisplayDate(language, report.date, 'long')}
                      </div>
                      <div className="text-sm text-neutral-700">
                        {(() => {
                          const parsed =
                            report.maintenanceTypeCode === 'other' && report.description
                              ? parseMaintenanceV2Description(report.description)
                              : null;
                          if (parsed) {
                            return (
                              <>
                                <span className="font-medium text-neutral-900">
                                  {resolveCatalogDisplayValue(parsed.component, t)}
                                </span>
                                {parsed.details ? (
                                  <span className="text-neutral-600"> — {parsed.details}</span>
                                ) : null}
                              </>
                            );
                          }
                          return (
                            <span className="text-neutral-700 line-clamp-2">
                              {report.description ||
                                (report.maintenanceTypeCode
                                  ? t(`maintenanceType.${report.maintenanceTypeCode}`)
                                  : t('reports.maintenanceLog'))}
                            </span>
                          );
                        })()}
                      </div>
                      <div className="text-sm text-neutral-600">
                        {t('reports.submittedBy')}:{' '}
                        <span className="font-medium text-neutral-900">{report.submittedBy}</span>
                      </div>
                      <div className="text-xs text-neutral-500">
                        {t('details.submittedOn')}: {formatDisplayDateTime(language, report.submittedAt)}
                      </div>
                    </div>
                  ) : (
                    <>
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
                    </>
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

              {(selectedType === 'maintenance' ? filteredMaintenanceReports : filteredReports).length === 0 &&
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
        ) : null}
      </div>
    </div>
  );
}
