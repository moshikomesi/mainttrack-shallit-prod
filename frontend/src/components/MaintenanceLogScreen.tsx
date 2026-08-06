import { useEffect, useState, useRef, useCallback, useMemo } from 'react';
import toast from 'react-hot-toast';
import { useLanguage } from '../context/LanguageContext';
import { Loader2 } from 'lucide-react';
import { AppHeader } from './AppHeader';
import { MaintenanceImagePicker } from './MaintenanceImagePicker';
import { ReadOnlyDateBanner } from './ReadOnlyDateBanner';
import { validateRequired, type FieldCheck } from '../utils/validateForm';
import {
  applyMaintenanceSubmitOutcomes,
  getFullFailureToastKey,
  submitMaintenanceRows,
} from '../services/maintenanceBatchSubmit';
import {
  MAINTENANCE_LOG_IMAGE_MAX_BYTES,
  MAINTENANCE_LOG_MAX_ADDITIONAL_IMAGES,
  MaintenanceImageValidationError,
  validateMaintenanceImageFile,
} from '../services/uploadService';
import type { MaintenanceLogEntry, MaintenanceLogScreenProps } from '../types/maintenance';
import { apiFetch } from '../api/apiClient';
import { endpoints } from '../api/endpoints';
import { getCurrentUserDisplayName } from '../auth/authSession';
import { getMaintenanceTypes } from '../services/maintenanceTypeService';

type MaintenanceTypeOption = { id: string; code: string };

function sortMaintenanceTypesForDisplay(
  items: MaintenanceTypeOption[],
  t: (key: string) => string
): MaintenanceTypeOption[] {
  const list = [...items];
  list.sort((a, b) => {
    if (a.code === 'other') return 1;
    if (b.code === 'other') return -1;
    return t(`maintenanceType.${a.code}`).localeCompare(t(`maintenanceType.${b.code}`));
  });
  return list;
}

