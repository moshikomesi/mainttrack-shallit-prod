import { ChevronDown, ChevronUp } from 'lucide-react';
import { MorningRoundV2MachineStatus } from '../MorningRoundV2MachineStatus';
import type { MorningRoundV2MachineStatusValue } from '../MorningRoundV2MachineStatus';
import { MORNING_ROUND_V2_CONVEYORS_ARRAY_ID } from '../../morningRoundV2Grouping';

export type MorningRoundV2HierarchyMachine = {
  id: string;
  nameKey: string;
  status: MorningRoundV2MachineStatusValue;
  notes: string;
};

export type MorningRoundV2HierarchyArray = {
  arrayId: string | null;
  nameKey: string;
  machines: MorningRoundV2HierarchyMachine[];
  // Optional nested arrays for presentation-only grouping (e.g. a "Cooling
  // Array" parent containing "Water Cooling" and "Room Cooling"). When
  // present, this section renders as an expandable group of sub-sections
  // instead of a flat machine list.
  children?: MorningRoundV2HierarchyArray[];
};

function countMachinesRecursively(array: MorningRoundV2HierarchyArray): number {
  const own = array.machines.length;
  const nested = array.children?.reduce((sum, child) => sum + countMachinesRecursively(child), 0) ?? 0;
  return own + nested;
}

type Props = {
  arrays: MorningRoundV2HierarchyArray[];
  expandedArrays: Record<string, boolean>;
  onToggleArray: (key: string) => void;
  t: (key: string) => string;
  readOnly?: boolean;
  onSelectFail?: (machineId: string) => void;
  onSelectPass?: (machineId: string) => void;
  onNotesChange?: (machineId: string, notes: string) => void;
  onArrayFocus?: (arrayKey: string, arrayNameKey: string) => void;
  onMachineFocus?: (machineId: string, machineNameKey: string) => void;
};

export function MorningRoundV2HierarchyView({
  arrays,
  expandedArrays,
  onToggleArray,
  t,
  readOnly = false,
  onSelectFail,
  onSelectPass,
  onNotesChange,
  onArrayFocus,
  onMachineFocus,
}: Props) {
  return (
    <>
      {arrays.map((array) => {
        const arrayKey = array.arrayId ?? 'unassigned';
        const isExpanded = expandedArrays[arrayKey] ?? false;
        const isConveyorsArray = array.arrayId === MORNING_ROUND_V2_CONVEYORS_ARRAY_ID;

        return (
          <section
            key={arrayKey}
            className="bg-white border border-neutral-200 rounded-lg overflow-hidden"
          >
            <button
              type="button"
              onClick={() => {
                const willExpand = !isExpanded;
                onToggleArray(arrayKey);
                if (willExpand) {
                  onArrayFocus?.(arrayKey, array.nameKey);
                }
              }}
              className="w-full flex items-center justify-between gap-3 px-4 py-3 text-start bg-neutral-100 border-b border-neutral-200"
            >
              <div>
                <h2 className="text-sm font-semibold text-neutral-900">
                  {t(array.nameKey)}
                </h2>
                <p className="text-xs text-neutral-500 mt-0.5">
                  {countMachinesRecursively(array)} {t('morningV2.machines')}
                </p>
              </div>
              {isExpanded ? (
                <ChevronUp className="w-5 h-5 text-neutral-600 flex-shrink-0" />
              ) : (
                <ChevronDown className="w-5 h-5 text-neutral-600 flex-shrink-0" />
              )}
            </button>

            {isExpanded && array.children && array.children.length > 0 && (
              <div className="divide-y divide-neutral-200 bg-neutral-50 p-2 space-y-2">
                <MorningRoundV2HierarchyView
                  arrays={array.children}
                  expandedArrays={expandedArrays}
                  onToggleArray={onToggleArray}
                  t={t}
                  readOnly={readOnly}
                  onSelectFail={onSelectFail}
                  onSelectPass={onSelectPass}
                  onNotesChange={onNotesChange}
                  onArrayFocus={onArrayFocus}
                  onMachineFocus={onMachineFocus}
                />
              </div>
            )}

            {isExpanded && (!array.children || array.children.length === 0) && (
              <div className="divide-y divide-neutral-200">
                {array.machines.map((machine) => (
                  <div
                    key={machine.id}
                    className="p-4 space-y-3"
                    onClick={() => onMachineFocus?.(machine.id, machine.nameKey)}
                  >
                    {!isConveyorsArray && (
                      <p className="text-sm font-medium text-neutral-900">
                        {t(machine.nameKey)}
                      </p>
                    )}

                    <MorningRoundV2MachineStatus
                      status={machine.status}
                      failAriaLabel={t('morningV2.statusFail')}
                      passAriaLabel={t('morningV2.statusPass')}
                      readOnly={readOnly}
                      onSelectFail={
                        readOnly ? undefined : () => onSelectFail?.(machine.id)
                      }
                      onSelectPass={
                        readOnly ? undefined : () => onSelectPass?.(machine.id)
                      }
                    />

                    {readOnly ? (
                      machine.notes ? (
                        <div className="text-sm text-neutral-600 bg-amber-50 border border-amber-200 rounded px-2 py-1">
                          {machine.notes}
                        </div>
                      ) : null
                    ) : (
                      <input
                        type="text"
                        value={machine.notes}
                        onChange={(e) => onNotesChange?.(machine.id, e.target.value)}
                        placeholder={t('morningV2.notes')}
                        className="w-full px-3 py-2 bg-neutral-50 border border-neutral-200 rounded-lg text-sm text-neutral-900 placeholder:text-neutral-400 focus:outline-none focus:ring-2 focus:ring-neutral-800 focus:bg-white"
                      />
                    )}
                  </div>
                ))}
              </div>
            )}
          </section>
        );
      })}
    </>
  );
}
