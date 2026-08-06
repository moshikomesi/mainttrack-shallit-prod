import { useRef, useState, type CSSProperties } from 'react';
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
  const primaryInputRef = useRef<HTMLInputElement>(null);
  const additionalInputRef = useRef<HTMLInputElement>(null);
  const [dropTargetActive, setDropTargetActive] = useState(false);
  const primarySrc = safeImageSrc(primaryImage);
  const additionalSources = additionalImages
    .map((src, index) => ({ src: safeImageSrc(src), index }))
    .filter((item): item is { src: string; index: number } => Boolean(item.src));
  const canAddAdditional =
    Boolean(primarySrc) && additionalImages.length < MAINTENANCE_LOG_MAX_ADDITIONAL_IMAGES;

  const selectPrimary = (event: React.ChangeEvent<HTMLInputElement>) => {
    const file = event.target.files?.[0];
    event.target.value = '';
    if (file) onPrimarySelected(file);
  };

  const selectAdditional = (event: React.ChangeEvent<HTMLInputElement>) => {
    const files = Array.from(event.target.files ?? []);
    event.target.value = '';
    if (files.length > 0) onAdditionalSelected(files);
  };

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
          <button
            type="button"
            onClick={() => primaryInputRef.current?.click()}
            disabled={disabled}
            className="w-full px-3 py-2 border border-neutral-300 rounded-lg text-sm font-medium text-neutral-700 hover:bg-neutral-50 disabled:opacity-50"
          >
            {t('log.replacePrimaryPhoto')}
          </button>
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
          <button
            type="button"
            onClick={() => primaryInputRef.current?.click()}
            disabled={disabled}
            className="w-full p-3 flex flex-col items-center justify-center gap-1 text-neutral-700 hover:bg-neutral-50 transition-colors rounded-lg disabled:opacity-50"
          >
            <div className="flex items-center justify-center gap-2">
              <Camera className="w-5 h-5" aria-hidden />
              <span className="text-sm font-medium">{t('log.takePicture')}</span>
            </div>
            <span className="text-xs text-neutral-500">{t('log.dropPhoto')}</span>
          </button>
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
            <button
              type="button"
              onClick={() => additionalInputRef.current?.click()}
              disabled={disabled}
              className="w-full inline-flex items-center justify-center gap-1.5 px-3 py-2 border border-neutral-300 rounded-lg text-sm font-medium text-neutral-700 hover:bg-neutral-50 disabled:opacity-50 disabled:cursor-not-allowed"
            >
              <Images className="w-4 h-4" aria-hidden />
              {t('log.addOptionalPhotos')}
            </button>
          )}
        </div>
      )}

      {/* Native file inputs must stay display:none — browsers paint "No file chosen" otherwise. */}
      <div className="hidden" aria-hidden="true">
        <input
          ref={primaryInputRef}
          type="file"
          accept="image/jpeg,image/png,image/webp"
          onChange={selectPrimary}
          tabIndex={-1}
          style={{ display: 'none' }}
        />
        <input
          ref={additionalInputRef}
          type="file"
          accept="image/jpeg,image/png,image/webp"
          multiple
          onChange={selectAdditional}
          tabIndex={-1}
          style={{ display: 'none' }}
        />
      </div>
    </div>
  );
}
