import { apiClient } from './client';
import type { AuditLog, Payment, PlatformSummary, PlatformUser, Provider, RoutingRule, Website } from '../types';

export async function getSummary(params?:Record<string,unknown>){ const {data}=await apiClient.get<PlatformSummary>('/api/platform/summary',{params}); return data; }
export async function getWebsites(){ const {data}=await apiClient.get<Website[]>('/api/platform/websites'); return data; }
export async function createWebsite(body:{merchantCode:string;displayName:string;vertical:string;callbackUrl:string;commissionPercent:number;subAdminId:string}){ const {data}=await apiClient.post('/api/platform/websites',body); return data; }
export async function changeWebsiteStatus(id:string,action:string,reason?:string){ const {data}=await apiClient.patch(`/api/platform/websites/${id}/status`,{action,reason}); return data; }
export async function rotateWebsiteSecrets(id:string){ const {data}=await apiClient.post(`/api/platform/websites/${id}/rotate-secrets`); return data; }
export async function getProviders(tenantId?:string){ const {data}=await apiClient.get<Provider[]>('/api/platform/providers',{params:tenantId?{tenantId}:undefined}); return data; }
export async function createProvider(body:Record<string,unknown>){ const {data}=await apiClient.post('/api/platform/providers',body); return data; }
export async function changeProviderStatus(id:string,enabled:boolean){ const {data}=await apiClient.patch(`/api/platform/providers/${id}/status`,{enabled}); return data; }
export async function checkProviderHealth(id:string){ const {data}=await apiClient.post(`/api/platform/providers/${id}/health`); return data as {providerId:string;healthStatus:string}; }
export async function getRouting(tenantId?:string){ const {data}=await apiClient.get<RoutingRule[]>('/api/platform/routing',{params:tenantId?{tenantId}:undefined}); return data; }
export async function createRouting(body:Record<string,unknown>){ const {data}=await apiClient.post('/api/platform/routing',body); return data; }
export async function changeRoutingStatus(id:string,enabled:boolean){ const {data}=await apiClient.patch(`/api/platform/routing/${id}/status`,{enabled}); return data; }
export async function getUsers(){ const {data}=await apiClient.get<PlatformUser[]>('/api/platform/users'); return data; }
export async function getPayments(params?:Record<string,unknown>){ const {data}=await apiClient.get<Payment[]>('/api/platform/payments',{params}); return data; }
export async function getPayment(id:string){ const {data}=await apiClient.get<Payment>(`/api/platform/payments/${id}`); return data; }
export async function refundPayment(id:string,amount:number,reason?:string){ const {data}=await apiClient.post(`/api/platform/payments/${id}/refund`,{amount,reason}); return data; }
export async function getAuditLogs(take=200){ const {data}=await apiClient.get<AuditLog[]>('/api/platform/audit',{params:{take}}); return data; }
export function reportUrl(){ const base=import.meta.env.VITE_API_BASE_URL; if(!base) throw new Error('VITE_API_BASE_URL is not configured.'); return `${base}/api/platform/reports/payments.csv`; }
