import { useEffect, useState } from 'react';
import { Toaster } from 'react-hot-toast';
import { Routes, Route, useNavigate, useParams, useLocation, Navigate } from 'react-router-dom';
import { getUserRoleId, isNewMorningRoundEnabled, isNewMaintenanceLogEnabled } from './auth/authSession';
import { ROLE, canSeeForklift, canSeeMorningRound, canUseMorningRoundV2Submission, canSeeReports, canSeeTreatments, type RoleId } from './auth/roles';
import { fetchCurrentUser, logout } from './services/authService';
import { LanguageProvider } from './context/LanguageContext';
import { LoginScreen } from './components/LoginScreen';
import { HomeScreen } from './components/HomeScreen';
import { MorningRoundScreen } from './components/MorningRoundScreen';
import { MorningRoundV2Screen } from './components/MorningRoundV2Screen';
import { MaintenanceLogScreen } from './components/MaintenanceLogScreen';
import { MaintenanceLogV2Screen } from './components/MaintenanceLogV2Screen';
import { MaintenanceTasksLogScreen } from './components/MaintenanceTasksLogScreen';
import { TreatmentsReportScreen } from './components/TreatmentsReportScreen';
import { ForkliftReportScreen } from './components/ForkliftReportScreen';
import { AnnualPlansScreen } from './components/AnnualPlansScreen';
import { ReportsListScreen } from './components/ReportsListScreen';
import { ReportDetailsScreen } from './components/ReportDetailsScreen';
import type { ReportType, ReportsListReturnContext } from './types/reports';
import { SettingsScreen } from './components/SettingsScreen';

const screenIdToPath: Record<string, string> = {
  morningRound: '/morning-round',
  morningRoundV2: '/morning-round-v2',
  // Legacy Maintenance Log keeps its own path; the new hierarchical Maintenance
  // Log takes over the canonical "/maintenance" path once enabled per user.
  maintenanceLog: '/maintenance-v1',
  maintenanceLogV2: '/maintenance',
  maintenanceTasksLog: '/maintenance-tasks',
  treatments: '/treatments',
  forklift: '/forklift',
  annualPlans: '/annual-plans',
  reports: '/reports',
  settings: '/settings',
};

