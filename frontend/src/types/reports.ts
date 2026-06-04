export type ReportType = 'morning' | 'maintenance' | 'treatments';

export type UserRoleId = 1 | 2 | 3;

export type ReportListItem = {
  id: string;
  date: string;
  submittedBy: string;
  submittedAt: string;
  type: ReportType;
};

export type ReportsListScreenProps = {
  onBack: () => void;
  onSelectReport: (reportId: string, type: ReportType) => void;
  userRoleId: UserRoleId;
};

