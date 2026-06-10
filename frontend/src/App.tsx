import { useEffect, useState } from 'react';
import { Toaster } from 'react-hot-toast';
import { Routes, Route, useNavigate, useParams, Navigate } from 'react-router-dom';
import { getUserRoleId } from './auth/authSession';
import { ROLE, canSeeForklift, canSeeReports, canSeeTreatments, type RoleId } from './auth/roles';
import { fetchCurrentUser, logout } from './services/authService';
import { LanguageProvider } from './context/LanguageContext';
import { LoginScreen } from './components/LoginScreen';
import { HomeScreen } from './components/HomeScreen';
import { MorningRoundScreen } from './components/MorningRoundScreen';
import { MaintenanceLogScreen } from './components/MaintenanceLogScreen';
import { MaintenanceTasksLogScreen } from './components/MaintenanceTasksLogScreen';
import { TreatmentsReportScreen } from './components/TreatmentsReportScreen';
import { ForkliftReportScreen } from './components/ForkliftReportScreen';
import { AnnualPlansScreen } from './components/AnnualPlansScreen';
import { ReportsListScreen } from './components/ReportsListScreen';
import { ReportDetailsScreen } from './components/ReportDetailsScreen';
import type { ReportType } from './types/reports';
import { SettingsScreen } from './components/SettingsScreen';

const screenIdToPath: Record<string, string> = {
  morningRound: '/morning-round',
  maintenanceLog: '/maintenance',
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

  useEffect(() => {
    let cancelled = false;
    fetchCurrentUser()
      .then((user) => {
        if (cancelled || !user) return;
        setIsLoggedIn(true);
        setUserRoleId(getUserRoleId());
      })
      .finally(() => {
        if (!cancelled) setAuthReady(true);
      });
    return () => {
      cancelled = true;
    };
  }, []);

  const handleLogin = () => {
    setUserRoleId(getUserRoleId());
    setIsLoggedIn(true);
  };

  const handleNavigate = (screen: string) => {
    const path = screenIdToPath[screen] ?? '/home';
    navigate(path);
  };

  const handleLogout = async () => {
    await logout();
    setUserRoleId(ROLE.WORKER);
    setIsLoggedIn(false);
    navigate('/login');
  };

  const handleSelectReport = (reportId: string, type: ReportType) => {
    navigate(`/reports/${type}/${reportId}`);
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
          path="/maintenance"
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
              <ReportDetailsRoute onBack={() => navigate('/reports')} userRoleId={userRoleId} />
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
  onBack,
  userRoleId,
}: {
  onBack: () => void;
  userRoleId: number;
}) {
  const { id, type } = useParams<{ id: string; type: ReportType }>();

  if (!id || !type) {
    return <Navigate to="/home" replace />;
  }

  if (!canSeeReports(userRoleId)) {
    return <Navigate to="/home" replace />;
  }

  if (type === 'treatments' && !canSeeTreatments(userRoleId)) {
    return <Navigate to="/home" replace />;
  }

  return (
    <ReportDetailsScreen reportId={id} reportType={type} onBack={onBack} />
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
