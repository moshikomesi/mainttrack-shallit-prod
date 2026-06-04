import { useState, useEffect, useRef, useCallback } from 'react';

export interface PagedResponse<T> {
  items: T[];
  pageNumber?: number;
  pageSize?: number;
  totalCount?: number;
}

export interface UseInfiniteScrollOptions<T extends { id: string }> {
  /** Fetches a single page. Optional signal allows aborting in-flight requests. */
  fetchPage: (pageNumber: number, pageSize: number, signal?: AbortSignal) => Promise<PagedResponse<T>>;
  pageSize?: number;
  /** When this value changes, state resets to page 1 and items are cleared (e.g. filter or search). */
  resetKey?: string | number;
  /** When false, no fetch runs. Use to avoid fetching until the list is active (e.g. tab selected). */
  enabled?: boolean;
}

export interface UseInfiniteScrollResult<T extends { id: string }> {
  items: T[];
  loading: boolean;
  hasMore: boolean;
  error: Error | null;
  /** Attach to a sentinel element at the bottom of the list to trigger loading the next page. */
  loadMoreRef: (node: Element | null) => void;
  /** Call after error to retry loading (resets to page 1 and refetches). */
  retry: () => void;
}

const DEFAULT_PAGE_SIZE = 20;

export function useInfiniteScroll<T extends { id: string }>({
  fetchPage,
  pageSize = DEFAULT_PAGE_SIZE,
  resetKey,
  enabled = true,
}: UseInfiniteScrollOptions<T>): UseInfiniteScrollResult<T> {
  const [items, setItems] = useState<T[]>([]);
  const [page, setPage] = useState(1);
  const [loading, setLoading] = useState(false);
  const [hasMore, setHasMore] = useState(true);
  const [error, setError] = useState<Error | null>(null);

  const fetchPageRef = useRef(fetchPage);
  fetchPageRef.current = fetchPage;
  const prevResetKeyRef = useRef(resetKey);
  const lastRequestedPageRef = useRef<number | null>(null);
  const requestIdRef = useRef(0);
  const abortRef = useRef<AbortController | null>(null);
  const observerRef = useRef<IntersectionObserver | null>(null);
  const loadMoreRef = useRef<Element | null>(null);

  const loadingRef = useRef(loading);
  const hasMoreRef = useRef(hasMore);
  loadingRef.current = loading;
  hasMoreRef.current = hasMore;

  // Reset when resetKey changes (e.g. search/filter).
  useEffect(() => {
    setPage(1);
    setItems([]);
    setHasMore(true);
    setError(null);
    lastRequestedPageRef.current = null;
  }, [resetKey]);

  useEffect(
    () => {
      if (!enabled) return;
      const isReset = prevResetKeyRef.current !== resetKey;
      if (isReset) {
        prevResetKeyRef.current = resetKey;
      }
      const pageToFetch = isReset ? 1 : page;

      if (lastRequestedPageRef.current === pageToFetch) return;
      lastRequestedPageRef.current = pageToFetch;

      const currentRequestId = ++requestIdRef.current;

      if (abortRef.current) {
        abortRef.current.abort();
      }
      abortRef.current = new AbortController();
      const signal = abortRef.current.signal;

      console.debug('[InfiniteScroll] fetching page:', pageToFetch);

      const run = async () => {
        setLoading(true);
        setError(null);
        try {
          const data = await fetchPageRef.current(pageToFetch, pageSize, signal);
          if (currentRequestId !== requestIdRef.current) return;
          const list = Array.isArray(data.items) ? data.items : [];
          if (list.length === 0 && pageToFetch > 1) {
            setHasMore(false);
            return;
          }
          setItems((prev) => {
            if (pageToFetch === 1) return list;
            const existingIds = new Set(prev.map((item) => item.id));
            const filtered = list.filter((item) => !existingIds.has(item.id));
            return [...prev, ...filtered];
          });
          const totalCount = data.totalCount;
          if (totalCount != null) {
            const loadedAfterThisPage = (pageToFetch - 1) * pageSize + list.length;
            setHasMore(loadedAfterThisPage < totalCount);
          } else {
            if (list.length === 0) {
              setHasMore(false);
            } else {
              setHasMore(list.length >= pageSize);
            }
          }
        } catch (err) {
          if (currentRequestId !== requestIdRef.current) return;
          if (err instanceof Error && err.name === 'AbortError') return;
          setError(err instanceof Error ? err : new Error(String(err)));
        } finally {
          if (currentRequestId === requestIdRef.current) {
            setLoading(false);
          }
        }
      };
      run();
      return () => {
        if (abortRef.current) {
          abortRef.current.abort();
        }
      };
    },
    [enabled, page, pageSize, resetKey]
  );

  const setLoadMoreRef = useCallback((node: Element | null) => {
    if (observerRef.current) {
      observerRef.current.disconnect();
      observerRef.current = null;
    }
    loadMoreRef.current = node;
    if (!node) return;
    observerRef.current = new IntersectionObserver(
      (entries) => {
        const entry = entries[0];
        if (!entry?.isIntersecting) return;
        if (loadingRef.current) return;
        if (!hasMoreRef.current) return;
        setPage((p) => p + 1);
      },
      { rootMargin: '200px', threshold: 0 }
    );
    observerRef.current.observe(node);
  }, []);

  useEffect(() => {
    return () => {
      if (observerRef.current) {
        observerRef.current.disconnect();
        observerRef.current = null;
      }
      if (abortRef.current) {
        abortRef.current.abort();
        abortRef.current = null;
      }
    };
  }, []);

  const retry = useCallback(() => {
    setError(null);
    setPage(1);
    setItems([]);
    setHasMore(true);
    lastRequestedPageRef.current = null;
  }, []);

  return {
    items,
    loading,
    hasMore,
    error,
    loadMoreRef: setLoadMoreRef,
    retry,
  };
}
