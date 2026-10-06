export type UserRole = 'SuperAdmin' | 'Admin' | 'SubAdmin';
export type UserStatus = 'PendingApproval' | 'Active' | 'Suspended' | 'Blocked' | 'Rejected';
export type TenantStatus = UserStatus;

export interface AuthenticatedUser { id:string; email:string; displayName:string; role:UserRole; status:UserStatus; }
export interface LoginResponse { token:string; expiresAt:string; user:AuthenticatedUser; }
export interface PlatformSummary {
  role:UserRole; collection:number; successful:number; failed:number; pending:number;
  activeWebsites:number; websites:number; pendingWebsites:number; admins:number; subAdmins:number;
  providerAccounts:number; activeRoutes:number; recentPayments: Payment[];
}
export interface Payment {
  id:string; tenantId:string; merchantOrderRef:string; amount:number; currency:string; status:string;
  psp:string; customer?:string|null; createdAt:string; pspOrderId?:string|null; pspPaymentId?:string|null;
  failureReason?:string|null; completedAt?:string|null; sellerId?:string|null;
}
export interface Website {
  id:string; subAdminId:string; merchantCode:string; displayName:string; vertical:string;
  callbackUrl:string; commissionPercent:number; apiKey:string; status:TenantStatus; isActive:boolean; createdAt:string;
}
export interface Provider {
  id:string; ownerUserId:string; tenantId?:string|null; providerCode:string; displayName:string;
  accountLabel:string; merchantAccountRef?:string|null; keyId?:string|null; isActive:boolean;
  isTestMode:boolean; healthStatus:string; createdAt:string; updatedAt:string;
}
export interface RoutingRule {
  id:string; ownerUserId:string; tenantId?:string|null; paymentProviderId:string; minAmount:number;
  maxAmount?:number|null; priority:number; isEnabled:boolean; fallbackProviderId?:string|null;
  paymentMethod?:string|null; createdAt:string; updatedAt:string;
}
export interface PlatformUser {
  id:string; role:UserRole; status:UserStatus; parentId?:string|null; email:string; displayName:string;
  creditBalance:number; creditLimit:number; isBlocked:boolean; statusReason?:string|null;
  lastLoginAt?:string|null; createdAt:string; updatedAt:string;
}
export interface SellerSummary { id:string; label:string; status:string; commissionPercent:number; }
export interface SellerFull {
  id:string; sellerCode:string; businessName:string; contactPhone:string; contactEmail:string|null;
  pspLinkedAccountId:string|null; status:string; commissionPercent:number;
}
export interface OnboardSellerRequest { businessName:string; contactPhone:string; contactEmail:string; upiCollectionApp?:string; commissionPercent:number; }
export interface CreateUserRequest { email:string; displayName:string; password:string; initialCreditLimit:number; }
export interface AdjustCreditRequest { delta:number; reason:string; }
export interface RedFlagRequest { identityType:'device'|'ip'|'phone'|'upi'|'name'; rawIdentityValue:string; reason:string; temporaryHours?:number; }
export interface OrderStatusEvent { tenantId:string; orderId:string; status:'Succeeded'|'Failed'; amount:number; currency:string; at:string; }
export interface AuditLog { id:number; actorId:string|null; action:string; targetType:string|null; targetId:string|null; metadataJson:string|null; allowed:boolean; createdAt:string; }
