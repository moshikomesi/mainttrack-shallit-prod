import { useEffect, useState } from 'react';
import { useLanguage } from '../context/LanguageContext';
import { getAnnualPlan } from '../services/annualPlansService';
import type { AnnualPlanDto, PlanType } from '../types/annualPlans';

interface AnnualPlanReportScreenProps {
  year: number;
  type: PlanType;
}

interface MonthBucket {
  key: string;
  datesByTask: Record<string, string[]>;
}

function isPreventive(type: unknown) {
  return type === 0 || type === 'Preventive';
}

export function AnnualPlanReportScreen({ year, type }: AnnualPlanReportScreenProps) {
  const { t } = useLanguage();
  const [plan, setPlan] = useState<AnnualPlanDto | null>(null);
  const [isLoading, setIsLoading] = useState(false);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    let cancelled = false;
    const load = async () => {
      try {
        setIsLoading(true);
        setError(null);
        const result = await getAnnualPlan(year, type);
        if (cancelled) return;
        setPlan(result);
      } catch (err) {
        console.error('Failed to load annual plan report', err);
        if (!cancelled) {
          setError(t('messages.failedToLoadAnnualPlan'));
        }
      } finally {
        if (!cancelled) {
          setIsLoading(false);
        }
      }
    };

    void load();
    return () => {
      cancelled = true;
    };
  }, [year, type]);

  if (isLoading) {
    return (
      <div className="bg-white border border-neutral-200 rounded-lg p-8 text-center text-neutral-500">
        {t('common.loading')}
      </div>
    );
  }

  if (error) {
    return (
      <div className="bg-white border border-neutral-200 rounded-lg p-8 text-center text-neutral-500">
        {error}
      </div>
    );
  }

  if (!plan) {
    return (
      <div className="bg-white border border-neutral-200 rounded-lg p-8 text-center text-neutral-500">
        {t('annual.noAnnualPlanExistsForYear')}
      </div>
    );
  }

  if (isPreventive(plan.type)) {
    const months: MonthBucket[] = Array.from({ length: 12 }, (_, i) => ({
      key: `month.${['jan', 'feb', 'mar', 'apr', 'may', 'jun', 'jul', 'aug', 'sep', 'oct', 'nov', 'dec'][i]}`,
      datesByTask: {},
    }));

    const taskKeys = [
      'annual.lubricationIntake',
      'annual.lubricationConveyors',
      'annual.coolingService',
      'annual.tempSensorInspection',
    ];

    for (const item of plan.items) {
      if (!taskKeys.includes(item.taskKey)) continue;
      for (const date of item.dates) {
        if (!date) continue;
        const d = new Date(date);
        if (Number.isNaN(d.getTime())) continue;
        const monthIndex = d.getMonth();
        const bucket = months[monthIndex];
        if (!bucket.datesByTask[item.taskKey]) {
          bucket.datesByTask[item.taskKey] = [];
        }
        bucket.datesByTask[item.taskKey].push(date);
      }
    }

    return (
      <div className="space-y-3">
        {months.map((month, _index) => {
          const hasAnyDates = Object.values(month.datesByTask).some((dates) => dates.length > 0);
          if (!hasAnyDates) {
            return null;
          }

          return (
            <div
              key={month.key}
              className="bg-white border border-neutral-200 rounded-lg p-4 space-y-3"
            >
              <div className="text-sm font-semibold text-neutral-900 mb-1">
                {t(month.key)}
              </div>
              {taskKeys.map((taskKey) => {
                const dates = month.datesByTask[taskKey] || [];
                if (!dates.length) return null;
                return (
                  <div key={taskKey}>
                    <div className="text-xs font-medium text-neutral-700 mb-1">
                      {t(taskKey)}
                    </div>
                    <ul className="list-disc list-inside text-xs text-neutral-700 space-y-0.5">
                      {dates.map((d) => {
                        const dateObj = new Date(d);
                        const formatted = Number.isNaN(dateObj.getTime())
                          ? d
                          : dateObj.toLocaleDateString(undefined, {
                              day: '2-digit',
                              month: '2-digit',
                              year: 'numeric',
                            });
                        return <li key={d}>{formatted}</li>;
                      })}
                    </ul>
                  </div>
                );
              })}
            </div>
          );
        })}
      </div>
    );
  }

  // Summer plan report - card layout
  return (
    <div className="space-y-3">
      {plan.items.map((item) => {
        const exec = item.execution;
        if (!exec) return null;

        const workersLabel = exec.workers.map((w) => t(w)).join(', ');

        return (
          <div
            key={item.taskKey}
            className="bg-white border border-neutral-200 rounded-lg p-4 space-y-3"
          >
            <h3 className="font-semibold text-neutral-900">{t(item.taskKey)}</h3>

            <div className="grid grid-cols-2 gap-3 text-sm text-neutral-800">
              <div>
                <div className="text-xs text-neutral-500 mb-0.5">
                  {t('annual.plannedStart')}
                </div>
                <div>{exec.plannedStart ?? ''}</div>
              </div>

              <div>
                <div className="text-xs text-neutral-500 mb-0.5">
                  {t('annual.requiredDays')}
                </div>
                <div>{exec.requiredDays != null ? String(exec.requiredDays) : ''}</div>
              </div>

              <div>
                <div className="text-xs text-neutral-500 mb-0.5">
                  {t('annual.plannedFinish')}
                </div>
                <div>{exec.plannedFinish ?? ''}</div>
              </div>

              <div>
                <div className="text-xs text-neutral-500 mb-0.5">
                  {t('annual.actualFinish')}
                </div>
                <div>{exec.actualFinish ?? ''}</div>
              </div>
            </div>

            {exec.description && (
              <div className="text-sm text-neutral-800">
                <div className="text-xs text-neutral-500 mb-0.5">
                  {t('annual.plannedWork')}
                </div>
                <div>{exec.description}</div>
              </div>
            )}

            {workersLabel && (
              <div className="text-sm text-neutral-800">
                <div className="text-xs text-neutral-500 mb-0.5">
                  {t('annual.performedBy')}
                </div>
                <div>{workersLabel}</div>
              </div>
            )}
          </div>
        );
      })}
    </div>
  );
}

