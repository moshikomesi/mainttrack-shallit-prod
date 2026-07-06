export const HIDDEN_MORNING_ROUND_V2_ARRAY_KEYS = new Set([
  'array.unassigned',
  'array.thai_dorms',
  'array.ginoshar',
]);

export function filterVisibleMorningRoundV2Arrays<T extends { nameKey: string }>(
  arrays: T[]
): T[] {
  return arrays.filter((array) => !HIDDEN_MORNING_ROUND_V2_ARRAY_KEYS.has(array.nameKey));
}
