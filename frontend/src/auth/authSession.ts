import { ROLE, type RoleId } from './roles';

export interface AuthUser {
  userId: string;
  tenantId: string;
  username: string;
  email: string;
  displayName: string;
  role: string;
  roleId: number;
}

let currentUser: AuthUser | null = null;

export function setSession(user: AuthUser | null): void {
  currentUser = user;
}

export function getSession(): AuthUser | null {
  return currentUser;
}

export function clearSession(): void {
  currentUser = null;
}

export function getCurrentUserDisplayName(): string {
  return currentUser?.displayName?.trim() ?? currentUser?.username?.trim() ?? '';
}

export function getUserRoleId(): RoleId {
  const roleId = currentUser?.roleId;
  if (roleId === ROLE.WORKER) return ROLE.WORKER;
  if (roleId === ROLE.MANAGER) return ROLE.MANAGER;
  if (roleId === ROLE.SUPER_ADMIN) return ROLE.SUPER_ADMIN;
  return ROLE.WORKER;
}

export function isAuthenticated(): boolean {
  return currentUser !== null;
}
