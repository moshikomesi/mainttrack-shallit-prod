export type MorningRoundV2ReportMachine = {
  machineId: string;
  nameKey: string;
  status: 'ok' | 'fail' | null;
  notes: string | null;
};

export type MorningRoundV2ReportArray = {
  arrayId: string | null;
  nameKey: string;
  machines: MorningRoundV2ReportMachine[];
};

export type MorningRoundV2ReportSubmittedBy = {
  userId: string;
  fullName: string;
};

export type MorningRoundV2Report = {
  reportId: string;
  date: string;
  submittedAt: string;
  submittedBy: MorningRoundV2ReportSubmittedBy;
  arrays: MorningRoundV2ReportArray[];
};

export type MorningRoundV2ReportSummary = {
  reportId: string;
  date: string;
  submittedAt: string;
  submittedBy: MorningRoundV2ReportSubmittedBy;
};

export type MorningRoundV2ReportScreenProps = Record<string, never>;
