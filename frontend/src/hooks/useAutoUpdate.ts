import { useEffect } from 'react';

// -----------------------------------------------------------------------------
// Frontend auto-update mechanism (startup-only check).
//
// Why comparing the bundle filename works reliably:
//   Vite fingerprints every build's JS entry chunk with a content hash, e.g.
//   `/assets/index-D6brXeUF.js`. The hash changes if and only if the built
//   output changes. `index.html` always references the *current* deployment's
//   hashed filename, so fetching `index.html` fresh and extracting that
//   filename is a cheap, always-accurate way to learn "what is the latest
//   build?" — no extra metadata file or backend endpoint needs to be
//   introduced or kept in sync.
//
// Why compare only the bundle filename instead of the full HTML:
//   `index.html` can change for reasons that do NOT represent a new
//   deployment relevant to a running client (whitespace, meta tag ordering,
//   injected analytics snippets, etc.), which would trigger a reload even
//   when nothing meaningful changed if the raw HTML were diffed
//   byte-for-byte. The hashed bundle filename only changes when the actual
//   JS output changes, so it's the smallest signal that is both necessary
//   and sufficient to detect a real new release.
//
// Why no backend endpoint is required:
//   The static `index.html` served by the web server already embeds the
//   current build identity via the hashed script tag. Fetching it once with
//   `cache: "no-store"` bypasses HTTP caching for that one small request
//   without needing any dedicated `/version` API, `version.json`, service
//   worker, or database/backend change of any kind — the existing static
//   asset pipeline is the source of truth.
//
// Why this only runs once, on startup:
//   Reloading a page a user is actively working on is disruptive. Instead,
//   the freshest build is picked up the next time the app is launched
//   (page load / new tab), which is the only moment this hook runs at all —
//   there is no interval, no visibility listener, and no other trigger.
// -----------------------------------------------------------------------------

/** Matches the hashed Vite entry bundle, e.g. `/assets/index-D6brXeUF.js`. */
const BUNDLE_FILENAME_PATTERN = /\/assets\/index-[\w-]+\.js/;

function extractBundleFilename(html: string): string | null {
  const match = html.match(BUNDLE_FILENAME_PATTERN);
  return match ? match[0] : null;
}

/** The bundle filename actually loaded and running in this page right now. */
function getCurrentBundleFilename(): string | null {
  const scriptSrcs = Array.from(document.scripts).map((script) => script.getAttribute('src') ?? '');
  for (const src of scriptSrcs) {
    const match = src.match(BUNDLE_FILENAME_PATTERN);
    if (match) return match[0];
  }
  return null;
}

async function fetchLatestBundleFilename(): Promise<string | null> {
  try {
    // `no-store` applies only to this one-off HTML request — it never
    // disables caching for the JS/CSS bundles themselves, which keep their
    // normal, aggressive, hash-based caching.
    const response = await fetch('/index.html', { cache: 'no-store' });
    if (!response.ok) return null;

    const html = await response.text();
    return extractBundleFilename(html);
  } catch {
    // Network hiccup or offline — say nothing; there is no retry, the next
    // opportunity to check is the next app launch.
    return null;
  }
}

function logInfo(message: string, ...args: unknown[]): void {
  if (import.meta.env.DEV) {
    console.info(message, ...args);
  }
}

/**
 * Checks exactly once, on startup, whether a newer frontend build has been
 * deployed, and reloads the page immediately if so — without any backend,
 * database, or API changes. Performs no background polling and no
 * visibility-based re-checks; the app never auto-refreshes while in use.
 *
 * Mount once at the application root (see `App.tsx`).
 */
export function useAutoUpdate(): void {
  useEffect(() => {
    let isMounted = true;

    (async () => {
      const currentBundle = getCurrentBundleFilename();
      const latestBundle = await fetchLatestBundleFilename();
      if (!isMounted || latestBundle === null) return;

      logInfo('[AutoUpdate] Current bundle:', currentBundle);

      if (currentBundle !== null && latestBundle !== currentBundle) {
        logInfo('[AutoUpdate] New version detected. Reloading...');
        window.location.reload();
      }
    })();

    return () => {
      isMounted = false;
    };
  }, []);
}
