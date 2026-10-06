import { Navigate } from 'react-router-dom';
import type { ReactNode } from 'react';
import { useAuth } from '../context/AuthContext';
import type { UserRole } from '../types';

interface Props {
  allowedRoles?: UserRole[];
  children: ReactNode;
}

/**
 * This is a UX convenience only — it hides nav items and redirects so
 * people land on the right screen. It is NOT the security boundary: every
 * one of these role checks is re-enforced server-side (RequireAuthorization
 * + per-endpoint role checks in AdminEndpoints.cs), which is what actually
 * protects the data. A client-side check can always be bypassed.
 */
export function RequireAuth({ allowedRoles, children }: Props) {
  const { user, loading } = useAuth();

  if (loading) return null;
  if (!user) return <Navigate to="/login" replace />;
  if (allowedRoles && !allowedRoles.includes(user.role)) return <Navigate to="/" replace />;

  return <>{children}</>;
}
