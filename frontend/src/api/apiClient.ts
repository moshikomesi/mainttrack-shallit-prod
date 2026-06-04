import { clearSession } from '../auth/authSession';

const getBaseUrl = (): string => {
  const url = import.meta.env.VITE_API_URL;
  if (typeof url !== 'string' || !url) {
    return '';
  }
  return url.replace(/\/$/, '');
};

export async function apiFetch<T>(
  path: string,
  options: RequestInit = {}
): Promise<T> {
  const baseUrl = getBaseUrl();
  const url = path.startsWith('http') ? path : `${baseUrl}${path}`;

  const headers: Record<string, string> = {
    ...((options.headers as Record<string, string>) ?? {}),
  };

  let body = options.body;
  const isFormData =
    typeof FormData !== 'undefined' && body instanceof FormData;

  if (
    (options.method === 'POST' || options.method === 'PUT' || options.method === 'PATCH') &&
    body !== undefined &&
    typeof body === 'object' &&
    !isFormData
  ) {
    body = JSON.stringify(body);
    if (!headers['Content-Type']) {
      headers['Content-Type'] = 'application/json';
    }
  } else if (
    (options.method === 'POST' || options.method === 'PUT' || options.method === 'PATCH') &&
    body !== undefined &&
    !isFormData &&
    !headers['Content-Type']
  ) {
    headers['Content-Type'] = 'application/json';
  }

  const mergedHeaders: Record<string, string> = {
    ...headers,
    ...((options.headers as Record<string, string>) ?? {}),
  };

  if (isFormData) {
    delete mergedHeaders['Content-Type'];
    delete mergedHeaders['content-type'];
  }

  const response = await fetch(url, {
    ...options,
    body,
    headers: mergedHeaders,
    credentials: 'include',
  });

  if (response.status === 401) {
    clearSession();
    const isAuthProbe = path.includes('/auth/login') || path.includes('/auth/me');
    if (!isAuthProbe) {
      window.location.href = '/';
    }
    throw new Error('Unauthorized');
  }

  if (!response.ok) {
    const text = await response.text();
    throw new Error(text || `HTTP ${response.status}`);
  }

  if (response.status === 204) {
    return undefined as unknown as T;
  }

  const contentType = response.headers.get('Content-Type') ?? '';
  if (contentType.includes('application/json')) {
    return response.json() as Promise<T>;
  }

  return undefined as unknown as T;
}
