import type { HierarchyArray } from '../services/hierarchyService';

type MaintenanceReportArraySource = {
  arrayId?: string | null;
  machineId: string;
};

/**
 * Resolves the display label for a maintenance report's array:
 * 1. Use report.arrayId when present (derived from machine at read time).
 * 2. Otherwise resolve from the machine's position in the hierarchy.
 * 3. Return empty only when neither yields a named array.
 */
export function resolveMaintenanceReportArrayLabel(
  report: MaintenanceReportArraySource,
  hierarchy: HierarchyArray[],
  t: (key: string) => string
): string {
  if (report.arrayId) {
    const fromReport = hierarchy.find((array) => array.arrayId === report.arrayId);
    if (fromReport?.arrayId) {
      return t(fromReport.nameKey);
    }
  }

  for (const array of hierarchy) {
    if (array.arrayId == null) continue;
    if (array.machines.some((machine) => machine.id === report.machineId)) {
      return t(array.nameKey);
    }
  }

  return '';
}