export function MaintenanceLogScreen({ onSubmit }: MaintenanceLogScreenProps) {
  const { t } = useLanguage();
  const today = new Date().toLocaleDateString('en-CA');
  const entriesRef = useRef<MaintenanceLogEntry[]>([]);
  const mountedRef = useRef(true);
  const submittingRef = useRef(false);
  const submitAbortRef = useRef<AbortController | null>(null);
  const [machines, setMachines] = useState<{ id: string; name: string }[]>([]);
  const [isLoadingMachines, setIsLoadingMachines] = useState(false);
  const [loadMachinesError, setLoadMachinesError] = useState<string | null>(null);

  const [maintenanceTypes, setMaintenanceTypes] = useState<MaintenanceTypeOption[]>([]);
  const [isLoadingMaintenanceTypes, setIsLoadingMaintenanceTypes] = useState(false);
  const [loadMaintenanceTypesError, setLoadMaintenanceTypesError] = useState<string | null>(null);

  const sortedMaintenanceTypes = useMemo(
    () => sortMaintenanceTypesForDisplay(maintenanceTypes, t),
    [maintenanceTypes, t]
  );

  const [entries, setEntries] = useState<MaintenanceLogEntry[]>([
    {
      id: '1',
      date: today,
      machine: '',
      maintenanceTypeId: '',
      maintenanceTypeCode: '',
      fault: '',
      spareParts: '',
      workHours: '',
    },
  ]);

  entriesRef.current = entries;

  const [name] = useState(() => getCurrentUserDisplayName());
  const [isDeclarationConfirmed, setIsDeclarationConfirmed] = useState(false);

  const [invalidFieldId, setInvalidFieldId] = useState<string | null>(null);
  const [invalidErrorKey, setInvalidErrorKey] = useState<string | null>(null);
  const [isSubmitting, setIsSubmitting] = useState(false);
  useEffect(() => {
    mountedRef.current = true;
    submitAbortRef.current = new AbortController();
    const ac = submitAbortRef.current;
    return () => {
      mountedRef.current = false;
      ac.abort();
      for (const e of entriesRef.current) {
        if (e.photoPreviewUrl) {
          URL.revokeObjectURL(e.photoPreviewUrl);
        }
        e.additionalPhotoPreviewUrls?.forEach((url) => URL.revokeObjectURL(url));
      }
    };
  }, []);

  useEffect(() => {
    let cancelled = false;
    const loadMachines = async () => {
      try {
        setIsLoadingMachines(true);
        setLoadMachinesError(null);
        const list = await apiFetch(endpoints.machines);
        if (cancelled) return;
        setMachines(
          Array.isArray(list)
            ? list.map((m: { id: string; name: string }) => ({
                id: m.id,
                name: m.name,
              }))
            : []
        );
      } catch (err) {
        console.error(err);
        if (!cancelled) {
          setLoadMachinesError(t('messages.failedToLoadMachines'));
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
    // eslint-disable-next-line react-hooks/exhaustive-deps -- load once on mount; t is stable enough for error strings
  }, []);

  useEffect(() => {
    let cancelled = false;
    const loadTypes = async () => {
      try {
        setIsLoadingMaintenanceTypes(true);
        setLoadMaintenanceTypesError(null);
        const list = await getMaintenanceTypes();
        if (cancelled) return;
        const mapped: MaintenanceTypeOption[] = Array.isArray(list)
          ? list.map((x) => ({
              id: x.id,
              code: x.code,
            }))
          : [];
        setMaintenanceTypes(sortMaintenanceTypesForDisplay(mapped, t));
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
    // eslint-disable-next-line react-hooks/exhaustive-deps -- load once on mount; sort uses t from closure
  }, []);

  const updateEntry = (id: string, field: keyof MaintenanceLogEntry, value: string) => {
    setEntries(
      entries.map((entry) => (entry.id === id ? { ...entry, [field]: value } : entry))
    );
  };

  const assignPhotoToEntry = useCallback(
    (entryId: string, file: File) => {
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

      setEntries((prev) =>
        prev.map((entry) => {
          if (entry.id !== entryId) return entry;
          if (entry.photoPreviewUrl) {
            URL.revokeObjectURL(entry.photoPreviewUrl);
          }
          return {
            ...entry,
            photoFile: file,
            photoPreviewUrl: URL.createObjectURL(file),
            uploadedImageUrl: undefined,
          };
        })
      );
    },
    [t]
  );

  const assignAdditionalPhotosToEntry = useCallback(
    (entryId: string, files: File[]) => {
      if (submittingRef.current) return;
      const entry = entriesRef.current.find((item) => item.id === entryId);
      if (!entry?.photoFile && !entry?.uploadedImageUrl) return;

      const availableSlots =
        MAINTENANCE_LOG_MAX_ADDITIONAL_IMAGES - (entry.additionalPhotoFiles?.length ?? 0);
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

      const previewUrls = accepted.map((file) => URL.createObjectURL(file));
      setEntries((prev) =>
        prev.map((item) =>
          item.id === entryId
            ? {
                ...item,
                additionalPhotoFiles: [...(item.additionalPhotoFiles ?? []), ...accepted],
                additionalPhotoPreviewUrls: [
                  ...(item.additionalPhotoPreviewUrls ?? []),
                  ...previewUrls,
                ],
              }
            : item
        )
      );
    },
    [t]
  );

  const removePhoto = (entryId: string) => {
    setEntries((prev) =>
      prev.map((entry) => {
        if (entry.id !== entryId) return entry;
        if (entry.photoPreviewUrl) {
          URL.revokeObjectURL(entry.photoPreviewUrl);
        }
        entry.additionalPhotoPreviewUrls?.forEach((url) => URL.revokeObjectURL(url));
        return {
          ...entry,
          photoFile: undefined,
          photoPreviewUrl: undefined,
          uploadedImageUrl: undefined,
          additionalPhotoFiles: undefined,
          additionalPhotoPreviewUrls: undefined,
        };
      })
    );
  };

  const removeAdditionalPhoto = (entryId: string, index: number) => {
    setEntries((prev) =>
      prev.map((entry) => {
        if (entry.id !== entryId) return entry;
        const previewUrls = [...(entry.additionalPhotoPreviewUrls ?? [])];
        const files = [...(entry.additionalPhotoFiles ?? [])];
        const removedUrl = previewUrls[index];
        if (removedUrl) URL.revokeObjectURL(removedUrl);
        previewUrls.splice(index, 1);
        files.splice(index, 1);
        return {
          ...entry,
          additionalPhotoFiles: files,
          additionalPhotoPreviewUrls: previewUrls,
        };
      })
    );
  };

  const handleSubmit = async () => {
    if (submittingRef.current) return;

    if (!isDeclarationConfirmed) {
      toast.error(t('validation.maintenanceDeclarationRequired'));
      return;
    }

    const hasImageEveryRow = entries.every((e) => e.photoFile || e.uploadedImageUrl);
    if (!hasImageEveryRow) {
      toast.error(t('validation.imageRequired'));
      return;
    }

    const first = entries[0];
    const requiredChecks: FieldCheck[] = [
      { fieldId: 'field-entry-date', value: first?.date ?? '', errorKey: 'validation.requiredDate' },
      { fieldId: 'field-entry-machine', value: first?.machine ?? '', errorKey: 'validation.requiredMachine' },
      {
        fieldId: 'field-entry-maintenance-type',
        value: first?.maintenanceTypeId ?? '',
        errorKey: 'validation.maintenanceTypeRequired',
      },
    ];
    if (first?.maintenanceTypeCode === 'other') {
      requiredChecks.push({
        fieldId: 'field-entry-fault',
        value: first?.fault ?? '',
        errorKey: 'validation.requiredFault',
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

    const incompleteEntry = entries.find((entry) => {
      if (!entry.date || !entry.machine || !entry.maintenanceTypeId) return true;
      if (entry.maintenanceTypeCode === 'other' && !entry.fault.trim()) return true;
      return false;
    });

    if (incompleteEntry) {
      toast.error(t('validation.requiredAllRows'));
      return;
    }

    const signal = submitAbortRef.current?.signal;
    if (!signal) return;

    submittingRef.current = true;
    setIsSubmitting(true);

    try {
      const rowsSnapshot = entries;
      const { results } = await submitMaintenanceRows(rowsSnapshot, signal);

      if (!mountedRef.current) return;

      const successCount = results.filter((r) => r.status === 'success').length;
      const total = results.length;

      if (successCount === total) {
        toast.success(t('messages.maintenanceAllEntriesSaved'));
        onSubmit();
        return;
      }

      if (successCount > 0) {
        setEntries((prev) => applyMaintenanceSubmitOutcomes(prev, results));
        toast.error(t('messages.maintenancePartialSaveSuccess'));
        return;
      }

      const onlyAborted = results.every((r) => r.status === 'aborted');
      if (onlyAborted && signal.aborted) {
        return;
      }

      toast.error(t(getFullFailureToastKey(results)));
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
      <AppHeader title={t('home.maintenanceLog')} showBack={true} showHome={true} />

      <div className="p-4 space-y-4">
        <ReadOnlyDateBanner date={today} />

        {/* Maintenance Entries */}
        {entries.map((entry, index) => {
          const rowHasImage = Boolean(entry.photoFile || entry.uploadedImageUrl);
          return (
          <div
            key={entry.id}
            className={`bg-white rounded-lg p-4 space-y-3 border-2 transition-colors ${
              !rowHasImage
                ? 'border-red-300 ring-1 ring-red-100'
                : 'border border-neutral-200'
            }`}
          >
            <div className="flex items-center justify-between mb-2">
              <span className="text-sm font-semibold text-neutral-900">
                {t('log.entry')} #{index + 1}
              </span>
            </div>

            <div className="grid gap-3">
              {/* Machine */}
              <div>
                <label className="block text-xs font-medium text-neutral-600 mb-1">
                  {t('machine.select')}
                  <span className="text-red-500 ml-1">*</span>
                </label>
                <select
                  id={index === 0 ? 'field-entry-machine' : undefined}
                  value={entry.machine}
                  disabled={isSubmitting || isLoadingMachines}
                  onChange={(e) => {
                    updateEntry(entry.id, 'machine', e.target.value);
                    if (index === 0 && invalidFieldId === 'field-entry-machine') {
                      setInvalidFieldId(null);
                      setInvalidErrorKey(null);
                    }
                  }}
                  className={`w-full px-3 py-2 bg-white border rounded text-sm text-neutral-900 focus:outline-none focus:ring-2 focus:ring-neutral-800 ${
                    index === 0 && invalidFieldId === 'field-entry-machine'
                      ? 'border-red-500'
                      : 'border-neutral-300'
                  }`}
                >
                  <option value="">{t('machine.select')}</option>
                  {isLoadingMachines && (
                    <option value="" disabled>
                      {t('common.loading')}
                    </option>
                  )}
                  {!isLoadingMachines &&
                    machines.map((m) => (
                      <option key={m.id} value={m.id}>
                        {t(m.name)}
                      </option>
                    ))}
                </select>
                {index === 0 && invalidFieldId === 'field-entry-machine' && invalidErrorKey && (
                  <p className="text-red-500 text-sm mt-1">{t(invalidErrorKey)}</p>
                )}
                {loadMachinesError && (
                  <p className="text-red-500 text-xs mt-1">{loadMachinesError}</p>
                )}
              </div>

              {/* Maintenance type */}
              <div>
                <label className="block text-xs font-medium text-neutral-600 mb-1">
                  {t('log.maintenanceTypeLabel')}
                  <span className="text-red-500 ml-1">*</span>
                </label>
                <select
                  id={index === 0 ? 'field-entry-maintenance-type' : undefined}
                  value={entry.maintenanceTypeId}
                  disabled={isSubmitting || isLoadingMaintenanceTypes}
                  onChange={(e) => {
                    const v = e.target.value;
                    const mt = sortedMaintenanceTypes.find((x) => x.id === v);
                    const code = mt?.code ?? '';
                    setEntries((prev) =>
                      prev.map((row) =>
                        row.id === entry.id
                          ? {
                              ...row,
                              maintenanceTypeId: v,
                              maintenanceTypeCode: code,
                              fault: code === 'other' ? row.fault : '',
                            }
                          : row
                      )
                    );
                    if (index === 0 && invalidFieldId === 'field-entry-maintenance-type') {
                      setInvalidFieldId(null);
                      setInvalidErrorKey(null);
                    }
                  }}
                  className={`w-full px-3 py-2 bg-white border rounded text-sm text-neutral-900 focus:outline-none focus:ring-2 focus:ring-neutral-800 ${
                    index === 0 && invalidFieldId === 'field-entry-maintenance-type'
                      ? 'border-red-500'
                      : 'border-neutral-300'
                  }`}
                >
                  <option value="" disabled={isLoadingMaintenanceTypes}>
                    {isLoadingMaintenanceTypes ? t('common.loading') : t('log.selectMaintenanceType')}
                  </option>
                  {!isLoadingMaintenanceTypes &&
                    sortedMaintenanceTypes.map((mt) => (
                      <option key={mt.id} value={mt.id}>
                        {t(`maintenanceType.${mt.code}`)}
                      </option>
                    ))}
                </select>
                {index === 0 &&
                  invalidFieldId === 'field-entry-maintenance-type' &&
                  invalidErrorKey && (
                    <p className="text-red-500 text-sm mt-1">{t(invalidErrorKey)}</p>
                  )}
                {loadMaintenanceTypesError && (
                  <p className="text-red-500 text-xs mt-1">{loadMaintenanceTypesError}</p>
                )}
              </div>

              {/* Fault / details — only for "Other" */}
              {entry.maintenanceTypeCode === 'other' && (
                <div>
                  <label className="block text-xs font-medium text-neutral-600 mb-1">
                    {t('log.fault')}
                    <span className="text-red-500 ml-1">*</span>
                  </label>
                  <textarea
                    id={index === 0 ? 'field-entry-fault' : undefined}
                    value={entry.fault}
                    disabled={isSubmitting}
                    onChange={(e) => {
                      updateEntry(entry.id, 'fault', e.target.value);
                      if (index === 0 && invalidFieldId === 'field-entry-fault') {
                        setInvalidFieldId(null);
                        setInvalidErrorKey(null);
                      }
                    }}
                    placeholder={t('log.fault')}
                    rows={2}
                    className={`w-full px-3 py-2 bg-white border rounded text-sm text-neutral-900 placeholder:text-neutral-400 focus:outline-none focus:ring-2 focus:ring-neutral-800 resize-none ${index === 0 && invalidFieldId === 'field-entry-fault' ? 'border-red-500' : 'border-neutral-300'}`}
                  />
                  {index === 0 && invalidFieldId === 'field-entry-fault' && invalidErrorKey && (
                    <p className="text-red-500 text-sm mt-1">{t(invalidErrorKey)}</p>
                  )}
                </div>
              )}

              {/* Photo: capture + gallery/files + drag & drop (required) */}
              <div>
                <label className="block text-xs font-medium text-neutral-600 mb-2">
                  {t('log.photo')}
                  <span className="text-red-500 ml-1">*</span>
                </label>
                <MaintenanceImagePicker
                  primaryImage={entry.uploadedImageUrl ?? entry.photoPreviewUrl}
                  additionalImages={entry.additionalPhotoPreviewUrls ?? []}
                  disabled={isSubmitting}
                  onPrimarySelected={(file) => assignPhotoToEntry(entry.id, file)}
                  onAdditionalSelected={(files) =>
                    assignAdditionalPhotosToEntry(entry.id, files)
                  }
                  onRemovePrimary={() => removePhoto(entry.id)}
                  onRemoveAdditional={(imageIndex) =>
                    removeAdditionalPhoto(entry.id, imageIndex)
                  }
                />
                {!rowHasImage && (
                  <p className="text-sm text-red-600 mt-1" role="status">
                    {t('validation.imageRequired')}
                  </p>
                )}
              </div>

              {/* Spare Parts */}
              <div>
                <label className="block text-xs font-medium text-neutral-600 mb-1">
                  {t('log.spareParts')}
                </label>
                <input
                  type="text"
                  value={entry.spareParts}
                  disabled={isSubmitting}
                  onChange={(e) => updateEntry(entry.id, 'spareParts', e.target.value)}
                  placeholder={t('log.spareParts')}
                  className="w-full px-3 py-2 bg-white border border-neutral-300 rounded text-sm text-neutral-900 placeholder:text-neutral-400 focus:outline-none focus:ring-2 focus:ring-neutral-800"
                />
              </div>
            </div>
          </div>
          );
        })}

        {/* Declaration + signature (checkbox UI-only; not sent to API) */}
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
              <p
                className="text-sm text-red-600 mt-2"
                id="declaration-required-hint"
                role="status"
              >
                {t('validation.maintenanceDeclarationRequired')}
              </p>
            )}
          </div>
        </div>
      </div>

      {/* Submit Button - Sticky */}
      <div className="fixed bottom-0 left-0 right-0 p-4 bg-white border-t border-neutral-200">
        <div className="max-w-md mx-auto">
          <button
            type="button"
            onClick={handleSubmit}
            disabled={isSubmitting}
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
