import type { CSSProperties } from 'react';
import { useLanguage } from '../../context/LanguageContext';
import type { ClearanceStatus } from '../../types/clearance';

/** Inline colors: the precompiled CSS has no red/green utilities. */
const STATUS_STYLES: Record<ClearanceStatus, CSSProperties> = {
  pending: { backgroundColor: '#fffbeb', color: '#92400e', borderColor: '#fde68a' },
  approved: { backgroundColor: '#f0fdf4', color: '#166534', borderColor: '#bbf7d0' },
  rejected: { backgroundColor: '#fef2f2', color: '#991b1b', borderColor: '#fecaca' },
};

export function ClearanceStatusBadge({ status }: { status: ClearanceStatus }) {
  const { t } = useLanguage();
  return (
    <span
      className="inline-flex items-center px-2 py-0.5 text-xs font-semibold border rounded"
      style={STATUS_STYLES[status]}
    >
      {t(`clearance.status.${status}`)}
    </span>
  );
}
