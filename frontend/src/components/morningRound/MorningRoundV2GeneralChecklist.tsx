import { MorningRoundV2MachineStatus } from '../MorningRoundV2MachineStatus';
import type { MorningRoundV2MachineStatusValue } from '../MorningRoundV2MachineStatus';

export type MorningRoundV2GeneralChecklistItem = {
  id: string;
  translationKey: string;
  status: MorningRoundV2MachineStatusValue;
  notes: string;
};

type Props = {
  items: MorningRoundV2GeneralChecklistItem[];
  t: (key: string) => string;
  readOnly?: boolean;
  onSelectFail?: (itemId: string) => void;
  onSelectPass?: (itemId: string) => void;
  onNotesChange?: (itemId: string, notes: string) => void;
  // Translation key for the section heading. Defaults to the general
  // checklist title so existing callers are unaffected.
  titleKey?: string;
};

export function MorningRoundV2GeneralChecklist({
  items,
  t,
  readOnly = false,
  onSelectFail,
  onSelectPass,
  onNotesChange,
  titleKey = 'morningV2.generalChecklist',
}: Props) {
  return (
    <section className="bg-white border border-neutral-200 rounded-lg overflow-hidden">
      <div className="px-4 py-3 bg-neutral-100 border-b border-neutral-200">
        <h2 className="text-sm font-semibold text-neutral-900">
          {t(titleKey)}
        </h2>
      </div>

      <div className="divide-y divide-neutral-200">
        {items.map((item) => (
          <div key={item.id} className="p-4 space-y-3">
            <p className="text-sm font-medium text-neutral-900">
              {t(item.translationKey)}
            </p>

            <MorningRoundV2MachineStatus
              status={item.status}
              failAriaLabel={t('morningV2.statusFail')}
              passAriaLabel={t('morningV2.statusPass')}
              readOnly={readOnly}
              onSelectFail={readOnly ? undefined : () => onSelectFail?.(item.id)}
              onSelectPass={readOnly ? undefined : () => onSelectPass?.(item.id)}
            />

            {readOnly ? (
              item.notes ? (
                <div className="text-sm text-neutral-600 bg-amber-50 border border-amber-200 rounded px-2 py-1">
                  {item.notes}
                </div>
              ) : null
            ) : (
              <input
                type="text"
                value={item.notes}
                onChange={(e) => onNotesChange?.(item.id, e.target.value)}
                placeholder={t('morningV2.notes')}
                className="w-full px-3 py-2 bg-neutral-50 border border-neutral-200 rounded-lg text-sm text-neutral-900 placeholder:text-neutral-400 focus:outline-none focus:ring-2 focus:ring-neutral-800 focus:bg-white"
              />
            )}
          </div>
        ))}
      </div>
    </section>
  );
}
