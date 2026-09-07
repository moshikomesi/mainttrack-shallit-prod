import { useEffect, useMemo, useState } from 'react';
import toast from 'react-hot-toast';
import { Navigate } from 'react-router-dom';
import { useLanguage } from '../context/LanguageContext';
import { AppHeader } from './AppHeader';
import { ReadOnlyDateBanner } from './ReadOnlyDateBanner';
import { validateRequired } from '../utils/validateForm';
import { createTreatment } from '../services/treatmentService';
import { getHierarchy, type HierarchyArray } from '../services/hierarchyService';
import {
  getMachineComponents,
  type MachineComponentOption,
} from '../services/machineComponentService';
import { canSeeTreatments } from '../auth/roles';
import { Button } from './ui/button';
import type {
  CreateTreatmentRequest,
  TreatmentsReportScreenProps,
} from '../types/treatment';

export function TreatmentsReportScreen({ onSubmit, userRoleId }: TreatmentsReportScreenProps) {
  const { t } = useLanguage();
  const today = new Date().toLocaleDateString('en-CA');

  // Business rule: only SuperAdmin can see treatments UI.
  if (!canSeeTreatments(userRoleId)) {
    return <Navigate to="/home" replace />;
  }

  const [hierarchy, setHierarchy] = useState<HierarchyArray[]>([]);
  const [isLoadingHierarchy, setIsLoadingHierarchy] = useState(false);
  const [loadHierarchyError, setLoadHierarchyError] = useState<string | null>(null);

  const [selectedArrayId, setSelectedArrayId] = useState('');
  const [selectedMachineId, setSelectedMachineId] = useState('');
  const [machineComponentId, setMachineComponentId] = useState('');

  const [components, setComponents] = useState<MachineComponentOption[]>([]);
  const [isLoadingComponents, setIsLoadingComponents] = useState(false);
  const [loadComponentsError, setLoadComponentsError] = useState<string | null>(null);

  const [date] = useState(today);
  const [description, setDescription] = useState('');
  const [technician, setTechnician] = useState('');
  const [nextScheduled, setNextScheduled] = useState('');
  const [notes, setNotes] = useState('');

  const [invalidFieldId, setInvalidFieldId] = useState<string | null>(null);
  const [invalidErrorKey, setInvalidErrorKey] = useState<string | null>(null);

  const activeArrays = useMemo(
    () => hierarchy.filter((array) => array.arrayId != null),
    [hierarchy]
  );

  const machinesForArray = useMemo(() => {
    if (!selectedArrayId) return [];
    const array = activeArrays.find((item) => item.arrayId === selectedArrayId);
    return array?.machines ?? [];
  }, [activeArrays, selectedArrayId]);

  useEffect(() => {
    let cancelled = false;
    const load = async () => {
      try {
        setIsLoadingHierarchy(true);
        setLoadHierarchyError(null);
        const hierarchyData = await getHierarchy();
        if (!cancelled) {
          setHierarchy(Array.isArray(hierarchyData) ? hierarchyData : []);
        }
      } catch (err) {
        console.error(err);
        if (!cancelled) {
          setLoadHierarchyError(t('common.failedToLoad'));
        }
      } finally {
        if (!cancelled) {
          setIsLoadingHierarchy(false);
        }
      }
    };

    load();
    return () => {
      cancelled = true;
    };
  }, [t]);

  useEffect(() => {
    if (!selectedMachineId) {
      setComponents([]);
      setLoadComponentsError(null);
      return;
    }

    let cancelled = false;
    const load = async () => {
      try {
        setIsLoadingComponents(true);
        setLoadComponentsError(null);
        const list = await getMachineComponents(selectedMachineId);
        if (cancelled) return;
        setComponents(Array.isArray(list) ? list : []);
      } catch (err) {
        console.error(err);
        if (!cancelled) {
          setLoadComponentsError(t('common.failedToLoad'));
          setComponents([]);
        }
      } finally {
        if (!cancelled) {
          setIsLoadingComponents(false);
        }
      }
    };

    load();
    return () => {
      cancelled = true;
    };
  }, [selectedMachineId, t]);

  const handleArrayChange = (arrayId: string) => {
    setSelectedArrayId(arrayId);
    setSelectedMachineId('');
    setMachineComponentId('');
    setComponents([]);
    setLoadComponentsError(null);
    if (
      invalidFieldId === 'field-treatments-array' ||
      invalidFieldId === 'field-treatments-machine' ||
      invalidFieldId === 'field-treatments-treatment-type'
    ) {
      setInvalidFieldId(null);
      setInvalidErrorKey(null);
    }
  };

  const handleMachineChange = (machineId: string) => {
    setSelectedMachineId(machineId);
    setMachineComponentId('');
    if (
      invalidFieldId === 'field-treatments-machine' ||
      invalidFieldId === 'field-treatments-treatment-type'
    ) {
      setInvalidFieldId(null);
      setInvalidErrorKey(null);
    }
  };

  const handleSubmit = async () => {
    const result = validateRequired([
      { fieldId: 'field-treatments-date', value: date, errorKey: 'validation.requiredDate' },
      { fieldId: 'field-treatments-array', value: selectedArrayId, errorKey: 'validation.requiredArray' },
      {
        fieldId: 'field-treatments-machine',
        value: selectedMachineId,
        errorKey: 'validation.requiredMachine',
      },
      {
        fieldId: 'field-treatments-treatment-type',
        value: machineComponentId,
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
        machineId: selectedMachineId,
        treatmentDate: date,
        machineComponentId,
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

        <div className="bg-white border border-neutral-200 rounded-lg p-4 space-y-4">
          <div>
            <label className="block text-sm font-medium text-neutral-700 mb-2">
              {t('maintenanceLogV2.selectArray')}
              <span className="text-red-500 ms-1">*</span>
            </label>
            <select
              id="field-treatments-array"
              value={selectedArrayId}
              disabled={isLoadingHierarchy}
              onChange={(e) => handleArrayChange(e.target.value)}
              className={`w-full px-3 py-2.5 bg-white border rounded-lg text-neutral-900 focus:outline-none focus:ring-2 focus:ring-neutral-800 ${
                invalidFieldId === 'field-treatments-array' ? 'border-red-500' : 'border-neutral-300'
              }`}
            >
              <option value="">{t('maintenanceLogV2.selectArray')}</option>
              {isLoadingHierarchy && (
                <option value="" disabled>
                  {t('common.loading')}
                </option>
              )}
              {!isLoadingHierarchy &&
                activeArrays.map((array) => (
                  <option key={array.arrayId!} value={array.arrayId!}>
                    {t(array.nameKey)}
                  </option>
                ))}
            </select>
            {invalidFieldId === 'field-treatments-array' && invalidErrorKey && (
              <p className="text-red-500 text-sm mt-1">{t(invalidErrorKey)}</p>
            )}
            {loadHierarchyError && (
              <p className="text-red-500 text-xs mt-1">{loadHierarchyError}</p>
            )}
          </div>

          <div>
            <label className="block text-sm font-medium text-neutral-700 mb-2">
              {t('machine.select')}
              <span className="text-red-500 ms-1">*</span>
            </label>
            <select
              id="field-treatments-machine"
              value={selectedMachineId}
              disabled={!selectedArrayId}
              onChange={(e) => handleMachineChange(e.target.value)}
              className={`w-full px-3 py-2.5 bg-white border rounded-lg text-neutral-900 focus:outline-none focus:ring-2 focus:ring-neutral-800 ${
                invalidFieldId === 'field-treatments-machine' ? 'border-red-500' : 'border-neutral-300'
              }`}
            >
              <option value="">{t('machine.select')}</option>
              {machinesForArray.map((machine) => (
                <option key={machine.id} value={machine.id}>
                  {t(machine.name)}
                </option>
              ))}
            </select>
            {invalidFieldId === 'field-treatments-machine' && invalidErrorKey && (
              <p className="text-red-500 text-sm mt-1">{t(invalidErrorKey)}</p>
            )}
          </div>

          <div>
            <label className="block text-sm font-medium text-neutral-700 mb-2">
              {t('treatment.type')}
              <span className="text-red-500 ms-1">*</span>
            </label>
            <select
              id="field-treatments-treatment-type"
              value={machineComponentId}
              disabled={!selectedMachineId || isLoadingComponents}
              onChange={(e) => {
                setMachineComponentId(e.target.value);
                if (invalidFieldId === 'field-treatments-treatment-type') {
                  setInvalidFieldId(null);
                  setInvalidErrorKey(null);
                }
              }}
              className={`w-full px-3 py-2.5 bg-white border rounded-lg text-neutral-900 focus:outline-none focus:ring-2 focus:ring-neutral-800 ${
                invalidFieldId === 'field-treatments-treatment-type'
                  ? 'border-red-500'
                  : 'border-neutral-300'
              }`}
            >
              <option value="" disabled={isLoadingComponents}>
                {isLoadingComponents ? t('common.loading') : t('treatment.type')}
              </option>
              {!isLoadingComponents &&
                components.map((component) => (
                  <option key={component.id} value={component.id}>
                    {t(component.nameKey)}
                  </option>
                ))}
            </select>
            {invalidFieldId === 'field-treatments-treatment-type' && invalidErrorKey && (
              <p className="text-red-500 text-sm mt-1">{t(invalidErrorKey)}</p>
            )}
            {loadComponentsError && (
              <p className="text-red-500 text-xs mt-1">{loadComponentsError}</p>
            )}
          </div>
        </div>

        <div className="bg-white border border-neutral-200 rounded-lg p-4 space-y-4">
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

          <div>
            <label className="block text-sm font-medium text-neutral-700 mb-2">
              {t('common.technician')}
              <span className="text-red-500 ms-1">*</span>
            </label>
            <select
              id="field-treatments-technician"
              value={technician}
              onChange={(e) => {
                setTechnician(e.target.value);
                if (invalidFieldId === 'field-treatments-technician') {
                  setInvalidFieldId(null);
                  setInvalidErrorKey(null);
                }
              }}
              className={`w-full px-3 py-2.5 bg-white border rounded-lg text-neutral-900 focus:outline-none focus:ring-2 focus:ring-neutral-800 ${
                invalidFieldId === 'field-treatments-technician'
                  ? 'border-red-500'
                  : 'border-neutral-300'
              }`}
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

      <div className="fixed bottom-0 left-0 right-0 p-4 bg-white border-t border-neutral-200">
        <div className="max-w-md mx-auto">
          <Button
            onClick={handleSubmit}
            className="w-full py-4 bg-neutral-800 text-white font-semibold rounded-lg hover:bg-neutral-900 active:bg-neutral-950 transition-colors"
          >
            {t('common.submit')}
          </Button>
        </div>
      </div>
    </div>
  );
}
