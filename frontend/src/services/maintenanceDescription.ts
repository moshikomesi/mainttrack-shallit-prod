import type { MaintenanceTranslationsResponse } from '../types/maintenance';
import { resolveCatalogDisplayValue } from '../utils/resolveCatalogDisplayValue.ts';

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

/**
 * Description to store for a submitted "other" row.
 * Text that already starts with the V1 or V2 sentinel line is a finished description and is
 * returned as is, so a V2 description is never wrapped a second time. Any other text is
 * ordinary free text and is marked V1. Blank text stays blank.
 */
export function buildMaintenanceDescriptionForSubmit(fault: string): string {
  const text = fault.trim();
  if (!text) return '';
  const firstLine = normalizeNewlines(text).split('\n')[0];
  if (firstLine === MAINTENANCE_LOG_V1_SENTINEL || firstLine === MAINTENANCE_LOG_V2_SENTINEL) {
    return text;
  }
  return buildMaintenanceLogV1Description(text);
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

const MAINTENANCE_COMPONENT_KEY = /^maintenanceComponent\.[A-Za-z][A-Za-z0-9]*$/;

function isMaintenanceComponentKey(line: string): boolean {
  return MAINTENANCE_COMPONENT_KEY.test(line);
}

/**
 * Recognizes a V1 or V2 sentinel only when the raw first line matches it exactly,
 * the same rule as the backend. A legacy component is recognized only when the
 * trimmed first line is a maintenanceComponent.* key. Every other legacy
 * description, including one with a padded sentinel, is one narrative.
 */
export function viewMaintenanceDescription(
  description: string | null | undefined
): MaintenanceDescriptionView | null {
  if (description == null) return null;

  const lines = normalizeNewlines(description).split('\n');
  const firstLine = lines[0] ?? '';
  const legacyComponentCandidate = firstLine.trim();

  if (firstLine === MAINTENANCE_LOG_V1_SENTINEL) {
    return {
      version: 'v1',
      component: null,
      narrative: lines.slice(1).join('\n').trim(),
    };
  }

  if (firstLine === MAINTENANCE_LOG_V2_SENTINEL) {
    return {
      version: 'v2',
      component: (lines[1] ?? '').trim(),
      narrative: lines.slice(2).join('\n').trim(),
    };
  }

  if (isMaintenanceComponentKey(legacyComponentCandidate)) {
    return {
      version: 'legacy',
      component: legacyComponentCandidate,
      narrative: lines.slice(1).join('\n').trim(),
    };
  }

  const narrative = normalizeNewlines(description).trim();
  if (!narrative) return null;
  return {
    version: 'legacy',
    component: null,
    narrative,
  };
}

export type MaintenanceDescriptionDisplayVersion = MaintenanceDescriptionVersion | 'plain';

export type MaintenanceDescriptionDisplay = {
  version: MaintenanceDescriptionDisplayVersion;
  /** Localized component label. Null when this report has no component line. */
  componentLabel: string | null;
  /** Free-text narrative in its stored language. */
  narrative: string;
};

function isInternalMarker(value: string): boolean {
  return value.startsWith('maintenanceLog.') || value.startsWith('maintenanceComponent.');
}

/**
 * Catalog component keys go through the existing translation function.
 * A missing key becomes the shared "not provided" label. Free text is unchanged.
 * Format sentinels are never returned.
 */
export function displayMaintenanceComponentLabel(
  component: string | null | undefined,
  t: (key: string) => string
): string {
  const trimmed = component?.trim() ?? '';
  if (!trimmed || trimmed.startsWith('maintenanceLog.')) return '';

  const resolved = resolveCatalogDisplayValue(trimmed, t);
  if (!resolved || isInternalMarker(resolved)) {
    const fallback = t('common.notProvided');
    return isInternalMarker(fallback) ? '' : fallback;
  }
  return resolved;
}

/**
 * Display model for a stored maintenance description.
 * Marked V1 and V2 reports are recognized from their sentinel even when the
 * maintenance type is not "other". A legacy component key is localized on its
 * own; the narrative never includes that key or its label.
 */
export function formatMaintenanceDescriptionDisplay(
  description: string | null | undefined,
  maintenanceTypeCode: string | null | undefined,
  t: (key: string) => string
): MaintenanceDescriptionDisplay {
  void maintenanceTypeCode;
  const view = viewMaintenanceDescription(description);
  if (!view) {
    return { version: 'plain', componentLabel: null, narrative: '' };
  }

  if (view.version === 'v1') {
    return { version: 'v1', componentLabel: null, narrative: view.narrative };
  }

  if (view.version === 'v2' || view.component) {
    const componentLabel = displayMaintenanceComponentLabel(view.component, t);
    return {
      version: view.version,
      componentLabel: componentLabel || null,
      narrative: view.narrative,
    };
  }

  return {
    version: 'legacy',
    componentLabel: null,
    narrative: view.narrative,
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

export type LocalizedMaintenanceDescription = MaintenanceDescriptionDisplay & {
  /** True when `narrative` is the completed translation instead of the stored text. */
  translated: boolean;
};

/**
 * The single description to show: localized component label plus one narrative.
 * A completed translation for the UI language replaces the stored narrative. Any other
 * outcome (no response, pending, unavailable, missing, empty, identical) keeps the original.
 */
export function localizeMaintenanceDescription(
  description: string | null | undefined,
  maintenanceTypeCode: string | null | undefined,
  response: MaintenanceTranslationsResponse | null | undefined,
  language: string,
  t: (key: string) => string
): LocalizedMaintenanceDescription {
  const display = formatMaintenanceDescriptionDisplay(description, maintenanceTypeCode, t);
  const translation = selectNarrativeTranslation(response, display.narrative, language);
  return translation === null
    ? { ...display, translated: false }
    : { ...display, narrative: translation, translated: true };
}
