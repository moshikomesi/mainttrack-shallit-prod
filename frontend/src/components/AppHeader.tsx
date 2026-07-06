import { useNavigate } from 'react-router-dom';
import { useLocation } from 'react-router-dom';
import { ArrowLeft, LogOut } from 'lucide-react';
import { useLanguage } from '../context/LanguageContext';

type Props = {
  title: string;
  showBack?: boolean;
  showHome?: boolean;
  showLogout?: boolean;
  onLogout?: () => void;
  onBack?: () => void;
};

export function AppHeader({ title, showBack, showHome, showLogout, onLogout, onBack }: Props) {
  const navigate = useNavigate();
  const location = useLocation();
  const { isRTL, language, setLanguage, t } = useLanguage();

  // In this app, the "home" screen is mounted at `/home`.
  const isHomePage = location.pathname === '/home';
  const shouldShowLogo = Boolean(showHome) || isHomePage;

  return (
    <div className="bg-white border-b border-neutral-200 sticky top-0 z-20">
      <div className={`flex items-center justify-between px-4 py-3 ${isRTL ? 'flex-row-reverse' : ''}`}>
        {/* Left */}
        <div className="flex items-center gap-2">
          {showBack && (
            <button
              type="button"
              onClick={() => (onBack ? onBack() : navigate(-1))}
              className="p-2 hover:bg-neutral-100 rounded-lg active:bg-neutral-200 transition-colors"
              aria-label={t('common.back')}
            >
              <ArrowLeft
                className={`w-5 h-5 text-neutral-700 ${isRTL ? 'rotate-180' : ''}`}
              />
            </button>
          )}

          {showLogout && onLogout && (
            <button
              type="button"
              onClick={onLogout}
              className="p-2 hover:bg-neutral-100 rounded-lg active:bg-neutral-200 transition-colors"
              aria-label={t('common.logout')}
            >
              <LogOut className="w-5 h-5 text-neutral-700" />
            </button>
          )}
        </div>

        {/* Center */}
        <h1 className="text-lg font-semibold text-center flex-1">{title}</h1>

        {/* Right */}
        <div className="flex items-center gap-2">
          {shouldShowLogo &&
            (isHomePage ? (
              <img
                src="/logo.svg"
                alt="logo"
                className="h-6 w-6 opacity-80 object-contain flex-shrink-0"
              />
            ) : (
              <button
                type="button"
                onClick={() => navigate('/home')}
                className="p-2 hover:bg-neutral-100 rounded-lg active:bg-neutral-200 transition-colors flex items-center justify-center flex-shrink-0"
                aria-label={t('home.title')}
              >
                <img
                  src="/logo.svg"
                  alt="logo"
                  className="h-6 w-6 opacity-80 object-contain"
                />
              </button>
            ))}

          {/* Paste EXACT language switcher from LoginScreen */}
          <div className="p-4 flex justify-end">
            <div className="inline-flex bg-white border border-neutral-300 rounded-lg overflow-hidden">
              <button
                onClick={() => setLanguage('en')}
                className={`px-4 py-2 text-sm font-medium ${
                  language === 'en'
                    ? 'bg-neutral-800 text-white'
                    : 'bg-white text-neutral-700 hover:bg-neutral-50'
                }`}
              >
                EN
              </button>
              <button
                onClick={() => setLanguage('he')}
                className={`px-4 py-2 text-sm font-medium ${
                  language === 'he'
                    ? 'bg-neutral-800 text-white'
                    : 'bg-white text-neutral-700 hover:bg-neutral-50'
                }`}
              >
                HE
              </button>
              <button
                onClick={() => setLanguage('th')}
                className={`px-4 py-2 text-sm font-medium ${
                  language === 'th'
                    ? 'bg-neutral-800 text-white'
                    : 'bg-white text-neutral-700 hover:bg-neutral-50'
                }`}
              >
                TH
              </button>
            </div>
          </div>
        </div>
      </div>
    </div>
  );
}

