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
  peopleStatus: SearchStatus | null;
  peopleTotal: number | null;
  peopleDone: number;
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
  ownerName: string | null;
  people: string | null;
  peopleCount: number;
  teamSize: string | null;
  turnover: string | null;
  gstin: string | null;
  peopleEnrichedAt: string | null;
  lastEnrichedAt: string | null;
  leadScore: number | null;
  verifiedCount: number;
};

export type Person = {
  id: number;
  fullName: string;
  designation: string | null;
  isOwner: boolean;
  isDecisionMaker: boolean;
  phone: string | null;
  email: string | null;
  linkedInUrl: string | null;
  facebookUrl: string | null;
  instagramUrl: string | null;
  source: string | null;
  sourceUrl: string;
  sourceCount: number;
  sourceDomains: string | null;
};

export type Channel = {
  channelType: "Phone" | "Email";
  normalizedValue: string;
  phoneKind: string | null;
  isValid: boolean | null;
  validationNote: string | null;
  sourceUrl: string | null;
  sourceCount: number;
  sourceDomains: string | null;
};

export type Fact = { fieldName: string; value: string; sourceUrl: string; quote: string | null; extractedBy: string; foundAt: string; verified: boolean };

export type CompanyDetail = {
  id: number;
  name: string;
  category: string | null;
  address: string | null;
  website: string | null;
  mapsUrl: string | null;
  rating: number | null;
  reviewCount: number | null;
  ownerName: string | null;
  teamSize: string | null;
  turnover: string | null;
  gstin: string | null;
  peopleEnrichedAt: string | null;
  lastEnrichedAt: string | null;
  leadScore: number | null;
  facebookFollowers: number | null;
  people: Person[];
  channels: Channel[];
  socials: { platform: string; url: string }[];
  facts: Fact[];
};

export type Role = "Admin" | "TenantAdmin" | "User";

export type UserListItem = {
  id: number;
  tenantId: number;
  email: string;
  fullName: string;
  role: Role;
  isActive: boolean;
  lastLoginAt: string | null;
  createdAt: string;
};

export type Tenant = {
  id: number;
  name: string;
  isActive: boolean;
  userCount: number;
  sessionCount: number;
  leadCount: number;
  createdAt: string;
};

export type Platform = "LinkedIn" | "Facebook" | "Instagram" | "IndiaMart" | "Justdial";

export type AccountStatus = "ConnectRequested" | "WaitingForLogin" | "SaveRequested" | "Connected" | "Expired" | "Failed" | "Disconnected";

export type ConnectedAccount = {
  platform: Platform;
  status: AccountStatus;
  accountLabel: string | null;
  requestedAt: string | null;
  connectedAt: string | null;
  lastUsedAt: string | null;
  lastError: string | null;
  usageToday: number;
};

export type Paged<T> = { items: T[]; total: number; page: number; pageSize: number };
