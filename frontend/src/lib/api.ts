import type {
  City,
  CompanyDetail,
  ConnectedAccount,
  Country,
  Platform,
  Role,
  Tenant,
  UserListItem,
  CreateSearchRequest,
  Lead,
  LoginResponse,
  Paged,
  SearchDetail,
  SearchSummary,
  User,
} from "./types";

const API_URL = process.env.NEXT_PUBLIC_API_URL ?? "http://localhost:5264";
const TOKEN_KEY = "deeplead.token";
export const UNAUTHORIZED_EVENT = "deeplead:unauthorized";

export class ApiError extends Error {
  constructor(public status: number, message: string) {
    super(message);
  }
}

export const tokenStore = {
  get(): string | null {
    try {
      return localStorage.getItem(TOKEN_KEY);
    } catch {
      return null;
    }
  },
  set(token: string) {
    try {
      localStorage.setItem(TOKEN_KEY, token);
    } catch {
      /* storage blocked: session lasts until reload */
    }
  },
  clear() {
    try {
      localStorage.removeItem(TOKEN_KEY);
    } catch {
      /* ignore */
    }
  },
};

async function request(path: string, init: RequestInit = {}): Promise<Response> {
  const token = tokenStore.get();
  const headers = new Headers(init.headers);
  if (token) headers.set("Authorization", `Bearer ${token}`);
  if (init.body && !headers.has("Content-Type")) headers.set("Content-Type", "application/json");

  let res: Response;
  try {
    res = await fetch(`${API_URL}${path}`, { ...init, headers });
  } catch {
    throw new ApiError(0, `Cannot reach the API at ${API_URL}. Is DeepLead.Api running?`);
  }

  if (res.status === 401 && path !== "/api/auth/login") {
    tokenStore.clear();
    // AuthProvider listens for this and signs the user out (the app layout then routes to /login).
    window.dispatchEvent(new Event(UNAUTHORIZED_EVENT));
  }

  if (!res.ok) throw new ApiError(res.status, await errorMessage(res));
  return res;
}

async function errorMessage(res: Response): Promise<string> {
  try {
    const body = await res.json();
    if (body?.errors) return Object.values(body.errors as Record<string, string[]>).flat().join(" ");
    return body?.message ?? body?.title ?? `Request failed (${res.status})`;
  } catch {
    return `Request failed (${res.status})`;
  }
}

async function json<T>(path: string, init?: RequestInit): Promise<T> {
  const res = await request(path, init);
  // 202/204 and other empty bodies carry no JSON.
  const text = await res.text();
  return (text ? JSON.parse(text) : undefined) as T;
}

export const api = {
  login: (email: string, password: string) =>
    json<LoginResponse>("/api/auth/login", { method: "POST", body: JSON.stringify({ email, password }) }),
  me: () => json<User>("/api/auth/me"),
  changePassword: (currentPassword: string, newPassword: string) =>
    json<void>("/api/auth/change-password", { method: "POST", body: JSON.stringify({ currentPassword, newPassword }) }),

  users: (tenantId?: number) => json<UserListItem[]>(`/api/users${tenantId ? `?tenantId=${tenantId}` : ""}`),
  createUser: (body: { email: string; fullName: string; role: Role; password: string; tenantId?: number }) =>
    json<UserListItem>("/api/users", { method: "POST", body: JSON.stringify(body) }),
  updateUser: (id: number, body: { fullName?: string; role?: Role; isActive?: boolean }) =>
    json<void>(`/api/users/${id}`, { method: "PATCH", body: JSON.stringify(body) }),
  resetPassword: (id: number, newPassword: string) =>
    json<void>(`/api/users/${id}/reset-password`, { method: "POST", body: JSON.stringify({ newPassword }) }),

  tenants: () => json<Tenant[]>("/api/tenants"),
  createTenant: (body: { name: string; adminEmail: string; adminName: string; adminPassword: string }) =>
    json<{ id: number }>("/api/tenants", { method: "POST", body: JSON.stringify(body) }),
  updateTenant: (id: number, body: { name?: string; isActive?: boolean }) =>
    json<void>(`/api/tenants/${id}`, { method: "PATCH", body: JSON.stringify(body) }),

  countries: () => json<Country[]>("/api/geo/countries"),
  cities: (iso2: string, q: string, take = 15) =>
    json<City[]>(`/api/geo/countries/${iso2}/cities?q=${encodeURIComponent(q)}&take=${take}`),
  topCities: (iso2: string, n: number) => json<City[]>(`/api/geo/countries/${iso2}/cities/top?n=${n}`),

  searches: () => json<SearchSummary[]>("/api/searches"),
  search: (id: number) => json<SearchDetail>(`/api/searches/${id}`),
  createSearch: (body: CreateSearchRequest) =>
    json<SearchDetail>("/api/searches", { method: "POST", body: JSON.stringify(body) }),
  pause: (id: number) => json<void>(`/api/searches/${id}/pause`, { method: "POST" }),
  resume: (id: number) => json<void>(`/api/searches/${id}/resume`, { method: "POST" }),
  cancel: (id: number) => json<void>(`/api/searches/${id}/cancel`, { method: "POST" }),

  company: (searchId: number, companyId: number) => json<CompanyDetail>(`/api/searches/${searchId}/companies/${companyId}`),

  accounts: () => json<ConnectedAccount[]>("/api/settings/accounts"),
  connectAccount: (platform: Platform, accountLabel: string | null) =>
    json<void>(`/api/settings/accounts/${platform}/connect`, { method: "POST", body: JSON.stringify({ accountLabel }) }),
  saveAccount: (platform: Platform) => json<void>(`/api/settings/accounts/${platform}/save`, { method: "POST" }),
  disconnectAccount: (platform: Platform) => json<void>(`/api/settings/accounts/${platform}/disconnect`, { method: "POST" }),

  leads: (id: number, opts: { aspectId?: number | null; q?: string; page: number; pageSize: number }) => {
    const p = new URLSearchParams({ page: String(opts.page), pageSize: String(opts.pageSize) });
    if (opts.aspectId) p.set("aspectId", String(opts.aspectId));
    if (opts.q) p.set("q", opts.q);
    return json<Paged<Lead>>(`/api/searches/${id}/leads?${p}`);
  },

  /** Downloads the export through fetch so the auth header is sent, then saves it via a temporary link. */
  async download(id: number, format: "excel" | "csv", aspectId?: number | null) {
    const p = new URLSearchParams({ format });
    if (aspectId) p.set("aspectId", String(aspectId));
    const res = await request(`/api/searches/${id}/export?${p}`);
    const blob = await res.blob();
    const disposition = res.headers.get("Content-Disposition") ?? "";
    const match = /filename\*?=(?:UTF-8'')?"?([^";]+)"?/i.exec(disposition);
    const fileName = match ? decodeURIComponent(match[1]) : `leads.${format === "csv" ? "csv" : "xlsx"}`;

    const url = URL.createObjectURL(blob);
    const a = document.createElement("a");
    a.href = url;
    a.download = fileName;
    a.click();
    URL.revokeObjectURL(url);
  },
};
