// Presentation-only grouping for the Morning Round V2 screen.
//
// This does NOT change array ids, machine mappings, HierarchyService, the
// database, or any API. It only re-arranges/nests whichever arrays are
// already returned by the existing Morning Round V2 checklist endpoint so
// they render in the requested top-level order and grouping.

export const MORNING_ROUND_V2_WASHING_KEY = 'array.washing_system';

// Virtual group label reused from the existing "Packing House" translation.
export const MORNING_ROUND_V2_PACKING_GROUP_KEY = 'array.packing_house';

export const MORNING_ROUND_V2_CONVEYORS_KEY = 'array.conveyors';
export const MORNING_ROUND_V2_CONVEYORS_ARRAY_ID = 'group:conveyors';

// New virtual group label for the merged Cooling Array parent.
export const MORNING_ROUND_V2_COOLING_GROUP_KEY = 'array.cooling_group';

// The two arrays that should be nested under the "Cooling Array" parent.
const COOLING_MEMBER_KEYS = new Set(['array.water_cooling_system', 'array.rooms']);

// Any array considered part of the "Packing House" section. Includes the
// existing flat packing_house array plus the finer-grained production-floor
// arrays, if/when those exist for a given tenant.
const PACKING_MEMBER_KEYS = new Set([
  'array.packing_house',
  'array.production.washing',
  'array.production.polisher',
  'array.production.hopper_feeding',
  'array.production.cooling',
  'array.production.general_conveyor',
  'array.production.sorting_processing',
  'array.production.cooling_fluid',
  'array.production.wear_maintenance',
]);

export type MorningRoundV2GroupableArray<TMachine> = {
  arrayId: string | null;
  nameKey: string;
  machines: TMachine[];
};

export type MorningRoundV2GroupedArray<TMachine> = MorningRoundV2GroupableArray<TMachine> & {
  children?: MorningRoundV2GroupedArray<TMachine>[];
};

/**
 * Reorders and nests the visible Morning Round V2 arrays for display:
 *   1) Washing Array
 *   2) Packing House (existing packing-related arrays nested underneath,
 *      only when there is more than one — otherwise shown as-is)
 *   3) Cooling Array (Water Cooling + Room Cooling nested underneath)
 *   4) Anything else, in its original order
 */
export function groupMorningRoundV2Arrays<TMachine>(
  arrays: MorningRoundV2GroupableArray<TMachine>[]
): MorningRoundV2GroupedArray<TMachine>[] {
  const washing = arrays.filter((array) => array.nameKey === MORNING_ROUND_V2_WASHING_KEY);
  const coolingMembers = arrays.filter((array) => COOLING_MEMBER_KEYS.has(array.nameKey));
  const packingMembers = arrays.filter((array) => PACKING_MEMBER_KEYS.has(array.nameKey));

  const usedNameKeys = new Set([
    ...washing.map((a) => a.nameKey),
    ...coolingMembers.map((a) => a.nameKey),
    ...packingMembers.map((a) => a.nameKey),
  ]);
  const rest = arrays.filter((array) => !usedNameKeys.has(array.nameKey));

  const result: MorningRoundV2GroupedArray<TMachine>[] = [...washing];

  if (packingMembers.length > 1) {
    result.push({
      arrayId: 'group:packing_house',
      nameKey: MORNING_ROUND_V2_PACKING_GROUP_KEY,
      machines: [],
      children: packingMembers,
    });
  } else if (packingMembers.length === 1) {
    result.push(packingMembers[0]);
  }

  if (coolingMembers.length > 1) {
    result.push({
      arrayId: 'group:cooling',
      nameKey: MORNING_ROUND_V2_COOLING_GROUP_KEY,
      machines: [],
      children: coolingMembers,
    });
  } else if (coolingMembers.length === 1) {
    result.push(coolingMembers[0]);
  }

  return [...result, ...rest];
}

/** Inserts the presentation-only Conveyors checklist array before Cooling. */
export function insertMorningRoundV2ConveyorsArray<TMachine>(
  arrays: MorningRoundV2GroupedArray<TMachine>[],
  conveyors: MorningRoundV2GroupedArray<TMachine>
): MorningRoundV2GroupedArray<TMachine>[] {
  if (arrays.some((array) => array.arrayId === MORNING_ROUND_V2_CONVEYORS_ARRAY_ID)) {
    return arrays;
  }

  const coolingIndex = arrays.findIndex(
    (array) =>
      array.nameKey === MORNING_ROUND_V2_COOLING_GROUP_KEY ||
      COOLING_MEMBER_KEYS.has(array.nameKey)
  );

  if (coolingIndex !== -1) {
    return [...arrays.slice(0, coolingIndex), conveyors, ...arrays.slice(coolingIndex)];
  }

  return [...arrays, conveyors];
}
