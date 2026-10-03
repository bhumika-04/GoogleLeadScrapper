// Mirrors DeepLead.Core.Contracts (camelCase JSON from ASP.NET Core).

export type User = {
  id: number;
  tenantId: number;
  tenantName: string;
  email: string;
  fullName: string;
  role: "Admin" | "TenantAdmin" | "User";
};

export type LoginResponse = { token: string; expiresAt: string; user: User };

export type Country = { iso2: string; name: string; phoneCode: string };

export type City = {
  id: number;
  name: string;
  region: string | null;
  population: number | null;
  isUserAdded: boolean;
};

export type SearchStatus = "Pending" | "Running" | "Paused" | "Blocked" | "Completed" | "Failed" | "Cancelled";

export type SearchSummary = {
  id: number;
  name: string;
  countryIso2: string;
  countryName: string;
  status: SearchStatus;
  cityCount: number;
  keywordCount: number;
  aspectCount: number;
  aspectsCompleted: number;
  leadCount: number;
  createdAt: string;
};

export type Aspect = {
  id: number;
  sequence: number;
  city: string;
  region: string | null;
  keyword: string;
  status: SearchStatus;
  leadCount: number;
  itemsTotal: number | null;
  itemsDone: number;
  startedAt: string | null;
  finishedAt: string | null;
  lastError: string | null;
};

export type SearchDetail = {
  summary: SearchSummary;
  icpPrompt: string | null;
  keywords: string[];
  aspects: Aspect[];
};

export type CreateSearchRequest = {
  name: string | null;
  countryIso2: string;
  cityIds: number[];
  newCityNames: string[];
  keywords: string[];
  icpPrompt: string | null;
};

export type Lead = {
  companyId: number;
  aspectId: number;
  mapsRank: number | null;
  keyword: string;
  city: string;
  name: string;
  category: string | null;
  phones: string | null;
  emails: string | null;
  website: string | null;
  socials: string | null;
  address: string | null;
  rating: number | null;
  reviewCount: number | null;
  businessStatus: string;
  latitude: number | null;
  longitude: number | null;
  mapsUrl: string | null;
  foundAt: string;
};

export type Paged<T> = { items: T[]; total: number; page: number; pageSize: number };
