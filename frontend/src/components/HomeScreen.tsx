import { useEffect, useState, type CSSProperties } from 'react';
import { useLanguage } from '../context/LanguageContext';
import { ClipboardCheck, ClipboardList, FileText, SlidersHorizontal, Truck, List, Calendar, Droplets, ShieldCheck, type LucideIcon } from 'lucide-react';
import { AppHeader } from './AppHeader';
import {
  CLEARANCE_CHANGED_EVENT,
  getClearanceAccess,
  getPendingClearanceCount,
} from '../services/clearanceService';

import { FORKLIFT_REPORTS_UI_ENABLED, canSeeForklift, canSeeMaintenance, canSeeMorningRound, canSeeReports, canSeeTreatments, canSeeAnnualPlans } from '../auth/roles';

type HomeMenuItem = {
  id: string;
  label: string;
  icon: LucideIcon;
  color: string;
  iconBoxStyle?: CSSProperties;
  visible: boolean;
  badgeCount?: number | null;
};

interface HomeScreenProps {
  onNavigate: (screen: string) => void;
  userRoleId: number;
  /** Per-user feature flag — see HomeScreenProps usage in App.tsx for how it's populated. */
  enableNewMorningRound: boolean;
  /** Per-user feature flag — see HomeScreenProps usage in App.tsx for how it's populated. */
  enableNewMaintenanceLog: boolean;
  onLogout: () => void;
}

