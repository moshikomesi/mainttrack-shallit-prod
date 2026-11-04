export type ClearanceStatus = 'pending' | 'approved' | 'rejected';

export type ClearanceSourceType = 'maintenance_entry' | 'morning_round_v2';

export type ClearanceFinding = {
  machineId: string;
  machineName: string;
  arrayId: string | null;
  notes: string | null;
};

export type ClearanceItem = {
  id: string;
  sourceType: ClearanceSourceType;
  maintenanceEntryId: string | null;
  morningRoundV2SubmissionId: string | null;
  machineId: string | null;
  machineName: string | null;
  arrayId: string | null;
  reportDate: string;
  description: string | null;
  maintenanceTypeCode: string | null;
  findings: ClearanceFinding[];
  status: ClearanceStatus;
  submittedByUserId: string;
  submittedByName: string | null;
  submittedAt: string;
  decidedByUserId: string | null;
  decidedByName: string | null;
  decidedAt: string | null;
  decisionNote: string | null;
};

export type ClearanceQueueResponse = {
  items: ClearanceItem[];
  pageNumber: number;
  pageSize: number;
  totalCount: number;
  canApprove: boolean;
};

export type ClearanceAccess = {
  canView: boolean;
  canApprove: boolean;
};

export type ClearanceSourceItemResponse = {
  item: ClearanceItem;
  canApprove: boolean;
};

export type ClearanceDecision = {
  id: string;
  status: ClearanceStatus;
  decidedByUserId: string;
  decidedAt: string;
  decisionNote: string | null;
};

export const CLEARANCE_REJECTION_NOTE_MAX_LENGTH = 2000;
