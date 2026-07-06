export type ReportType = 'morning' | 'morning-v2' | 'maintenance' | 'treatments';

export type MorningRoundReportVariant = 'v1' | 'v2';

export type UserRoleId = 1 | 2 | 3;

export type ReportListItem = {
  id: string;
  date: string;
  submittedBy: string;
  submittedAt: string;
  type: ReportType;
  /** Present for maintenance reports (v1+v2). */
  machineId?: string;
  /** Present for maintenance reports (v1+v2). */
  maintenanceTypeCode?: string | null;
  /** Present for maintenance reports (v1+v2). */
  description?: string;
};

export type ReportsListReturnContext = {
  selectedType: 'morning' | 'maintenance' | 'maintenance-tasks' | 'treatments' | 'annual-plans';
  morningVariant?: MorningRoundReportVariant | null;
};

export type ReportsListScreenProps = {
  onBack: () => void;
  onSelectReport: (
    reportId: string,
    type: ReportType,
    returnContext: ReportsListReturnContext
  ) => void;
  userRoleId: UserRoleId;
};

