import { useCallback, useEffect, useMemo, useRef, useState, type CSSProperties } from 'react';
import toast from 'react-hot-toast';
import { ChevronLeft, ChevronRight, Images, Loader2, RefreshCw, SlidersHorizontal, Trash2, X } from 'lucide-react';
import { useLanguage } from '../context/LanguageContext';
import { AppHeader } from './AppHeader';
import { ImageWithFallback } from './figma/ImageWithFallback';
import {
  MAINTENANCE_LOG_IMAGE_MAX_BYTES,
  MaintenanceImageValidationError,
  validateMaintenanceImageFile,
} from '../services/uploadService';
import {
  deleteMachineParameterPhoto,
  getMachineParameterPhotos,
  getMachineParameterPhotosHierarchy,
  uploadMachineParameterPhotos,
} from '../services/machineParameterPhotosService';
import type { HierarchyArray } from '../services/hierarchyService';
import type { MachineParameterPhoto } from '../types/machineParameterPhotos';
import { resolveCatalogDisplayValue } from '../utils/resolveCatalogDisplayValue';
import { safeImageSrc } from '../utils/safeUrl';

const MAXIMUM_FILES_PER_REQUEST = 10;

interface MachineParameterPhotosScreenProps {
  onBack: () => void;
}

const imageFrameStyle: CSSProperties = { position: 'relative' };

const carouselNavButtonStyle = (side: 'left' | 'right'): CSSProperties => ({
  position: 'absolute',
  top: '50%',
  [side]: 8,
  transform: 'translateY(-50%)',
  zIndex: 2,
  padding: 8,
  border: 'none',
  borderRadius: 9999,
  backgroundColor: 'rgba(0,0,0,0.45)',
  color: '#ffffff',
  cursor: 'pointer',
  display: 'inline-flex',
  alignItems: 'center',
  justifyContent: 'center',
});

const carouselCounterStyle: CSSProperties = {
  position: 'absolute',
  bottom: 8,
  left: '50%',
  transform: 'translateX(-50%)',
  zIndex: 2,
  padding: '2px 8px',
  borderRadius: 9999,
  backgroundColor: 'rgba(0,0,0,0.5)',
  color: '#ffffff',
  fontSize: 12,
};

const viewerOverlayStyle: CSSProperties = {
  position: 'fixed',
  inset: 0,
  zIndex: 100,
  backgroundColor: 'rgba(0,0,0,0.95)',
  display: 'flex',
  alignItems: 'center',
  justifyContent: 'center',
};

const viewerCloseButtonStyle: CSSProperties = {
  position: 'absolute',
  top: 16,
  insetInlineEnd: 16,
  zIndex: 10,
  width: 44,
  height: 44,
  padding: 0,
  border: '1px solid rgba(255,255,255,0.7)',
  borderRadius: 9999,
  backgroundColor: 'rgba(0,0,0,0.55)',
  color: '#ffffff',
  cursor: 'pointer',
  display: 'inline-flex',
  alignItems: 'center',
  justifyContent: 'center',
};

const viewerNavButtonStyle = (side: 'left' | 'right'): CSSProperties => ({
  position: 'absolute',
  top: '50%',
  [side]: 12,
  transform: 'translateY(-50%)',
  zIndex: 10,
  padding: 8,
  border: 'none',
  borderRadius: 9999,
  backgroundColor: 'rgba(255,255,255,0.15)',
  color: '#ffffff',
  cursor: 'pointer',
  display: 'inline-flex',
  alignItems: 'center',
  justifyContent: 'center',
});

const viewerCounterStyle: CSSProperties = {
  position: 'absolute',
  bottom: 16,
  left: '50%',
  transform: 'translateX(-50%)',
  color: '#ffffff',
  fontSize: 14,
};

const viewerImageStyle: CSSProperties = {
  maxWidth: '96vw',
  maxHeight: '80vh',
  width: 'auto',
  height: 'auto',
  objectFit: 'contain',
};

