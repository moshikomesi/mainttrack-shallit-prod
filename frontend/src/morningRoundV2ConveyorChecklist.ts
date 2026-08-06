// Conveyors checklist for Morning Round V2, rendered inside the "מסועים"
// array accordion using the same pass/fail + notes UI as other lists.

import {
  MORNING_ROUND_V2_CONVEYORS_ARRAY_ID,
  MORNING_ROUND_V2_CONVEYORS_KEY,
} from './morningRoundV2Grouping';
import type { MorningRoundV2HierarchyArray } from './components/morningRound/MorningRoundV2HierarchyView';

export type MorningRoundV2ConveyorChecklistItemConfig = {
  id: string;
  translationKey: string;
};

export const MORNING_ROUND_V2_CONVEYOR_CHECKLIST_ITEMS: MorningRoundV2ConveyorChecklistItemConfig[] = [
  { id: 'conveyor-check-18', translationKey: 'check.18' },
];

export type MorningRoundV2ConveyorChecklistItemState = {
  id: string;
  translationKey: string;
  status: 'ok' | 'fail' | null;
  notes: string;
};

export function isConveyorChecklistItemId(id: string): boolean {
  return id.startsWith('conveyor-check-');
}

export function buildInitialConveyorChecklistState(): MorningRoundV2ConveyorChecklistItemState[] {
  return MORNING_ROUND_V2_CONVEYOR_CHECKLIST_ITEMS.map((item) => ({
    id: item.id,
    translationKey: item.translationKey,
    status: null,
    notes: '',
  }));
}

export function buildConveyorsHierarchyArray(
  items: MorningRoundV2ConveyorChecklistItemState[]
): MorningRoundV2HierarchyArray {
  return {
    arrayId: MORNING_ROUND_V2_CONVEYORS_ARRAY_ID,
    nameKey: MORNING_ROUND_V2_CONVEYORS_KEY,
    machines: items.map((item) => ({
      id: item.id,
      nameKey: item.translationKey,
      status: item.status,
      notes: item.notes,
    })),
  };
}

const CONVEYOR_CHECKLIST_STORAGE_PREFIX = 'mainttrack:mr-v2-conveyor:';

type StoredConveyorChecklistItem = {
  id: string;
  status: 'ok' | 'fail';
  notes: string;
};

export function saveMorningRoundV2ConveyorChecklist(
  reportId: string,
  items: MorningRoundV2ConveyorChecklistItemState[]
): void {
  const payload: StoredConveyorChecklistItem[] = items
    .filter((item): item is MorningRoundV2ConveyorChecklistItemState & { status: 'ok' | 'fail' } =>
      item.status === 'ok' || item.status === 'fail'
    )
    .map((item) => ({
      id: item.id,
      status: item.status,
      notes: item.notes.trim(),
    }));

  try {
    localStorage.setItem(`${CONVEYOR_CHECKLIST_STORAGE_PREFIX}${reportId}`, JSON.stringify(payload));
  } catch {
    // Ignore storage quota or privacy errors.
  }
}

function readConveyorChecklistRaw(reportId: string): string | null {
  const key = `${CONVEYOR_CHECKLIST_STORAGE_PREFIX}${reportId}`;
  const lowerKey = `${CONVEYOR_CHECKLIST_STORAGE_PREFIX}${reportId.toLowerCase()}`;
  try {
    return localStorage.getItem(key) ?? localStorage.getItem(lowerKey);
  } catch {
    return null;
  }
}

function normalizeStoredStatus(status: unknown): 'ok' | 'fail' | null {
  if (status == null) return null;
  const normalized = String(status).trim().toLowerCase();
  if (normalized === 'ok' || normalized === 'pass') return 'ok';
  if (normalized === 'fail') return 'fail';
  return null;
}

export function loadMorningRoundV2ConveyorChecklist(
  reportId: string
): MorningRoundV2ConveyorChecklistItemState[] {
  const defaults = buildInitialConveyorChecklistState();

  try {
    const raw = readConveyorChecklistRaw(reportId);
    if (!raw) {
      return defaults;
    }

    const stored = JSON.parse(raw) as StoredConveyorChecklistItem[];
    if (!Array.isArray(stored)) {
      return defaults;
    }

    return defaults.map((item) => {
      const match = stored.find((entry) => entry.id === item.id);
      if (!match) {
        return item;
      }

      const status = normalizeStoredStatus(match.status);
      if (!status) {
        return item;
      }

      return {
        ...item,
        status,
        notes: match.notes ?? '',
      };
    });
  } catch {
    return defaults;
  }
}
