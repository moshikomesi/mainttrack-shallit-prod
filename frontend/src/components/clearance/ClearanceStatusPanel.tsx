import { useCallback, useEffect, useState } from 'react';
import { ApiError } from '../../api/apiClient';
import { useLanguage } from '../../context/LanguageContext';
import { getClearanceBySource } from '../../services/clearanceService';
import type { ClearanceSourceItemResponse, ClearanceSourceType } from '../../types/clearance';
import { formatDisplayDateTime } from '../../utils/formatDate';
import { ClearanceDecisionActions } from './ClearanceDecisionActions';
import { ClearanceStatusBadge } from './ClearanceStatusBadge';

/**
 * Clearance approval status of a source report. Kept separate from the technician's
 * safety declaration (MaintenanceEntry.isSafeToOperate), which it never reads or changes.
 * Renders nothing for reports that have no Clearance item (e.g. a fully normal Morning Round)
 * and for users without a Clearance view or approver assignment (403). Nothing is rendered
 * until the first response arrives, so unauthorized users never see the panel appear.
 */
export function ClearanceStatusPanel({
  sourceType,
  sourceId,
}: {
  sourceType: ClearanceSourceType;
  sourceId: string;
}) {
  const { t, language } = useLanguage();
  const [data, setData] = useState<ClearanceSourceItemResponse | null>(null);
  const [hasError, setHasError] = useState(false);
  const [hasNoItem, setHasNoItem] = useState(false);

  const load = useCallback(async () => {
    setHasError(false);
    try {
      setData(await getClearanceBySource(sourceType, sourceId));
      setHasNoItem(false);
    } catch (err) {
      setData(null);
      if (err instanceof ApiError && (err.status === 404 || err.status === 403)) {
        setHasNoItem(true);
      } else {
        console.error(err);
        setHasError(true);
      }
    }
  }, [sourceType, sourceId]);

  useEffect(() => {
    setData(null);
    setHasNoItem(false);
    load();
  }, [load]);

  // No data and no error means the request is still pending (or the report has no item, or the
  // user may not view it): render nothing rather than a heading or loading state.
  if (hasNoItem || (!data && !hasError)) {
    return null;
  }

  const item = data?.item;

  return (
    <div className="bg-white border border-neutral-200 rounded-lg p-4">
      <h2 className="text-sm font-semibold text-neutral-900 mb-3">{t('clearance.sectionTitle')}</h2>
      {hasError && (
        <div className="text-sm" style={{ color: '#b91c1c' }}>
          {t('clearance.statusLoadFailed')}
        </div>
      )}
      {item && (
        <div className="space-y-2 text-sm">
          <div>
            <ClearanceStatusBadge status={item.status} />
          </div>
          {item.status !== 'pending' && (
            <>
              <div className="flex justify-between">
                <span className="text-neutral-600">
                  {item.status === 'approved' ? t('clearance.approvedBy') : t('clearance.rejectedBy')}:
                </span>
                <span className="font-medium text-neutral-900">
                  {item.decidedByName ?? t('common.notProvided')}
                </span>
              </div>
              {item.decidedAt && (
                <div className="flex justify-between">
                  <span className="text-neutral-600">{t('clearance.decidedAt')}:</span>
                  <span className="font-medium text-neutral-900">
                    {formatDisplayDateTime(language, item.decidedAt)}
                  </span>
                </div>
              )}
            </>
          )}
          {item.status === 'approved' && (
            <div
              className="bg-neutral-100 border border-neutral-300 rounded-lg p-3 space-y-1"
              data-testid="approver-safety-declaration"
            >
              <p className="text-sm font-semibold text-neutral-900">{t('clearance.approverSafetyDeclaration')}</p>
              <p className="text-sm text-neutral-900">{t('clearance.safetyDeclaration')}</p>
              <p className="text-sm text-neutral-700">
                <span className="font-semibold">{t('clearance.declaredBy')}: </span>
                <bdi>{item.decidedByName ?? t('common.notProvided')}</bdi>
                {item.decidedAt && (
                  <>
                    {' · '}
                    <bdi>{formatDisplayDateTime(language, item.decidedAt)}</bdi>
                  </>
                )}
              </p>
            </div>
          )}
          {item.status === 'rejected' && item.decisionNote && (
            <div>
              <span className="text-neutral-600">{t('clearance.rejectionNote')}: </span>
              <span className="text-neutral-900" style={{ whiteSpace: 'pre-wrap' }}>
                {item.decisionNote}
              </span>
            </div>
          )}
          {item.status === 'pending' && data?.canApprove && (
            <div className="mt-2">
              <ClearanceDecisionActions itemId={item.id} onDecided={load} />
            </div>
          )}
        </div>
      )}
    </div>
  );
}
