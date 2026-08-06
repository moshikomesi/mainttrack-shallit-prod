import { useCallback, useEffect, useMemo, useRef, useState } from 'react';
import toast from 'react-hot-toast';
import { useLanguage } from '../context/LanguageContext';
import { Loader2 } from 'lucide-react';
import { AppHeader } from './AppHeader';
import { MaintenanceImagePicker } from './MaintenanceImagePicker';
import { ReadOnlyDateBanner } from './ReadOnlyDateBanner';
import { validateRequired, type FieldCheck } from '../utils/validateForm';
import { submitOneMaintenanceRow } from '../services/maintenanceBatchSubmit';
import {
  MAINTENANCE_LOG_IMAGE_MAX_BYTES,
  MAINTENANCE_LOG_MAX_ADDITIONAL_IMAGES,
  MaintenanceImageValidationError,
  validateMaintenanceImageFile,
} from '../services/uploadService';
import { getHierarchy, type HierarchyArray } from '../services/hierarchyService';
import { getMachineComponents, type MachineComponentOption } from '../services/machineComponentService';
import { getMaintenanceTypes } from '../services/maintenanceTypeService';
import {
  buildMaintenanceLogV2Description,
  MAINTENANCE_LOG_V2_OTHER_COMPONENT,
} from '../services/maintenanceLogV2Submit';
import type { MaintenanceLogEntry, MaintenanceLogScreenProps } from '../types/maintenance';
import { getCurrentUserDisplayName } from '../auth/authSession';

