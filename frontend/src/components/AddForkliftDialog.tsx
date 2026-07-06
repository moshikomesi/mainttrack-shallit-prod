import { useEffect, useState } from 'react';
import toast from 'react-hot-toast';
import { useLanguage } from '../context/LanguageContext';
import { createForklift } from '../services/forkliftsService';
import { validateRequired } from '../utils/validateForm';
import { parseApiErrorMessage } from '../utils/parseApiError';
import {
  Dialog,
  DialogContent,
  DialogFooter,
  DialogHeader,
  DialogTitle,
} from './ui/dialog';

type AddForkliftDialogProps = {
  open: boolean;
  onOpenChange: (open: boolean) => void;
  onCreated: (forkliftId: string) => void;
};

export function AddForkliftDialog({ open, onOpenChange, onCreated }: AddForkliftDialogProps) {
  const { t } = useLanguage();
  const [licenseNumber, setLicenseNumber] = useState('');
  const [invalidFieldId, setInvalidFieldId] = useState<string | null>(null);
  const [invalidErrorKey, setInvalidErrorKey] = useState<string | null>(null);
  const [isSaving, setIsSaving] = useState(false);

  useEffect(() => {
    if (!open) {
      setLicenseNumber('');
      setInvalidFieldId(null);
      setInvalidErrorKey(null);
      setIsSaving(false);
    }
  }, [open]);

  const handleClose = () => {
    if (isSaving) return;
    onOpenChange(false);
  };

  const handleSave = async () => {
    if (isSaving) return;

    const trimmed = licenseNumber.trim();
    const result = validateRequired([
      {
        fieldId: 'field-new-forklift-license',
        value: trimmed,
        errorKey: 'validation.requiredForkliftNumber',
      },
    ]);

    if (!result.valid && result.firstInvalidField && result.errorKey) {
      setInvalidFieldId(result.firstInvalidField);
      setInvalidErrorKey(result.errorKey);
      toast.error(t(result.errorKey));
      return;
    }

    setInvalidFieldId(null);
    setInvalidErrorKey(null);
    setIsSaving(true);

    try {
      const created = await createForklift({ licenseNumber: trimmed });
      onCreated(created.id);
      onOpenChange(false);
    } catch (error) {
      const message = parseApiErrorMessage(error, t('messages.saveFailed'));
      if (message === 'Forklift license number already exists.') {
        toast.error(t('forklift.duplicateLicenseNumber'));
        return;
      }
      toast.error(message);
    } finally {
      setIsSaving(false);
    }
  };

  return (
    <Dialog open={open} onOpenChange={(nextOpen) => !isSaving && onOpenChange(nextOpen)}>
      <DialogContent className="bg-white border-neutral-200 sm:max-w-md">
        <DialogHeader>
          <DialogTitle className="text-neutral-900">{t('forklift.addDialog.title')}</DialogTitle>
        </DialogHeader>

        <div>
          <label
            htmlFor="field-new-forklift-license"
            className="block text-sm font-medium text-neutral-700 mb-2"
          >
            {t('forklift.addDialog.licenseNumber')}
            <span className="text-red-500 ml-1">*</span>
          </label>
          <input
            id="field-new-forklift-license"
            type="text"
            value={licenseNumber}
            disabled={isSaving}
            onChange={(e) => {
              setLicenseNumber(e.target.value);
              if (invalidFieldId === 'field-new-forklift-license') {
                setInvalidFieldId(null);
                setInvalidErrorKey(null);
              }
            }}
            placeholder={t('forklift.addDialog.licenseNumber')}
            className={`w-full px-3 py-2.5 bg-white border rounded-lg text-neutral-900 placeholder:text-neutral-400 focus:outline-none focus:ring-2 focus:ring-neutral-800 ${
              invalidFieldId === 'field-new-forklift-license' ? 'border-red-500' : 'border-neutral-300'
            }`}
          />
          {invalidFieldId === 'field-new-forklift-license' && invalidErrorKey && (
            <p className="text-red-500 text-sm mt-1">{t(invalidErrorKey)}</p>
          )}
        </div>

        <DialogFooter className="gap-2 sm:gap-2">
          <button
            type="button"
            onClick={handleClose}
            disabled={isSaving}
            className="px-4 py-2 rounded-lg border border-neutral-300 text-sm font-medium text-neutral-700 hover:bg-neutral-50 disabled:opacity-50"
          >
            {t('common.cancel')}
          </button>
          <button
            type="button"
            onClick={handleSave}
            disabled={isSaving}
            className="px-4 py-2 rounded-lg bg-neutral-800 text-sm font-medium text-white hover:bg-neutral-900 disabled:opacity-50"
          >
            {t('forklift.addDialog.add')}
          </button>
        </DialogFooter>
      </DialogContent>
    </Dialog>
  );
}