const deleteButtonStyle: CSSProperties = {
  width: '100%',
  display: 'inline-flex',
  alignItems: 'center',
  justifyContent: 'center',
  gap: 8,
  padding: '10px 16px',
  border: 'none',
  borderRadius: 8,
  backgroundColor: '#dc2626',
  color: '#ffffff',
  fontSize: 14,
  fontWeight: 500,
  cursor: 'pointer',
};

const modalOverlayStyle: CSSProperties = {
  position: 'fixed',
  inset: 0,
  zIndex: 110,
  backgroundColor: 'rgba(0,0,0,0.55)',
  display: 'flex',
  alignItems: 'center',
  justifyContent: 'center',
  padding: 16,
};

const modalCardStyle: CSSProperties = {
  width: '100%',
  maxWidth: 320,
  backgroundColor: '#ffffff',
  borderRadius: 12,
  padding: 20,
  boxShadow: '0 10px 30px rgba(0,0,0,0.25)',
};

const modalButtonRowStyle: CSSProperties = {
  display: 'flex',
  gap: 8,
  marginTop: 16,
};

const modalButtonStyle: CSSProperties = {
  flex: 1,
  display: 'inline-flex',
  alignItems: 'center',
  justifyContent: 'center',
  minHeight: 44,
  padding: '10px 16px',
  borderRadius: 8,
  fontSize: 14,
  fontWeight: 500,
  cursor: 'pointer',
};

const modalNoButtonStyle: CSSProperties = {
  ...modalButtonStyle,
  border: '1px solid #d4d4d4',
  backgroundColor: '#ffffff',
  color: '#404040',
};

const modalYesButtonStyle: CSSProperties = {
  ...modalButtonStyle,
  border: 'none',
  backgroundColor: '#dc2626',
  color: '#ffffff',
};

