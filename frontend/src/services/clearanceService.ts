import { apiFetch } from '../api/apiClient';
import { endpoints } from '../api/endpoints';
import type {
  ClearanceAccess,
  ClearanceDecision,
  ClearanceQueueResponse,
  ClearanceSourceItemResponse,
  ClearanceSourceType,
  ClearanceStatus,
} from '../types/clearance';

export type ClearanceQueueFilters = {
  status: ClearanceStatus;
  arrayId?: string;
  machineId?: string;
  pageNumber: number;
  pageSize: number;
};

export async function getClearanceAccess(): Promise<ClearanceAccess> {
  return apiFetch<ClearanceAccess>(endpoints.clearanceAccess);
}

export async function getClearanceQueue(filters: ClearanceQueueFilters): Promise<ClearanceQueueResponse> {
  const params = new URLSearchParams({
    status: filters.status,
    pageNumber: String(filters.pageNumber),
    pageSize: String(filters.pageSize),
  });
  if (filters.arrayId) params.set('arrayId', filters.arrayId);
  if (filters.machineId) params.set('machineId', filters.machineId);
  return apiFetch<ClearanceQueueResponse>(`${endpoints.clearance}?${params.toString()}`);
}

export async function getClearanceBySource(
  sourceType: ClearanceSourceType,
  sourceId: string
): Promise<ClearanceSourceItemResponse> {
  return apiFetch<ClearanceSourceItemResponse>(endpoints.clearanceBySource(sourceType, sourceId));
}

/** Window event fired after any approve/reject attempt so pending counts can refresh. */
export const CLEARANCE_CHANGED_EVENT = 'mainttrack:clearance-changed';

function notifyClearanceChanged() {
  window.dispatchEvent(new Event(CLEARANCE_CHANGED_EVENT));
}

/** Server-side total of items with status exactly `pending` in the current tenant (all source types). */
export async function getPendingClearanceCount(): Promise<number> {
  const response = await getClearanceQueue({ status: 'pending', pageNumber: 1, pageSize: 1 });
  return response.totalCount;
}

export async function approveClearance(id: string): Promise<ClearanceDecision> {
  return apiFetch<ClearanceDecision>(endpoints.clearanceApprove(id), { method: 'POST' }).finally(
    notifyClearanceChanged,
  );
}

export async function rejectClearance(id: string, note: string | null): Promise<ClearanceDecision> {
  return apiFetch<ClearanceDecision>(endpoints.clearanceReject(id), {
    method: 'POST',
    body: JSON.stringify({ note }),
  }).finally(notifyClearanceChanged);
}