export function MaintenanceLogV2Screen({ onSubmit }: MaintenanceLogScreenProps) {
  const { t } = useLanguage();
  const today = new Date().toLocaleDateString('en-CA');
  const mountedRef = useRef(true);
  const submittingRef = useRef(false);
  const submitAbortRef = useRef<AbortController | null>(null);

  const [hierarchy, setHierarchy] = useState<HierarchyArray[]>([]);
  const [isLoadingHierarchy, setIsLoadingHierarchy] = useState(false);
  const [loadHierarchyError, setLoadHierarchyError] = useState<string | null>(null);

  const [selectedArrayId, setSelectedArrayId] = useState('');
  const [selectedMachineId, setSelectedMachineId] = useState('');
  const [selectedComponent, setSelectedComponent] = useState('');
  const [otherComponentText, setOtherComponentText] = useState('');

  const [components, setComponents] = useState<MachineComponentOption[]>([]);
  const [isLoadingComponents, setIsLoadingComponents] = useState(false);
  const [loadComponentsError, setLoadComponentsError] = useState<string | null>(null);

  const [otherMaintenanceTypeId, setOtherMaintenanceTypeId] = useState('');
  const [fault, setFault] = useState('');

  const [photoFile, setPhotoFile] = useState<File | undefined>();
  const [photoPreviewUrl, setPhotoPreviewUrl] = useState<string | undefined>();
  const [uploadedImageUrl, setUploadedImageUrl] = useState<string | null | undefined>();
  const [additionalPhotoFiles, setAdditionalPhotoFiles] = useState<File[]>([]);
  const [additionalPhotoPreviewUrls, setAdditionalPhotoPreviewUrls] = useState<string[]>([]);
  const photoPreviewUrlRef = useRef<string | undefined>();
  const additionalPreviewUrlsRef = useRef<string[]>([]);

  const [name] = useState(() => getCurrentUserDisplayName());
  const [isDeclarationConfirmed, setIsDeclarationConfirmed] = useState(false);

  const [invalidFieldId, setInvalidFieldId] = useState<string | null>(null);
  const [invalidErrorKey, setInvalidErrorKey] = useState<string | null>(null);
  const [isSubmitting, setIsSubmitting] = useState(false);

  const activeArrays = useMemo(
    () => hierarchy.filter((array) => array.arrayId != null),
    [hierarchy]
  );

  const machinesForArray = useMemo(() => {
    if (!selectedArrayId) return [];
    const array = activeArrays.find((item) => item.arrayId === selectedArrayId);
    return array?.machines ?? [];
  }, [activeArrays, selectedArrayId]);

  const isOtherComponent = selectedComponent === MAINTENANCE_LOG_V2_OTHER_COMPONENT;

  const formReady = useMemo(() => {
    if (!selectedArrayId || !selectedMachineId || !selectedComponent) return false;
    if (isOtherComponent && !otherComponentText.trim()) return false;
    return true;
  }, [selectedArrayId, selectedMachineId, selectedComponent, isOtherComponent, otherComponentText]);

  const rowHasImage = Boolean(photoFile || uploadedImageUrl);
  photoPreviewUrlRef.current = photoPreviewUrl;
  additionalPreviewUrlsRef.current = additionalPhotoPreviewUrls;

  useEffect(() => {
    mountedRef.current = true;
    submitAbortRef.current = new AbortController();
    const ac = submitAbortRef.current;
    return () => {
      mountedRef.current = false;
      ac.abort();
      if (photoPreviewUrlRef.current) URL.revokeObjectURL(photoPreviewUrlRef.current);
      additionalPreviewUrlsRef.current.forEach((url) => URL.revokeObjectURL(url));
    };
  }, []);

  useEffect(() => {
    let cancelled = false;

    const load = async () => {
      try {
        setIsLoadingHierarchy(true);
        setLoadHierarchyError(null);
        const [hierarchyData, maintenanceTypes] = await Promise.all([
          getHierarchy(),
          getMaintenanceTypes(),
        ]);
        if (cancelled) return;

        setHierarchy(Array.isArray(hierarchyData) ? hierarchyData : []);
        const otherType = maintenanceTypes.find((type) => type.code === 'other');
        setOtherMaintenanceTypeId(otherType?.id ?? '');
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
    // eslint-disable-next-line react-hooks/exhaustive-deps -- load once on mount
  }, []);

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
    setSelectedComponent('');
    setOtherComponentText('');
    setComponents([]);
    setLoadComponentsError(null);
    if (invalidFieldId === 'field-v2-array') {
      setInvalidFieldId(null);
      setInvalidErrorKey(null);
    }
  };

  const handleMachineChange = (machineId: string) => {
    setSelectedMachineId(machineId);
    setSelectedComponent('');
    setOtherComponentText('');
    if (invalidFieldId === 'field-v2-machine') {
      setInvalidFieldId(null);
      setInvalidErrorKey(null);
    }
  };

  const handleComponentChange = (value: string) => {
    setSelectedComponent(value);
    if (value !== MAINTENANCE_LOG_V2_OTHER_COMPONENT) {
      setOtherComponentText('');
    }
    if (invalidFieldId === 'field-v2-component' || invalidFieldId === 'field-v2-other-component') {
      setInvalidFieldId(null);
      setInvalidErrorKey(null);
    }
  };

  const assignPhoto = useCallback(
    (file: File) => {
      if (submittingRef.current) return;
      try {
        validateMaintenanceImageFile(file, MAINTENANCE_LOG_IMAGE_MAX_BYTES);
      } catch (e) {
        if (e instanceof MaintenanceImageValidationError) {
          toast.error(t(e.translationKey));
          return;
        }
        throw e;
      }

      if (photoPreviewUrl) {
        URL.revokeObjectURL(photoPreviewUrl);
      }
      setPhotoFile(file);
      setPhotoPreviewUrl(URL.createObjectURL(file));
      setUploadedImageUrl(undefined);
    },
    [photoPreviewUrl, t]
  );

  const assignAdditionalPhotos = useCallback(
    (files: File[]) => {
      if (submittingRef.current || !rowHasImage) return;
      const availableSlots = MAINTENANCE_LOG_MAX_ADDITIONAL_IMAGES - additionalPhotoFiles.length;
      if (availableSlots <= 0) {
        toast.error(t('validation.maximumAdditionalImages'));
        return;
      }

      const accepted: File[] = [];
      for (const file of files.slice(0, availableSlots)) {
        try {
          validateMaintenanceImageFile(file, MAINTENANCE_LOG_IMAGE_MAX_BYTES);
          accepted.push(file);
        } catch (e) {
          if (e instanceof MaintenanceImageValidationError) {
            toast.error(t(e.translationKey));
            continue;
          }
          throw e;
        }
      }
      if (files.length > availableSlots) {
        toast.error(t('validation.maximumAdditionalImages'));
      }
      if (accepted.length === 0) return;

      setAdditionalPhotoFiles((prev) => [...prev, ...accepted]);
      setAdditionalPhotoPreviewUrls((prev) => [
        ...prev,
        ...accepted.map((file) => URL.createObjectURL(file)),
      ]);
    },
    [additionalPhotoFiles.length, rowHasImage, t]
  );

  const removePhoto = () => {
    if (photoPreviewUrl) {
      URL.revokeObjectURL(photoPreviewUrl);
    }
    additionalPhotoPreviewUrls.forEach((url) => URL.revokeObjectURL(url));
    setPhotoFile(undefined);
    setPhotoPreviewUrl(undefined);
    setUploadedImageUrl(undefined);
    setAdditionalPhotoFiles([]);
    setAdditionalPhotoPreviewUrls([]);
  };

  const removeAdditionalPhoto = (index: number) => {
    const removedUrl = additionalPhotoPreviewUrls[index];
    if (removedUrl) URL.revokeObjectURL(removedUrl);
    setAdditionalPhotoFiles((prev) => prev.filter((_, itemIndex) => itemIndex !== index));
    setAdditionalPhotoPreviewUrls((prev) =>
      prev.filter((_, itemIndex) => itemIndex !== index)
    );
  };

  const handleSubmit = async () => {
    if (submittingRef.current || !formReady) return;

    if (!isDeclarationConfirmed) {
      toast.error(t('validation.maintenanceDeclarationRequired'));
      return;
    }

    if (!rowHasImage) {
      toast.error(t('validation.imageRequired'));
      return;
    }

    if (!otherMaintenanceTypeId) {
      toast.error(t('common.failedToLoad'));
      return;
    }

    const requiredChecks: FieldCheck[] = [
      { fieldId: 'field-v2-array', value: selectedArrayId, errorKey: 'validation.requiredArray' },
      { fieldId: 'field-v2-machine', value: selectedMachineId, errorKey: 'validation.requiredMachine' },
      {
        fieldId: 'field-v2-component',
        value: selectedComponent,
        errorKey: 'validation.requiredComponent',
      },
    ];

    if (isOtherComponent) {
      requiredChecks.push({
        fieldId: 'field-v2-other-component',
        value: otherComponentText,
        errorKey: 'validation.requiredOtherComponent',
      });
    }

    requiredChecks.push({
      fieldId: 'field-confirmName',
      value: name,
      errorKey: 'validation.requiredConfirmName',
    });

    const result = validateRequired(requiredChecks);
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

    const mappedComponent = components.find(
      (component) => component.code === selectedComponent
    );

    const componentSelection =
      isOtherComponent
        ? ({ kind: 'other' as const, text: otherComponentText })
        : mappedComponent
          ? ({ kind: 'mapped' as const, code: mappedComponent.code, nameKey: mappedComponent.nameKey })
          : null;

    if (!componentSelection) {
      toast.error(t('validation.requiredComponent'));
      return;
    }

    const description = buildMaintenanceLogV2Description(componentSelection, fault, t);
    if (!description.trim()) {
      toast.error(t('validation.requiredDescription'));
      return;
    }

    const signal = submitAbortRef.current?.signal;
    if (!signal) return;

    const row: MaintenanceLogEntry = {
      id: 'v2-entry',
      date: today,
      machine: selectedMachineId,
      maintenanceTypeId: otherMaintenanceTypeId,
      maintenanceTypeCode: 'other',
      fault: description,
      spareParts: '',
      workHours: '',
      photoFile,
      photoPreviewUrl,
      uploadedImageUrl,
      additionalPhotoFiles,
      additionalPhotoPreviewUrls,
    };

    submittingRef.current = true;
    setIsSubmitting(true);

    try {
      const outcome = await submitOneMaintenanceRow(row, signal);
      if (!mountedRef.current) return;

      if (outcome.status === 'success') {
        toast.success(t('messages.maintenanceAllEntriesSaved'));
        onSubmit();
        return;
      }

      if (outcome.status === 'aborted') {
        return;
      }

      toast.error(t(outcome.translationKey));
    } catch (err) {
      if (!mountedRef.current) return;
      console.error(err);
      toast.error(t('messages.failedToSaveMaintenanceEntries'));
    } finally {
      submittingRef.current = false;
      if (mountedRef.current) {
        setIsSubmitting(false);
      }
    }
  };

  return (
    <div className="min-h-screen bg-neutral-50 pb-24">
      <AppHeader title={t('home.maintenanceLogV2')} showBack={true} showHome={true} />

      <div className="p-4 space-y-4">
        <ReadOnlyDateBanner date={today} />

        <div
          className={`bg-white rounded-lg p-4 space-y-3 border-2 transition-colors ${
            !rowHasImage ? 'border-red-300 ring-1 ring-red-100' : 'border border-neutral-200'
          }`}
        >
          <div className="grid gap-3">
            <div>
              <label className="block text-xs font-medium text-neutral-600 mb-1">
                {t('maintenanceLogV2.selectArray')}
                <span className="text-red-500 ml-1">*</span>
              </label>
              <select
                id="field-v2-array"
                value={selectedArrayId}
                disabled={isSubmitting || isLoadingHierarchy}
                onChange={(e) => handleArrayChange(e.target.value)}
                className={`w-full px-3 py-2 bg-white border rounded text-sm text-neutral-900 focus:outline-none focus:ring-2 focus:ring-neutral-800 ${
                  invalidFieldId === 'field-v2-array' ? 'border-red-500' : 'border-neutral-300'
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
              {invalidFieldId === 'field-v2-array' && invalidErrorKey && (
                <p className="text-red-500 text-sm mt-1">{t(invalidErrorKey)}</p>
              )}
              {loadHierarchyError && (
                <p className="text-red-500 text-xs mt-1">{loadHierarchyError}</p>
              )}
            </div>

            <div>
              <label className="block text-xs font-medium text-neutral-600 mb-1">
                {t('machine.select')}
                <span className="text-red-500 ml-1">*</span>
              </label>
              <select
                id="field-v2-machine"
                value={selectedMachineId}
                disabled={isSubmitting || !selectedArrayId}
                onChange={(e) => handleMachineChange(e.target.value)}
                className={`w-full px-3 py-2 bg-white border rounded text-sm text-neutral-900 focus:outline-none focus:ring-2 focus:ring-neutral-800 ${
                  invalidFieldId === 'field-v2-machine' ? 'border-red-500' : 'border-neutral-300'
                }`}
              >
                <option value="">{t('machine.select')}</option>
                {machinesForArray.map((machine) => (
                  <option key={machine.id} value={machine.id}>
                    {t(machine.name)}
                  </option>
                ))}
              </select>
              {invalidFieldId === 'field-v2-machine' && invalidErrorKey && (
                <p className="text-red-500 text-sm mt-1">{t(invalidErrorKey)}</p>
              )}
            </div>

            <div>
              <label className="block text-xs font-medium text-neutral-600 mb-1">
                {t('maintenanceLogV2.selectComponent')}
                <span className="text-red-500 ml-1">*</span>
              </label>
              <select
                id="field-v2-component"
                value={selectedComponent}
                disabled={isSubmitting || !selectedMachineId || isLoadingComponents}
                onChange={(e) => handleComponentChange(e.target.value)}
                className={`w-full px-3 py-2 bg-white border rounded text-sm text-neutral-900 focus:outline-none focus:ring-2 focus:ring-neutral-800 ${
                  invalidFieldId === 'field-v2-component' ? 'border-red-500' : 'border-neutral-300'
                }`}
              >
                <option value="" disabled={isLoadingComponents}>
                  {isLoadingComponents ? t('common.loading') : t('maintenanceLogV2.selectComponent')}
                </option>
                {!isLoadingComponents &&
                  components.map((component) => (
                    <option key={component.id} value={component.code}>
                      {t(component.nameKey)}
                    </option>
                  ))}
                {!isLoadingComponents && selectedMachineId && (
                  <option value={MAINTENANCE_LOG_V2_OTHER_COMPONENT}>
                    {t('maintenanceComponent.other')}
                  </option>
                )}
              </select>
              {invalidFieldId === 'field-v2-component' && invalidErrorKey && (
                <p className="text-red-500 text-sm mt-1">{t(invalidErrorKey)}</p>
              )}
              {loadComponentsError && (
                <p className="text-red-500 text-xs mt-1">{loadComponentsError}</p>
              )}
            </div>

            {isOtherComponent && (
              <div>
                <label className="block text-xs font-medium text-neutral-600 mb-1">
                  {t('maintenanceLogV2.otherComponent')}
                  <span className="text-red-500 ml-1">*</span>
                </label>
                <input
                  id="field-v2-other-component"
                  type="text"
                  value={otherComponentText}
                  disabled={isSubmitting}
                  onChange={(e) => {
                    setOtherComponentText(e.target.value);
                    if (invalidFieldId === 'field-v2-other-component') {
                      setInvalidFieldId(null);
                      setInvalidErrorKey(null);
                    }
                  }}
                  placeholder={t('maintenanceLogV2.otherComponent')}
                  className={`w-full px-3 py-2 bg-white border rounded text-sm text-neutral-900 placeholder:text-neutral-400 focus:outline-none focus:ring-2 focus:ring-neutral-800 ${
                    invalidFieldId === 'field-v2-other-component' ? 'border-red-500' : 'border-neutral-300'
                  }`}
                />
                {invalidFieldId === 'field-v2-other-component' && invalidErrorKey && (
                  <p className="text-red-500 text-sm mt-1">{t(invalidErrorKey)}</p>
                )}
              </div>
            )}

            <div>
              <label className="block text-xs font-medium text-neutral-600 mb-1">
                {t('log.fault')}
              </label>
              <textarea
                value={fault}
                disabled={isSubmitting}
                onChange={(e) => setFault(e.target.value)}
                placeholder={t('log.fault')}
                rows={2}
                className="w-full px-3 py-2 bg-white border border-neutral-300 rounded text-sm text-neutral-900 placeholder:text-neutral-400 focus:outline-none focus:ring-2 focus:ring-neutral-800 resize-none"
              />
            </div>

            <div>
              <label className="block text-xs font-medium text-neutral-600 mb-2">
                {t('log.photo')}
                <span className="text-red-500 ml-1">*</span>
              </label>
              <MaintenanceImagePicker
                primaryImage={uploadedImageUrl ?? photoPreviewUrl}
                additionalImages={additionalPhotoPreviewUrls}
                disabled={isSubmitting}
                onPrimarySelected={assignPhoto}
                onAdditionalSelected={assignAdditionalPhotos}
                onRemovePrimary={removePhoto}
                onRemoveAdditional={removeAdditionalPhoto}
              />
              {!rowHasImage && (
                <p className="text-sm text-red-600 mt-1" role="status">
                  {t('validation.imageRequired')}
                </p>
              )}
            </div>
          </div>
        </div>

        <div
          className={`bg-neutral-100 rounded-lg p-4 space-y-3 border-2 transition-colors ${
            !isDeclarationConfirmed
              ? 'border-red-300 ring-1 ring-red-100'
              : 'border border-neutral-300'
          }`}
        >
          <p className="text-sm text-neutral-900 font-medium">{t('log.declaration')}</p>
          <div>
            <div className="flex items-start gap-3 mt-3">
              <input
                id="field-declaration-confirmed"
                type="checkbox"
                checked={isDeclarationConfirmed}
                disabled={isSubmitting}
                onChange={(e) => setIsDeclarationConfirmed(e.target.checked)}
                aria-label={t('log.maintenanceRowDeclarationLabel')}
                aria-describedby={!isDeclarationConfirmed ? 'declaration-required-hint' : undefined}
                className="w-4 h-4 mt-1 shrink-0 rounded border-neutral-300 text-neutral-800 focus:ring-neutral-800"
              />
              <div className="flex-1 min-w-0">
                <label
                  htmlFor="field-confirmName"
                  className="block text-xs font-medium text-neutral-600"
                >
                  {t('log.name')}
                  <span className="text-red-500 ml-1">*</span>
                </label>
                <div className="w-full mt-1 px-3 py-2 bg-neutral-100 border border-neutral-300 rounded-lg text-sm text-neutral-900">
                  {name}
                </div>
                {invalidFieldId === 'field-confirmName' && invalidErrorKey && (
                  <p className="text-red-500 text-sm mt-1">{t(invalidErrorKey)}</p>
                )}
              </div>
            </div>
            {!isDeclarationConfirmed && (
              <p className="text-sm text-red-600 mt-2" id="declaration-required-hint" role="status">
                {t('validation.maintenanceDeclarationRequired')}
              </p>
            )}
          </div>
        </div>
      </div>

      <div className="fixed bottom-0 left-0 right-0 p-4 bg-white border-t border-neutral-200">
        <div className="max-w-md mx-auto">
          <button
            type="button"
            onClick={handleSubmit}
            disabled={!formReady || isSubmitting}
            className="w-full py-4 bg-neutral-800 text-white font-semibold rounded-lg hover:bg-neutral-900 active:bg-neutral-950 transition-colors disabled:opacity-60 disabled:cursor-not-allowed flex items-center justify-center gap-2"
          >
            {isSubmitting ? (
              <>
                <Loader2 className="w-5 h-5 animate-spin shrink-0" aria-hidden />
                <span>{t('log.uploading')}</span>
              </>
            ) : (
              <span>{t('morning.submit')}</span>
            )}
          </button>
        </div>
      </div>
    </div>
  );
}
