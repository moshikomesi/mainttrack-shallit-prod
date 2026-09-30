import { useId, useRef, useState, type CSSProperties, type ChangeEvent } from 'react';
import { Camera, Images, X } from 'lucide-react';
import { useLanguage } from '../context/LanguageContext';
import { MAINTENANCE_LOG_MAX_ADDITIONAL_IMAGES } from '../services/uploadService';
import { safeImageSrc } from '../utils/safeUrl';

interface MaintenanceImagePickerProps {
  primaryImage?: string | null;
  additionalImages: string[];
  disabled?: boolean;
  onPrimarySelected: (file: File) => void;
  onAdditionalSelected: (files: File[]) => void;
  onRemovePrimary: () => void;
  onRemoveAdditional: (index: number) => void;
}

type ImageSelectionPurpose = 'primary' | 'additional';

const imagePreviewFrameStyle: CSSProperties = {
  position: 'relative',
};

const removeOverlayButtonStyle: CSSProperties = {
  position: 'absolute',
  top: 8,
  right: 8,
  zIndex: 2,
  display: 'inline-flex',
  alignItems: 'center',
  justifyContent: 'center',
  width: 36,
  height: 36,
  padding: 0,
  border: 'none',
  borderRadius: 8,
  backgroundColor: '#ef4444',
  color: '#ffffff',
  cursor: 'pointer',
  boxShadow: '0 1px 3px rgba(0,0,0,0.35)',
};

const removeTextButtonStyle: CSSProperties = {
  width: '100%',
  padding: '8px 12px',
  border: '1px solid #fca5a5',
  borderRadius: 8,
  fontSize: 14,
  fontWeight: 500,
  color: '#dc2626',
  backgroundColor: 'transparent',
  cursor: 'pointer',
};

const sourceButtonClassName =
  'w-full inline-flex items-center justify-center gap-2 px-3 py-2.5 border border-neutral-300 rounded-lg bg-white text-sm font-medium text-neutral-700 hover:bg-neutral-50 disabled:opacity-50 disabled:cursor-not-allowed';

function PhotoSourceActions({
  disabled,
  labelledBy,
  takePhotoLabel,
  choosePhotosLabel,
  onTakePhoto,
  onChoosePhotos,
}: {
  disabled: boolean;
  labelledBy?: string;
  takePhotoLabel: string;
  choosePhotosLabel: string;
  onTakePhoto: () => void;
  onChoosePhotos: () => void;
}) {
  return (
    <div className="grid grid-cols-2 gap-2" role="group" aria-labelledby={labelledBy}>
      <button type="button" onClick={onTakePhoto} disabled={disabled} className={sourceButtonClassName}>
        <Camera className="w-4 h-4 shrink-0" aria-hidden />
        {takePhotoLabel}
      </button>
      <button type="button" onClick={onChoosePhotos} disabled={disabled} className={sourceButtonClassName}>
        <Images className="w-4 h-4 shrink-0" aria-hidden />
        {choosePhotosLabel}
      </button>
    </div>
  );
}

