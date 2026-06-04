import { useState } from 'react';
import toast from 'react-hot-toast';
import { Navigate } from 'react-router-dom';
import { useLanguage } from '../context/LanguageContext';
import { AppHeader } from './AppHeader';
import { validateRequired } from '../utils/validateForm';
import { createTreatment } from '../services/treatmentService';
import { canSeeTreatments } from '../auth/roles';
import type {
  CreateTreatmentRequest,
  TreatmentsReportScreenProps,
  UiEquipmentKey,
  UiTreatmentTypeKey,
} from '../types/treatment';
import { equipmentMap, treatmentTypeMap } from '../types/treatment';

export function TreatmentsReportScreen({ onSubmit, userRoleId }: TreatmentsReportScreenProps) {
  const { t } = useLanguage();
  const today = new Date().toLocaleDateString('en-CA');

  // Business rule: only SuperAdmin can see treatments UI.
  if (!canSeeTreatments(userRoleId)) {
    return <Navigate to="/home" replace />;
  }
  
  const [equipment, setEquipment] = useState<UiEquipmentKey>('airCompressor');
  const [date, setDate] = useState(today);
  const [treatmentType, setTreatmentType] = useState<UiTreatmentTypeKey | ''>('');
  const [description, setDescription] = useState('');
  const [technician, setTechnician] = useState('');
  const [cost, setCost] = useState('');
  const [nextScheduled, setNextScheduled] = useState('');
  const [notes, setNotes] = useState('');

  const [invalidFieldId, setInvalidFieldId] = useState<string | null>(null);
  const [invalidErrorKey, setInvalidErrorKey] = useState<string | null>(null);

  const handleSubmit = async () => {
    const result = validateRequired([
      { fieldId: 'field-treatments-date', value: date, errorKey: 'validation.requiredDate' },
      { fieldId: 'field-treatments-description', value: description, errorKey: 'validation.requiredDescription' },
      { fieldId: 'field-treatments-technician', value: technician, errorKey: 'validation.requiredTechnician' },
    ]);
    if (!result.valid && result.firstInvalidField && result.errorKey) {
      setInvalidFieldId(result.firstInvalidField);
      setInvalidErrorKey(result.errorKey);
      toast.error(t(result.errorKey));
      document
        .getElementById(result.firstInvalidField)
        ?.scrollIntoView({ behavior: 'smooth', block: 'center' });
      document.getElementById(result.firstInvalidField)?.focus();
      return;
    }
    setInvalidFieldId(null);
    setInvalidErrorKey(null);

    try {
      const payload: CreateTreatmentRequest = {
        equipmentType: equipmentMap[equipment],
        treatmentDate: date,
        treatmentType: treatmentTypeMap[treatmentType as UiTreatmentTypeKey] ?? 0,
        description,
        technician,
        cost: cost ? Number(cost) : 0,
        nextDueDate: nextScheduled || null,
      };

      await createTreatment(payload);
      toast.success(t('messages.treatmentSubmittedSuccessfully'));
      onSubmit();
    } catch (err) {
      console.error(err);
      toast.error(t('messages.failedToSubmitTreatmentReport'));
    }
  };

  return (
    <div className="min-h-screen bg-neutral-50 pb-24">
      <AppHeader title={t('treatments.title')} showBack={true} showHome={true} />

      <div className="p-4 space-y-4">
        {/* Equipment Selection */}
        <div className="bg-white border border-neutral-200 rounded-lg p-4">
          <label className="block text-sm font-medium text-neutral-700 mb-3">
            {t('treatments.equipment')}
          </label>
          <div className="grid grid-cols-2 gap-3">
            <button
              onClick={() => setEquipment('airCompressor')}
              className={`px-4 py-3 rounded-lg border-2 font-medium transition-colors ${
                equipment === 'airCompressor'
                  ? 'border-neutral-800 bg-neutral-800 text-white'
                  : 'border-neutral-300 bg-white text-neutral-700 hover:bg-neutral-50'
              }`}
            >
              {t('treatments.airCompressor')}
            </button>
            <button
              onClick={() => setEquipment('coolingSystem')}
              className={`px-4 py-3 rounded-lg border-2 font-medium transition-colors ${
                equipment === 'coolingSystem'
                  ? 'border-neutral-800 bg-neutral-800 text-white'
                  : 'border-neutral-300 bg-white text-neutral-700 hover:bg-neutral-50'
              }`}
            >
              {t('treatments.coolingSystem')}
            </button>
          </div>
        </div>

        {/* Treatment Details */}
        <div className="bg-white border border-neutral-200 rounded-lg p-4 space-y-4">
          {/* Date */}
          <div>
            <label className="block text-sm font-medium text-neutral-700 mb-2">
              {t('common.date')}
              <span className="text-red-500 ml-1">*</span>
            </label>
            <input
              id="field-treatments-date"
              type="date"
              value={date}
              onChange={(e) => { setDate(e.target.value); if (invalidFieldId === 'field-treatments-date') { setInvalidFieldId(null); setInvalidErrorKey(null); } }}
              className={`w-full px-3 py-2.5 bg-white border rounded-lg text-neutral-900 focus:outline-none focus:ring-2 focus:ring-neutral-800 ${invalidFieldId === 'field-treatments-date' ? 'border-red-500' : 'border-neutral-300'}`}
            />
            {invalidFieldId === 'field-treatments-date' && invalidErrorKey && (
              <p className="text-red-500 text-sm mt-1">{t(invalidErrorKey)}</p>
            )}
          </div>

          {/* Treatment Type */}
          <div>
            <label className="block text-sm font-medium text-neutral-700 mb-2">
              {t('treatments.treatmentType')}
            </label>
            <select
              value={treatmentType}
              onChange={(e) => setTreatmentType(e.target.value as UiTreatmentTypeKey | '')}
              className="w-full px-3 py-2.5 bg-white border border-neutral-300 rounded-lg text-neutral-900 placeholder:text-neutral-400 focus:outline-none focus:ring-2 focus:ring-neutral-800"
            >
              <option value="">{t('treatments.treatmentType')}</option>
              <option value="preventive">Preventive</option>
              <option value="repair">Repair</option>
            </select>
          </div>

          {/* Description */}
          <div>
            <label className="block text-sm font-medium text-neutral-700 mb-2">
              {t('treatments.description')}
              <span className="text-red-500 ml-1">*</span>
            </label>
            <textarea
              id="field-treatments-description"
              value={description}
              onChange={(e) => { setDescription(e.target.value); if (invalidFieldId === 'field-treatments-description') { setInvalidFieldId(null); setInvalidErrorKey(null); } }}
              placeholder={t('treatments.description')}
              rows={3}
              className={`w-full px-3 py-2.5 bg-white border rounded-lg text-neutral-900 placeholder:text-neutral-400 focus:outline-none focus:ring-2 focus:ring-neutral-800 resize-none ${invalidFieldId === 'field-treatments-description' ? 'border-red-500' : 'border-neutral-300'}`}
            />
            {invalidFieldId === 'field-treatments-description' && invalidErrorKey && (
              <p className="text-red-500 text-sm mt-1">{t(invalidErrorKey)}</p>
            )}
          </div>

          {/* Technician */}
          <div>
            <label className="block text-sm font-medium text-neutral-700 mb-2">
              {t('common.technician')}
              <span className="text-red-500 ml-1">*</span>
            </label>
            <select
              id="field-treatments-technician"
              value={technician}
              onChange={(e) => { setTechnician(e.target.value); if (invalidFieldId === 'field-treatments-technician') { setInvalidFieldId(null); setInvalidErrorKey(null); } }}
              className={`w-full px-3 py-2.5 bg-white border rounded-lg text-neutral-900 focus:outline-none focus:ring-2 focus:ring-neutral-800 ${invalidFieldId === 'field-treatments-technician' ? 'border-red-500' : 'border-neutral-300'}`}
            >
              <option value="">{t('common.technician')}</option>
              <option value="eli">{t('tech.eli')}</option>
              <option value="thaiDom">{t('tech.thaiDom')}</option>
              <option value="solomon">{t('tech.solomon')}</option>
              <option value="yehuda">{t('tech.yehuda')}</option>
            </select>
            {invalidFieldId === 'field-treatments-technician' && invalidErrorKey && (
              <p className="text-red-500 text-sm mt-1">{t(invalidErrorKey)}</p>
            )}
          </div>

          {/* Cost */}
          <div>
            <label className="block text-sm font-medium text-neutral-700 mb-2">
              {t('common.cost')}
            </label>
            <input
              type="number"
              value={cost}
              onChange={(e) => setCost(e.target.value)}
              placeholder="0.00"
              className="w-full px-3 py-2.5 bg-white border border-neutral-300 rounded-lg text-neutral-900 placeholder:text-neutral-400 focus:outline-none focus:ring-2 focus:ring-neutral-800"
            />
          </div>

          {/* Next Scheduled Date */}
          <div>
            <label className="block text-sm font-medium text-neutral-700 mb-2">
              {t('treatments.nextScheduled')}
            </label>
            <input
              type="date"
              value={nextScheduled}
              onChange={(e) => setNextScheduled(e.target.value)}
              className="w-full px-3 py-2.5 bg-white border border-neutral-300 rounded-lg text-neutral-900 focus:outline-none focus:ring-2 focus:ring-neutral-800"
            />
          </div>

          {/* Notes */}
          <div>
            <label className="block text-sm font-medium text-neutral-700 mb-2">
              {t('log.notes')}
            </label>
            <textarea
              value={notes}
              onChange={(e) => setNotes(e.target.value)}
              placeholder={t('log.notes')}
              rows={3}
              className="w-full px-3 py-2.5 bg-white border border-neutral-300 rounded-lg text-neutral-900 placeholder:text-neutral-400 focus:outline-none focus:ring-2 focus:ring-neutral-800 resize-none"
            />
          </div>
        </div>
      </div>

      {/* Submit Button - Sticky */}
      <div className="fixed bottom-0 left-0 right-0 p-4 bg-white border-t border-neutral-200">
        <div className="max-w-md mx-auto">
          <button
            onClick={handleSubmit}
            className="w-full py-4 bg-neutral-800 text-white font-semibold rounded-lg hover:bg-neutral-900 active:bg-neutral-950 transition-colors"
          >
            {t('common.submit')}
          </button>
        </div>
      </div>
    </div>
  );
}