export function HomeScreen({
  onNavigate,
  userRoleId,
  enableNewMorningRound,
  enableNewMaintenanceLog,
  onLogout,
}: HomeScreenProps) {
  const { t } = useLanguage();
  const [canViewClearance, setCanViewClearance] = useState(false);
  // null = unknown (loading or failed); the badge is shown only for a confirmed count > 0.
  const [pendingClearanceCount, setPendingClearanceCount] = useState<number | null>(null);

  // Access is re-read on mount, focus, tab visibility and Clearance changes, because grants can be
  // revoked while Home stays open. The count is requested only while access allows viewing.
  useEffect(() => {
    let cancelled = false;
    let inFlight = false;
    let rerunRequested = false;

    const run = async () => {
      inFlight = true;
      try {
        const access = await getClearanceAccess();
        if (cancelled) return;
        setCanViewClearance(access.canView);
        if (!access.canView) {
          setPendingClearanceCount(null);
          return;
        }
        try {
          const count = await getPendingClearanceCount();
          if (!cancelled) setPendingClearanceCount(count);
        } catch (err) {
          console.error(err);
          if (!cancelled) setPendingClearanceCount(null);
        }
      } catch (err) {
        console.error(err);
      } finally {
        inFlight = false;
        if (rerunRequested && !cancelled) {
          rerunRequested = false;
          void run();
        }
      }
    };

    // Focus and visibilitychange usually fire together: join a refresh already in flight.
    const refreshOnReturn = () => {
      if (!inFlight) void run();
    };
    const refreshWhenVisible = () => {
      if (document.visibilityState === 'visible') refreshOnReturn();
    };
    // A decision made during a refresh may not be reflected in it, so run once more afterwards.
    const refreshAfterChange = () => {
      if (inFlight) rerunRequested = true;
      else void run();
    };

    void run();
    window.addEventListener(CLEARANCE_CHANGED_EVENT, refreshAfterChange);
    window.addEventListener('focus', refreshOnReturn);
    document.addEventListener('visibilitychange', refreshWhenVisible);
    return () => {
      cancelled = true;
      window.removeEventListener(CLEARANCE_CHANGED_EVENT, refreshAfterChange);
      window.removeEventListener('focus', refreshOnReturn);
      document.removeEventListener('visibilitychange', refreshWhenVisible);
    };
  }, []);

  // Single menu entry per feature; the feature flag decides which screen (legacy vs
  // new) the user is routed to. Never show both a v1 and v2 entry at once.
  const morningRoundMenuItem: HomeMenuItem | null = canSeeMorningRound(userRoleId)
    ? {
        id: enableNewMorningRound ? 'morningRoundV2' : 'morningRound',
        label: enableNewMorningRound ? t('home.morningRoundV2') : t('home.morningRound'),
        icon: ClipboardList,
        color: 'bg-slate-600',
        visible: true,
      }
    : null;

  const maintenanceLogMenuItem: HomeMenuItem | null = canSeeMaintenance(userRoleId)
    ? {
        id: enableNewMaintenanceLog ? 'maintenanceLogV2' : 'maintenanceLog',
        label: enableNewMaintenanceLog ? t('home.maintenanceLogV2') : t('home.maintenanceLog'),
        icon: FileText,
        color: 'bg-teal-700',
        visible: true,
      }
    : null;

  const menuItems: HomeMenuItem[] = [
    ...(morningRoundMenuItem ? [morningRoundMenuItem] : []),
    ...(maintenanceLogMenuItem ? [maintenanceLogMenuItem] : []),
    {
      id: 'machineParameterPhotos',
      label: t('home.machineParameterPhotos'),
      icon: SlidersHorizontal,
      color: 'bg-amber-700',
      iconBoxStyle: { backgroundColor: '#eab308' },
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
      visible: FORKLIFT_REPORTS_UI_ENABLED && canSeeForklift(userRoleId),
    },
    {
      id: 'annualPlans',
      label: t('home.annualPlans'),
      icon: Calendar,
      color: 'bg-purple-700',
      visible: canSeeAnnualPlans(userRoleId),
    },
    {
      id: 'clearance',
      label: t('home.clearance'),
      icon: ShieldCheck,
      color: 'bg-teal-700',
      visible: canViewClearance,
      badgeCount: pendingClearanceCount,
    },
    {
      id: 'reports',
      label: t('home.viewReports'),
      icon: List,
      color: 'bg-slate-700',
      visible: canSeeReports(userRoleId),
    },
  ].filter((item) => item.visible);

  return (
    <div className="min-h-screen bg-neutral-50">
      <AppHeader title={t('home.title')} showLogout={true} onLogout={onLogout} />

      {/* Menu Cards */}
      <div className="p-6 space-y-4">
        {menuItems.map((item) => {
          const Icon = item.icon;
          const badgeCount = item.badgeCount ?? 0;
          const badgeLabel =
            badgeCount > 0 ? t('clearance.pendingCountLabel').replace('{count}', String(badgeCount)) : undefined;
          return (
            <button
              key={item.id}
              onClick={() => onNavigate(item.id)}
              aria-label={badgeLabel ? `${item.label}, ${badgeLabel}` : undefined}
              className="w-full bg-white border border-neutral-200 rounded-lg p-6 flex items-center gap-4 hover:bg-neutral-50 active:bg-neutral-100 transition-colors"
            >
              <div
                className={`${item.color} w-12 h-12 rounded-lg flex items-center justify-center flex-shrink-0`}
                style={{ ...item.iconBoxStyle, position: 'relative' }}
              >
                <Icon className="w-6 h-6 text-white" />
                {badgeCount > 0 && (
                  <span
                    data-testid={`${item.id}-badge`}
                    title={badgeLabel}
                    aria-hidden
                    style={{
                      position: 'absolute',
                      top: -6,
                      insetInlineEnd: -6,
                      minWidth: 20,
                      height: 20,
                      padding: '0 5px',
                      borderRadius: 9999,
                      backgroundColor: '#dc2626',
                      color: '#ffffff',
                      fontSize: 11,
                      fontWeight: 700,
                      lineHeight: '20px',
                      textAlign: 'center',
                      boxShadow: '0 0 0 2px #ffffff',
                    }}
                  >
                    {badgeCount > 99 ? '99+' : badgeCount}
                  </span>
                )}
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