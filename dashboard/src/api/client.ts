import axios from 'axios';

const BASE_URL = import.meta.env.VITE_API_BASE_URL;
const TOKEN_STORAGE_KEY = 'payorch_token';

if (!BASE_URL) throw new Error('VITE_API_BASE_URL is not configured.');

export const apiClient = axios.create({ baseURL: BASE_URL, timeout: 20000 });

export function getStoredToken(): string | null { return localStorage.getItem(TOKEN_STORAGE_KEY); }
export function setStoredToken(token: string | null) {
  if (token) localStorage.setItem(TOKEN_STORAGE_KEY, token);
  else localStorage.removeItem(TOKEN_STORAGE_KEY);
}

apiClient.interceptors.request.use((config) => {
  const token = getStoredToken();
  if (token) {
    config.headers = config.headers ?? {};
    config.headers.Authorization = `Bearer ${token}`;
  }
  return config;
});

apiClient.interceptors.response.use(
  (response) => response,
  (error) => {
    if (error?.response?.status === 401) {
      setStoredToken(null);
      localStorage.removeItem('payorch_user');
      window.dispatchEvent(new Event('payorch-logout'));
    }
    return Promise.reject(error);
  }
);
