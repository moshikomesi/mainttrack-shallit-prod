import { apiFetch } from '../api/apiClient';
import { endpoints } from '../api/endpoints';
import {
  clearSession,
  setSession,
  type AuthUser,
} from '../auth/authSession';

export interface LoginResponse {
  expiresAtUtc: string;
  user: AuthUserDto;
}

interface AuthUserDto {
  userId: string;
  tenantId: string;
  username: string;
  email: string;
  displayName: string;
  role: string;
  roleId: number;
}

function mapUser(dto: AuthUserDto): AuthUser {
  return {
    userId: dto.userId,
    tenantId: dto.tenantId,
    username: dto.username,
    email: dto.email,
    displayName: dto.displayName,
    role: dto.role,
    roleId: dto.roleId,
  };
}

export async function login(username: string, password: string): Promise<LoginResponse> {
  const res = await apiFetch<LoginResponse>(endpoints.authLogin, {
    method: 'POST',
    body: JSON.stringify({ username, password }),
  });
  setSession(mapUser(res.user));
  return res;
}

export async function fetchCurrentUser(): Promise<AuthUser | null> {
  try {
    const dto = await apiFetch<AuthUserDto>(endpoints.authMe);
    const user = mapUser(dto);
    setSession(user);
    return user;
  } catch {
    clearSession();
    return null;
  }
}

export async function logout(): Promise<void> {
  try {
    await apiFetch<void>(endpoints.authLogout, { method: 'POST' });
  } finally {
    clearSession();
  }
}