export function MachineParameterPhotosScreen({ onBack }: MachineParameterPhotosScreenProps) {
  const { t } = useLanguage();
  const fileInputRef = useRef<HTMLInputElement>(null);
  const mountedRef = useRef(true);
  const uploadingRef = useRef(false);
  const viewerTriggerRef = useRef<HTMLElement | null>(null);
  const touchStartXRef = useRef<number | null>(null);
  const viewerTouchStartXRef = useRef<number | null>(null);
  const viewerDidSwipeRef = useRef(false);

  const [hierarchy, setHierarchy] = useState<HierarchyArray[]>([]);
  const [canManage, setCanManage] = useState(false);
  const [loadingHierarchy, setLoadingHierarchy] = useState(false);
  const [hierarchyError, setHierarchyError] = useState<string | null>(null);

  const [selectedArrayId, setSelectedArrayId] = useState('');
  const [selectedMachineId, setSelectedMachineId] = useState('');

  const [photos, setPhotos] = useState<MachineParameterPhoto[]>([]);
  const [loadingPhotos, setLoadingPhotos] = useState(false);
  const [photosError, setPhotosError] = useState<string | null>(null);

  const [uploading, setUploading] = useState(false);
  const [deletingPhotoId, setDeletingPhotoId] = useState<string | null>(null);
  const [confirmDelete, setConfirmDelete] = useState(false);

  const [carouselIndex, setCarouselIndex] = useState(0);
  const [viewerImageIndex, setViewerImageIndex] = useState<number | null>(null);

  const machinesForArray = useMemo(() => {
    if (!selectedArrayId) return [];
    const array = hierarchy.find((item) => item.arrayId === selectedArrayId);
    return array?.machines ?? [];
  }, [hierarchy, selectedArrayId]);

  const photoSources = useMemo(
    () =>
      photos
        .map((photo) => ({
          photo,
          src: safeImageSrc(photo.imageUrl),
        }))
        .filter((item): item is { photo: MachineParameterPhoto; src: string } => Boolean(item.src)),
    [photos]
  );

  const showCarousel = photoSources.length > 1;
  const currentPhoto = photoSources[carouselIndex];

  const closeImageViewer = () => {
    setViewerImageIndex(null);
    const trigger = viewerTriggerRef.current;
    viewerTriggerRef.current = null;
    queueMicrotask(() => trigger?.focus());
  };

  const openImageViewer = (index: number, trigger: HTMLElement) => {
    viewerTriggerRef.current = trigger;
    setViewerImageIndex(index);
  };

  const goToPreviousImage = useCallback(() => {
    if (photoSources.length < 2) return;
    setCarouselIndex((current) => (current - 1 + photoSources.length) % photoSources.length);
  }, [photoSources.length]);

  const goToNextImage = useCallback(() => {
    if (photoSources.length < 2) return;
    setCarouselIndex((current) => (current + 1) % photoSources.length);
  }, [photoSources.length]);

  const stepViewer = useCallback(
    (direction: -1 | 1) => {
      if (photoSources.length < 2) return;
      setViewerImageIndex((current) => {
        if (current == null) return current;
        const next = (current + direction + photoSources.length) % photoSources.length;
        setCarouselIndex(next);
        return next;
      });
    },
    [photoSources.length]
  );

  const loadHierarchy = useCallback(async () => {
    try {
      setLoadingHierarchy(true);
      setHierarchyError(null);
      const result = await getMachineParameterPhotosHierarchy();
      if (!mountedRef.current) return;
      setHierarchy(Array.isArray(result.arrays) ? result.arrays : []);
      setCanManage(result.canManage === true);
    } catch (err) {
      console.error(err);
      if (!mountedRef.current) return;
      setHierarchy([]);
      setCanManage(false);
      setHierarchyError(t('common.failedToLoad'));
    } finally {
      if (mountedRef.current) {
        setLoadingHierarchy(false);
      }
    }
  }, [t]);

  const loadPhotos = useCallback(
    async (machineId: string, options?: { focusIndex?: number }) => {
      try {
        setLoadingPhotos(true);
        setPhotosError(null);
        const result = await getMachineParameterPhotos(machineId);
        if (!mountedRef.current) return;
        const nextPhotos = Array.isArray(result) ? result : [];
        setPhotos(nextPhotos);
        const nextIndex =
          options?.focusIndex != null
            ? Math.min(Math.max(options.focusIndex, 0), Math.max(nextPhotos.length - 1, 0))
            : 0;
        setCarouselIndex(nextPhotos.length === 0 ? 0 : nextIndex);
      } catch (err) {
        console.error(err);
        if (!mountedRef.current) return;
        setPhotos([]);
        setCarouselIndex(0);
        setPhotosError(t('common.failedToLoad'));
      } finally {
        if (mountedRef.current) {
          setLoadingPhotos(false);
        }
      }
    },
    [t]
  );

  useEffect(() => {
    mountedRef.current = true;
    return () => {
      mountedRef.current = false;
    };
  }, []);

  useEffect(() => {
    void loadHierarchy();
  }, [loadHierarchy]);

  useEffect(() => {
    setConfirmDelete(false);
    setViewerImageIndex(null);
    viewerTriggerRef.current = null;
    touchStartXRef.current = null;
    viewerTouchStartXRef.current = null;
    viewerDidSwipeRef.current = false;

    if (!selectedMachineId) {
      setPhotos([]);
      setPhotosError(null);
      setLoadingPhotos(false);
      setCarouselIndex(0);
      return;
    }

    void loadPhotos(selectedMachineId);
  }, [loadPhotos, selectedMachineId]);

  useEffect(() => {
    if (carouselIndex < photoSources.length) return;
    setCarouselIndex(0);
  }, [carouselIndex, photoSources.length]);

  useEffect(() => {
    if (!confirmDelete) return;
    const previousOverflow = document.body.style.overflow;
    document.body.style.overflow = 'hidden';
    const handleKeyDown = (event: KeyboardEvent) => {
      if (event.key === 'Escape' && !deletingPhotoId) {
        setConfirmDelete(false);
      }
    };
    window.addEventListener('keydown', handleKeyDown);
    return () => {
      document.body.style.overflow = previousOverflow;
      window.removeEventListener('keydown', handleKeyDown);
    };
  }, [confirmDelete, deletingPhotoId]);

  useEffect(() => {
    if (viewerImageIndex == null) return;
    const previousOverflow = document.body.style.overflow;
    document.body.style.overflow = 'hidden';

    const handleKeyDown = (event: KeyboardEvent) => {
      if (event.key === 'Escape') {
        closeImageViewer();
      } else if (event.key === 'ArrowLeft') {
        stepViewer(-1);
      } else if (event.key === 'ArrowRight') {
        stepViewer(1);
      }
    };
    window.addEventListener('keydown', handleKeyDown);
    return () => {
      document.body.style.overflow = previousOverflow;
      window.removeEventListener('keydown', handleKeyDown);
    };
  }, [stepViewer, viewerImageIndex]);

  const handleArrayChange = (arrayId: string) => {
    setSelectedArrayId(arrayId);
    setSelectedMachineId('');
    setPhotos([]);
    setPhotosError(null);
    setCarouselIndex(0);
    setConfirmDelete(false);
    setViewerImageIndex(null);
  };

  const handleMachineChange = (machineId: string) => {
    setSelectedMachineId(machineId);
    setConfirmDelete(false);
  };

  const validateSelectedFiles = (files: File[]): File[] | null => {
    if (files.length === 0) {
      return null;
    }

    if (files.length > MAXIMUM_FILES_PER_REQUEST) {
      toast.error(t('machineParameterPhotos.tooManyFiles'));
      return null;
    }

    for (const file of files) {
      if (file.size <= 0) {
        toast.error(t('machineParameterPhotos.emptyFile'));
        return null;
      }

      try {
        validateMaintenanceImageFile(file, MAINTENANCE_LOG_IMAGE_MAX_BYTES);
      } catch (err) {
        if (err instanceof MaintenanceImageValidationError) {
          toast.error(t(err.translationKey));
          return null;
        }
        throw err;
      }
    }

    return files;
  };

  const handleFilesSelected = async (fileList: FileList | null) => {
    if (!canManage || !selectedMachineId || uploadingRef.current) return;
    const files = validateSelectedFiles(Array.from(fileList ?? []));
    if (!files) return;

    uploadingRef.current = true;
    setUploading(true);
    try {
      const previousCount = photos.length;
      await uploadMachineParameterPhotos(selectedMachineId, files);
      if (!mountedRef.current) return;
      toast.success(t('machineParameterPhotos.uploadSuccess'));
      await loadPhotos(selectedMachineId, { focusIndex: previousCount });
    } catch (err) {
      console.error(err);
      if (mountedRef.current) {
        toast.error(t('machineParameterPhotos.uploadFailed'));
      }
    } finally {
      uploadingRef.current = false;
      if (mountedRef.current) {
        setUploading(false);
      }
      if (fileInputRef.current) {
        fileInputRef.current.value = '';
      }
    }
  };

  const handleDeleteCurrentPhoto = async () => {
    if (!canManage || !currentPhoto || deletingPhotoId) return;

    const photoId = currentPhoto.photo.id;
    setDeletingPhotoId(photoId);
    try {
      await deleteMachineParameterPhoto(photoId);
      if (!mountedRef.current) return;
      toast.success(t('machineParameterPhotos.deleteSuccess'));
      setConfirmDelete(false);
      const nextIndex = Math.max(carouselIndex - (carouselIndex >= photoSources.length - 1 ? 1 : 0), 0);
      await loadPhotos(selectedMachineId, { focusIndex: nextIndex });
    } catch (err) {
      console.error(err);
      if (mountedRef.current) {
        toast.error(t('machineParameterPhotos.deleteFailed'));
      }
    } finally {
      if (mountedRef.current) {
        setDeletingPhotoId(null);
      }
    }
  };

  const viewerSrc =
    viewerImageIndex != null ? photoSources[viewerImageIndex]?.src : undefined;

  return (
    <div className="min-h-screen bg-neutral-50 pb-24">
      <AppHeader
        title={t('home.machineParameterPhotos')}
        titleIcon={SlidersHorizontal}
        showBack={true}
        showHome={true}
        onBack={onBack}
      />

      <div className="p-4 space-y-4">
        {hierarchyError && !loadingHierarchy && (
          <div className="bg-white border border-neutral-200 rounded-lg p-4 flex flex-col items-center gap-3">
            <p className="text-sm text-neutral-600 text-center">{hierarchyError}</p>
            <button
              type="button"
              onClick={() => void loadHierarchy()}
              className="inline-flex items-center gap-2 px-4 py-2 bg-neutral-800 text-white text-sm font-medium rounded-lg hover:bg-neutral-900 active:bg-neutral-950 transition-colors"
            >
              <RefreshCw className="w-4 h-4" />
              {t('common.retry')}
            </button>
          </div>
        )}

        <div className="bg-white border border-neutral-200 rounded-lg p-4 space-y-3">
          <div>
            <label className="block text-xs font-medium text-neutral-600 mb-1">
              {t('maintenanceLogV2.selectArray')}
            </label>
            <select
              value={selectedArrayId}
              disabled={loadingHierarchy || Boolean(hierarchyError)}
              onChange={(event) => handleArrayChange(event.target.value)}
              className="w-full px-3 py-2 bg-white border border-neutral-300 rounded text-sm text-neutral-900 focus:outline-none focus:ring-2 focus:ring-neutral-800"
            >
              <option value="">{t('maintenanceLogV2.selectArray')}</option>
              {loadingHierarchy && (
                <option value="" disabled>
                  {t('common.loading')}
                </option>
              )}
              {!loadingHierarchy &&
                hierarchy.map((array) =>
                  array.arrayId ? (
                    <option key={array.arrayId} value={array.arrayId}>
                      {t(array.nameKey)}
                    </option>
                  ) : null
                )}
            </select>
          </div>

          <div>
            <label className="block text-xs font-medium text-neutral-600 mb-1">
              {t('machine.select')}
            </label>
            <select
              value={selectedMachineId}
              disabled={!selectedArrayId || uploading}
              onChange={(event) => handleMachineChange(event.target.value)}
              className="w-full px-3 py-2 bg-white border border-neutral-300 rounded text-sm text-neutral-900 focus:outline-none focus:ring-2 focus:ring-neutral-800 disabled:bg-neutral-100 disabled:text-neutral-400"
            >
              <option value="">{t('machine.select')}</option>
              {machinesForArray.map((machine) => (
                <option key={machine.id} value={machine.id}>
                  {resolveCatalogDisplayValue(machine.name, t)}
                </option>
              ))}
            </select>
          </div>
        </div>

        {selectedMachineId && (
          <div className="bg-white border border-neutral-200 rounded-lg p-4 space-y-3">
            {loadingPhotos && (
              <div className="flex items-center justify-center gap-2 py-8 text-sm text-neutral-500" role="status">
                <Loader2 className="w-5 h-5 animate-spin" />
                {t('common.loading')}
              </div>
            )}

            {photosError && !loadingPhotos && (
              <div className="flex flex-col items-center gap-3 py-4">
                <p className="text-sm text-neutral-600 text-center">{photosError}</p>
                <button
                  type="button"
                  onClick={() => void loadPhotos(selectedMachineId)}
                  className="inline-flex items-center gap-2 px-4 py-2 bg-neutral-800 text-white text-sm font-medium rounded-lg hover:bg-neutral-900 active:bg-neutral-950 transition-colors"
                >
                  <RefreshCw className="w-4 h-4" />
                  {t('common.retry')}
                </button>
              </div>
            )}

            {!loadingPhotos && !photosError && photoSources.length === 0 && (
              <p className="text-sm text-neutral-600 text-center py-6">
                {t('machineParameterPhotos.empty')}
              </p>
            )}

            {!loadingPhotos && !photosError && currentPhoto && (
              <div
                style={imageFrameStyle}
                onTouchStart={(event) => {
                  if (!showCarousel) return;
                  touchStartXRef.current = event.changedTouches[0]?.clientX ?? null;
                }}
                onTouchEnd={(event) => {
                  if (!showCarousel || touchStartXRef.current == null) return;
                  const endX = event.changedTouches[0]?.clientX;
                  if (endX == null) {
                    touchStartXRef.current = null;
                    return;
                  }
                  const deltaX = endX - touchStartXRef.current;
                  touchStartXRef.current = null;
                  if (Math.abs(deltaX) < 40) return;
                  if (deltaX > 0) goToPreviousImage();
                  else goToNextImage();
                }}
              >
                <button
                  type="button"
                  onClick={(event) =>
                    openImageViewer(showCarousel ? carouselIndex : 0, event.currentTarget)
                  }
                  aria-label={
                    showCarousel
                      ? `${t('log.openImage')} ${carouselIndex + 1}`
                      : t('log.openImage')
                  }
                  className="block w-full rounded-lg focus:outline-none focus:ring-2 focus:ring-neutral-800"
                >
                  <ImageWithFallback
                    key={currentPhoto.src}
                    src={currentPhoto.src}
                    alt={
                      showCarousel
                        ? `${t('machineParameterPhotos.photoAlt')} ${carouselIndex + 1}`
                        : t('machineParameterPhotos.photoAlt')
                    }
                    className="w-full rounded-lg border border-neutral-300"
                  />
                </button>
                {showCarousel && (
                  <>
                    <button
                      type="button"
                      onClick={goToPreviousImage}
                      aria-label={t('log.previousImage')}
                      style={carouselNavButtonStyle('left')}
                    >
                      <ChevronLeft className="w-6 h-6" aria-hidden />
                    </button>
                    <button
                      type="button"
                      onClick={goToNextImage}
                      aria-label={t('log.nextImage')}
                      style={carouselNavButtonStyle('right')}
                    >
                      <ChevronRight className="w-6 h-6" aria-hidden />
                    </button>
                    <div style={carouselCounterStyle}>
                      {carouselIndex + 1} / {photoSources.length}
                    </div>
                  </>
                )}
              </div>
            )}

            {canManage && !loadingPhotos && !photosError && (
              <div className="space-y-2">
                <input
                  ref={fileInputRef}
                  type="file"
                  accept="image/jpeg,image/png,image/webp"
                  multiple
                  style={{ display: 'none' }}
                  onChange={(event) => {
                    void handleFilesSelected(event.target.files);
                  }}
                />
                <button
                  type="button"
                  disabled={uploading || Boolean(deletingPhotoId)}
                  onClick={() => fileInputRef.current?.click()}
                  className="w-full inline-flex items-center justify-center gap-2 px-4 py-2.5 bg-neutral-800 text-white text-sm font-medium rounded-lg hover:bg-neutral-900 active:bg-neutral-950 transition-colors disabled:opacity-50 disabled:cursor-not-allowed"
                >
                  {uploading ? (
                    <>
                      <Loader2 className="w-4 h-4 animate-spin" />
                      {t('machineParameterPhotos.uploading')}
                    </>
                  ) : (
                    <>
                      <Images className="w-4 h-4" />
                      {t('machineParameterPhotos.addPhotos')}
                    </>
                  )}
                </button>

                {currentPhoto && (
                  <button
                    type="button"
                    disabled={uploading || Boolean(deletingPhotoId)}
                    onClick={() => setConfirmDelete(true)}
                    style={{
                      ...deleteButtonStyle,
                      opacity: uploading || deletingPhotoId ? 0.5 : 1,
                      cursor: uploading || deletingPhotoId ? 'not-allowed' : 'pointer',
                    }}
                  >
                    <Trash2 className="w-4 h-4" />
                    {t('machineParameterPhotos.deletePhoto')}
                  </button>
                )}
              </div>
            )}
          </div>
        )}
      </div>

      {confirmDelete && currentPhoto && (
        <div
          style={modalOverlayStyle}
          role="dialog"
          aria-modal="true"
          aria-labelledby="machine-parameter-photos-delete-title"
          onClick={() => {
            if (!deletingPhotoId) {
              setConfirmDelete(false);
            }
          }}
        >
          <div
            style={modalCardStyle}
            onClick={(event) => event.stopPropagation()}
          >
            <p
              id="machine-parameter-photos-delete-title"
              className="text-sm text-neutral-800 text-center"
              style={{ margin: 0, fontSize: 16, fontWeight: 600, color: '#262626', textAlign: 'center' }}
            >
              {t('machineParameterPhotos.confirmDelete')}
            </p>
            <div style={modalButtonRowStyle}>
              <button
                type="button"
                disabled={Boolean(deletingPhotoId)}
                onClick={() => setConfirmDelete(false)}
                style={{
                  ...modalNoButtonStyle,
                  opacity: deletingPhotoId ? 0.5 : 1,
                  cursor: deletingPhotoId ? 'not-allowed' : 'pointer',
                }}
              >
                {t('common.no')}
              </button>
              <button
                type="button"
                disabled={Boolean(deletingPhotoId)}
                onClick={() => void handleDeleteCurrentPhoto()}
                style={{
                  ...modalYesButtonStyle,
                  opacity: deletingPhotoId ? 0.5 : 1,
                  cursor: deletingPhotoId ? 'not-allowed' : 'pointer',
                }}
              >
                {deletingPhotoId ? <Loader2 className="w-4 h-4 animate-spin" /> : t('common.yes')}
              </button>
            </div>
          </div>
        </div>
      )}

      {viewerSrc && viewerImageIndex != null && (
        <div
          style={viewerOverlayStyle}
          role="dialog"
          aria-modal="true"
          aria-label={t('log.imageViewer')}
          onClick={() => {
            if (viewerDidSwipeRef.current) {
              viewerDidSwipeRef.current = false;
              return;
            }
            closeImageViewer();
          }}
          onTouchStart={(event) => {
            viewerTouchStartXRef.current = event.changedTouches[0]?.clientX ?? null;
            viewerDidSwipeRef.current = false;
          }}
          onTouchEnd={(event) => {
            if (viewerTouchStartXRef.current == null) return;
            const endX = event.changedTouches[0]?.clientX;
            if (endX == null) {
              viewerTouchStartXRef.current = null;
              return;
            }
            const deltaX = endX - viewerTouchStartXRef.current;
            viewerTouchStartXRef.current = null;
            if (Math.abs(deltaX) < 40) return;
            viewerDidSwipeRef.current = true;
            if (deltaX > 0) stepViewer(-1);
            else stepViewer(1);
          }}
        >
          <button
            type="button"
            onClick={(event) => {
              event.stopPropagation();
              closeImageViewer();
            }}
            aria-label={t('log.closeViewer')}
            style={viewerCloseButtonStyle}
          >
            <X className="w-7 h-7" aria-hidden />
          </button>
          <img
            src={viewerSrc}
            alt={
              showCarousel
                ? `${t('machineParameterPhotos.photoAlt')} ${viewerImageIndex + 1}`
                : t('machineParameterPhotos.photoAlt')
            }
            style={viewerImageStyle}
            onClick={(event) => event.stopPropagation()}
          />
          {showCarousel && (
            <>
              <button
                type="button"
                onClick={(event) => {
                  event.stopPropagation();
                  stepViewer(-1);
                }}
                aria-label={t('log.previousImage')}
                style={viewerNavButtonStyle('left')}
              >
                <ChevronLeft className="w-8 h-8" aria-hidden />
              </button>
              <button
                type="button"
                onClick={(event) => {
                  event.stopPropagation();
                  stepViewer(1);
                }}
                aria-label={t('log.nextImage')}
                style={viewerNavButtonStyle('right')}
              >
                <ChevronRight className="w-8 h-8" aria-hidden />
              </button>
              <div
                style={viewerCounterStyle}
                onClick={(event) => event.stopPropagation()}
              >
                {viewerImageIndex + 1} / {photoSources.length}
              </div>
            </>
          )}
        </div>
      )}
    </div>
  );
}
