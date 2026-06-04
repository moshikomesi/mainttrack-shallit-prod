import { createContext, useContext, useEffect, useState } from 'react';
import { translations, type Language } from '../i18n/translations';

const LANG_STORAGE_KEY = 'lang';
const DEFAULT_LANGUAGE: Language = 'he';

interface LanguageContextType {
  language: Language;
  setLanguage: (lang: Language) => void;
  t: (key: string) => string;
  isRTL: boolean;
}

const LanguageContext = createContext<LanguageContextType | undefined>(undefined);

function isLanguage(value: string | null): value is Language {
  return value === 'en' || value === 'he' || value === 'th';
}

function getInitialLanguage(): Language {
  try {
    const saved = localStorage.getItem(LANG_STORAGE_KEY);
    return isLanguage(saved) ? saved : DEFAULT_LANGUAGE;
  } catch {
    return DEFAULT_LANGUAGE;
  }
}

export function LanguageProvider({ children }: { children: React.ReactNode }) {
  const [language, setLanguage] = useState<Language>(getInitialLanguage);

  // Persist on change.
  useEffect(() => {
    try {
      if (language) localStorage.setItem(LANG_STORAGE_KEY, language);
    } catch {
      // ignore (e.g. storage blocked)
    }
  }, [language]);


  const t = (key: string): string => translations[key]?.[language] || key;

  const isRTL = language === 'he';

  useEffect(() => {
    document.documentElement.dir = isRTL ? 'rtl' : 'ltr';
    document.documentElement.lang = language;
  }, [isRTL, language]);

  return (
    <LanguageContext.Provider value={{ language, setLanguage, t, isRTL }}>
      {children}
    </LanguageContext.Provider>
  );
}

export function useLanguage() {
  const context = useContext(LanguageContext);
  if (!context) throw new Error('useLanguage must be used within LanguageProvider');
  return context;
}

