import type { ReactNode } from 'react';
import { Check, X } from 'lucide-react';
import './morningRoundV2MachineStatus.css';

export type MorningRoundV2MachineStatusValue = 'ok' | 'fail' | null;

type Props = {
  status: MorningRoundV2MachineStatusValue;
  failAriaLabel: string;
  passAriaLabel: string;
  onSelectFail?: () => void;
  onSelectPass?: () => void;
  readOnly?: boolean;
};

function buttonClass(isActive: boolean, activeModifier: string): string {
  return ['mr-v2-status__btn', isActive ? activeModifier : ''].filter(Boolean).join(' ');
}

function StatusControl({
  isActive,
  activeModifier,
  ariaLabel,
  ariaPressed,
  onClick,
  readOnly,
  children,
}: {
  isActive: boolean;
  activeModifier: string;
  ariaLabel: string;
  ariaPressed: boolean;
  onClick?: () => void;
  readOnly?: boolean;
  children: ReactNode;
}) {
  const className = `${buttonClass(isActive, activeModifier)}${readOnly ? ' mr-v2-status__btn--readonly' : ''}`;

  if (readOnly) {
    return (
      <div
        className={className}
        aria-label={ariaLabel}
        aria-pressed={ariaPressed}
        role="img"
      >
        {children}
      </div>
    );
  }

  return (
    <button
      type="button"
      aria-label={ariaLabel}
      aria-pressed={ariaPressed}
      onClick={onClick}
      className={className}
    >
      {children}
    </button>
  );
}

export function MorningRoundV2MachineStatus({
  status,
  failAriaLabel,
  passAriaLabel,
  onSelectFail,
  onSelectPass,
  readOnly = false,
}: Props) {
  return (
    <div className="mr-v2-status" role="group">
      <StatusControl
        isActive={status === 'fail'}
        activeModifier="mr-v2-status__btn--fail-active"
        ariaLabel={failAriaLabel}
        ariaPressed={status === 'fail'}
        onClick={onSelectFail}
        readOnly={readOnly}
      >
        <X className="mr-v2-status__icon" strokeWidth={2.5} aria-hidden="true" />
      </StatusControl>
      <StatusControl
        isActive={status === 'ok'}
        activeModifier="mr-v2-status__btn--pass-active"
        ariaLabel={passAriaLabel}
        ariaPressed={status === 'ok'}
        onClick={onSelectPass}
        readOnly={readOnly}
      >
        <Check className="mr-v2-status__icon" strokeWidth={2.5} aria-hidden="true" />
      </StatusControl>
    </div>
  );
}
