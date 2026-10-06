import { apiClient } from './client';
import type { LoginResponse } from '../types';

export async function login(email: string, password: string): Promise<LoginResponse> {
  const { data } = await apiClient.post<LoginResponse>('/api/auth/login', { email, password });
  return data;
}

export async function logout(): Promise<void> {
  await apiClient.post('/api/auth/logout');
}
