import type { Language } from '../i18n/translations';

export function languageToLocale(language: Language): string {
  switch (language) {
    case 'he':
      return 'he-IL';
    case 'th':
      return 'th-TH';
    default:
      return 'en-US';
  }
}

function parseDateInput(dateStr: string): Date {
  if (/^\d{4}-\d{2}-\d{2}$/.test(dateStr)) {
    const [year, month, day] = dateStr.split('-').map(Number);
    return new Date(year, month - 1, day);
  }
  return new Date(dateStr);
}

type DateFormatStyle = 'short' | 'long';

export function formatDisplayDate(
  language: Language,
  dateStr: string,
  style: DateFormatStyle = 'short'
): string {
  const date = parseDateInput(dateStr);
  if (Number.isNaN(date.getTime())) return dateStr;

  const locale = languageToLocale(language);
  if (style === 'long') {
    return date.toLocaleDateString(locale, {
      weekday: 'long',
      year: 'numeric',
      month: 'long',
      day: 'numeric',
    });
  }

  return date.toLocaleDateString(locale, {
    weekday: 'short',
    year: 'numeric',
    month: 'short',
    day: 'numeric',
  });
}

/** Read-only form date (e.g. DD/MM/YYYY per locale). */
export function formatFormDate(language: Language, dateStr: string): string {
  const date = parseDateInput(dateStr);
  if (Number.isNaN(date.getTime())) return dateStr;

  return date.toLocaleDateString(languageToLocale(language), {
    day: '2-digit',
    month: '2-digit',
    year: 'numeric',
  });
}

export function formatDisplayDateTime(language: Language, dateStr: string): string {
  const date = parseDateInput(dateStr);
  if (Number.isNaN(date.getTime())) return dateStr;

  return date.toLocaleString(languageToLocale(language), {
    year: 'numeric',
    month: 'short',
    day: 'numeric',
    hour: '2-digit',
    minute: '2-digit',
    hour12: false,
  });
}
