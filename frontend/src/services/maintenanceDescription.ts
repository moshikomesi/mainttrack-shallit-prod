import type { MaintenanceTranslationsResponse } from '../types/maintenance';

export const MAINTENANCE_LOG_V1_SENTINEL = 'maintenanceLog.v1';
export const MAINTENANCE_LOG_V2_SENTINEL = 'maintenanceLog.v2';
export const MAINTENANCE_LOG_V2_OTHER_COMPONENT = '__OTHER__';

export type MaintenanceLogV2ComponentSelection =
  | { kind: 'mapped'; code: string; nameKey: string }
  | { kind: 'other'; text: string };

export type MaintenanceDescriptionVersion = 'legacy' | 'v1' | 'v2';

export type MaintenanceDescriptionView = {
  version: MaintenanceDescriptionVersion;
  /** Original-language component line. Null for a marked V1 report. */
  component: string | null;
  /** Free-text narrative. This is the only part that may be translated. */
  narrative: string;
};

function normalizeNewlines(value: string): string {
  return value.replace(/\r\n/g, '\n').replace(/\r/g, '\n');
}

export function buildMaintenanceLogV1Description(fault: string): string {
  const narrative = fault.trim();
  if (!narrative) return '';
  return `${MAINTENANCE_LOG_V1_SENTINEL}\n${narrative}`;
}

export function buildMaintenanceLogV2Description(
  component: MaintenanceLogV2ComponentSelection,
  faultDescription: string
): string {
  const componentLine =
    component.kind === 'other'
      ? component.text.trim() || 'maintenanceComponent.other'
      : component.nameKey;
  const fault = faultDescription.trim();
  const lines = [MAINTENANCE_LOG_V2_SENTINEL, componentLine];
  if (fault) lines.push(fault);
  return lines.join('\n');
}

/**
 * Recognizes an exact first-line sentinel. Unmarked text keeps the historical
 * first-line component and remaining-lines details split.
 */
export function viewMaintenanceDescription(
  description: string | null | undefined
): MaintenanceDescriptionView | null {
  if (description == null) return null;

  const lines = normalizeNewlines(description).split('\n');
  const first = lines[0] ?? '';

  if (first === MAINTENANCE_LOG_V1_SENTINEL) {
    return {
      version: 'v1',
      component: null,
      narrative: lines.slice(1).join('\n').trim(),
    };
  }

  if (first === MAINTENANCE_LOG_V2_SENTINEL) {
    return {
      version: 'v2',
      component: (lines[1] ?? '').trim(),
      narrative: lines.slice(2).join('\n').trim(),
    };
  }

  const component = first.trim();
  if (!component) return null;
  return {
    version: 'legacy',
    component,
    narrative: lines.slice(1).join('\n').trim(),
  };
}

/**
 * Completed translation of the narrative into the UI language. Null means show only the
 * original narrative: the response belongs to another narrative, or the translation is
 * pending, unavailable, missing, or identical to it.
 */
export function selectNarrativeTranslation(
  response: MaintenanceTranslationsResponse | null | undefined,
  originalNarrative: string,
  language: string
): string | null {
  const narrative = originalNarrative.trim();
  if (!response || !narrative || response.originalText?.trim() !== narrative) return null;
  const item = response.translations?.find((candidate) => candidate.language === language);
  if (item?.status !== 'completed') return null;
  const text = item.text?.trim() ?? '';
  if (!text || text === narrative) return null;
  return text;
}
