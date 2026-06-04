import { useEffect, useState } from 'react';
import toast from 'react-hot-toast';
import { useLanguage } from '../context/LanguageContext';
import { AppHeader } from './AppHeader';
import { validateRequired } from '../utils/validateForm';
import { createMorningRound, getMorningRoundTemplate } from '../services/morningRoundsService';
import type {
  MorningRoundTemplateItemDto,
  MorningRoundChecklistItem,
  MorningRoundScreenProps,
} from '../types/morningRound';

export function MorningRoundScreen({ onSubmit }: MorningRoundScreenProps) {
  const { t, isRTL } = useLanguage();
  const today = new Date().toLocaleDateString('en-CA');
  const [date, setDate] = useState(today);
  const [templateItems, setTemplateItems] = useState<MorningRoundTemplateItemDto[]>([]);
  const [checklist, setChecklist] = useState<MorningRoundChecklistItem[]>([]);

  const [invalidFieldId, setInvalidFieldId] = useState<string | null>(null);
  const [invalidErrorKey, setInvalidErrorKey] = useState<string | null>(null);
  const [isSubmitting, setIsSubmitting] = useState(false);
  const [isLoadingTemplate, setIsLoadingTemplate] = useState(false);
  const [templateError, setTemplateError] = useState<string | null>(null);

  useEffect(() => {
    let cancelled = false;
    const loadTemplate = async () => {
      try {
        setIsLoadingTemplate(true);
        setTemplateError(null);
        const items = await getMorningRoundTemplate();
        if (cancelled) return;
        const ordered = [...items].sort((a, b) => a.order - b.order);
        setTemplateItems(ordered);
        setChecklist(
          ordered.map((item) => ({
            templateItemId: item.id,
            translationKey: item.translationKey,
            checked: false,
            comment: '',
          }))
        );
      } catch (err) {
        console.error(err);
        if (!cancelled) {
          setTemplateError(t('messages.failedToLoadMorningRoundTemplate'));
        }
      } finally {
        if (!cancelled) {
          setIsLoadingTemplate(false);
        }
      }
    };

    loadTemplate();
    return () => {
      cancelled = true;
    };
  }, []);

  const handleCheckboxChange = (index: number) => {
    setChecklist((prev) =>
      prev.map((item, i) =>
        i === index ? { ...item, checked: !item.checked } : item
      )
    );
  };

  const handleCommentChange = (index: number, comment: string) => {
    setChecklist((prev) =>
      prev.map((item, i) =>
        i === index ? { ...item, comment } : item
      )
    );
  };

  const handleSubmit = async () => {
    if (isLoadingTemplate || templateItems.length === 0) {
      toast.error(t('messages.morningTemplateNotLoadedYet'));
      return;
    }
    const result = validateRequired([
      { fieldId: 'field-morning-date', value: date, errorKey: 'validation.requiredDate' },
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

    // Ensure all items are checked
    const unchecked = checklist.filter((item) => !item.checked);
    if (unchecked.length > 0) {
      toast.error(t('validation.requiredAllChecked'));
      return;
    }

    setInvalidFieldId(null);
    setInvalidErrorKey(null);

    const notes: Record<string, string> = {};
    checklist.forEach((item) => {
      if (item.comment.trim()) {
        notes[item.templateItemId] = item.comment.trim();
      }
    });

    try {
      setIsSubmitting(true);
      await createMorningRound({
        reportDate: date,
        notes: Object.keys(notes).length > 0 ? notes : undefined,
      });
      toast.success(t('messages.reportSaved'));
      onSubmit();
    } catch (err) {
      console.error(err);
      toast.error(t('messages.saveFailed'));
    } finally {
      setIsSubmitting(false);
    }
  };

  return (
    <div className="min-h-screen bg-neutral-50 pb-24">
      <AppHeader title={t('morning.title')} showBack={true} showHome={true} />

      <div className="p-4 space-y-4">
        {/* Date */}
        <div className="bg-white border border-neutral-200 rounded-lg p-4 space-y-4">
          <div>
            <label className="block text-sm font-medium text-neutral-700 mb-2">
              {t('morning.date')}
              <span className="text-red-500 ml-1">*</span>
            </label>
            <input
              id="field-morning-date"
              type="date"
              value={date}
              onChange={(e) => { setDate(e.target.value); if (invalidFieldId === 'field-morning-date') { setInvalidFieldId(null); setInvalidErrorKey(null); } }}
              className={`w-full px-3 py-2.5 bg-white border rounded-lg text-neutral-900 focus:outline-none focus:ring-2 focus:ring-neutral-800 ${invalidFieldId === 'field-morning-date' ? 'border-red-500' : 'border-neutral-300'}`}
            />
            {invalidFieldId === 'field-morning-date' && invalidErrorKey && (
              <p className="text-red-500 text-sm mt-1">{t(invalidErrorKey)}</p>
            )}
          </div>
        </div>

        {/* Checklist Section */}
        <div className="bg-white border border-neutral-200 rounded-lg p-4">
          <h2 className="text-sm font-semibold text-neutral-900 mb-4">
            {t('morning.checklist')}
          </h2>
          <div className="space-y-3">
          {isLoadingTemplate && (
            <div className="text-sm text-neutral-500">{t('common.loading')}</div>
          )}
          {templateError && !isLoadingTemplate && checklist.length === 0 && (
            <div className="text-sm text-red-600">{templateError}</div>
          )}
          {!isLoadingTemplate && !templateError && checklist.map((item, index) => (
              <div
                key={item.templateItemId}
                className="pb-3 border-b border-neutral-200 last:border-0 last:pb-0"
              >
                <div className="flex items-start gap-3 mb-2">
                  <input
                    type="checkbox"
                    checked={item.checked}
                    onChange={() => handleCheckboxChange(index)}
                    className="w-5 h-5 mt-0.5 rounded border-neutral-300 text-neutral-800 focus:ring-2 focus:ring-neutral-800 flex-shrink-0"
                  />
                  <div className="flex-1">
                    <label className="text-sm text-neutral-900 cursor-pointer">
                      {index + 1}. {t(item.translationKey)}
                    </label>
                  </div>
                </div>
                <div className={`${isRTL ? 'mr-8' : 'ml-8'}`}>
                  <input
                    type="text"
                    value={item.comment}
                    onChange={(e) => handleCommentChange(index, e.target.value)}
                    placeholder={t('morning.comments')}
                    className="w-full px-3 py-2 bg-neutral-50 border border-neutral-200 rounded text-sm text-neutral-900 placeholder:text-neutral-400 focus:outline-none focus:ring-2 focus:ring-neutral-800 focus:bg-white"
                  />
                </div>
              </div>
            ))}
          </div>
        </div>
      </div>

      {/* Submit Button - Sticky */}
      <div className="fixed bottom-0 left-0 right-0 p-4 bg-white border-t border-neutral-200">
        <div className="max-w-md mx-auto">
          <button
            onClick={handleSubmit}
            disabled={isSubmitting}
            className="w-full py-4 bg-neutral-800 text-white font-semibold rounded-lg hover:bg-neutral-900 active:bg-neutral-950 transition-colors disabled:opacity-60 disabled:cursor-not-allowed"
          >
            {isSubmitting ? t('messages.loading') : t('morning.submit')}
          </button>
        </div>
      </div>
    </div>
  );
}