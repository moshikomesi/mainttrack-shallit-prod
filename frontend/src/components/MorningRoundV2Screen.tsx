import { useEffect, useMemo, useState } from 'react';
import toast from 'react-hot-toast';
import { useLanguage } from '../context/LanguageContext';
import { AppHeader } from './AppHeader';
import { ReadOnlyDateBanner } from './ReadOnlyDateBanner';
import { MorningRoundV2GeneralChecklist } from './morningRound/MorningRoundV2GeneralChecklist';
import { MorningRoundV2HierarchyView } from './morningRound/MorningRoundV2HierarchyView';
import {
  getMorningRoundV2Checklist,
  submitMorningRoundV2,
} from '../services/morningRoundV2Service';
import { filterVisibleMorningRoundV2Arrays } from '../morningRoundV2Config';
import { buildInitialGeneralChecklistState, saveMorningRoundV2GeneralChecklist } from '../morningRoundV2GeneralChecklist';
import { buildInitialConveyorChecklistState, buildConveyorsHierarchyArray, isConveyorChecklistItemId, saveMorningRoundV2ConveyorChecklist } from '../morningRoundV2ConveyorChecklist';
import { groupMorningRoundV2Arrays, insertMorningRoundV2ConveyorsArray } from '../morningRoundV2Grouping';
import type { MorningRoundV2HierarchyArray } from './morningRound/MorningRoundV2HierarchyView';
import type {
  MorningRoundV2Array,
  MorningRoundV2MachineState,
  MorningRoundV2ScreenProps,
} from '../types/morningRoundV2';

function buildInitialState(arrays: MorningRoundV2Array[]): Record<string, MorningRoundV2MachineState> {
  const state: Record<string, MorningRoundV2MachineState> = {};
  for (const array of arrays) {
    for (const machine of array.machines) {
      state[machine.id] = {
        machineId: machine.id,
        status: null,
        notes: '',
      };
    }
  }
  return state;
}

