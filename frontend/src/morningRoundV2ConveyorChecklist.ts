// Standalone "Conveyor Inspection" section for Morning Round V2.
//
// This is intentionally NOT tied to any array or machine hierarchy — it is a
// flat checklist, presented as its own section between the Washing Array and
// Packing House sections. Items reuse the existing conveyor-related entries
// from the legacy checklist translation set (unchanged, exactly as defined
// today).

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

export function buildInitialConveyorChecklistState(): MorningRoundV2ConveyorChecklistItemState[] {
  return MORNING_ROUND_V2_CONVEYOR_CHECKLIST_ITEMS.map((item) => ({
    id: item.id,
    translationKey: item.translationKey,
    status: null,
    notes: '',
  }));
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

export function loadMorningRoundV2ConveyorChecklist(
  reportId: string
): MorningRoundV2ConveyorChecklistItemState[] {
  const defaults = buildInitialConveyorChecklistState();

  try {
    const raw = localStorage.getItem(`${CONVEYOR_CHECKLIST_STORAGE_PREFIX}${reportId}`);
    if (!raw) {
      return defaults;
    }

    const stored = JSON.parse(raw) as StoredConveyorChecklistItem[];
    return defaults.map((item) => {
      const match = stored.find((entry) => entry.id === item.id);
      if (!match) {
        return item;
      }

      return {
        ...item,
        status: match.status,
        notes: match.notes ?? '',
      };
    });
  } catch {
    return defaults;
  }
}
