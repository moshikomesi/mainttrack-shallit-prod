import type { MaintenanceTranslationsResponse } from '../types/maintenance';

export const PENDING_REFRESH_INTERVAL_MS = 10_000;
export const MAX_PENDING_REFRESHES = 6;

export type MaintenanceTranslationFetcher = (
  maintenanceEntryId: string
) => Promise<MaintenanceTranslationsResponse>;

type CacheEntry =
  | { state: 'loading' }
  | {
      state: 'ready';
      response: MaintenanceTranslationsResponse;
      refreshesUsed: number;
      refreshing?: boolean;
    }
  | { state: 'failed' };

export interface MaintenanceTranslationCache {
  /** Queues every id that has no entry yet. Known ids (loading, ready, or failed) are never requested again via request(). */
  request(ids: readonly string[]): void;
  /** Stored response, or null while loading, after an initial failure, or for an unknown id. */
  get(id: string): MaintenanceTranslationsResponse | null;
  /** Calls the listener after any entry settles. Returns an unsubscribe function. */
  subscribe(listener: () => void): () => void;
  /** Cancels any scheduled pending-translation refresh timers. */
  dispose(): void;
}

export interface MaintenanceTranslationCacheOptions {
  /** Maximum simultaneous requests. Default 4. */
  concurrency?: number;
  /** Delay before refreshing a response that still contains pending translations. Default 10,000 ms. */
  pendingRefreshIntervalMs?: number;
  /** Maximum bounded refresh attempts for a pending entry. Default 6. */
  maxPendingRefreshes?: number;
  onError?: (id: string, error: unknown) => void;
}

export function hasPendingTranslations(
  response: MaintenanceTranslationsResponse | null | undefined
): boolean {
  return Boolean(response?.translations.some((item) => item.status === 'pending'));
}

/**
 * Per-report cache over the existing translations endpoint.
 * Each id is requested once on demand so re-rendering, language changes, and
 * infinite-scroll growth never refetch completed/unavailable entries. Entries that
 * return `pending` schedule bounded background refreshes until settled or disposed.
 */
export function createMaintenanceTranslationCache(
  fetcher: MaintenanceTranslationFetcher,
  options: MaintenanceTranslationCacheOptions = {}
): MaintenanceTranslationCache {
  const concurrency = Math.max(1, options.concurrency ?? 4);
  const pendingRefreshIntervalMs = Math.max(
    1,
    options.pendingRefreshIntervalMs ?? PENDING_REFRESH_INTERVAL_MS
  );
  const maxPendingRefreshes = Math.max(
    0,
    options.maxPendingRefreshes ?? MAX_PENDING_REFRESHES
  );

  const entries = new Map<string, CacheEntry>();
  const queue: string[] = [];
  const listeners = new Set<() => void>();
  const timers = new Map<string, ReturnType<typeof setTimeout>>();
  let running = 0;
  let disposed = false;

  const notify = () => {
    listeners.forEach((listener) => listener());
  };

  const clearTimer = (id: string) => {
    const existing = timers.get(id);
    if (existing !== undefined) {
      clearTimeout(existing);
      timers.delete(id);
    }
  };

  const scheduleRefresh = (id: string) => {
    if (disposed || timers.has(id)) return;
    const entry = entries.get(id);
    if (
      entry?.state !== 'ready' ||
      entry.refreshing ||
      !hasPendingTranslations(entry.response) ||
      entry.refreshesUsed >= maxPendingRefreshes
    ) {
      return;
    }

    const timer = setTimeout(() => {
      timers.delete(id);
      if (disposed) return;

      const current = entries.get(id);
      if (
        current?.state !== 'ready' ||
        current.refreshing ||
        !hasPendingTranslations(current.response) ||
        current.refreshesUsed >= maxPendingRefreshes
      ) {
        return;
      }

      entries.set(id, { ...current, refreshing: true });
      queue.push(id);
      pump();
    }, pendingRefreshIntervalMs);

    timers.set(id, timer);
  };

  const settleSuccess = (id: string, response: MaintenanceTranslationsResponse) => {
    const previous = entries.get(id);
    const refreshesUsed = previous?.state === 'ready' ? previous.refreshesUsed + 1 : 0;
    entries.set(id, { state: 'ready', response, refreshesUsed });
    running -= 1;

    if (!disposed) {
      if (hasPendingTranslations(response) && refreshesUsed < maxPendingRefreshes) {
        scheduleRefresh(id);
      }
      notify();
      pump();
    }
  };

  const settleFailure = (id: string, error: unknown) => {
    options.onError?.(id, error);
    const previous = entries.get(id);
    if (previous?.state === 'ready') {
      // Keep last known ready payload and stop refreshing on transient refresh failure.
      entries.set(id, {
        state: 'ready',
        response: previous.response,
        refreshesUsed: maxPendingRefreshes,
      });
    } else {
      entries.set(id, { state: 'failed' });
    }
    running -= 1;

    if (!disposed) {
      notify();
      pump();
    }
  };

  const pump = () => {
    while (!disposed && running < concurrency && queue.length > 0) {
      const id = queue.shift() as string;
      running += 1;
      Promise.resolve()
        .then(() => fetcher(id))
        .then(
          (response) => settleSuccess(id, response),
          (error: unknown) => settleFailure(id, error)
        );
    }
  };

  return {
    request(ids) {
      if (disposed) return;
      let queued = false;
      for (const id of ids) {
        if (!id || entries.has(id)) continue;
        entries.set(id, { state: 'loading' });
        queue.push(id);
        queued = true;
      }
      if (queued) pump();
    },
    get(id) {
      const entry = entries.get(id);
      return entry?.state === 'ready' ? entry.response : null;
    },
    subscribe(listener) {
      disposed = false;
      listeners.add(listener);
      for (const id of entries.keys()) {
        scheduleRefresh(id);
      }
      return () => {
        listeners.delete(listener);
      };
    },
    dispose() {
      disposed = true;
      while (queue.length > 0) {
        const queuedId = queue.shift() as string;
        const entry = entries.get(queuedId);
        if (entry?.state === 'loading') {
          entries.delete(queuedId);
        } else if (entry?.state === 'ready' && entry.refreshing) {
          entries.set(queuedId, { ...entry, refreshing: false });
        }
      }
      for (const id of Array.from(timers.keys())) {
        clearTimer(id);
      }
    },
  };
}