export function MaintenanceImagePicker({
  primaryImage,
  additionalImages,
  disabled = false,
  onPrimarySelected,
  onAdditionalSelected,
  onRemovePrimary,
  onRemoveAdditional,
}: MaintenanceImagePickerProps) {
  const { t } = useLanguage();
  const fieldId = useId();
  const replaceLabelId = `${fieldId}-replace-primary`;
  const additionalLabelId = `${fieldId}-add-photos`;
  const cameraInputRef = useRef<HTMLInputElement>(null);
  const galleryInputRef = useRef<HTMLInputElement>(null);
  const purposeRef = useRef<ImageSelectionPurpose>('primary');
  const [dropTargetActive, setDropTargetActive] = useState(false);
  const primarySrc = safeImageSrc(primaryImage);
  const additionalSources = additionalImages
    .map((src, index) => ({ src: safeImageSrc(src), index }))
    .filter((item): item is { src: string; index: number } => Boolean(item.src));
  const canAddAdditional =
    Boolean(primarySrc) && additionalImages.length < MAINTENANCE_LOG_MAX_ADDITIONAL_IMAGES;

  const openCamera = (purpose: ImageSelectionPurpose) => {
    purposeRef.current = purpose;
    cameraInputRef.current?.click();
  };

  const openGallery = (purpose: ImageSelectionPurpose) => {
    purposeRef.current = purpose;
    galleryInputRef.current?.click();
  };

  const applySelectedFiles = (files: File[]) => {
    if (files.length === 0) return;

    const purpose = purposeRef.current;
    if (purpose === 'additional' && primarySrc) {
      onAdditionalSelected(files);
      return;
    }

    onPrimarySelected(files[0]);
    if (files.length > 1) {
      onAdditionalSelected(files.slice(1));
    }
  };

  const selectFromCamera = (event: ChangeEvent<HTMLInputElement>) => {
    const file = event.target.files?.[0];
    event.target.value = '';
    if (file) applySelectedFiles([file]);
  };

  const selectFromGallery = (event: ChangeEvent<HTMLInputElement>) => {
    const files = Array.from(event.target.files ?? []);
    event.target.value = '';
    applySelectedFiles(files);
  };

  const sourceActions = (purpose: ImageSelectionPurpose, labelledBy?: string) => (
    <PhotoSourceActions
      disabled={disabled}
      labelledBy={labelledBy}
      takePhotoLabel={t('log.takePhoto')}
      choosePhotosLabel={t('log.choosePhotos')}
      onTakePhoto={() => openCamera(purpose)}
      onChoosePhotos={() => openGallery(purpose)}
    />
  );

  return (
    <div className="space-y-3">
      {primarySrc ? (
        <div className="space-y-2">
          <div style={imagePreviewFrameStyle}>
            <img
              src={primarySrc}
              alt={t('log.maintenancePhotoAlt')}
              className="w-full h-48 object-cover rounded-lg border border-neutral-300"
            />
            <button
              type="button"
              onClick={onRemovePrimary}
              disabled={disabled}
              aria-label={t('log.removePhoto')}
              title={t('log.removePhoto')}
              style={{
                ...removeOverlayButtonStyle,
                opacity: disabled ? 0.5 : 1,
                cursor: disabled ? 'not-allowed' : 'pointer',
              }}
            >
              <X className="w-5 h-5" aria-hidden />
            </button>
          </div>
          <p id={replaceLabelId} className="text-xs font-medium text-neutral-600">
            {t('log.replacePrimaryPhoto')}
          </p>
          {sourceActions('primary', replaceLabelId)}
          <button
            type="button"
            onClick={onRemovePrimary}
            disabled={disabled}
            style={{
              ...removeTextButtonStyle,
              opacity: disabled ? 0.5 : 1,
              cursor: disabled ? 'not-allowed' : 'pointer',
            }}
          >
            {t('log.removePhoto')}
          </button>
        </div>
      ) : (
        <div
          onDragOver={(event) => {
            event.preventDefault();
            event.stopPropagation();
            if (!disabled) setDropTargetActive(true);
          }}
          onDragLeave={(event) => {
            event.preventDefault();
            event.stopPropagation();
            const related = event.relatedTarget as Node | null;
            if (!related || !event.currentTarget.contains(related)) setDropTargetActive(false);
          }}
          onDrop={(event) => {
            event.preventDefault();
            event.stopPropagation();
            setDropTargetActive(false);
            if (disabled) return;
            const file = event.dataTransfer.files?.[0];
            if (file) onPrimarySelected(file);
          }}
          className={`rounded-lg border-2 border-dashed transition-colors ${
            dropTargetActive ? 'border-neutral-800 bg-neutral-100' : 'border-neutral-300'
          }`}
        >
          <div className="p-3 space-y-2">
            {sourceActions('primary')}
            <p className="text-xs text-center text-neutral-500">{t('log.dropPhoto')}</p>
          </div>
        </div>
      )}

      {primarySrc && (
        <div className="space-y-3">
          {additionalSources.map(({ src, index }) => (
            <div key={`${src}-${index}`} className="space-y-2">
              <p className="text-xs font-medium text-neutral-600">
                {t('log.additionalPhotos')} ({index + 1}/{MAINTENANCE_LOG_MAX_ADDITIONAL_IMAGES})
              </p>
              <div style={imagePreviewFrameStyle}>
                <img
                  src={src}
                  alt={`${t('log.maintenancePhotoAlt')} ${index + 2}`}
                  className="w-full h-48 object-cover rounded-lg border border-neutral-300"
                />
                <button
                  type="button"
                  onClick={() => onRemoveAdditional(index)}
                  disabled={disabled}
                  aria-label={t('log.removePhoto')}
                  title={t('log.removePhoto')}
                  style={{
                    ...removeOverlayButtonStyle,
                    opacity: disabled ? 0.5 : 1,
                    cursor: disabled ? 'not-allowed' : 'pointer',
                  }}
                >
                  <X className="w-5 h-5" aria-hidden />
                </button>
              </div>
              <button
                type="button"
                onClick={() => onRemoveAdditional(index)}
                disabled={disabled}
                style={{
                  ...removeTextButtonStyle,
                  opacity: disabled ? 0.5 : 1,
                  cursor: disabled ? 'not-allowed' : 'pointer',
                }}
              >
                {t('log.removePhoto')}
              </button>
            </div>
          ))}

          {canAddAdditional && (
            <div className="space-y-2">
              <p id={additionalLabelId} className="text-xs font-medium text-neutral-600">
                {t('log.addOptionalPhotos')}
              </p>
              {sourceActions('additional', additionalLabelId)}
            </div>
          )}
        </div>
      )}

      {/* Native file inputs must stay display:none — browsers paint "No file chosen" otherwise. */}
      <div className="hidden" aria-hidden="true">
        <input
          ref={cameraInputRef}
          type="file"
          accept="image/jpeg,image/png,image/webp"
          capture="environment"
          onChange={selectFromCamera}
          tabIndex={-1}
          style={{ display: 'none' }}
        />
        <input
          ref={galleryInputRef}
          type="file"
          accept="image/jpeg,image/png,image/webp"
          multiple
          onChange={selectFromGallery}
          tabIndex={-1}
          style={{ display: 'none' }}
        />
      </div>
    </div>
  );
}
