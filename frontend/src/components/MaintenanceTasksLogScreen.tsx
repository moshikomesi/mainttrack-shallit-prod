import { useCallback, useEffect, useRef, useState, type ChangeEvent, type DragEvent } from 'react';
import toast from 'react-hot-toast';
import { Camera, Loader2, X } from 'lucide-react';
import { useLanguage } from '../context/LanguageContext';
import { createMaintenanceTask } from '../services/maintenanceTasksService';
import {
  MaintenanceImageValidationError,
  validateMaintenanceImageFile,
} from '../services/uploadService';
import type { MaintenanceLogScreenProps } from '../types/maintenance';
import { safeImageSrc } from '../utils/safeUrl';
import { AppHeader } from './AppHeader';

function isAbortLike(signal: AbortSignal | undefined, e: unknown): boolean {
  if (signal?.aborted) return true;
  if (e instanceof DOMException && e.name === 'AbortError') return true;
  if (e instanceof Error && e.name === 'AbortError') return true;
  return false;
}

export function MaintenanceTasksLogScreen({ onSubmit }: MaintenanceLogScreenProps) {
  const { t } = useLanguage();
  const today = new Date().toLocaleDateString('en-CA');
  const fileInputRef = useRef<HTMLInputElement>(null);
  const mountedRef = useRef(true);
  const submittingRef = useRef(false);
  const submitAbortRef = useRef<AbortController | null>(null);

  const [description, setDescription] = useState('');
  const [photoFile, setPhotoFile] = useState<File | undefined>();
  const [photoPreviewUrl, setPhotoPreviewUrl] = useState<string | undefined>();
  const [isConfirmed, setIsConfirmed] = useState(false);
  const [isSubmitting, setIsSubmitting] = useState(false);
  const [isDropTarget, setIsDropTarget] = useState(false);

  useEffect(() => {
    mountedRef.current = true;
    submitAbortRef.current = new AbortController();
    const ac = submitAbortRef.current;

    return () => {
      mountedRef.current = false;
      ac.abort();
      if (photoPreviewUrl) {
        URL.revokeObjectURL(photoPreviewUrl);
      }
    };
  }, [photoPreviewUrl]);

  const assignPhoto = useCallback(
    (file: File) => {
      if (submittingRef.current) return;

      try {
        validateMaintenanceImageFile(file);
      } catch (err) {
        if (err instanceof MaintenanceImageValidationError) {
          toast.error(t(err.translationKey));
          return;
        }
        throw err;
      }

      if (photoPreviewUrl) {
        URL.revokeObjectURL(photoPreviewUrl);
      }

      setPhotoFile(file);
      setPhotoPreviewUrl(URL.createObjectURL(file));
    },
    [photoPreviewUrl, t]
  );

  const removePhoto = () => {
    if (photoPreviewUrl) {
      URL.revokeObjectURL(photoPreviewUrl);
    }

    setPhotoFile(undefined);
    setPhotoPreviewUrl(undefined);
  };

  const handlePhotoSelect = (event: ChangeEvent<HTMLInputElement>) => {
    const file = event.target.files?.[0];
    event.target.value = '';
    if (file) assignPhoto(file);
  };

  const handleDragOver = (event: DragEvent<HTMLDivElement>) => {
    event.preventDefault();
    event.stopPropagation();
    if (!submittingRef.current) setIsDropTarget(true);
  };

  const handleDragLeave = (event: DragEvent<HTMLDivElement>) => {
    event.preventDefault();
    event.stopPropagation();
    const related = event.relatedTarget as Node | null;
    if (!related || !event.currentTarget.contains(related)) {
      setIsDropTarget(false);
    }
  };

  const handleDrop = (event: DragEvent<HTMLDivElement>) => {
    event.preventDefault();
    event.stopPropagation();
    setIsDropTarget(false);
    if (submittingRef.current) return;

    const file = event.dataTransfer.files?.[0];
    if (file) assignPhoto(file);
  };

  const handleSubmit = async () => {
    if (submittingRef.current) return;

    if (!photoFile) {
      toast.error(t('validation.imageRequired'));
      return;
    }

    if (!isConfirmed) {
      toast.error(t('validation.maintenanceDeclarationRequired'));
      return;
    }

    const signal = submitAbortRef.current?.signal;
    if (!signal) return;

    submittingRef.current = true;
    setIsSubmitting(true);

    try {
      await createMaintenanceTask({
        file: photoFile,
        description: description.trim() || null,
        isConfirmed,
      }, signal);

      if (!mountedRef.current) return;
      toast.success(t('messages.maintenanceAllEntriesSaved'));
      onSubmit();
    } catch (err) {
      if (!mountedRef.current || isAbortLike(signal, err)) return;

      if (err instanceof MaintenanceImageValidationError) {
        toast.error(t(err.translationKey));
        return;
      }

      console.error(err);
      toast.error(t('messages.failedToSaveMaintenanceEntries'));
    } finally {
      submittingRef.current = false;
      if (mountedRef.current) {
        setIsSubmitting(false);
      }
    }
  };

  const imageSrc = safeImageSrc(photoPreviewUrl);
  const isSubmitDisabled = isSubmitting;

  return (
    <div className="min-h-screen bg-neutral-50 pb-24">
      <AppHeader title={t('home.maintenanceTasksLog')} showBack={true} showHome={true} />

      <div className="p-4 space-y-4">
        <div>
          <label className="block text-sm font-medium text-neutral-700 mb-2">
            {t('log.date')}
          </label>
          <div className="w-full px-3 py-2.5 bg-neutral-100 border border-neutral-300 rounded-lg text-neutral-900">
            {today}
          </div>
        </div>

        <div>
          <label className="block text-sm font-medium text-neutral-700 mb-2">
            {t('log.photo')}
            <span className="text-red-500 ms-1">*</span>
          </label>
          {imageSrc ? (
            <div className="relative">
              <img
                src={imageSrc}
                alt={t('log.maintenanceTaskPhotoAlt')}
                className="w-full h-48 object-cover rounded-lg border border-neutral-300"
              />
              <button
                type="button"
                onClick={removePhoto}
                disabled={isSubmitting}
                className="absolute top-2 right-2 p-1 bg-red-500 text-white rounded-lg hover:bg-red-600 transition-colors disabled:opacity-50"
              >
                <X className="w-4 h-4" />
              </button>
            </div>
          ) : (
            <div
              onDragOver={handleDragOver}
              onDragLeave={handleDragLeave}
              onDrop={handleDrop}
              className={`rounded-lg border-2 border-dashed transition-colors ${
                isDropTarget ? 'border-neutral-800 bg-neutral-100' : 'border-neutral-300'
              }`}
            >
              <button
                type="button"
                onClick={() => fileInputRef.current?.click()}
                disabled={isSubmitting}
                className="w-full p-3 flex flex-col items-center justify-center gap-1 text-neutral-700 hover:border-neutral-500 hover:bg-neutral-50 transition-colors rounded-lg disabled:opacity-50"
              >
                <div className="flex items-center justify-center gap-2">
                  <Camera className="w-5 h-5" />
                  <span className="text-sm font-medium">{t('log.takePicture')}</span>
                </div>
                <span className="text-xs text-neutral-500">{t('log.dropPhoto')}</span>
              </button>
            </div>
          )}
        </div>

        <div>
          <label
            htmlFor="field-maintenance-task-description"
            className="block text-sm font-medium text-neutral-700 mb-2"
          >
            {t('log.taskDescription')}
          </label>
          <textarea
            id="field-maintenance-task-description"
            value={description}
            disabled={isSubmitting}
            onChange={(event) => setDescription(event.target.value)}
            placeholder={t('log.taskDescriptionPlaceholder')}
            rows={3}
            className="w-full px-3 py-2.5 bg-white border border-neutral-300 rounded-lg text-neutral-900 placeholder:text-neutral-400 focus:outline-none focus:ring-2 focus:ring-neutral-800 resize-none"
          />
        </div>

        <div>
          <label className="flex items-start gap-3 text-sm text-neutral-900">
            <input
              id="field-maintenance-task-confirmation"
              type="checkbox"
              checked={isConfirmed}
              disabled={isSubmitting}
              onChange={(event) => setIsConfirmed(event.target.checked)}
              className="w-4 h-4 mt-1 shrink-0 rounded border-neutral-300 text-neutral-800 focus:ring-neutral-800"
            />
            <span>{t('log.tasksConfirmation')}</span>
          </label>
        </div>
      </div>

      <div className="fixed bottom-0 left-0 right-0 p-4 bg-white border-t border-neutral-200">
        <div className="max-w-md mx-auto">
          <button
            type="button"
            onClick={handleSubmit}
            disabled={isSubmitDisabled}
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

      <input
        ref={fileInputRef}
        type="file"
        accept="image/*"
        onChange={handlePhotoSelect}
        className="hidden"
      />
    </div>
  );
}
