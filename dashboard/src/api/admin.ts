import { apiClient } from './client';
import type { AdjustCreditRequest, CreateUserRequest, OnboardSellerRequest, RedFlagRequest, SellerFull, SellerSummary } from '../types';

export async function createUser(req: CreateUserRequest) {
  const { data } = await apiClient.post<{ userId: string; status: string }>('/api/admin/users', req); return data;
}
export async function changeUserStatus(userId: string, action: string, reason?: string) {
  const { data } = await apiClient.patch(`/api/admin/users/${userId}/status`, { action, reason }); return data;
}
export async function resetPassword(userId:string,newPassword:string){ const {data}=await apiClient.post(`/api/admin/users/${userId}/reset-password`,{newPassword}); return data; }
export async function adjustCredit(targetUserId: string, req: AdjustCreditRequest) {
  const { data } = await apiClient.post<{ newBalance: number }>(`/api/admin/users/${targetUserId}/credit`, req); return data;
}
export async function onboardSeller(tenantId: string, req: OnboardSellerRequest) {
  const { data } = await apiClient.post(`/api/admin/tenants/${tenantId}/sellers`, req); return data as { sellerId:string; sellerCode:string; status:string; warning?:string };
}
export async function listSellers(tenantId: string): Promise<(SellerSummary | SellerFull)[]> {
  const { data } = await apiClient.get(`/api/admin/tenants/${tenantId}/sellers`); return data;
}
export async function changeSellerStatus(tenantId: string, sellerId: string, action: string, reason?: string) {
  const { data } = await apiClient.patch(`/api/admin/tenants/${tenantId}/sellers/${sellerId}/status`, { action, reason }); return data;
}
export async function redFlag(req: RedFlagRequest) { await apiClient.post('/api/admin/fraud/red-flag', req); }
export async function exportSellersCsv(tenantId: string) {
  const { data, headers } = await apiClient.get(`/api/admin/tenants/${tenantId}/sellers/export`, { responseType:'blob' });
  return { data, headers };
}