export function MorningRoundV2Screen({ onSubmit }: MorningRoundV2ScreenProps) {
  const { t } = useLanguage();
  const today = new Date().toLocaleDateString('en-CA');
  const [arrays, setArrays] = useState<MorningRoundV2Array[]>([]);
  const [machineState, setMachineState] = useState<Record<string, MorningRoundV2MachineState>>({});
  const [expandedArrays, setExpandedArrays] = useState<Record<string, boolean>>({});
  const [generalChecklist, setGeneralChecklist] = useState(buildInitialGeneralChecklistState);
  const [conveyorChecklist, setConveyorChecklist] = useState(buildInitialConveyorChecklistState);
  const [isLoading, setIsLoading] = useState(false);
  const [loadError, setLoadError] = useState<string | null>(null);
  const [isSubmitting, setIsSubmitting] = useState(false);

  const visibleArrays = useMemo(() => filterVisibleMorningRoundV2Arrays(arrays), [arrays]);

  const hierarchyArrays = useMemo(() => {
    const attachState = (array: MorningRoundV2Array): MorningRoundV2HierarchyArray => ({
      arrayId: array.arrayId,
      nameKey: array.nameKey,
      machines: array.machines.map((machine) => {
        const state = machineState[machine.id];
        return {
          id: machine.id,
          nameKey: machine.nameKey,
          status: state?.status ?? null,
          notes: state?.notes ?? '',
        };
      }),
    });

    const grouped = groupMorningRoundV2Arrays(visibleArrays).map((array) => ({
      ...attachState(array),
      children: array.children?.map(attachState),
    }));

    const conveyors = buildConveyorsHierarchyArray(conveyorChecklist);
    return insertMorningRoundV2ConveyorsArray(grouped, conveyors);
  }, [visibleArrays, machineState, conveyorChecklist]);

  useEffect(() => {
    let cancelled = false;

    const load = async () => {
      try {
        setIsLoading(true);
        setLoadError(null);
        const data = await getMorningRoundV2Checklist();
        if (cancelled) return;

        const visible = filterVisibleMorningRoundV2Arrays(data);
        setArrays(data);
        setMachineState(buildInitialState(visible));
        setExpandedArrays({});
        setGeneralChecklist(buildInitialGeneralChecklistState());
        setConveyorChecklist(buildInitialConveyorChecklistState());
      } catch (err) {
        console.error(err);
        if (!cancelled) {
          setLoadError(t('messages.failedToLoadMorningRoundV2'));
        }
      } finally {
        if (!cancelled) {
          setIsLoading(false);
        }
      }
    };

    load();
    return () => {
      cancelled = true;
    };
  }, [t]);

  const totalMachines = useMemo(
    () => visibleArrays.reduce((count, array) => count + array.machines.length, 0),
    [visibleArrays]
  );

  const toggleArray = (key: string) => {
    setExpandedArrays((prev) => ({ ...prev, [key]: !prev[key] }));
  };

  const setStatus = (machineId: string, status: 'ok' | 'fail') => {
    if (isConveyorChecklistItemId(machineId)) {
      setConveyorStatus(machineId, status);
      return;
    }

    setMachineState((prev) => ({
      ...prev,
      [machineId]: {
        ...prev[machineId],
        status,
      },
    }));
  };

  const setNotes = (machineId: string, notes: string) => {
    if (isConveyorChecklistItemId(machineId)) {
      setConveyorNotes(machineId, notes);
      return;
    }

    setMachineState((prev) => ({
      ...prev,
      [machineId]: {
        ...prev[machineId],
        notes,
      },
    }));
  };

  const setGeneralStatus = (itemId: string, status: 'ok' | 'fail') => {
    setGeneralChecklist((prev) =>
      prev.map((item) => (item.id === itemId ? { ...item, status } : item))
    );
  };

  const setGeneralNotes = (itemId: string, notes: string) => {
    setGeneralChecklist((prev) =>
      prev.map((item) => (item.id === itemId ? { ...item, notes } : item))
    );
  };

  const setConveyorStatus = (itemId: string, status: 'ok' | 'fail') => {
    setConveyorChecklist((prev) =>
      prev.map((item) => (item.id === itemId ? { ...item, status } : item))
    );
  };

  const setConveyorNotes = (itemId: string, notes: string) => {
    setConveyorChecklist((prev) =>
      prev.map((item) => (item.id === itemId ? { ...item, notes } : item))
    );
  };

  const handleSubmit = async () => {
    if (isLoading || totalMachines === 0) {
      toast.error(t('morningV2.noMachines'));
      return;
    }

    const visibleMachineIds = new Set(
      visibleArrays.flatMap((array) => array.machines.map((machine) => machine.id))
    );
    const entries = Object.values(machineState).filter((item) =>
      visibleMachineIds.has(item.machineId)
    );
    const incomplete = entries.filter((item) => item.status === null);
    const incompleteGeneral = generalChecklist.filter((item) => item.status === null);
    const incompleteConveyor = conveyorChecklist.filter((item) => item.status === null);
    if (incomplete.length > 0 || incompleteGeneral.length > 0 || incompleteConveyor.length > 0) {
      toast.error(t('morningV2.validationAllStatusesRequired'));
      return;
    }

    try {
      setIsSubmitting(true);
      const result = await submitMorningRoundV2({
        timestamp: new Date().toISOString(),
        items: entries.map((item) => ({
          machineId: item.machineId,
          status: item.status as 'ok' | 'fail',
          notes: item.notes.trim(),
        })),
      });
      saveMorningRoundV2GeneralChecklist(result.id, generalChecklist);
      saveMorningRoundV2ConveyorChecklist(result.id, conveyorChecklist);
      toast.success(t('messages.reportSaved'));
      onSubmit();
    } catch (err) {
      console.error(err);
      toast.error(t('messages.saveFailed'));
    } finally {
      setIsSubmitting(false);
    }
  };

  return (
    <div className="min-h-screen bg-neutral-50 pb-24">
      <AppHeader title={t('morningV2.title')} showBack={true} showHome={true} />

      <div className="p-4 space-y-4">
        <ReadOnlyDateBanner date={today} />

        {isLoading && (
          <div className="text-sm text-neutral-500">{t('common.loading')}</div>
        )}

        {loadError && !isLoading && (
          <div className="text-sm text-red-600">{loadError}</div>
        )}

        {!isLoading && !loadError && totalMachines === 0 && (
          <div className="bg-white border border-neutral-200 rounded-lg p-4 text-sm text-neutral-600">
            {t('morningV2.noMachines')}
          </div>
        )}

        {!isLoading && !loadError && (
          <>
            <MorningRoundV2HierarchyView
              arrays={hierarchyArrays}
              expandedArrays={expandedArrays}
              onToggleArray={toggleArray}
              t={t}
              onSelectFail={(machineId) => setStatus(machineId, 'fail')}
              onSelectPass={(machineId) => setStatus(machineId, 'ok')}
              onNotesChange={setNotes}
            />

            <MorningRoundV2GeneralChecklist
              items={generalChecklist}
              t={t}
              onSelectFail={(itemId) => setGeneralStatus(itemId, 'fail')}
              onSelectPass={(itemId) => setGeneralStatus(itemId, 'ok')}
              onNotesChange={setGeneralNotes}
            />
          </>
        )}
      </div>

      <div className="fixed bottom-0 left-0 right-0 p-4 bg-white border-t border-neutral-200">
        <div className="max-w-md mx-auto">
          <button
            type="button"
            onClick={handleSubmit}
            disabled={isSubmitting || isLoading || totalMachines === 0}
            className="w-full py-4 bg-neutral-800 text-white font-semibold rounded-lg hover:bg-neutral-900 active:bg-neutral-950 transition-colors disabled:opacity-60 disabled:cursor-not-allowed"
          >
            {isSubmitting ? t('messages.loading') : t('morningV2.submit')}
          </button>
        </div>
      </div>
    </div>
  );
}
