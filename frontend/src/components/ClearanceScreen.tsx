import { useCallback, useEffect, useMemo, useState } from 'react';
import { ApiError } from '../api/apiClient';
import { useLanguage } from '../context/LanguageContext';
import { getHierarchy, type HierarchyArray } from '../services/hierarchyService';
import { getClearanceAccess, getClearanceQueue } from '../services/clearanceService';
import type { ClearanceAccess, ClearanceItem, ClearanceStatus } from '../types/clearance';
import { formatDisplayDate, formatDisplayDateTime } from '../utils/formatDate';
import { resolveCatalogDisplayValue } from '../utils/resolveCatalogDisplayValue';
import { AppHeader } from './AppHeader';
import { ClearanceDecisionActions } from './clearance/ClearanceDecisionActions';
import { ClearanceStatusBadge } from './clearance/ClearanceStatusBadge';

const PAGE_SIZE = 20;

type QueueTab = Extract<ClearanceStatus, 'pending' | 'rejected'>;

type LoadState =
  | { kind: 'loading' }
  | { kind: 'forbidden' }
  | { kind: 'error' }
  | { kind: 'ready'; items: ClearanceItem[]; totalCount: number; canApprove: boolean };

interface ClearanceScreenProps {
  onBack: () => void;
  onOpenReport: (item: ClearanceItem) => void;
}

