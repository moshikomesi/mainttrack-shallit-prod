/** Notes keyed by MorningRoundTemplateItem id (GUID string). */
export type MorningRoundNotes = Record<string, string>;

export interface MorningRoundDto {
  id: string;
  reportDate: string;
  performedByUserId: string;
  performedByName: string;
  performedAt: string;
  notes?: MorningRoundNotes | null;
}

export interface CreateMorningRoundRequest {
  reportDate: string;
  notes?: Record<string, string> | null;
}

export interface MorningRoundTemplateItemDto {
  id: string;
  translationKey: string;
  order: number;
}

export type MorningRoundChecklistItem = {
  templateItemId: string;
  translationKey: string;
  checked: boolean;
  comment: string;
};

export type MorningRoundScreenProps = {
  onBack: () => void;
  onSubmit: () => void;
};

