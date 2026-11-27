import { useEffect, useState } from 'react';
import type { ReactNode } from 'react';

import { api } from '../api/client';
import type { UserDto } from '../api/types';
import { AuthContext } from './context';

export function AuthProvider({ children }: { children: ReactNode }) {
  const [user, setUser] = useState<UserDto | null>(null);
  const [loading, setLoading] = useState(true);

  useEffect(() => {
    let cancelled = false;
    api.me()
      .then((u) => { if (!cancelled) setUser(u); })
      .catch(() => { if (!cancelled) setUser(null); })
      .finally(() => { if (!cancelled) setLoading(false); });
    const onExpired = () => setUser(null);
    window.addEventListener('auth:expired', onExpired);
    return () => {
      cancelled = true;
      window.removeEventListener('auth:expired', onExpired);
    };
  }, []);

  const value = {
    user,
    loading,
    isAdmin: user?.role === 'Admin',
    login: async (email: string, password: string) => {
      const res = await api.login(email, password);
      setUser(res.user);
    },
    register: async (tenantName: string, fullName: string, email: string, password: string) => {
      const res = await api.register(tenantName, fullName, email, password);
      setUser(res.user);
    },
    logout: () => {
      api.clearToken();
      setUser(null);
    },
  };

  return <AuthContext.Provider value={value}>{children}</AuthContext.Provider>;
}
