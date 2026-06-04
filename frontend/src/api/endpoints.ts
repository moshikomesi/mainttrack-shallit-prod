export const endpoints = {
  authLogin: '/v1/auth/login',
  authLogout: '/v1/auth/logout',
  authMe: '/v1/auth/me',
  machines: '/v1/machines',
  morningRounds: '/morning-round',
  maintenance: '/maintenance',
  maintenanceTypes: '/v1/maintenance-types',
  treatments: '/v1/treatments',
  forklifts: '/v1/forklifts',
  forkliftReports: '/v1/forklift',
  uploads: '/uploads',
} as const;
