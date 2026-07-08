export type MorningRoundV2GeneralChecklistItemConfig = {
  id: string;
  translationKey: string;
};

export const MORNING_ROUND_V2_GENERAL_CHECKLIST_ITEMS: MorningRoundV2GeneralChecklistItemConfig[] = [
  { id: 'general-check-34', translationKey: 'check.34' },
];

// Removed from the active Morning Round V2 UI checklist. Kept here only so
// previously submitted reports that recorded data for these items keep
// displaying exactly as submitted (see loadMorningRoundV2GeneralChecklist).
const LEGACY_GENERAL_CHECKLIST_ITEMS: MorningRoundV2GeneralChecklistItemConfig[] = [
  { id: 'general-check-32', translationKey: 'check.32' },
  { id: 'general-check-35', translationKey: 'check.35' },
  { id: 'general-check-36', translationKey: 'check.36' },
];

export type MorningRoundV2GeneralChecklistItemState = {
  id: string;
  translationKey: string;
  status: 'ok' | 'fail' | null;
  notes: string;
};

export function buildInitialGeneralChecklistState(): MorningRoundV2GeneralChecklistItemState[] {
  return MORNING_ROUND_V2_GENERAL_CHECKLIST_ITEMS.map((item) => ({
    id: item.id,
    translationKey: item.translationKey,
    status: null,
    notes: '',
  }));
}

const GENERAL_CHECKLIST_STORAGE_PREFIX = 'mainttrack:mr-v2-general:';

type StoredGeneralChecklistItem = {
  id: string;
  status: 'ok' | 'fail';
  notes: string;
};

export function saveMorningRoundV2GeneralChecklist(
  reportId: string,
  items: MorningRoundV2GeneralChecklistItemState[]
): void {
  const payload: StoredGeneralChecklistItem[] = items
    .filter((item): item is MorningRoundV2GeneralChecklistItemState & { status: 'ok' | 'fail' } =>
      item.status === 'ok' || item.status === 'fail'
    )
    .map((item) => ({
      id: item.id,
      status: item.status,
      notes: item.notes.trim(),
    }));

  try {
    localStorage.setItem(`${GENERAL_CHECKLIST_STORAGE_PREFIX}${reportId}`, JSON.stringify(payload));
  } catch {
    // Ignore storage quota or privacy errors.
  }
}

export function loadMorningRoundV2GeneralChecklist(
  reportId: string
): MorningRoundV2GeneralChecklistItemState[] {
  const defaults = buildInitialGeneralChecklistState();

  try {
    const raw = localStorage.getItem(`${GENERAL_CHECKLIST_STORAGE_PREFIX}${reportId}`);
    if (!raw) {
      return defaults;
    }

    const stored = JSON.parse(raw) as StoredGeneralChecklistItem[];
    const mappedDefaults = defaults.map((item) => {
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

    // Legacy items are only re-attached when this specific report actually
    // recorded data for them, so newly submitted reports (which never show
    // these items) remain unaffected.
    const legacyWithData = LEGACY_GENERAL_CHECKLIST_ITEMS.reduce<MorningRoundV2GeneralChecklistItemState[]>(
      (acc, item) => {
        const match = stored.find((entry) => entry.id === item.id);
        if (match) {
          acc.push({
            id: item.id,
            translationKey: item.translationKey,
            status: match.status,
            notes: match.notes ?? '',
          });
        }
        return acc;
      },
      []
    );

    return [...mappedDefaults, ...legacyWithData];
  } catch {
    return defaults;
  }
}
