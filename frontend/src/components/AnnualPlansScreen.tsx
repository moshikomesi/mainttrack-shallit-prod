import { useEffect, useState } from 'react';
import toast from 'react-hot-toast';
import { useLanguage } from '../context/LanguageContext';
import { ChevronDown, ChevronUp, Plus, X } from 'lucide-react';
import { AppHeader } from './AppHeader';
import {
  getAnnualPlan,
  getMaintenanceTasks,
  getTechnicians,
  saveAnnualPlan,
} from '../services/annualPlansService';
import type {
  MaintenanceTaskDto,
  PlanType as ApiPlanType,
  TechnicianDto,
  UiPlanType,
  MonthData,
  SummerMaintenanceRow,
  SaveAnnualPlanRequest,
  AnnualPlanItem,
} from '../types/annualPlans';

type AnnualPlansScreenProps = {
  onBack: () => void;
};

function isSummerType(value: unknown): boolean {
  return value === 'Summer' || value === 'summer' || value === 1;
}

function isPreventiveType(value: unknown): boolean {
  return value === 'Preventive' || value === 'preventive' || value === 0;
}

function mapUiTypeToApiValue(planType: UiPlanType): number {
  return planType === 'preventive' ? 0 : 1;
}

export function AnnualPlansScreen({ onBack }: AnnualPlansScreenProps) {
  const { t } = useLanguage();
  const currentYear = new Date().getFullYear();
  const years = [
    currentYear - 2,
    currentYear - 1,
    currentYear,
    currentYear + 1,
    currentYear + 2,
  ];
  const [planType, setPlanType] = useState<UiPlanType>('preventive');
  const [year, setYear] = useState(new Date().getFullYear().toString());
  const [expandedMonth, setExpandedMonth] = useState<number | null>(null);
  
  // Preventive plan data
  const [monthsData, setMonthsData] = useState<MonthData[]>(
    Array.from({ length: 12 }, () => ({
      lubricationIntake: [''],
      lubricationConveyors: [''],
      coolingService: [''],
      tempSensorInspection: [''],
    }))
  );

  // Summer maintenance plan data
  const [summerRows, setSummerRows] = useState<SummerMaintenanceRow[]>([]);

  const [tasks, setTasks] = useState<MaintenanceTaskDto[]>([]);
  const [technicians, setTechnicians] = useState<TechnicianDto[]>([]);
  const [tasksLoaded, setTasksLoaded] = useState(false);
  const [isSaving, setIsSaving] = useState(false);

  const months = Array.from({ length: 12 }, (_, i) => ({
    key: `month.${['jan', 'feb', 'mar', 'apr', 'may', 'jun', 'jul', 'aug', 'sep', 'oct', 'nov', 'dec'][i]}`,
    index: i,
  }));

  const addDate = (monthIndex: number, field: keyof MonthData) => {
    const newMonthsData = [...monthsData];
    newMonthsData[monthIndex][field].push('');
    setMonthsData(newMonthsData);
  };

  const updateDate = (monthIndex: number, field: keyof MonthData, dateIndex: number, value: string) => {
    const newMonthsData = [...monthsData];
    newMonthsData[monthIndex][field][dateIndex] = value;
    setMonthsData(newMonthsData);
  };

  const removeDate = (monthIndex: number, field: keyof MonthData, dateIndex: number) => {
    const newMonthsData = [...monthsData];
    if (newMonthsData[monthIndex][field].length > 1) {
      newMonthsData[monthIndex][field].splice(dateIndex, 1);
      setMonthsData(newMonthsData);
    }
  };

  const updateSummerRow = (id: string, field: keyof SummerMaintenanceRow, value: any) => {
    setSummerRows(summerRows.map(row => 
      row.id === id ? { ...row, [field]: value } : row
    ));
  };

  const toggleTechnician = (rowId: string, tech: string) => {
    setSummerRows(summerRows.map(row => {
      if (row.id === rowId) {
        const performedBy = row.performedBy.includes(tech)
          ? row.performedBy.filter(t => t !== tech)
          : [...row.performedBy, tech];
        return { ...row, performedBy };
      }
      return row;
    }));
  };

  async function loadInitialData() {
    try {
      const [loadedTasks, loadedTechs] = await Promise.all([
        getMaintenanceTasks(),
        getTechnicians(),
      ]);
      if (process.env.NODE_ENV === 'development') {
      }
      setTasks(loadedTasks);
      setTechnicians(loadedTechs);
      setTasksLoaded(true);
    } catch (err) {
      console.error('Failed to load annual plan metadata', err);
    }
  }

  useEffect(() => {
    void loadInitialData();
  }, []);

  useEffect(() => {
    if (!tasksLoaded) return;
    void loadPlan();
  }, [tasksLoaded, year, planType]);

  useEffect(() => {
    if (!tasks.length) return;

    const summerTasks = tasks.filter((t) => isSummerType(t.type));

    setSummerRows(
      summerTasks.map((task) => ({
        id: task.id,
        machine: task.translationKey,
        plannedStart: '',
        requiredDays: '',
        plannedFinish: '',
        plannedWork: '',
        actualStart: '',
        actualFinish: '',
        performedBy: [],
      }))
    );
  }, [tasks]);

  function mapUiTypeToApi(planType: UiPlanType): ApiPlanType {
    return planType === 'preventive' ? 'Preventive' : 'Summer';
  }

  async function loadPlan() {
    const numericYear = Number(year);
    if (!numericYear || Number.isNaN(numericYear)) return;
    if (!tasks.length) return;
    try {
      const plan = await getAnnualPlan(numericYear, mapUiTypeToApi(planType));
      if (process.env.NODE_ENV === 'development') {
      }
      if (!plan) {
        // No plan yet - initialize default UI from tasks so user can start editing immediately.
        if (planType === 'preventive') {
          // Keep one empty slot per month/field as baseline; tasks drive semantics via translation keys.
          setMonthsData(
            Array.from({ length: 12 }, () => ({
              lubricationIntake: [''],
              lubricationConveyors: [''],
              coolingService: [''],
              tempSensorInspection: [''],
            }))
          );
        }
        return;
      }

      if (isPreventiveType(plan.type)) {
        const nextMonths: MonthData[] = Array.from({ length: 12 }, () => ({
          lubricationIntake: [],
          lubricationConveyors: [],
          coolingService: [],
          tempSensorInspection: [],
        }));

        const fieldByKey: Record<string, keyof MonthData> = {
          'annual.lubricationIntake': 'lubricationIntake',
          'annual.lubricationConveyors': 'lubricationConveyors',
          'annual.coolingService': 'coolingService',
          'annual.tempSensorInspection': 'tempSensorInspection',
        };

        for (const item of plan.items) {
          const field = fieldByKey[item.taskKey];
          if (!field) continue;
          for (const date of item.dates) {
            if (!date) continue;
            const d = new Date(date);
            if (Number.isNaN(d.getTime())) continue;
            const monthIndex = d.getMonth(); // 0-11
            nextMonths[monthIndex][field].push(date);
          }
        }

        // Ensure at least one empty slot for each field
        const normalized = nextMonths.map((m) => ({
          lubricationIntake: m.lubricationIntake.length ? m.lubricationIntake : [''],
          lubricationConveyors: m.lubricationConveyors.length ? m.lubricationConveyors : [''],
          coolingService: m.coolingService.length ? m.coolingService : [''],
          tempSensorInspection: m.tempSensorInspection.length ? m.tempSensorInspection : [''],
        }));

        setMonthsData(normalized);
      } else if (isSummerType(plan.type)) {
        // Summer plan: map executions to existing summerRows by index
        if (summerRows.length === 0) return;
        const updatedRows = summerRows.map((row, index) => {
          const item = plan.items[index];
          if (!item || !item.execution) return row;
          const exec = item.execution;
          return {
            ...row,
            plannedStart: exec.plannedStart ?? '',
            requiredDays: exec.requiredDays != null ? String(exec.requiredDays) : '',
            plannedFinish: exec.plannedFinish ?? '',
            plannedWork: exec.description ?? '',
            actualStart: exec.actualStart ?? '',
            actualFinish: exec.actualFinish ?? '',
            performedBy: exec.workers ?? [],
          };
        });
        setSummerRows(updatedRows);
      }
    } catch (err) {
      console.error('Failed to load annual plan', err);
    }
  }

  async function handleSave() {
    const numericYear = Number(year);
    if (!numericYear || Number.isNaN(numericYear)) return;

    const body: SaveAnnualPlanRequest = {
      year: numericYear,
      type: mapUiTypeToApiValue(planType),
      items: buildItems(),
    };

    if (process.env.NODE_ENV === 'development') {
      console.log('Annual plan payload', body);
    }

    try {
      setIsSaving(true);
      await saveAnnualPlan(body);
      toast.success(t('messages.reportSaved'));
      onBack();
    } catch (err) {
      console.error('Failed to save annual plan', err);
      toast.error(t('messages.saveFailed'));
    } finally {
      setIsSaving(false);
    }
  }

  function buildItems(): AnnualPlanItem[] {
    const items: AnnualPlanItem[] = [];

    if (planType === 'preventive') {
      const fieldByKey: Record<string, keyof MonthData> = {
        'annual.lubricationIntake': 'lubricationIntake',
        'annual.lubricationConveyors': 'lubricationConveyors',
        'annual.coolingService': 'coolingService',
        'annual.tempSensorInspection': 'tempSensorInspection',
      };

      tasks
        .filter((t) => isPreventiveType(t.type))
        .forEach((task) => {
          const field = fieldByKey[task.translationKey];
          if (!field) return;
          const dates: string[] = [];
          monthsData.forEach((m) => {
            m[field].forEach((d) => {
              if (d) dates.push(d);
            });
          });
          if (dates.length) {
            items.push({
              taskId: task.id,
              dates,
            });
          }
        });

      return items;
    }

    // Summer plan
    const summerTasks = tasks.filter((t) => isSummerType(t.type));
    if (!summerTasks.length) {
      return items;
    }

    const techByKey = new Map<string, string>(
      technicians.map((t) => [t.translationKey, t.id])
    );

    summerRows.forEach((row, index) => {
      const hasData =
        row.plannedStart ||
        row.requiredDays ||
        row.plannedFinish ||
        row.plannedWork ||
        row.actualStart ||
        row.actualFinish ||
        row.performedBy.length > 0;
      if (!hasData) return;

      const workerIds = row.performedBy
        .map((key) => techByKey.get(key))
        .filter((id): id is string => Boolean(id));

      const task = summerTasks[index];
      if (!task) return;

      items.push({
        taskId: task.id,
        dates: [],
        execution: {
          plannedStart: row.plannedStart || null,
          requiredDays: row.requiredDays ? Number(row.requiredDays) : null,
          plannedFinish: row.plannedFinish || null,
          description: row.plannedWork || null,
          actualStart: row.actualStart || null,
          actualFinish: row.actualFinish || null,
          workerIds,
        },
      });
    });

    return items;
  }

  return (
    <div className="min-h-screen bg-neutral-50 pb-6">
      <AppHeader title={t('home.annualPlans')} showBack={true} showHome={true} />

      <div className="p-4 space-y-4">
        {/* Plan Type Toggle */}
        <div className="bg-white border border-neutral-200 rounded-lg p-3">
          <div className="grid grid-cols-2 gap-2">
            <button
              onClick={() => setPlanType('preventive')}
              className={`px-4 py-2.5 rounded-lg font-medium transition-colors ${
                planType === 'preventive'
                  ? 'bg-neutral-800 text-white'
                  : 'bg-neutral-50 text-neutral-700 hover:bg-neutral-100'
              }`}
            >
              {t('annual.preventive')}
            </button>
            <button
              onClick={() => setPlanType('summer')}
              className={`px-4 py-2.5 rounded-lg font-medium transition-colors ${
                planType === 'summer'
                  ? 'bg-neutral-800 text-white'
                  : 'bg-neutral-50 text-neutral-700 hover:bg-neutral-100'
              }`}
            >
              {t('annual.summer')}
            </button>
          </div>
        </div>

        {/* Year Selector */}
        <div className="bg-white border border-neutral-200 rounded-lg p-4">
          <label className="block text-sm font-medium text-neutral-700 mb-2">
            {t('annual.year')}
          </label>
          <select
            value={year}
            onChange={(e) => setYear(e.target.value)}
            className="w-full px-4 py-3 bg-white border border-neutral-300 rounded-xl text-xl font-semibold text-neutral-900"          >
            {years.map((y) => (
              <option key={y} value={y}>
                {y}
              </option>
            ))}
          </select>
        </div>

        {planType === 'preventive' ? (
          /* Preventive Maintenance Plan */
          <div className="space-y-2">
            {months.map(({ key, index }) => (
              <div key={index} className="bg-white border border-neutral-200 rounded-lg overflow-hidden">
                <button
                  onClick={() => setExpandedMonth(expandedMonth === index ? null : index)}
                  className="w-full px-4 py-3 flex items-center justify-between hover:bg-neutral-50 transition-colors"
                >
                  <span className="font-medium text-neutral-900">{t(key)}</span>
                  {expandedMonth === index ? (
                    <ChevronUp className="w-5 h-5 text-neutral-500" />
                  ) : (
                    <ChevronDown className="w-5 h-5 text-neutral-500" />
                  )}
                </button>
                
                {expandedMonth === index && (
                  <div className="px-4 pb-4 space-y-4 border-t border-neutral-200">
                    {/* Lubrication - Intake System */}
                    <div className="pt-4">
                      <label className="block text-sm font-medium text-neutral-700 mb-2">
                        {t('annual.lubricationIntake')}
                      </label>
                      {monthsData[index].lubricationIntake.map((date, dateIndex) => (
                        <div key={dateIndex} className="flex gap-2 mb-2">
                          <input
                            type="date"
                            value={date}
                            onChange={(e) => updateDate(index, 'lubricationIntake', dateIndex, e.target.value)}
                            className="flex-1 px-3 py-2 bg-white border border-neutral-300 rounded text-sm text-neutral-900 focus:outline-none focus:ring-2 focus:ring-neutral-800"
                          />
                          {monthsData[index].lubricationIntake.length > 1 && (
                            <button
                              onClick={() => removeDate(index, 'lubricationIntake', dateIndex)}
                              className="p-2 hover:bg-neutral-100 rounded transition-colors"
                            >
                              <X className="w-4 h-4 text-neutral-500" />
                            </button>
                          )}
                        </div>
                      ))}
                      <button
                        onClick={() => addDate(index, 'lubricationIntake')}
                        className="text-sm text-neutral-600 hover:text-neutral-900 flex items-center gap-1"
                      >
                        <Plus className="w-4 h-4" />
                        {t('annual.addDate')}
                      </button>
                    </div>

                    {/* Lubrication - Conveyors */}
                    <div>
                      <label className="block text-sm font-medium text-neutral-700 mb-2">
                        {t('annual.lubricationConveyors')}
                      </label>
                      {monthsData[index].lubricationConveyors.map((date, dateIndex) => (
                        <div key={dateIndex} className="flex gap-2 mb-2">
                          <input
                            type="date"
                            value={date}
                            onChange={(e) => updateDate(index, 'lubricationConveyors', dateIndex, e.target.value)}
                            className="flex-1 px-3 py-2 bg-white border border-neutral-300 rounded text-sm text-neutral-900 focus:outline-none focus:ring-2 focus:ring-neutral-800"
                          />
                          {monthsData[index].lubricationConveyors.length > 1 && (
                            <button
                              onClick={() => removeDate(index, 'lubricationConveyors', dateIndex)}
                              className="p-2 hover:bg-neutral-100 rounded transition-colors"
                            >
                              <X className="w-4 h-4 text-neutral-500" />
                            </button>
                          )}
                        </div>
                      ))}
                      <button
                        onClick={() => addDate(index, 'lubricationConveyors')}
                        className="text-sm text-neutral-600 hover:text-neutral-900 flex items-center gap-1"
                      >
                        <Plus className="w-4 h-4" />
                        {t('annual.addDate')}
                      </button>
                    </div>

                    {/* Monthly Cooling Service */}
                    <div>
                      <label className="block text-sm font-medium text-neutral-700 mb-2">
                        {t('annual.coolingService')}
                      </label>
                      {monthsData[index].coolingService.map((date, dateIndex) => (
                        <div key={dateIndex} className="flex gap-2 mb-2">
                          <input
                            type="date"
                            value={date}
                            onChange={(e) => updateDate(index, 'coolingService', dateIndex, e.target.value)}
                            className="flex-1 px-3 py-2 bg-white border border-neutral-300 rounded text-sm text-neutral-900 focus:outline-none focus:ring-2 focus:ring-neutral-800"
                          />
                          {monthsData[index].coolingService.length > 1 && (
                            <button
                              onClick={() => removeDate(index, 'coolingService', dateIndex)}
                              className="p-2 hover:bg-neutral-100 rounded transition-colors"
                            >
                              <X className="w-4 h-4 text-neutral-500" />
                            </button>
                          )}
                        </div>
                      ))}
                      <button
                        onClick={() => addDate(index, 'coolingService')}
                        className="text-sm text-neutral-600 hover:text-neutral-900 flex items-center gap-1"
                      >
                        <Plus className="w-4 h-4" />
                        {t('annual.addDate')}
                      </button>
                    </div>

                    {/* Annual Temperature Sensor Inspection */}
                    <div>
                      <label className="block text-sm font-medium text-neutral-700 mb-2">
                        {t('annual.tempSensorInspection')}
                      </label>
                      {monthsData[index].tempSensorInspection.map((date, dateIndex) => (
                        <div key={dateIndex} className="flex gap-2 mb-2">
                          <input
                            type="date"
                            value={date}
                            onChange={(e) => updateDate(index, 'tempSensorInspection', dateIndex, e.target.value)}
                            className="flex-1 px-3 py-2 bg-white border border-neutral-300 rounded text-sm text-neutral-900 focus:outline-none focus:ring-2 focus:ring-neutral-800"
                          />
                          {monthsData[index].tempSensorInspection.length > 1 && (
                            <button
                              onClick={() => removeDate(index, 'tempSensorInspection', dateIndex)}
                              className="p-2 hover:bg-neutral-100 rounded transition-colors"
                            >
                              <X className="w-4 h-4 text-neutral-500" />
                            </button>
                          )}
                        </div>
                      ))}
                      <button
                        onClick={() => addDate(index, 'tempSensorInspection')}
                        className="text-sm text-neutral-600 hover:text-neutral-900 flex items-center gap-1"
                      >
                        <Plus className="w-4 h-4" />
                        {t('annual.addDate')}
                      </button>
                    </div>
                  </div>
                )}
              </div>
            ))}
          </div>
        ) : (
          /* Summer Maintenance Plan */
          <div className="space-y-3">
            {summerRows.map((row) => (
              <div key={row.id} className="bg-white border border-neutral-200 rounded-lg p-4 space-y-3">
                <div className="flex items-center justify-between">
                  <h3 className="font-semibold text-neutral-900">
                    {t(row.machine)}
                  </h3>
                </div>

                <div className="grid grid-cols-2 gap-3">
                  <div>
                    <label className="block text-xs font-medium text-neutral-600 mb-1">
                      {t('annual.plannedStart')}
                    </label>
                    <input
                      type="date"
                      value={row.plannedStart}
                      onChange={(e) => updateSummerRow(row.id, 'plannedStart', e.target.value)}
                      className="w-full px-3 py-2 bg-white border border-neutral-300 rounded text-sm text-neutral-900 focus:outline-none focus:ring-2 focus:ring-neutral-800"
                    />
                  </div>
                  <div>
                    <label className="block text-xs font-medium text-neutral-600 mb-1">
                      {t('annual.requiredDays')}
                    </label>
                    <input
                      type="number"
                      value={row.requiredDays}
                      onChange={(e) => updateSummerRow(row.id, 'requiredDays', e.target.value)}
                      placeholder="0"
                      className="w-full px-3 py-2 bg-white border border-neutral-300 rounded text-sm text-neutral-900 focus:outline-none focus:ring-2 focus:ring-neutral-800"
                    />
                  </div>
                </div>

                <div>
                  <label className="block text-xs font-medium text-neutral-600 mb-1">
                    {t('annual.plannedFinish')}
                  </label>
                  <input
                    type="date"
                    value={row.plannedFinish}
                    onChange={(e) => updateSummerRow(row.id, 'plannedFinish', e.target.value)}
                    className="w-full px-3 py-2 bg-white border border-neutral-300 rounded text-sm text-neutral-900 focus:outline-none focus:ring-2 focus:ring-neutral-800"
                  />
                </div>

                <div>
                  <label className="block text-xs font-medium text-neutral-600 mb-1">
                    {t('annual.plannedWork')}
                  </label>
                  <textarea
                    value={row.plannedWork}
                    onChange={(e) => updateSummerRow(row.id, 'plannedWork', e.target.value)}
                    rows={2}
                    className="w-full px-3 py-2 bg-white border border-neutral-300 rounded text-sm text-neutral-900 focus:outline-none focus:ring-2 focus:ring-neutral-800 resize-none"
                  />
                </div>

                <div className="grid grid-cols-2 gap-3">
                  <div>
                    <label className="block text-xs font-medium text-neutral-600 mb-1">
                      {t('annual.actualStart')}
                    </label>
                    <input
                      type="date"
                      value={row.actualStart}
                      onChange={(e) => updateSummerRow(row.id, 'actualStart', e.target.value)}
                      className="w-full px-3 py-2 bg-white border border-neutral-300 rounded text-sm text-neutral-900 focus:outline-none focus:ring-2 focus:ring-neutral-800"
                    />
                  </div>
                  <div>
                    <label className="block text-xs font-medium text-neutral-600 mb-1">
                      {t('annual.actualFinish')}
                    </label>
                    <input
                      type="date"
                      value={row.actualFinish}
                      onChange={(e) => updateSummerRow(row.id, 'actualFinish', e.target.value)}
                      className="w-full px-3 py-2 bg-white border border-neutral-300 rounded text-sm text-neutral-900 focus:outline-none focus:ring-2 focus:ring-neutral-800"
                    />
                  </div>
                </div>

                <div>
                  <label className="block text-xs font-medium text-neutral-600 mb-2">
                    {t('annual.performedBy')}
                  </label>
                  <div className="flex flex-wrap gap-2">
                    {technicians.map((tech) => (
                      <button
                        key={tech.id}
                        onClick={() => toggleTechnician(row.id, tech.translationKey)}
                        className={`px-3 py-1.5 rounded text-sm font-medium transition-colors ${
                          row.performedBy.includes(tech.translationKey)
                            ? 'bg-neutral-800 text-white'
                            : 'bg-neutral-100 text-neutral-700 hover:bg-neutral-200'
                        }`}
                      >
                        {t(tech.translationKey)}
                      </button>
                    ))}
                  </div>
                </div>
              </div>
            ))}
          </div>
        )}

        <button
          onClick={handleSave}
          disabled={isSaving}
          className="w-full bg-neutral-800 text-white py-3 rounded-lg font-medium disabled:opacity-60 disabled:cursor-not-allowed"
        >
          {isSaving ? t('messages.loading') : t('annual.savePlan')}
        </button>
      </div>
    </div>
  );
}
