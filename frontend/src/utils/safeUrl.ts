const ALLOWED_IMAGE_PROTOCOLS = new Set(['http:', 'https:', 'blob:', 'data:']);

/**
 * Returns a URL safe for img/src use, or undefined if the value is missing or unsafe.
 */
export function safeImageSrc(url: string | null | undefined): string | undefined {
  if (!url?.trim()) return undefined;

  try {
    const parsed = new URL(url, typeof window !== 'undefined' ? window.location.origin : 'https://localhost');
    if (!ALLOWED_IMAGE_PROTOCOLS.has(parsed.protocol)) return undefined;
    return parsed.href;
  } catch {
    return undefined;
  }
}
