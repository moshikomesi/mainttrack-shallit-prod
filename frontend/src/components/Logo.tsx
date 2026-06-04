import { LogOut, Home } from 'lucide-react';
import { useLanguage } from '../context/LanguageContext';

interface LogoProps {
  size?: 'sm' | 'md' | 'lg';
  showText?: boolean;
  isHome?: boolean;
  onLogout?: () => void;
  onGoHome?: () => void;
  title?: string;
}

export function Logo({ size = 'md', showText: _showText = true, isHome = false, onLogout, onGoHome, title }: LogoProps) {
  const { t, isRTL } = useLanguage();
  
  const sizeClasses = {
    sm: 'w-10 h-10',
    md: 'w-20 h-20',
    lg: 'w-32 h-32',
  };

  const button = (
    <>
      {isHome && onLogout && (
        <button
          onClick={onLogout}
          className="p-2 hover:bg-neutral-100 rounded-lg active:bg-neutral-200 transition-colors"
          title={t('common.logout')}
        >
          <LogOut className="w-5 h-5 text-neutral-700" />
        </button>
      )}
      {!isHome && onGoHome && (
        <button
          onClick={onGoHome}
          className="p-2 hover:bg-neutral-100 rounded-lg active:bg-neutral-200 transition-colors"
          title={t('common.goHome')}
        >
          <Home className="w-5 h-5 text-neutral-700" />
        </button>
      )}
    </>
  );

  const logo = (
    <div className="flex flex-col items-center">
      <img
        src="/logo.svg"
        alt={t('common.factoryLogoAlt')}
        className={`${sizeClasses[size]} object-contain`}
      />
    </div>
  );

  return (
    <>
      {isHome && !onLogout ? (
        // Login page: centered logo only
        <div className="flex justify-center w-full">
          {logo}
        </div>
      ) : isRTL ? (
        // RTL mode: button left, logo right, title below
        <div className="flex items-center gap-2 justify-between w-full">
          {button}
                    {title && <h1 className="text-lg font-semibold text-neutral-900 flex-1 text-center">{title}</h1>}

          {logo}
        </div>
      ) : (
        // LTR mode: logo left, title center, button right
        <div className="flex items-center gap-2 justify-between w-full">
          {logo}
          {title && <h1 className="text-lg font-semibold text-neutral-900 flex-1 text-center">{title}</h1>}
          {button}
        </div>
      )}
    </>
  );
}
