import { ROLE, type RoleId } from './roles';

export interface AuthUser {
  userId: string;
  tenantId: string;
  username: string;
  email: string;
  displayName: string;
  role: string;
  roleId: number;
  /** Feature flag: routes this user to Morning Round V2 instead of the legacy screen. */
  enableNewMorningRound: boolean;
  /** Feature flag: routes this user to the new hierarchical Maintenance Log instead of the legacy screen. */
  enableNewMaintenanceLog: boolean;
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

export function isNewMorningRoundEnabled(): boolean {
  return currentUser?.enableNewMorningRound ?? false;
}

export function isNewMaintenanceLogEnabled(): boolean {
  return currentUser?.enableNewMaintenanceLog ?? false;
}
