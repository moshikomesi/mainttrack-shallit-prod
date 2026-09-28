export const MAINTENANCE_LOG_V2_OTHER_COMPONENT = '__OTHER__';

export type MaintenanceLogV2ComponentSelection =
  | { kind: 'mapped'; code: string; nameKey: string }
  | { kind: 'other'; text: string };

export function buildMaintenanceLogV2Description(
  component: MaintenanceLogV2ComponentSelection,
  faultDescription: string
): string {
  const lines: string[] = [];

  if (component.kind === 'other') {
    // Empty free-text still needs a non-empty first line for type "other".
    // Store the catalog key so reports localize it like mapped components.
    lines.push(component.text.trim() || 'maintenanceComponent.other');
  } else {
    // Store the translation key (not the localized label) so report viewers can
    // render the component in the user's current language.
    lines.push(component.nameKey);
  }

  const fault = faultDescription.trim();
  if (fault) {
    lines.push(fault);
  }

  return lines.join('\n');
}
