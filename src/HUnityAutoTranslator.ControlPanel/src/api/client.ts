export interface ApiRequestOptions extends Omit<RequestInit, "body"> {
  body?: BodyInit | unknown;
}

function isBodyInit(value: unknown): value is BodyInit {
  return typeof value === "string" ||
    value instanceof Blob ||
    value instanceof FormData ||
    value instanceof URLSearchParams ||
    value instanceof ArrayBuffer;
}

async function readResponse<T>(response: Response): Promise<T> {
  const text = await response.text();
  if (!response.ok) {
    throw new Error(text || `请求失败：HTTP ${response.status}`);
  }

  if (!text) {
    return undefined as T;
  }

  const contentType = response.headers.get("content-type") ?? "";
  if (contentType.includes("application/json")) {
    return JSON.parse(text) as T;
  }

  return text as T;
}

export async function api<T>(path: string, options: ApiRequestOptions = {}): Promise<T> {
  const headers = new Headers(options.headers);
  let body: BodyInit | undefined;

  if (options.body !== undefined) {
    if (isBodyInit(options.body)) {
      body = options.body;
    } else {
      body = JSON.stringify(options.body);
      headers.set("content-type", "application/json; charset=utf-8");
    }
  }

  const response = await fetch(path, {
    ...options,
    headers,
    body,
    cache: "no-store"
  });

  return readResponse<T>(response);
}

export function getJson<T>(path: string): Promise<T> {
  return api<T>(path);
}

export async function getText(path: string): Promise<string> {
  const response = await fetch(path, { cache: "no-store" });
  const text = await response.text();
  if (!response.ok) {
    throw new Error(text || `请求失败：HTTP ${response.status}`);
  }

  return text;
}

export function postJson<T>(path: string, body: unknown = {}): Promise<T> {
  return api<T>(path, { method: "POST", body });
}

export function patchJson<T>(path: string, body: unknown): Promise<T> {
  return api<T>(path, { method: "PATCH", body });
}

export function deleteJson<T>(path: string, body: unknown): Promise<T> {
  return api<T>(path, { method: "DELETE", body });
}

export function buildQuery(path: string, values: Record<string, string | number | boolean | null | undefined>): string {
  const query = new URLSearchParams();
  for (const [key, value] of Object.entries(values)) {
    if (value !== null && value !== undefined && value !== "") {
      query.set(key, String(value));
    }
  }

  const suffix = query.toString();
  return suffix ? `${path}?${suffix}` : path;
}
