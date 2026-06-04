import { useLanguage } from '../context/LanguageContext';
import { ChevronRight, Globe, Wrench, AlertCircle, Users } from 'lucide-react';
import { AppHeader } from './AppHeader';

import { type RoleId, canSeeAdvancedSettings, canSeeBasicSettings } from '../auth/roles';

interface SettingsScreenProps {
  onBack: () => void;
  userRoleId: RoleId;
}

export function SettingsScreen({ userRoleId }: SettingsScreenProps) {
  const { t, isRTL, language, setLanguage } = useLanguage();

  const getLanguageName = () => {
    if (language === 'en') return t('languages.en');
    if (language === 'he') return t('languages.he');
    if (language === 'th') return t('languages.th');
    return t('languages.en');
  };

  const cycleLanguage = () => {
    if (language === 'en') setLanguage('he');
    else if (language === 'he') setLanguage('th');
    else setLanguage('en');
  };

  const allSettingsItems = [
    {
      id: 'language',
      icon: Globe,
      label: t('settings.language'),
      value: getLanguageName(),
      action: cycleLanguage,
      visible: canSeeBasicSettings(userRoleId),
    },
    {
      id: 'machines',
      icon: Wrench,
      label: t('settings.machines'),
      value: t('settings.machinesValue'),
      visible: canSeeAdvancedSettings(userRoleId),
    },
    {
      id: 'faultTypes',
      icon: AlertCircle,
      label: t('settings.faultTypes'),
      value: t('settings.faultTypesValue'),
      visible: canSeeAdvancedSettings(userRoleId),
    },
    {
      id: 'users',
      icon: Users,
      label: t('settings.users'),
      value: t('settings.usersValue'),
      visible: canSeeAdvancedSettings(userRoleId),
    },
  ];

  const settingsItems = allSettingsItems.filter((item) => item.visible);

  return (
    <div className="min-h-screen bg-neutral-50">
      <AppHeader title={t('settings.title')} showBack={true} showHome={true} />

      {/* Settings List */}
      <div className="p-4">
        <div className="bg-white border border-neutral-200 rounded-lg overflow-hidden">
          {settingsItems.map((item, index) => {
            const Icon = item.icon;
            return (
              <button
                key={item.id}
                onClick={item.action}
                className={`w-full px-4 py-4 flex items-center gap-4 hover:bg-neutral-50 active:bg-neutral-100 transition-colors ${
                  index !== settingsItems.length - 1 ? 'border-b border-neutral-200' : ''
                }`}
              >
                <div className="w-10 h-10 bg-neutral-100 rounded-lg flex items-center justify-center flex-shrink-0">
                  <Icon className="w-5 h-5 text-neutral-700" />
                </div>
                <div className="flex-1 text-left">
                  <div className="text-sm font-medium text-neutral-900">
                    {item.label}
                  </div>
                  {item.value && (
                    <div className="text-xs text-neutral-500 mt-0.5">
                      {item.value}
                    </div>
                  )}
                </div>
                <ChevronRight className={`w-5 h-5 text-neutral-400 flex-shrink-0 ${isRTL ? 'rotate-180' : ''}`} />
              </button>
            );
          })}
        </div>

        {/* App Info */}
        <div className="mt-8 text-center">
          <div className="text-xs text-neutral-500">
            {t('settings.appName')}
          </div>
          <div className="text-xs text-neutral-400 mt-1">
            {t('settings.version')}
          </div>
        </div>
      </div>
    </div>
  );
}