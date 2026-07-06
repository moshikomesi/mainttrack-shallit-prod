import { apiFetch } from '../api/apiClient';
import { endpoints } from '../api/endpoints';
import type {
  MorningRoundV2Array,
  SubmitMorningRoundV2Request,
} from '../types/morningRoundV2';
import type { MorningRoundV2Report, MorningRoundV2ReportSummary } from '../types/morningRoundV2Report';

export async function getMorningRoundV2Checklist(): Promise<MorningRoundV2Array[]> {
  return apiFetch<MorningRoundV2Array[]>(endpoints.morningRoundV2);
}

export async function submitMorningRoundV2(
  payload: SubmitMorningRoundV2Request
): Promise<{ id: string; reportDate: string; submittedAt: string }> {
  return apiFetch(`${endpoints.morningRoundV2}/submit`, {
    method: 'POST',
    body: payload,
  });
}

export async function getMorningRoundV2ReportById(reportId: string): Promise<MorningRoundV2Report> {
  return apiFetch<MorningRoundV2Report>(`${endpoints.morningRoundV2}/report/${reportId}`);
}

export async function getMorningRoundV2ReportByDate(date: string): Promise<MorningRoundV2Report> {
  return apiFetch<MorningRoundV2Report>(`${endpoints.morningRoundV2}/report?date=${encodeURIComponent(date)}`);
}

export async function listMorningRoundV2Reports(): Promise<MorningRoundV2ReportSummary[]> {
  return apiFetch<MorningRoundV2ReportSummary[]>(`${endpoints.morningRoundV2}/reports`);
}