function AppRoutes() {
  const navigate = useNavigate();
  const [authReady, setAuthReady] = useState(false);
  const [isLoggedIn, setIsLoggedIn] = useState(false);
  const [userRoleId, setUserRoleId] = useState<RoleId>(ROLE.WORKER);
  const [enableNewMorningRound, setEnableNewMorningRound] = useState(false);
  const [enableNewMaintenanceLog, setEnableNewMaintenanceLog] = useState(false);

  const syncSessionState = () => {
    setUserRoleId(getUserRoleId());
    setEnableNewMorningRound(isNewMorningRoundEnabled());
    setEnableNewMaintenanceLog(isNewMaintenanceLogEnabled());
  };

  useEffect(() => {
    let cancelled = false;
    fetchCurrentUser()
      .then((user) => {
        if (cancelled || !user) return;
        setIsLoggedIn(true);
        syncSessionState();
      })
      .finally(() => {
        if (!cancelled) setAuthReady(true);
      });
    return () => {
      cancelled = true;
    };
  }, []);

  const handleLogin = () => {
    syncSessionState();
    setIsLoggedIn(true);
  };

  const handleNavigate = (screen: string) => {
    const path = screenIdToPath[screen] ?? '/home';
    navigate(path);
  };

  const handleLogout = async () => {
    await logout();
    setUserRoleId(ROLE.WORKER);
    setEnableNewMorningRound(false);
    setEnableNewMaintenanceLog(false);
    setIsLoggedIn(false);
    navigate('/login');
  };

  const handleSelectReport = (
    reportId: string,
    type: ReportType,
    returnContext: ReportsListReturnContext
  ) => {
    navigate(`/reports/${type}/${reportId}`, {
      state: { reportsListContext: returnContext },
    });
  };

  const handleReportSubmitted = () => {
    navigate('/home');
  };

  if (!authReady) {
    return null;
  }

  return (
    <Routes>
      <Route
        path="/login"
        element={isLoggedIn ? <Navigate to="/home" replace /> : <LoginScreen onLogin={handleLogin} />}
      />
        <Route
          path="/home"
          element={
            !isLoggedIn ? (
              <Navigate to="/login" replace />
            ) : (
              <HomeScreen
                onNavigate={handleNavigate}
                userRoleId={userRoleId}
                enableNewMorningRound={enableNewMorningRound}
                enableNewMaintenanceLog={enableNewMaintenanceLog}
                onLogout={handleLogout}
              />
            )
          }
        />
        <Route
          path="/morning-round"
          element={
            !isLoggedIn ? (
              <Navigate to="/login" replace />
            ) : (
              <MorningRoundScreen
                onBack={() => navigate('/home')}
                onSubmit={handleReportSubmitted}
              />
            )
          }
        />
        <Route
          path="/morning-round-v2"
          element={
            !isLoggedIn ? (
              <Navigate to="/login" replace />
            ) : !canUseMorningRoundV2Submission(userRoleId) || !enableNewMorningRound ? (
              <Navigate to="/home" replace />
            ) : (
              <MorningRoundV2Screen
                onBack={() => navigate('/home')}
                onSubmit={handleReportSubmitted}
              />
            )
          }
        />
        {/* Legacy Maintenance Log (pre feature-flag rollout). */}
        <Route
          path="/maintenance-v1"
          element={
            !isLoggedIn ? (
              <Navigate to="/login" replace />
            ) : (
              <MaintenanceLogScreen
                onBack={() => navigate('/home')}
                onSubmit={handleReportSubmitted}
              />
            )
          }
        />
        {/* New hierarchical Maintenance Log — owns the canonical "/maintenance" path
            once enabled per user via the enableNewMaintenanceLog feature flag. */}
        <Route
          path="/maintenance"
          element={
            !isLoggedIn ? (
              <Navigate to="/login" replace />
            ) : !enableNewMaintenanceLog ? (
              <Navigate to="/home" replace />
            ) : (
              <MaintenanceLogV2Screen
                onBack={() => navigate('/home')}
                onSubmit={handleReportSubmitted}
              />
            )
          }
        />
        <Route
          path="/maintenance-tasks"
          element={
            !isLoggedIn ? (
              <Navigate to="/login" replace />
            ) : (
              <MaintenanceTasksLogScreen
                onBack={() => navigate('/home')}
                onSubmit={handleReportSubmitted}
              />
            )
          }
        />
        <Route
          path="/treatments"
          element={
            !isLoggedIn ? (
              <Navigate to="/login" replace />
            ) : (
              !canSeeTreatments(userRoleId) ? (
                <Navigate to="/home" replace />
              ) : (
                <TreatmentsReportScreen
                  userRoleId={userRoleId}
                  onBack={() => navigate('/home')}
                  onSubmit={handleReportSubmitted}
                />
              )
            )
          }
        />
        <Route
          path="/forklift"
          element={
            !isLoggedIn ? (
              <Navigate to="/login" replace />
            ) : (
              !canSeeForklift(userRoleId) ? (
                <Navigate to="/home" replace />
              ) : (
                <ForkliftReportScreen
                  onBack={() => navigate('/home')}
                  onSubmit={handleReportSubmitted}
                />
              )
            )
          }
        />
        <Route
          path="/annual-plans"
          element={
            !isLoggedIn ? (
              <Navigate to="/login" replace />
            ) : (
              <AnnualPlansScreen onBack={() => navigate('/home')} />
            )
          }
        />
        <Route
          path="/reports"
          element={
            !isLoggedIn ? (
              <Navigate to="/login" replace />
            ) : (
              !canSeeReports(userRoleId) ? (
                <Navigate to="/home" replace />
              ) : (
                <ReportsListScreen
                  onBack={() => navigate('/home')}
                  onSelectReport={handleSelectReport}
                  userRoleId={userRoleId}
                />
              )
            )
          }
        />
        <Route
          path="/reports/:type/:id"
          element={
            !isLoggedIn ? (
              <Navigate to="/login" replace />
            ) : (
              <ReportDetailsRoute userRoleId={userRoleId} />
            )
          }
        />
        <Route
          path="/settings"
          element={
            !isLoggedIn ? (
              <Navigate to="/login" replace />
            ) : (
              <SettingsScreen onBack={() => navigate('/home')} userRoleId={userRoleId} />
            )
          }
        />
        <Route path="/" element={<Navigate to="/login" replace />} />
    </Routes>
  );
}

function ReportDetailsRoute({
  userRoleId,
}: {
  userRoleId: number;
}) {
  const { id, type } = useParams<{ id: string; type: ReportType }>();
  const location = useLocation();
  const navigate = useNavigate();

  if (!id || !type) {
    return <Navigate to="/home" replace />;
  }

  if (!canSeeReports(userRoleId)) {
    return <Navigate to="/home" replace />;
  }

  if (type === 'treatments' && !canSeeTreatments(userRoleId)) {
    return <Navigate to="/home" replace />;
  }

  const handleBack = () => {
    const reportsListContext = (
      location.state as { reportsListContext?: ReportsListReturnContext } | null
    )?.reportsListContext;
    navigate('/reports', { state: reportsListContext ? { reportsListContext } : undefined });
  };

  return (
    <ReportDetailsScreen reportId={id} reportType={type} onBack={handleBack} />
  );
}

export default function App() {
  return (
    <LanguageProvider
      children={
        <>
          <div className="max-w-md mx-auto bg-white min-h-screen">
            <AppRoutes />
          </div>
          <Toaster position="top-center" />
        </>
      }
    />
  );
}
