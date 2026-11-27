import { Navigate, Route, Routes } from 'react-router-dom';
import { Box, CircularProgress } from '@mui/material';

import { useAuth } from './auth/context';
import { Layout } from './components/Layout';
import { AttendancePage } from './pages/AttendancePage';
import { AuditPage } from './pages/AuditPage';
import { DashboardPage } from './pages/DashboardPage';
import { HomeDetailPage } from './pages/HomeDetailPage';
import { HomesPage } from './pages/HomesPage';
import { LoginPage } from './pages/LoginPage';
import { PersonsPage } from './pages/PersonsPage';
import { RegisterPage } from './pages/RegisterPage';
import type { ReactNode } from 'react';

function RequireAuth({ children }: { children: ReactNode }) {
  const { user, loading } = useAuth();
  if (loading) {
    return (
      <Box sx={{ display: 'flex', justifyContent: 'center', mt: 10 }}>
        <CircularProgress />
      </Box>
    );
  }
  return user ? <>{children}</> : <Navigate to="/login" replace />;
}

function RequireAdmin({ children }: { children: ReactNode }) {
  const { isAdmin } = useAuth();
  return isAdmin ? <>{children}</> : <Navigate to="/" replace />;
}

export default function App() {
  return (
    <Routes>
      <Route path="/login" element={<LoginPage />} />
      <Route path="/register" element={<RegisterPage />} />
      <Route
        element={
          <RequireAuth>
            <Layout />
          </RequireAuth>
        }
      >
        <Route index element={<DashboardPage />} />
        <Route path="homes" element={<HomesPage />} />
        <Route path="homes/:homeId" element={<HomeDetailPage />} />
        <Route path="attendance" element={<AttendancePage />} />
        <Route path="persons" element={<PersonsPage />} />
        <Route
          path="audit"
          element={
            <RequireAdmin>
              <AuditPage />
            </RequireAdmin>
          }
        />
      </Route>
      <Route path="*" element={<Navigate to="/" replace />} />
    </Routes>
  );
}
