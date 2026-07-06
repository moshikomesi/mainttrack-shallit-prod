export type MorningRoundV2Machine = {
  id: string;
  nameKey: string;
};

export type MorningRoundV2Array = {
  arrayId: string | null;
  nameKey: string;
  machines: MorningRoundV2Machine[];
};

export type MorningRoundV2MachineState = {
  machineId: string;
  status: 'ok' | 'fail' | null;
  notes: string;
};

export type SubmitMorningRoundV2Item = {
  machineId: string;
  status: 'ok' | 'fail';
  notes: string;
};

export type SubmitMorningRoundV2Request = {
  timestamp: string;
  items: SubmitMorningRoundV2Item[];
};

export type MorningRoundV2ScreenProps = {
  onBack: () => void;
  onSubmit: () => void;
};
