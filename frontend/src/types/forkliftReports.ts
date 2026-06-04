export interface ForkliftReportListItem {
  reportId: string;
  forkliftId: string;
  forkliftNumber: string;
  reportDate: string;
  treatmentsCount: number;
  faultsCount: number;
}

export interface ExpiringInspection {
  forkliftId: string;
  licenseNumber: string;
  inspectionExpiryDate: string;
  daysRemaining: number;
}

export interface ForkliftReportsResponse {
  reports: ForkliftReportListItem[];
  expiringInspections: ExpiringInspection[];
}

export interface ForkliftTreatment {
  id: string;
  reportId: string;
  date: string;
  description: string;
  technician: string;
}

export interface ForkliftFault {
  id: string;
  reportId: string;
  faultType: string;
  description: string;
  repairCost: number;
}

export interface ForkliftReportDetails {
  id: string;
  tenantId: string;
  forkliftId: string;
  reportDate: string;
  createdByUserId: string;
  createdAt: string;
  treatments: ForkliftTreatment[];
  faults: ForkliftFault[];
  inspections: {
    id: string;
    reportId: string;
    testDate: string;
    expiryDate: string;
  }[];
}
