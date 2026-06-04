import { useState, useEffect } from 'react';
import toast from 'react-hot-toast';
import { useLanguage } from '../context/LanguageContext';
import { getForklifts } from '../services/forkliftsService';
import { createForkliftReport } from '../services/forkliftReportsService';
import type { Forklift, ForkliftReportScreenProps } from '../types/forklift';
import { validateRequired } from '../utils/validateForm';
import { AppHeader } from './AppHeader';

function formatDisplayDate(iso: string): string {
  const [date] = iso.split("T");
  const [year, month, day] = date.split("-");
  return `${day}/${month}/${year}`;
}

export function ForkliftReportScreen({ onSubmit }: ForkliftReportScreenProps) {
  const { t } = useLanguage();
  const today = new Date().toLocaleDateString('en-CA');

  const [forklifts, setForklifts] = useState<Forklift[]>([]);
  const [selectedForkliftId, setSelectedForkliftId] = useState('');

  // Treatment section
  const [treatmentDate, setTreatmentDate] = useState(today);
  const [treatmentDescription, setTreatmentDescription] = useState('');
  const [treatmentTechnician, _setTreatmentTechnician] = useState('');

  // Faults section
  const [faultType, setFaultType] = useState('');
  const [faultDescription, setFaultDescription] = useState('');
  const [repairCost, setRepairCost] = useState('');

  // Inspection section
  const [testDate, setTestDate] = useState('');
  const [expirationDate, setExpirationDate] = useState('');

  const [invalidFieldId, setInvalidFieldId] = useState<string | null>(null);
  const [invalidErrorKey, setInvalidErrorKey] = useState<string | null>(null);
  const [isSubmitting, setIsSubmitting] = useState(false);

  useEffect(() => {
    const loadForklifts = async () => {
      try {
        const data = await getForklifts();
        setForklifts(data ?? []);
      } catch (err) {
        console.error(err);
      }
    };
    loadForklifts();
  }, []);

  const selectedForklift = forklifts.find((f) => f.id === selectedForkliftId);

  useEffect(() => {
    if (!selectedForklift) {
      setTestDate('');
      setExpirationDate('');
      return;
    }
  
    if (selectedForklift.lastInspectionDate) {
      setTestDate(
        selectedForklift.lastInspectionDate.split("T")[0]
      );
    } else {
      setTestDate('');
    }
  
    if (selectedForklift.inspectionExpiryDate) {
      setExpirationDate(
        selectedForklift.inspectionExpiryDate.split("T")[0]
      );
    } else {
      setExpirationDate('');
    }
  
  }, [selectedForklift]);

  const handleSubmit = async () => {
    if (isSubmitting) return;
    const result = validateRequired([
      { fieldId: 'field-forklift', value: selectedForkliftId, errorKey: 'validation.requiredForklift' },
    ]);
    if (!result.valid && result.firstInvalidField && result.errorKey) {
      setInvalidFieldId(result.firstInvalidField);
      setInvalidErrorKey(result.errorKey);
      toast.error(t(result.errorKey));
      document.getElementById(result.firstInvalidField)?.scrollIntoView({ behavior: 'smooth', block: 'center' });
      document.getElementById(result.firstInvalidField)?.focus();
      return;
    }
    setInvalidFieldId(null);
    setInvalidErrorKey(null);

    const hasInspectionData = testDate.trim() !== '' && expirationDate.trim() !== '';

    if (hasInspectionData) {
      const testTime = new Date(testDate).getTime();
      const expiryTime = new Date(expirationDate).getTime();

      if (Number.isNaN(testTime) || Number.isNaN(expiryTime)) {
        toast.error(t('validation.invalidDate'));
        return;
      }

      if (expiryTime <= testTime) {
        toast.error(t('validation.inspectionExpiryAfterTest'));
        return;
      }
    }

    setIsSubmitting(true);
    try {
      const treatments = [
        {
          date: treatmentDate,
          description: treatmentDescription,
          technician: treatmentTechnician,
        },
      ].filter(
        (t) => t.date || t.description || t.technician
      );

      const faults = [
        {
          faultType,
          description: faultDescription,
          repairCost: repairCost ? parseFloat(repairCost) : 0,
        },
      ].filter(
        (f) => f.faultType || f.description || f.repairCost
      );

      const payload: {
        forkliftId: string;
        reportDate: string;
        treatments?: typeof treatments;
        faults?: typeof faults;
        inspections?: { testDate: string; expiryDate: string }[];
      } = {
        forkliftId: selectedForkliftId,
        reportDate: today,
      };

      if (treatments.length > 0) {
        payload.treatments = treatments;
      }

      if (faults.length > 0) {
        payload.faults = faults;
      }

      if (hasInspectionData) {
        payload.inspections = [{ testDate, expiryDate: expirationDate }];
      }

      await createForkliftReport(payload);
      toast.success(t('messages.reportSaved'));
      onSubmit();
    } catch (error) {
      let message = t('messages.saveFailed');
      if (error instanceof Error && error.message) {
        message = error.message;
      }
      toast.error(message);
    } finally {
      setIsSubmitting(false);
    }
  };

  return (
    <div className="min-h-screen bg-neutral-50 pb-24">
      <AppHeader title={t('forklift.title')} showBack={true} showHome={true} />


      <div className="bg-white border border-neutral-200 rounded-lg p-4 space-y-4">   
        <div>
          <h2 className="text-base font-semibold text-neutral-900">
              {t('common.forkliftnumber')}
              <span className="text-red-500 ml-1">*</span>
            </h2>
            <select
              id="field-forklift"
              value={selectedForkliftId}
              onChange={(e) => { setSelectedForkliftId(e.target.value); if (invalidFieldId === 'field-forklift') { setInvalidFieldId(null); setInvalidErrorKey(null); } }}
              className={`w-full px-3 py-2.5 bg-white border rounded-lg text-neutral-900 focus:outline-none focus:ring-2 focus:ring-neutral-800 ${invalidFieldId === 'field-forklift' ? 'border-red-500' : 'border-neutral-300'}`}
            >
              <option value="">{t('common.forkliftnumber')}</option>
              {forklifts.map((f) => (
                <option key={f.id} value={f.id}>
                  {f.licenseNumber}
                </option>
              ))}
            </select>
            {invalidFieldId === 'field-forklift' && invalidErrorKey && (
              <p className="text-red-500 text-sm mt-1">{t(invalidErrorKey)}</p>
            )}
          </div>  
        </div>
    
      <div className="p-4 space-y-4">
        {/* Treatments Section */}
        <div className="bg-white border border-neutral-200 rounded-lg p-4 space-y-4">
          <h2 className="text-base font-semibold text-neutral-900">
            {t('forklift.treatments')}
          </h2>
          
          <div>
            <label className="block text-sm font-medium text-neutral-700 mb-2">
              {t('common.date')}
            </label>
            <input
              type="date"
              value={treatmentDate}
              onChange={(e) => setTreatmentDate(e.target.value)}
              className="w-full px-3 py-2.5 bg-white border border-neutral-300 rounded-lg text-neutral-900 focus:outline-none focus:ring-2 focus:ring-neutral-800"
            />
          </div>

          <div>
            <label className="block text-sm font-medium text-neutral-700 mb-2">
              {t('common.description')}
            </label>
            <textarea
              value={treatmentDescription}
              onChange={(e) => setTreatmentDescription(e.target.value)}
              placeholder={t('common.description')}
              rows={3}
              className="w-full px-3 py-2.5 bg-white border border-neutral-300 rounded-lg text-neutral-900 placeholder:text-neutral-400 focus:outline-none focus:ring-2 focus:ring-neutral-800 resize-none"
            />
          </div>

        </div>

        {/* Faults Section */}
        <div className="bg-white border border-neutral-200 rounded-lg p-4 space-y-4">
          <h2 className="text-base font-semibold text-neutral-900">
            {t('forklift.faults')}
          </h2>
          
          <div>
            <label className="block text-sm font-medium text-neutral-700 mb-2">
              {t('forklift.faultType')}
            </label>
            <input
              type="text"
              value={faultType}
              onChange={(e) => setFaultType(e.target.value)}
              placeholder={t('forklift.faultType')}
              className="w-full px-3 py-2.5 bg-white border border-neutral-300 rounded-lg text-neutral-900 placeholder:text-neutral-400 focus:outline-none focus:ring-2 focus:ring-neutral-800"
            />
          </div>

          <div>
            <label className="block text-sm font-medium text-neutral-700 mb-2">
              {t('common.description')}
            </label>
            <textarea
              value={faultDescription}
              onChange={(e) => setFaultDescription(e.target.value)}
              placeholder={t('common.description')}
              rows={2}
              className="w-full px-3 py-2.5 bg-white border border-neutral-300 rounded-lg text-neutral-900 placeholder:text-neutral-400 focus:outline-none focus:ring-2 focus:ring-neutral-800 resize-none"
            />
          </div>

          <div>
            <label className="block text-sm font-medium text-neutral-700 mb-2">
              {t('forklift.repairCost')}
            </label>
            <input
              type="number"
              value={repairCost}
              onChange={(e) => setRepairCost(e.target.value)}
              placeholder="0.00"
              className="w-full px-3 py-2.5 bg-white border border-neutral-300 rounded-lg text-neutral-900 placeholder:text-neutral-400 focus:outline-none focus:ring-2 focus:ring-neutral-800"
            />
          </div>
        </div>

        {/* Inspection/Test Section */}
        <div className="bg-white border border-neutral-200 rounded-lg p-4 space-y-4">
          <h2 className="text-base font-semibold text-neutral-900">
            {t('forklift.inspection')}
          </h2>
          {selectedForklift?.lastInspectionDate && selectedForklift?.inspectionExpiryDate && (
            <div className="text-sm text-neutral-700 space-y-1">
              <p>
                {t('forklift.lastInspection')}: {formatDisplayDate(selectedForklift.lastInspectionDate)}
              </p>
              <p>
                {t('forklift.validUntil')}: {formatDisplayDate(selectedForklift.inspectionExpiryDate)}
              </p>
            </div>
          )}
          <div>
            <label className="block text-sm font-medium text-neutral-700 mb-2">
              {t('forklift.testDate')}
            </label>
            <input
              type="date"
              value={testDate}
              onChange={(e) => setTestDate(e.target.value)}
              className="w-full px-3 py-2.5 bg-white border border-neutral-300 rounded-lg text-neutral-900 focus:outline-none focus:ring-2 focus:ring-neutral-800"
            />
          </div>

          <div>
            <label className="block text-sm font-medium text-neutral-700 mb-2">
              {t('forklift.expirationDate')}
            </label>
            <input
              type="date"
              value={expirationDate}
              onChange={(e) => setExpirationDate(e.target.value)}
              className="w-full px-3 py-2.5 bg-white border border-neutral-300 rounded-lg text-neutral-900 focus:outline-none focus:ring-2 focus:ring-neutral-800"
            />
          </div>
        </div>
      </div>

      {/* Submit Button - Sticky */}
      <div className="fixed bottom-0 left-0 right-0 p-4 bg-white border-t border-neutral-200">
        <div className="max-w-md mx-auto">
          <button
            onClick={handleSubmit}
            disabled={isSubmitting}
            className={`w-full py-4 bg-neutral-800 text-white font-semibold rounded-lg hover:bg-neutral-900 active:bg-neutral-950 transition-colors ${isSubmitting ? 'opacity-60 cursor-not-allowed' : ''}`}
          >
            {t('common.submit')}
          </button>
        </div>
      </div>
    </div>
  );
}
