import { useEffect, useMemo, useState } from 'react';
import toast from 'react-hot-toast';
import { Navigate } from 'react-router-dom';
import { useLanguage } from '../context/LanguageContext';
import { AppHeader } from './AppHeader';
import { ReadOnlyDateBanner } from './ReadOnlyDateBanner';
import { validateRequired } from '../utils/validateForm';
import { createTreatment } from '../services/treatmentService';
import { getMachines, type MachineDto } from '../services/machinesService';
import { getMaintenanceTypes } from '../services/maintenanceTypeService';
import { canSeeTreatments } from '../auth/roles';
import { Button } from './ui/button';
import type {
  CreateTreatmentRequest,
  TreatmentsReportScreenProps,
} from '../types/treatment';

type MaintenanceTypeOption = { id: string; code: string };

export function TreatmentsReportScreen({ onSubmit, userRoleId }: TreatmentsReportScreenProps) {
  const { t } = useLanguage();
  const today = new Date().toLocaleDateString('en-CA');

  // Business rule: only SuperAdmin can see treatments UI.
  if (!canSeeTreatments(userRoleId)) {
    return <Navigate to="/home" replace />;
  }
  
  const [machines, setMachines] = useState<MachineDto[]>([]);
  const [isLoadingMachines, setIsLoadingMachines] = useState(false);
  const [loadMachinesError, setLoadMachinesError] = useState<string | null>(null);
  const [maintenanceTypes, setMaintenanceTypes] = useState<MaintenanceTypeOption[]>([]);
  const [isLoadingMaintenanceTypes, setIsLoadingMaintenanceTypes] = useState(false);
  const [loadMaintenanceTypesError, setLoadMaintenanceTypesError] = useState<string | null>(null);
  const sortedMaintenanceTypes = useMemo(
    () =>
      [...maintenanceTypes].sort((a, b) =>
        t(`maintenanceType.${a.code}`).localeCompare(t(`maintenanceType.${b.code}`))
      ),
    [maintenanceTypes, t]
  );

  const [machineId, setMachineId] = useState('');
  const [date, setDate] = useState(today);
  const [maintenanceTypeId, setMaintenanceTypeId] = useState('');
  const [description, setDescription] = useState('');
  const [technician, setTechnician] = useState('');
  const [nextScheduled, setNextScheduled] = useState('');
  const [notes, setNotes] = useState('');

  const [invalidFieldId, setInvalidFieldId] = useState<string | null>(null);
  const [invalidErrorKey, setInvalidErrorKey] = useState<string | null>(null);

  useEffect(() => {
    let cancelled = false;
    const loadMachines = async () => {
      try {
        setIsLoadingMachines(true);
        setLoadMachinesError(null);
        const list = await getMachines();
        if (!cancelled) {
          setMachines(Array.isArray(list) ? list : []);
        }
      } catch (err) {
        console.error(err);
        if (!cancelled) {
          setLoadMachinesError(t('common.failedToLoad'));
        }
      } finally {
        if (!cancelled) {
          setIsLoadingMachines(false);
        }
      }
    };

    loadMachines();
    return () => {
      cancelled = true;
    };
  }, [t]);

  useEffect(() => {
    let cancelled = false;
    const loadTypes = async () => {
      try {
        setIsLoadingMaintenanceTypes(true);
        setLoadMaintenanceTypesError(null);
        const list = await getMaintenanceTypes();
        if (!cancelled) {
          setMaintenanceTypes(
            Array.isArray(list) ? list.map((x) => ({ id: x.id, code: x.code })) : []
          );
        }
      } catch (err) {
        console.error(err);
        if (!cancelled) {
          setLoadMaintenanceTypesError(t('common.failedToLoad'));
        }
      } finally {
        if (!cancelled) {
          setIsLoadingMaintenanceTypes(false);
        }
      }
    };

    loadTypes();
    return () => {
      cancelled = true;
    };
  }, [t]);

  const handleSubmit = async () => {
    const result = validateRequired([
      { fieldId: 'field-treatments-date', value: date, errorKey: 'validation.requiredDate' },
      { fieldId: 'field-treatments-machine', value: machineId, errorKey: 'validation.requiredMachine' },
      {
        fieldId: 'field-treatments-maintenance-type',
        value: maintenanceTypeId,
        errorKey: 'validation.maintenanceTypeRequired',
      },
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
        machineId,
        treatmentDate: date,
        maintenanceTypeId,
        description,
        technician,
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
        <ReadOnlyDateBanner date={date} />

        {/* Equipment Selection */}
        <div className="bg-white border border-neutral-200 rounded-lg p-4">
          <label className="block text-sm font-medium text-neutral-700 mb-3">
            {t('machine.select')}
            <span className="text-red-500 ms-1">*</span>
          </label>
          <select
            id="field-treatments-machine"
            value={machineId}
            disabled={isLoadingMachines}
            onChange={(e) => { setMachineId(e.target.value); if (invalidFieldId === 'field-treatments-machine') { setInvalidFieldId(null); setInvalidErrorKey(null); } }}
            className={`w-full px-3 py-2.5 bg-white border rounded-lg text-neutral-900 placeholder:text-neutral-400 focus:outline-none focus:ring-2 focus:ring-neutral-800 ${invalidFieldId === 'field-treatments-machine' ? 'border-red-500' : 'border-neutral-300'}`}
          >
            <option value="">{isLoadingMachines ? t('common.loading') : t('machine.select')}</option>
            {!isLoadingMachines &&
              machines.map((machine) => (
                <option key={machine.id} value={machine.id}>
                  {t(machine.name)}
                </option>
              ))}
          </select>
          {invalidFieldId === 'field-treatments-machine' && invalidErrorKey && (
            <p className="text-red-500 text-sm mt-1">{t(invalidErrorKey)}</p>
          )}
          {loadMachinesError && (
            <p className="text-red-500 text-xs mt-1">{loadMachinesError}</p>
          )}
        </div>

        {/* Treatment Details */}
        <div className="bg-white border border-neutral-200 rounded-lg p-4 space-y-4">
          {/* Maintenance Type */}
          <div>
            <label className="block text-sm font-medium text-neutral-700 mb-2">
              {t('treatment.type')}
              <span className="text-red-500 ms-1">*</span>
            </label>
            <select
              id="field-treatments-maintenance-type"
              value={maintenanceTypeId}
              disabled={isLoadingMaintenanceTypes}
              onChange={(e) => { setMaintenanceTypeId(e.target.value); if (invalidFieldId === 'field-treatments-maintenance-type') { setInvalidFieldId(null); setInvalidErrorKey(null); } }}
              className={`w-full px-3 py-2.5 bg-white border rounded-lg text-neutral-900 placeholder:text-neutral-400 focus:outline-none focus:ring-2 focus:ring-neutral-800 ${invalidFieldId === 'field-treatments-maintenance-type' ? 'border-red-500' : 'border-neutral-300'}`}
            >
              <option value="">
                {isLoadingMaintenanceTypes ? t('common.loading') : t('log.selectMaintenanceType')}
              </option>
              {!isLoadingMaintenanceTypes &&
                sortedMaintenanceTypes.map((type) => (
                  <option key={type.id} value={type.id}>
                    {t(`maintenanceType.${type.code}`)}
                  </option>
                ))}
            </select>
            {invalidFieldId === 'field-treatments-maintenance-type' && invalidErrorKey && (
              <p className="text-red-500 text-sm mt-1">{t(invalidErrorKey)}</p>
            )}
            {loadMaintenanceTypesError && (
              <p className="text-red-500 text-xs mt-1">{loadMaintenanceTypesError}</p>
            )}
          </div>

          {/* Description */}
          <div>
            <label className="block text-sm font-medium text-neutral-700 mb-2">
              {t('treatments.description')}
            </label>
            <textarea
              id="field-treatments-description"
              value={description}
              onChange={(e) => setDescription(e.target.value)}
              placeholder={t('treatments.description')}
              rows={3}
              className="w-full px-3 py-2.5 bg-white border border-neutral-300 rounded-lg text-neutral-900 placeholder:text-neutral-400 focus:outline-none focus:ring-2 focus:ring-neutral-800 resize-none"
            />
          </div>

          {/* Technician */}
          <div>
            <label className="block text-sm font-medium text-neutral-700 mb-2">
              {t('common.technician')}
              <span className="text-red-500 ms-1">*</span>
            </label>
            <select
              id="field-treatments-technician"
              value={technician}
              onChange={(e) => { setTechnician(e.target.value); if (invalidFieldId === 'field-treatments-technician') { setInvalidFieldId(null); setInvalidErrorKey(null); } }}
              className={`w-full px-3 py-2.5 bg-white border rounded-lg text-neutral-900 focus:outline-none focus:ring-2 focus:ring-neutral-800 ${invalidFieldId === 'field-treatments-technician' ? 'border-red-500' : 'border-neutral-300'}`}
            >
              <option value="">{t('common.technician')}</option>
              <option value="eli">{t('tech.eli')}</option>
              <option value="sharon">{t('tech.sharon')}</option>
              <option value="tzadit">{t('tech.tzadit')}</option>
              <option value="tom">{t('tech.tom')}</option>
              <option value="emm">{t('tech.emm')}</option>
              <option value="pia">{t('tech.pia')}</option>
            </select>
            {invalidFieldId === 'field-treatments-technician' && invalidErrorKey && (
              <p className="text-red-500 text-sm mt-1">{t(invalidErrorKey)}</p>
            )}
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
      <div className="fixed bottom-0 left-0 right-0 p-4 bg-white border-t border-neutral-200">        <div className="max-w-md mx-auto">
          <Button
            onClick={handleSubmit}
            className="w-full py-4 bg-neutral-800 text-white font-semibold rounded-lg hover:bg-neutral-900 active:bg-neutral-950 transition-colors"          >
            {t('common.submit')}
          </Button>
        </div>
      </div>
    </div>
  );
}