export function ClearanceScreen({ onBack, onOpenReport }: ClearanceScreenProps) {
  const { t, language } = useLanguage();
  const [access, setAccess] = useState<ClearanceAccess | null>(null);
  const [tab, setTab] = useState<QueueTab>('pending');
  const [selectedArrayId, setSelectedArrayId] = useState('');
  const [selectedMachineId, setSelectedMachineId] = useState('');
  const [pageNumber, setPageNumber] = useState(1);
  const [hierarchy, setHierarchy] = useState<HierarchyArray[]>([]);
  const [isLoadingHierarchy, setIsLoadingHierarchy] = useState(true);
  const [state, setState] = useState<LoadState>({ kind: 'loading' });

  useEffect(() => {
    let cancelled = false;
    getClearanceAccess()
      .then((result) => {
        if (!cancelled) setAccess(result);
      })
      .catch((err) => {
        console.error(err);
        if (!cancelled) setState({ kind: 'error' });
      });
    getHierarchy()
      .then((data) => {
        if (!cancelled) setHierarchy(Array.isArray(data) ? data : []);
      })
      .catch((err) => console.error(err))
      .finally(() => {
        if (!cancelled) setIsLoadingHierarchy(false);
      });
    return () => {
      cancelled = true;
    };
  }, []);

  const loadQueue = useCallback(async () => {
    setState({ kind: 'loading' });
    try {
      const response = await getClearanceQueue({
        status: tab,
        arrayId: selectedArrayId || undefined,
        machineId: selectedMachineId || undefined,
        pageNumber,
        pageSize: PAGE_SIZE,
      });
      setState({
        kind: 'ready',
        items: response.items,
        totalCount: response.totalCount,
        canApprove: response.canApprove,
      });
    } catch (err) {
      if (err instanceof ApiError && err.status === 403) {
        setState({ kind: 'forbidden' });
      } else {
        console.error(err);
        setState({ kind: 'error' });
      }
    }
  }, [tab, selectedArrayId, selectedMachineId, pageNumber]);

  useEffect(() => {
    if (!access) return;
    if (!access.canView) {
      setState({ kind: 'forbidden' });
      return;
    }
    loadQueue();
  }, [access, loadQueue]);

  useEffect(() => {
    // A decision can empty the last page; step back instead of showing an empty page.
    if (state.kind === 'ready' && state.items.length === 0 && pageNumber > 1) {
      setPageNumber((p) => p - 1);
    }
  }, [state, pageNumber]);

  const activeArrays = useMemo(
    () => hierarchy.filter((array) => array.arrayId != null),
    [hierarchy]
  );

  const machinesForSelectedArray = useMemo(() => {
    if (!selectedArrayId) return [];
    return activeArrays.find((a) => a.arrayId === selectedArrayId)?.machines ?? [];
  }, [activeArrays, selectedArrayId]);

  const arrayLabel = useCallback(
    (arrayId: string | null) => {
      if (!arrayId) return null;
      const array = activeArrays.find((a) => a.arrayId === arrayId);
      return array ? t(array.nameKey) : null;
    },
    [activeArrays, t]
  );

  const totalPages =
    state.kind === 'ready' ? Math.max(1, Math.ceil(state.totalCount / PAGE_SIZE)) : 1;

  const selectClass =
    'w-full px-3 py-2 bg-white border border-neutral-300 rounded text-sm text-neutral-900 focus:outline-none focus:ring-2 focus:ring-neutral-800 disabled:bg-neutral-50 disabled:text-neutral-500';

  const tabClass = (active: boolean) =>
    `flex-1 px-4 py-2 text-sm font-medium ${
      active ? 'bg-neutral-800 text-white' : 'bg-white text-neutral-700 hover:bg-neutral-50'
    }`;

  return (
    <div className="min-h-screen bg-neutral-50 pb-6">
      <AppHeader title={t('clearance.title')} showBack={true} showHome={true} onBack={onBack} />

      {state.kind === 'forbidden' ? (
        <div className="p-4">
          <div className="bg-white border border-neutral-200 rounded-lg p-4 text-sm text-neutral-700">
            {t('clearance.noPermission')}
          </div>
        </div>
      ) : (
        <div className="p-4 space-y-4">
          <div className="flex bg-white border border-neutral-300 rounded-lg overflow-hidden">
            {(['pending', 'rejected'] as const).map((value) => (
              <button
                key={value}
                type="button"
                className={tabClass(tab === value)}
                onClick={() => {
                  setTab(value);
                  setPageNumber(1);
                }}
              >
                {t(`clearance.tab.${value}`)}
              </button>
            ))}
          </div>

          <div className="grid grid-cols-2 gap-3">
            <div>
              <label className="block text-sm font-medium text-neutral-700 mb-1">
                {t('reports.maintenanceFilters.array')}
              </label>
              <select
                value={selectedArrayId}
                disabled={isLoadingHierarchy}
                onChange={(e) => {
                  setSelectedArrayId(e.target.value);
                  setSelectedMachineId('');
                  setPageNumber(1);
                }}
                className={selectClass}
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
                  setPageNumber(1);
                }}
                className={selectClass}
              >
                <option value="">{t('reports.maintenanceFilters.allMachines')}</option>
                {machinesForSelectedArray.map((machine) => (
                  <option key={machine.id} value={machine.id}>
                    {t(machine.name)}
                  </option>
                ))}
              </select>
            </div>
          </div>

          {state.kind === 'ready' && !state.canApprove && (
            <div className="bg-amber-50 border border-amber-200 rounded-lg p-3 text-sm text-neutral-700">
              {t('clearance.viewOnly')}
            </div>
          )}

          {state.kind === 'loading' && (
            <div className="text-sm text-neutral-500">{t('common.loading')}</div>
          )}

          {state.kind === 'error' && (
            <div className="bg-white border border-neutral-200 rounded-lg p-4 space-y-3">
              <div className="text-sm" style={{ color: '#b91c1c' }}>
                {t('clearance.loadFailed')}
              </div>
              <button
                type="button"
                onClick={() => (access ? loadQueue() : window.location.reload())}
                className="px-4 py-2 text-sm font-medium bg-neutral-800 text-white rounded-lg"
              >
                {t('common.retry')}
              </button>
            </div>
          )}

          {state.kind === 'ready' && state.items.length === 0 && (
            <div className="bg-white border border-neutral-200 rounded-lg p-4 text-sm text-neutral-500">
              {tab === 'pending' ? t('clearance.emptyPending') : t('clearance.emptyRejected')}
            </div>
          )}

          {state.kind === 'ready' &&
            state.items.map((item) => (
              <div key={item.id} className="bg-white border border-neutral-200 rounded-lg p-4 space-y-3">
                <div className="flex items-center justify-between gap-2">
                  <span className="text-sm font-semibold text-neutral-900">
                    {item.sourceType === 'maintenance_entry'
                      ? t('home.maintenanceLog')
                      : t('home.morningRoundV2')}
                  </span>
                  <ClearanceStatusBadge status={item.status} />
                </div>

                <div className="space-y-2 text-sm">
                  <div className="flex justify-between gap-2">
                    <span className="text-neutral-600">{t('common.date')}:</span>
                    <span className="font-medium text-neutral-900">
                      {formatDisplayDate(language, item.reportDate.slice(0, 10))}
                    </span>
                  </div>
                  {item.sourceType === 'maintenance_entry' && (
                    <>
                      <div className="flex justify-between gap-2">
                        <span className="text-neutral-600">{t('reports.maintenanceFilters.array')}:</span>
                        <span className="font-medium text-neutral-900">
                          {arrayLabel(item.arrayId) ?? t('common.notProvided')}
                        </span>
                      </div>
                      <div className="flex justify-between gap-2">
                        <span className="text-neutral-600">{t('reports.maintenanceFilters.machine')}:</span>
                        <span className="font-medium text-neutral-900">
                          {item.machineName
                            ? resolveCatalogDisplayValue(item.machineName, t)
                            : t('common.notProvided')}
                        </span>
                      </div>
                    </>
                  )}
                  <div className="flex justify-between gap-2">
                    <span className="text-neutral-600">{t('reports.submittedBy')}:</span>
                    <span className="font-medium text-neutral-900">
                      {item.submittedByName ?? t('common.notProvided')}
                    </span>
                  </div>
                  <div className="flex justify-between gap-2">
                    <span className="text-neutral-600">{t('details.submittedOn')}:</span>
                    <span className="font-medium text-neutral-900">
                      {formatDisplayDateTime(language, item.submittedAt)}
                    </span>
                  </div>
                </div>

                {item.sourceType === 'maintenance_entry' && item.description?.trim() && (
                  <div className="text-sm text-neutral-700" style={{ whiteSpace: 'pre-wrap' }}>
                    {item.description.split('\n').map((line, index) =>
                      index === 0 && item.maintenanceTypeCode === 'other'
                        ? resolveCatalogDisplayValue(line, t)
                        : line
                    ).join('\n')}
                  </div>
                )}

                {item.sourceType === 'morning_round_v2' && item.findings.length > 0 && (
                  <div className="bg-amber-50 border border-amber-200 rounded p-3 space-y-2">
                    <div className="text-xs font-semibold text-neutral-700">{t('clearance.findings')}</div>
                    {item.findings.map((finding) => (
                      <div key={finding.machineId} className="text-sm">
                        <div className="font-medium text-neutral-900">
                          {resolveCatalogDisplayValue(finding.machineName, t)}
                          {arrayLabel(finding.arrayId) ? ` · ${arrayLabel(finding.arrayId)}` : ''}
                        </div>
                        {finding.notes?.trim() && (
                          <div className="text-neutral-700" style={{ whiteSpace: 'pre-wrap' }}>
                            {finding.notes}
                          </div>
                        )}
                      </div>
                    ))}
                  </div>
                )}

                {item.status === 'rejected' && (
                  <div className="space-y-2 text-sm border-t border-neutral-200" style={{ paddingTop: 8 }}>
                    <div className="flex justify-between gap-2">
                      <span className="text-neutral-600">{t('clearance.rejectedBy')}:</span>
                      <span className="font-medium text-neutral-900">
                        {item.decidedByName ?? t('common.notProvided')}
                      </span>
                    </div>
                    {item.decidedAt && (
                      <div className="flex justify-between gap-2">
                        <span className="text-neutral-600">{t('clearance.decidedAt')}:</span>
                        <span className="font-medium text-neutral-900">
                          {formatDisplayDateTime(language, item.decidedAt)}
                        </span>
                      </div>
                    )}
                    {item.decisionNote && (
                      <div>
                        <span className="text-neutral-600">{t('clearance.rejectionNote')}: </span>
                        <span className="text-neutral-900" style={{ whiteSpace: 'pre-wrap' }}>
                          {item.decisionNote}
                        </span>
                      </div>
                    )}
                  </div>
                )}

                <button
                  type="button"
                  onClick={() => onOpenReport(item)}
                  className="w-full px-3 py-2 text-sm font-medium bg-white border border-neutral-300 rounded-lg text-neutral-700 hover:bg-neutral-50"
                >
                  {t('clearance.openReport')}
                </button>

                {item.status === 'pending' && state.canApprove && (
                  <ClearanceDecisionActions itemId={item.id} onDecided={loadQueue} showDeclaration />
                )}
              </div>
            ))}

          {state.kind === 'ready' && state.totalCount > PAGE_SIZE && (
            <div className="flex items-center justify-between gap-2">
              <button
                type="button"
                disabled={pageNumber <= 1}
                onClick={() => setPageNumber((p) => Math.max(1, p - 1))}
                className="px-4 py-2 text-sm font-medium bg-white border border-neutral-300 rounded-lg text-neutral-700 disabled:bg-neutral-300 disabled:cursor-not-allowed"
              >
                {t('clearance.previousPage')}
              </button>
              <span className="text-sm text-neutral-600">
                {t('clearance.pageOf')
                  .replace('{page}', String(pageNumber))
                  .replace('{total}', String(totalPages))}
              </span>
              <button
                type="button"
                disabled={pageNumber >= totalPages}
                onClick={() => setPageNumber((p) => p + 1)}
                className="px-4 py-2 text-sm font-medium bg-white border border-neutral-300 rounded-lg text-neutral-700 disabled:bg-neutral-300 disabled:cursor-not-allowed"
              >
                {t('clearance.nextPage')}
              </button>
            </div>
          )}
        </div>
      )}
    </div>
  );
}
