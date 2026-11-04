import { useId, useRef, useState } from 'react';
import toast from 'react-hot-toast';
import { ApiError } from '../../api/apiClient';
import { useLanguage } from '../../context/LanguageContext';
import { approveClearance, rejectClearance } from '../../services/clearanceService';
import { CLEARANCE_REJECTION_NOTE_MAX_LENGTH } from '../../types/clearance';
import { parseApiErrorMessage } from '../../utils/parseApiError';

type Mode = 'idle' | 'reject';

const buttonClass =
  'flex-1 px-3 py-2 text-sm font-medium rounded-lg disabled:bg-neutral-300 disabled:cursor-not-allowed';
const approveStyle = { backgroundColor: '#15803d', color: '#ffffff' };
const rejectStyle = { backgroundColor: '#b91c1c', color: '#ffffff' };

/**
 * Approve/reject controls for one pending item. Render only when the API reported
 * canApprove; the backend still re-checks every decision.
 */
export function ClearanceDecisionActions({
  itemId,
  onDecided,
  showDeclaration = false,
}: {
  itemId: string;
  /** Called after any outcome that may have changed the item (success, 409, 403, 404). */
  onDecided: () => void;
  /** Shows the safety declaration that clicking Approve accepts, above the buttons. */
  showDeclaration?: boolean;
}) {
  const { t } = useLanguage();
  const [mode, setMode] = useState<Mode>('idle');
  const [note, setNote] = useState('');
  const [isSubmitting, setIsSubmitting] = useState(false);
  const inFlight = useRef(false);
  const noteId = useId();

  const decide = async (action: 'approve' | 'reject') => {
    // Synchronous guard: a fast double-click fires before the disabled state re-renders.
    if (inFlight.current) return;
    inFlight.current = true;
    setIsSubmitting(true);
    try {
      if (action === 'approve') {
        await approveClearance(itemId);
        toast.success(t('clearance.approvedToast'));
      } else {
        const trimmed = note.trim();
        await rejectClearance(itemId, trimmed.length > 0 ? trimmed : null);
        toast.success(t('clearance.rejectedToast'));
      }
      setMode('idle');
      setNote('');
      onDecided();
    } catch (err) {
      if (err instanceof ApiError && (err.status === 409 || err.status === 404)) {
        toast.error(t('clearance.alreadyDecided'));
        setMode('idle');
        onDecided();
      } else if (err instanceof ApiError && err.status === 403) {
        toast.error(t('clearance.notAuthorizedToDecide'));
        setMode('idle');
        onDecided();
      } else {
        toast.error(parseApiErrorMessage(err, t('clearance.decisionFailed')));
      }
    } finally {
      inFlight.current = false;
      setIsSubmitting(false);
    }
  };

  if (mode === 'reject') {
    return (
      <div className="space-y-2">
        <label htmlFor={noteId} className="block text-sm font-medium text-neutral-700">
          {t('clearance.rejectNoteLabel')}
        </label>
        <textarea
          id={noteId}
          value={note}
          maxLength={CLEARANCE_REJECTION_NOTE_MAX_LENGTH}
          onChange={(e) => setNote(e.target.value)}
          rows={3}
          disabled={isSubmitting}
          className="w-full px-3 py-2 bg-white border border-neutral-300 rounded text-sm text-neutral-900 resize-none focus:outline-none focus:ring-2 focus:ring-neutral-800"
        />
        <div className="text-xs text-neutral-500">
          {note.length} / {CLEARANCE_REJECTION_NOTE_MAX_LENGTH}
        </div>
        <div className="flex gap-2">
          <button
            type="button"
            className={buttonClass}
            style={rejectStyle}
            disabled={isSubmitting}
            onClick={() => decide('reject')}
          >
            {t('clearance.confirmReject')}
          </button>
          <button
            type="button"
            className={`${buttonClass} bg-white border border-neutral-300 text-neutral-700 hover:bg-neutral-50`}
            disabled={isSubmitting}
            onClick={() => {
              setMode('idle');
              setNote('');
            }}
          >
            {t('common.cancel')}
          </button>
        </div>
      </div>
    );
  }

  // Clicking Approve is the approver's acceptance of the safety declaration.
  const buttons = (
    <div className="flex gap-2">
      <button
        type="button"
        className={buttonClass}
        style={approveStyle}
        disabled={isSubmitting}
        aria-busy={isSubmitting}
        onClick={() => decide('approve')}
      >
        {t('clearance.approve')}
      </button>
      <button
        type="button"
        className={buttonClass}
        style={rejectStyle}
        disabled={isSubmitting}
        onClick={() => setMode('reject')}
      >
        {t('clearance.reject')}
      </button>
    </div>
  );

  if (!showDeclaration) return buttons;

  return (
    <div className="space-y-2">
      <div className="bg-neutral-100 border border-neutral-300 rounded-lg p-3">
        <p className="text-sm text-neutral-900">{t('clearance.safetyDeclaration')}</p>
      </div>
      {buttons}
    </div>
  );
}
