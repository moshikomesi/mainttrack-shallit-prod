import { apiFetch } from '../api/apiClient';
import { endpoints } from '../api/endpoints';
import type {
  CreateMorningRoundRequest,
  MorningRoundDto,
  MorningRoundTemplateItemDto,
} from '../types/morningRound';

export interface GetMorningRoundsParams {
  fromDate?: string;
  toDate?: string;
  performedByUserId?: string;
  pageNumber?: number;
  pageSize?: number;
}

function buildSearchParams(params?: GetMorningRoundsParams): string {
  if (!params) return '';
  const search = new URLSearchParams();
  if (params.fromDate != null) search.set('fromDate', params.fromDate);
  if (params.toDate != null) search.set('toDate', params.toDate);
  if (params.performedByUserId != null)
    search.set('performedByUserId', params.performedByUserId);
  if (params.pageNumber != null) search.set('pageNumber', String(params.pageNumber));
  if (params.pageSize != null) search.set('pageSize', String(params.pageSize));
  const q = search.toString();
  return q ? `?${q}` : '';
}

export async function getMorningRounds(
  params?: GetMorningRoundsParams
): Promise<MorningRoundDto[]> {
  return apiFetch<MorningRoundDto[]>(`${endpoints.morningRounds}${buildSearchParams(params)}`);
}

export async function getMorningRoundById(id: string): Promise<MorningRoundDto> {
  return apiFetch<MorningRoundDto>(`${endpoints.morningRounds}/${id}`);
}

export async function createMorningRound(body: CreateMorningRoundRequest): Promise<{ id: string }> {
  return apiFetch<{ id: string }>(endpoints.morningRounds, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify(body),
  });
}

export async function getMorningRoundTemplate(): Promise<MorningRoundTemplateItemDto[]> {
  return apiFetch<MorningRoundTemplateItemDto[]>('/morning-round/template');
}

