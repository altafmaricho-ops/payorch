import { createContext, useContext, useEffect, useState, type ReactNode } from 'react';
import { login as loginApi, logout as logoutApi } from '../api/auth';
import { getStoredToken, setStoredToken } from '../api/client';
import type { AuthenticatedUser } from '../types';

interface AuthContextValue {
  user: AuthenticatedUser | null;
  token: string | null;
  loading: boolean;
  login: (email: string, password: string) => Promise<void>;
  logout: () => Promise<void>;
}
const AuthContext = createContext<AuthContextValue | undefined>(undefined);
const USER_STORAGE_KEY = 'payorch_user';

export function AuthProvider({ children }: { children: ReactNode }) {
  const [user, setUser] = useState<AuthenticatedUser | null>(null);
  const [token, setToken] = useState<string | null>(null);
  const [loading, setLoading] = useState(true);

  useEffect(() => {
    try {
      const storedToken = getStoredToken();
      const storedUser = localStorage.getItem(USER_STORAGE_KEY);
      if (storedToken && storedUser) {
        const parsed = JSON.parse(storedUser) as AuthenticatedUser;
        setToken(storedToken); setUser(parsed);
      }
    } catch {
      setStoredToken(null); localStorage.removeItem(USER_STORAGE_KEY);
    } finally { setLoading(false); }
  }, []);

  useEffect(() => {
    const handler = () => { setToken(null); setUser(null); };
    window.addEventListener('payorch-logout', handler);
    return () => window.removeEventListener('payorch-logout', handler);
  }, []);

  async function login(email: string, password: string) {
    const res = await loginApi(email.trim(), password);
    setStoredToken(res.token); localStorage.setItem(USER_STORAGE_KEY, JSON.stringify(res.user));
    setToken(res.token); setUser(res.user);
  }

  async function logout() {
    try {
      if (getStoredToken()) await logoutApi();
    } catch {
      // A local sign-out must still succeed if the API is unavailable.
    } finally {
      setStoredToken(null); localStorage.removeItem(USER_STORAGE_KEY);
      setToken(null); setUser(null);
      window.dispatchEvent(new Event('payorch-logout'));
    }
  }

  return <AuthContext.Provider value={{ user, token, loading, login, logout }}>{children}</AuthContext.Provider>;
}
export function useAuth(): AuthContextValue {
  const ctx = useContext(AuthContext);
  if (!ctx) throw new Error('useAuth must be used within an AuthProvider');
  return ctx;
}
