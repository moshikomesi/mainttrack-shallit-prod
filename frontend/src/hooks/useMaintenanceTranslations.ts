import { useCallback, useEffect, useRef, useState } from 'react';
import { getMaintenanceTranslations } from '../services/maintenanceService';
import {
  createMaintenanceTranslationCache,
  type MaintenanceTranslationCache,
} from '../services/maintenanceTranslationCache';
import type { MaintenanceTranslationsResponse } from '../types/maintenance';

/**
 * Loads narrative translations for the given maintenance entry ids and returns a lookup.
 * Responses are cached per entry id for the life of the screen, so a re-render, a language
 * change, or a longer list never refetches. A failed entry resolves to null (original narrative).
 */
export function useMaintenanceTranslations(
  ids: readonly string[]
): (id: string | null | undefined) => MaintenanceTranslationsResponse | null {
  const cacheRef = useRef<MaintenanceTranslationCache | null>(null);
  if (cacheRef.current === null) {
    cacheRef.current = createMaintenanceTranslationCache(getMaintenanceTranslations, {
      onError: (id, error) => console.error('Maintenance translation failed', id, error),
    });
  }
  const cache = cacheRef.current;

  const [version, setVersion] = useState(0);

  useEffect(() => {
    let mounted = true;
    const unsubscribe = cache.subscribe(() => {
      if (mounted) setVersion((version) => version + 1);
    });
    return () => {
      mounted = false;
      unsubscribe();
      cache.dispose();
    };
  }, [cache]);

  const idsKey = ids.join('|');
  useEffect(() => {
    cache.request(idsKey ? idsKey.split('|') : []);
  }, [cache, idsKey]);

  // The lookup changes identity whenever an entry settles, so memoized consumers recompute.
  // eslint-disable-next-line react-hooks/exhaustive-deps
  return useCallback((id) => (id ? cache.get(id) : null), [cache, version]);
}
