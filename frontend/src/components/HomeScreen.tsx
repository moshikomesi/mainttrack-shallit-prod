import { useEffect } from 'react';
import { useLanguage } from '../context/LanguageContext';
import { ClipboardCheck, ClipboardList, FileText, Truck, List, Calendar, Droplets } from 'lucide-react';
import { getForkliftReportsOverview } from '../services/forkliftReportsService';
import { AppHeader } from './AppHeader';

import { canSeeForklift, canSeeMaintenance, canSeeMorningRound, canSeeReports, canSeeTreatments, canSeeAnnualPlans } from '../auth/roles';

interface HomeScreenProps {
  onNavigate: (screen: string) => void;
  userRoleId: number;
  onLogout: () => void;
}

export function HomeScreen({ onNavigate, userRoleId, onLogout }: HomeScreenProps) {
  const { t } = useLanguage();

  const menuItems = [
    {
      id: 'morningRound',
      label: t('home.morningRound'),
      icon: ClipboardList,
      color: 'bg-slate-600',
      visible: canSeeMorningRound(userRoleId),
    },
    {
      id: 'maintenanceLog',
      label: t('home.maintenanceLog'),
      icon: FileText,
      color: 'bg-teal-700',
      visible: canSeeMaintenance(userRoleId),
    },
    {
      id: 'maintenanceTasksLog',
      label: t('home.maintenanceTasksLog'),
      icon: ClipboardCheck,
      color: 'bg-amber-700',
      visible: canSeeMaintenance(userRoleId),
    },
    {
      id: 'treatments',
      label: t('home.treatmentsReport'),
      icon: Droplets,
      color: 'bg-blue-700',
      visible: canSeeTreatments(userRoleId),
    },
    {
      id: 'forklift',
      label: t('home.forkliftReport'),
      icon: Truck,
      color: 'bg-amber-700',
      visible: canSeeForklift(userRoleId),
    },
    {
      id: 'annualPlans',
      label: t('home.annualPlans'),
      icon: Calendar,
      color: 'bg-purple-700',
      visible: canSeeAnnualPlans(userRoleId),
    },
    // "View reports" is forklift-related in this app.
    {
      id: 'reports',
      label: t('home.viewReports'),
      icon: List,
      color: 'bg-slate-700',
      visible: canSeeReports(userRoleId),
    },

  ].filter((item) => item.visible && item.id !== 'forklift');

  return (
    <div className="min-h-screen bg-neutral-50">
      <AppHeader title={t('home.title')} showLogout={true} onLogout={onLogout} />

      {/* Menu Cards */}
      <div className="p-6 space-y-4">
        {menuItems.map((item) => {
          const Icon = item.icon;
          return (
            <button
              key={item.id}
              onClick={() => onNavigate(item.id)}
              className="w-full bg-white border border-neutral-200 rounded-lg p-6 flex items-center gap-4 hover:bg-neutral-50 active:bg-neutral-100 transition-colors"
            >
              <div className={`${item.color} w-12 h-12 rounded-lg flex items-center justify-center flex-shrink-0`}>
                <Icon className="w-6 h-6 text-white" />
              </div>
              <span className="text-lg font-medium text-neutral-900">
                {item.label}
              </span>
            </button>
          );
        })}
      </div>
    </div>
  );
}