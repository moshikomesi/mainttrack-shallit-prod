import type { CSSProperties, ReactNode } from 'react';
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

/** Defensive normalize for API / localStorage values before comparing for active UI. */
export function normalizeMorningRoundV2Status(
  status: string | null | undefined
): MorningRoundV2MachineStatusValue {
  if (status == null) return null;
  const normalized = String(status).trim().toLowerCase();
  if (normalized === 'ok' || normalized === 'pass') return 'ok';
  if (normalized === 'fail') return 'fail';
  return null;
}

function buttonClass(isActive: boolean, activeModifier: string): string {
  return ['mr-v2-status__btn', isActive ? activeModifier : ''].filter(Boolean).join(' ');
}

/** Inline fallback so selected state stays visible even if CSS classes fail to apply. */
function activeReadOnlyStyle(kind: 'pass' | 'fail'): CSSProperties {
  if (kind === 'fail') {
    return {
      backgroundColor: '#dc2626',
      borderColor: '#dc2626',
      color: '#ffffff',
      boxShadow: '0 4px 6px -1px rgba(0, 0, 0, 0.15)',
    };
  }
  return {
    backgroundColor: '#16a34a',
    borderColor: '#16a34a',
    color: '#ffffff',
    boxShadow: '0 4px 6px -1px rgba(0, 0, 0, 0.15)',
  };
}

function StatusControl({
  isActive,
  activeModifier,
  activeStyle,
  ariaLabel,
  ariaPressed,
  onClick,
  readOnly,
  children,
}: {
  isActive: boolean;
  activeModifier: string;
  activeStyle?: CSSProperties;
  ariaLabel: string;
  ariaPressed: boolean;
  onClick?: () => void;
  readOnly?: boolean;
  children: ReactNode;
}) {
  const className = `${buttonClass(isActive, activeModifier)}${readOnly ? ' mr-v2-status__btn--readonly' : ''}`;
  const style = readOnly && isActive ? activeStyle : undefined;

  if (readOnly) {
    return (
      <div
        className={className}
        style={style}
        aria-label={ariaLabel}
        aria-pressed={ariaPressed}
        data-active={isActive ? 'true' : 'false'}
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
      data-active={isActive ? 'true' : 'false'}
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
  const normalized = normalizeMorningRoundV2Status(status);

  return (
    <div className="mr-v2-status" role="group">
      <StatusControl
        isActive={normalized === 'fail'}
        activeModifier="mr-v2-status__btn--fail-active"
        activeStyle={activeReadOnlyStyle('fail')}
        ariaLabel={failAriaLabel}
        ariaPressed={normalized === 'fail'}
        onClick={onSelectFail}
        readOnly={readOnly}
      >
        <X className="mr-v2-status__icon" strokeWidth={2.5} aria-hidden="true" />
      </StatusControl>
      <StatusControl
        isActive={normalized === 'ok'}
        activeModifier="mr-v2-status__btn--pass-active"
        activeStyle={activeReadOnlyStyle('pass')}
        ariaLabel={passAriaLabel}
        ariaPressed={normalized === 'ok'}
        onClick={onSelectPass}
        readOnly={readOnly}
      >
        <Check className="mr-v2-status__icon" strokeWidth={2.5} aria-hidden="true" />
      </StatusControl>
    </div>
  );
}
