export interface Forklift {
  id: string;
  licenseNumber: string;
  description?: string;
  lastInspectionDate?: string;
  inspectionExpiryDate?: string;
}

export type ForkliftReportScreenProps = {
  onBack: () => void;
  onSubmit: () => void;
};
