import { translations, type Language } from '../i18n/translations';

/**
 * Catalog/entity translation key prefixes. Values for these keys are language
 * variants of the same concept and may be reverse-resolved for display.
 */
const CATALOG_PREFIXES = [
  'maintenanceComponent.',
  'machine.',
  'array.',
  'maintenanceType.',
] as const;

/** Lower number = preferred when multiple keys share the same label. */
const PREFIX_PRIORITY: Record<string, number> = {
  'maintenanceComponent.': 0,
  'machine.': 1,
  'array.': 2,
  'maintenanceType.': 3,
};

const LANGUAGES: Language[] = ['en', 'he', 'th'];

function isCatalogKey(key: string): boolean {
  return CATALOG_PREFIXES.some((prefix) => key.startsWith(prefix));
}

function prefixPriority(key: string): number {
  for (const [prefix, priority] of Object.entries(PREFIX_PRIORITY)) {
    if (key.startsWith(prefix)) return priority;
  }
  return 99;
}

function normalizeLabel(value: string): string {
  return value.replace(/\s+/g, ' ').trim();
}

/** Map normalized label (any language) → catalog translation key. Built once. */
let reverseIndex: Map<string, string> | null = null;

function getReverseIndex(): Map<string, string> {
  if (reverseIndex) return reverseIndex;

  const map = new Map<string, string>();
  for (const [key, entry] of Object.entries(translations)) {
    if (!isCatalogKey(key) || !entry) continue;
    for (const lang of LANGUAGES) {
      const label = entry[lang];
      if (typeof label !== 'string') continue;
      const normalized = normalizeLabel(label);
      if (!normalized) continue;
      const existing = map.get(normalized);
      if (!existing || prefixPriority(key) < prefixPriority(existing)) {
        map.set(normalized, key);
      }
    }
  }

  reverseIndex = map;
  return map;
}

/**
 * Resolve a stored catalog label or translation key to its catalog key.
 * Returns null for free-text / unknown values.
 */
export function resolveCatalogKey(value: string | null | undefined): string | null {
  if (value == null) return null;
  const trimmed = value.trim();
  if (!trimmed) return null;

  if (isCatalogKey(trimmed) && translations[trimmed]) {
    return trimmed;
  }

  return getReverseIndex().get(normalizeLabel(trimmed)) ?? null;
}

/**
 * Resolve a stored catalog label or translation key to the current UI language.
 * Free-text that does not match any catalog entry is returned unchanged.
 */
export function resolveCatalogDisplayValue(
  value: string | null | undefined,
  t: (key: string) => string
): string {
  if (value == null) return '';
  const trimmed = value.trim();
  if (!trimmed) return value;

  const key = resolveCatalogKey(trimmed);
  if (key) {
    return t(key);
  }

  return value;
}
