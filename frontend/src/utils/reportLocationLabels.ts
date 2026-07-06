import type { Language } from '../i18n/translations';
import { formatDisplayDate } from './formatDate';

function interpolate(template: string, values: Record<string, string>): string {
  return Object.entries(values).reduce(
    (result, [key, value]) => result.replace(`{${key}}`, value),
    template
  );
}

export function reportDateLocationLabel(
  t: (key: string) => string,
  language: Language,
  date: string
): string {
  return interpolate(t('reports.location.reportDate'), {
    date: formatDisplayDate(language, date.slice(0, 10)),
  });
}

export function arrayLocationLabel(t: (key: string) => string, name: string): string {
  return interpolate(t('reports.location.array'), { name });
}

export function machineLocationLabel(t: (key: string) => string, name: string): string {
  return interpolate(t('reports.location.machine'), { name });
}

export function checklistItemLocationLabel(t: (key: string) => string, name: string): string {
  return interpolate(t('reports.location.checklistItem'), { name });
}

export function taskLocationLabel(t: (key: string) => string, name: string): string {
  return interpolate(t('reports.location.task'), { name });
}
