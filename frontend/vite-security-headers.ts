import type { Connect, Plugin } from 'vite';

const COMMON_HEADERS: Record<string, string> = {
  'X-Content-Type-Options': 'nosniff',
  'X-Frame-Options': 'DENY',
  'Referrer-Policy': 'strict-origin-when-cross-origin',
  'Permissions-Policy': 'camera=(), microphone=(), geolocation=()',
};

function apiOrigin(apiUrl: string): string {
  if (!apiUrl.trim()) return '';
  try {
    return new URL(apiUrl).origin;
  } catch {
    return '';
  }
}

function buildProductionCsp(apiUrl: string): string {
  const connect = ["'self'", apiOrigin(apiUrl)].filter(Boolean).join(' ');
  return [
    "default-src 'self'",
    "script-src 'self'",
    "style-src 'self' 'unsafe-inline'",
    `connect-src ${connect} https:`,
    "img-src 'self' data: blob: https:",
    "font-src 'self'",
    "object-src 'none'",
    "base-uri 'self'",
    "form-action 'self'",
    "frame-ancestors 'none'",
  ].join('; ');
}

function buildDevelopmentCsp(apiUrl: string): string {
  const api = apiOrigin(apiUrl);
  const connect = ["'self'", 'ws:', 'http://localhost:*', 'http://127.0.0.1:*', api]
    .filter(Boolean)
    .join(' ');
  return [
    "default-src 'self'",
    "script-src 'self' 'unsafe-inline' 'unsafe-eval'",
    "style-src 'self' 'unsafe-inline'",
    `connect-src ${connect}`,
    "img-src 'self' data: blob: https: http:",
    "font-src 'self'",
    "object-src 'none'",
    "base-uri 'self'",
    "form-action 'self'",
    "frame-ancestors 'none'",
  ].join('; ');
}

function applyHeaders(server: { middlewares: Connect.Server }, csp: string): void {
  server.middlewares.use((_req, res, next) => {
    for (const [name, value] of Object.entries(COMMON_HEADERS)) {
      res.setHeader(name, value);
    }
    res.setHeader('Content-Security-Policy', csp);
    next();
  });
}

export function securityHeadersPlugin(apiUrl: string, mode: string): Plugin {
  const productionCsp = buildProductionCsp(apiUrl);
  const developmentCsp = buildDevelopmentCsp(apiUrl);
  const cspForBuild = mode === 'development' ? developmentCsp : productionCsp;

  return {
    name: 'mainttrack-security-headers',
    configureServer(server) {
      applyHeaders(server, developmentCsp);
    },
    configurePreviewServer(server) {
      applyHeaders(server, mode === 'development' ? developmentCsp : productionCsp);
    },
    transformIndexHtml(html) {
      const escaped = cspForBuild.replace(/"/g, '&quot;');
      const tag = `    <meta http-equiv="Content-Security-Policy" content="${escaped}" />`;
      return html.replace('<head>', `<head>\n${tag}`);
    },
  };
}
