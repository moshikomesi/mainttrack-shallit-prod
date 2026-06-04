import { apiFetch } from '../api/apiClient';
import { endpoints } from '../api/endpoints';
import type { ForkliftReportsResponse, ForkliftReportDetails } from '../types/forkliftReports';

export async function getForkliftReportsOverview(params?: {
  forkliftNumber?: string;
  fromDate?: string;
  toDate?: string;
  type?: string;
  inspectionExpiringSoon?: boolean;
  expiryDays?: number;
  search?: string;
}): Promise<ForkliftReportsResponse> {
  const query = new URLSearchParams();

  if (params?.forkliftNumber) query.append('forkliftNumber', params.forkliftNumber);
  if (params?.fromDate) query.append('fromDate', params.fromDate);
  if (params?.toDate) query.append('toDate', params.toDate);
  if (params?.type) query.append('type', params.type);
  if (params?.inspectionExpiringSoon !== undefined)
    query.append('inspectionExpiringSoon', String(params.inspectionExpiringSoon));
  if (params?.expiryDays) query.append('expiryDays', String(params.expiryDays));
  if (params?.search) query.append('search', params.search);

  const queryString = query.toString();
  const url = queryString
    ? `/api/v1/reports/forklifts?${queryString}`
    : `/api/v1/reports/forklifts`;

  return apiFetch<ForkliftReportsResponse>(url);
}

export async function getForkliftReports(): Promise<unknown> {
  return apiFetch(endpoints.forkliftReports);
}

export async function createForkliftReport(body: Record<string, unknown>): Promise<unknown> {
  return apiFetch(endpoints.forkliftReports, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify(body),
  });
}

export async function getForkliftReportById(id: string): Promise<ForkliftReportDetails> {
  return apiFetch<ForkliftReportDetails>(`${endpoints.forkliftReports}/${id}`);
}
