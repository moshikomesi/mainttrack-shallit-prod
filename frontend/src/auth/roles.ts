export const ROLE = {
  WORKER: 1,
  MANAGER: 2,
  SUPER_ADMIN: 3,
} as const;

export type RoleId = (typeof ROLE)[keyof typeof ROLE];

export function canSeeForklift(roleId: number): boolean {
  return roleId >= ROLE.MANAGER;
}

export function canSeeTreatments(roleId: number): boolean {
  return roleId === ROLE.SUPER_ADMIN;
}

export function canSeeReports(roleId: number): boolean {
  return roleId >= ROLE.SUPER_ADMIN;
}

export function canSeeMaintenance(roleId: number): boolean {
  return roleId >= ROLE.WORKER;
}

export function canSeeMorningRound(roleId: number): boolean {
  return roleId >= ROLE.WORKER;
}

export function canSeeBasicSettings(roleId: number): boolean {
  return roleId >= ROLE.WORKER;
}

export function canSeeAdvancedSettings(roleId: number): boolean {
  return roleId === ROLE.SUPER_ADMIN;
}

export function canSeeAnnualPlans(roleId: number): boolean {
  return roleId === ROLE.SUPER_ADMIN;
}

export function isWorker(roleId: number): boolean {
  return roleId === ROLE.WORKER;
}

