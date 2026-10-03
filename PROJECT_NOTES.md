# DeepLead RPA – Project Notes

> Living document. Status: **R&D / requirements phase – no code yet.**
> Last updated: 2026-10-01

## 1. Vision
A multilingual lead-intelligence tool that, for any country, collects business lead data from many sources and builds a **full company profile** (not just contact details). Results are stored separately per search aspect (country + city + keyword).

## 1a. Main Motive (owner, 2026-10-03)
Find potential leads AND, for each company:
- **Owner**: name, phone number, social media handles
- **Core team members**: name, phone number, social media handles
Company-level data (Maps details, turnover, team size) is secondary to these people + contacts.

### Social media access – DECIDED 2026-10-03: "Connect account" in Settings, no-login sources first
(Built: Settings page with LinkedIn / Facebook / Instagram / IndiaMART / Justdial cards; worker opens a visible login window,
user logs in, session saved AES-GCM encrypted in `ConnectedAccounts`. Not yet USED by scrapers – next step.)

**Search-engine finding (2026-10-03, one IP, no proxies):** Google and Brave CAPTCHA on the first query (even in a visible browser);
Bing serves deliberately irrelevant results to automated clients; Yahoo returns 500; DuckDuckGo works briefly then rate-limits the IP
for hours. => general web search cannot be a reliable backbone. Stage 2 therefore treats search as best-effort (circuit breaker,
"partial" flag) and relies on sources that need no search engine: company website, IndiaMART profile, and (next) logged-in platform search.

Original proposal:
Playwright is not limited to a guest browser:
- **Persistent profile**: a dedicated browser profile folder per account (`LaunchPersistentContext(userDataDir)`); log in once by hand in a visible window, cookies are reused on later runs.
- **"Connect account" flow**: worker opens a visible browser, user logs in (incl. 2FA), session saved encrypted per tenant + platform; re-connect when it expires.
- Use **dedicated accounts**, never someone's main personal account: LinkedIn/Meta restrict or ban accounts that browse like bots; keep volumes low and human-paced.
- **No-login sources first**: Google `site:linkedin.com/in` snippets (name + title), public Facebook pages (phone/email in About), YouTube, company websites, IndiaMART/Justdial contact person, MCA director lists. Login-based scraping only for what these can't give.

## 2. Confirmed Decisions
| Area | Decision |
|---|---|
| Backend | C# (.NET) |
| Database | MS SQL Server + **DbUp**, **no Entity Framework** (Dapper) |
| Frontend | Next.js, web only |
| Users | Multi-customer (multi-tenant) + internal team; login required |
| Hosting | Windows PC now, Windows Server later |
| Data access | **No official/paid APIs** – browser automation + HTTP scraping |
| AI | OpenAI key only (translation, extraction, enrichment) |
| Result depth | No limit – continue until end of results |
| Export | Excel, CSV, Google Sheets |
| Purpose | Data collection; optional Interakt (WhatsApp) integration via credentials + approved template in Settings |
| Compliance | Skipped for now (owner decision) |
| Budget | OpenAI only – no proxies (confirmed: not even later), no CAPTCHA solver |
| Data truth policy | **Real figures only – no AI estimates.** Every value must have a source URL; if not found, the field stays empty ("Not found") |
| First market | India (then other countries; design multi-country from day 1) |
| Billing/credits | Not now |

## 3. Data Sources
Google Maps, Google Search (open every result), lead's own website, LinkedIn, Facebook, Instagram, YouTube, IndiaMART, magicpin.

## 4. Lead Data Model (target)
**From Google Maps:** name, phone, email, address, website, rating, category, location (lat/long).
**Main focus – company intelligence:**
- Turnover / revenue range, team size, domain/industry
- Products manufactured / services offered
- Core team members: name, designation, phone, email
- Key Decision Maker: name, phone, email
- Social handles: LinkedIn, Facebook, Instagram, YouTube
- Validation flags: phone valid (per-country), email valid
- Per-field **source + confidence score** (important since turnover/team size are often inferred)

## 5. Core Flow (pipeline order confirmed by owner)
0. User picks country -> cities (multi) -> keywords (multi). Each combination = one **Aspect**; results stored separately. OpenAI translates keywords into the country's native language(s) + English.
1. **Stage 1 – Google Maps:** scrape all listings (scroll to end; split city into sub-areas to beat the ~120 cap). **Discard listings marked "Permanently closed"** (not saved). Fields: name, phone, email, address, website, rating, category, location.
2. **Stage 2 – Company intelligence (approved approach, OpenAI web search NOT used):** for each Stage 1 lead:
   1. **Targeted searches by our own scraper** on Google (Bing/DuckDuckGo fallback):
      - `"<name> <city> turnover"`
      - `"<name> <city> IndiaMART"`
      - `"<name> <city> director"` (MCA-based sites: Zauba, Tofler, The Company Check)
      - `"<name> <city> LinkedIn"`
      - `"<name> <city> owner"` / `"founder"`
   2. **Open top results** (lead website About/Team pages, IndiaMART, MCA-based sites, LinkedIn public page). Name/city/phone/domain must match the lead, else page is ignored (avoids wrong-company data).
   3. **Rules first:** site parsers read structured fields directly (IndiaMART turnover/employees, MCA directors). Fields already found skip OpenAI.
   4. **OpenAI extraction (text only, no web access)** for remaining fields among the 4: **Team Size, Turnover, Owner Name, Core Member Names**. Must return value + exact quote + URL, or null. No answers from model memory.
   5. **Code verification:** quote must exist in the stored page text, else value rejected.
   6. Store value + source URL + quote + date. Missing = "Not found".
   - Everything else (products, phones, emails, social handles, decision-maker contacts) comes from Stages 1, 3, 4 – not OpenAI.
3. **Stage 3 – Google Search:** search per lead (name + city, name + "director", name + "turnover", etc.) and per keyword; paginate until results end.
4. **Stage 4 – Visit every result URL:** Playwright opens each page (lead website, IndiaMART, magicpin, LinkedIn/FB/Insta/YouTube public pages, directories); OpenAI extracts structured data from page text with the URL as evidence.
5. **Stage 5 – Merge & verify:** cross-check Stage 2 values against Stage 4 pages (value confirmed by 2+ sources = "Verified"), validate phone/email, dedupe, export.

### "Real figures" – what is realistically available (India)
| Field | Real public sources | Note |
|---|---|---|
| Turnover | IndiaMART profile ("Annual Turnover" range), MCA-based sites (Zauba Corp, Tofler free tier, The Company Check), company website, news/press | Usually a **range** (e.g., 5–25 Cr), self-declared; exact figures mostly behind paid filings |
| Team size | IndiaMART ("No. of Employees"), LinkedIn company page size band, website | Usually a range |
| Directors / decision makers | MCA-based sites (director names + DIN), LinkedIn, website "About/Team" | Names common; direct phone/email of directors is rare |
| Products | Website, IndiaMART catalogue | Good coverage |
| GST / CIN | IndiaMART, GST search sites, Zauba | Good for India, useful for dedupe |
If none of these exist for a lead, the field stays empty – never estimated.

## 5a. Search Console (user input screen – from owner's reference screenshots)
All search input is entered by the user; nothing is hard-coded (Indore + Printing Companies was only the POC test input).
| Field | Behaviour |
|---|---|
| Session name | Optional; groups the run's leads, validation & research. Blank = auto-named (e.g. "Paper Trader +6 — India, 50 cities") |
| Market | Country dropdown (flag + ISO code) |
| City | Multi-select chips with search; user can also **type any city + Enter** (saved as user-added city); "N cities selected" counter; clear-all |
| Quick-add top cities | Top 50 / 100 / 200 / 500 / Clear – **by population from GeoNames (free, exact, no AI cost)** |
| Keyword / Business Category | Comma-separated list (e.g. Paper Trader, Paper Stockists, Paper Converters …) |
| ICP for this session | Editable prompt (collapsible) describing owner's business + scoring rules; saved on this session only. OpenAI scores each lead 0–100 + relevant true/false + reason |
| Also search for | "AI suggest related" – OpenAI suggests related keywords shown as "+ chip"; click to add |
| Preview | "Will search N combinations sequentially (C cities × K keywords)" with numbered list: city-major order (1. Keyword1 — City1, 2. Keyword2 — City1 …) |

DB mapping: `Searches` (session, IcpPrompt) -> `SearchKeywords` (User/AiSuggested) + `SearchCities` -> `SearchAspects` (Sequence) -> `SearchLeadScores` (ICP score per session per company).

## 6. Technical Risks (no APIs, no proxies – must be understood)
| Risk | Reality | Mitigation |
|---|---|---|
| Google Search blocks (CAPTCHA/429) | Unlimited depth from one IP gets blocked quickly; **no proxies ever** means throughput is capped by one IP | Slow human-like throttling, randomised delays, persistent browser profiles, auto-pause/cool-down/resume, "solve CAPTCHA manually" prompt in UI (headful browser), fallback to Bing/DuckDuckGo; Stage 2 adds ~5 extra searches per lead, so search volume is the main bottleneck – company cache avoids repeats |
| OpenAI cost | Tokens per extraction (text only), × every lead | Skip OpenAI when rules already found the value, send only relevant page sections, per-job cost estimate, per-tenant usage tracking, company cache |
| AI hallucination | Model may invent numbers | Citations mandatory; value rejected if the cited page doesn't contain it (we re-fetch and check) |
| Google Maps unlimited scroll | Works but slow; Maps caps ~120 results per query | Split query by sub-area/neighbourhood grid + keyword variants to get more |
| LinkedIn/Facebook/Instagram | Login walls, strong bot detection, account-ban risk | Use public data only (search snippets, public pages, website links); optional user-supplied session cookie; treat as best-effort |
| Turnover / team size | Rarely published | Infer from: website text, LinkedIn size band, IndiaMART "annual turnover/employees" fields (very common there), business registries; LLM estimate flagged low-confidence |
| ToS | Scraping violates source ToS | Owner accepted; keep rate-limits polite |
| Windows PC hosting | Sleep/IP changes/uptime | Run as Windows Service; later move to server |

## 7. Tech Stack (FINAL – awaiting owner's last confirmation)
- **Runtime:** .NET 10 (LTS, supported to Nov 2028).
- **Database:** SQL Server **Express** (free; 10 GB per DB); DbUp scripts embedded in a migrator project.
- **Page storage (hybrid – approved):**
  1. Rules extract first (phones, emails, social links, IndiaMART/Zauba fields) – free.
  2. OpenAI only for relevant pages rules couldn't fully read (About/Team/Products), sending only relevant sections -> returns JSON.
  3. Extracted JSON stored in DB (`NVARCHAR(MAX)` + `ISJSON` check; queryable with `JSON_VALUE`/`OPENJSON`).
  4. Raw HTML **not kept**; only cleaned page text, gzip on disk (`data/pages/{yyyy}/{MM}/{id}.txt.gz`), **auto-deleted after 30 days** (quote re-verification window). DB keeps path + hash + URL + fetched date.
- **API:** ASP.NET Core Web API, Dapper, DbUp, Serilog, FluentValidation.
- **Auth/multi-tenant:** custom JWT auth (BCrypt password hashing) with Dapper (no Identity/EF); `TenantId` on every table; roles: Admin / TenantAdmin / User; per-tenant quotas.
- **Job engine:** Hangfire (SQL Server storage) + separate Worker Service; queue per source, concurrency limits, Polly retries, pause/resume/cancel, schedules.
- **Scraping:** Playwright for .NET (Maps, Google, IndiaMART, magicpin, social) + AngleSharp for static pages; persistent browser contexts; stealth settings; domain-level rate limiter.
- **AI layer:** OpenAI official .NET SDK, structured outputs (JSON schema), **no web search tool** – used only for (a) Stage 2 text extraction of Team Size, Turnover, Owner Name, Core Member Names from pages we scraped, (b) keyword translation; token/cost tracking per tenant; no inferred/estimated values. Stage 4: rules first, OpenAI JSON extraction only for relevant pages rules couldn't fully read (hybrid).
- **Validation:** phone – libphonenumber-csharp (country-aware); email – syntax + disposable check + MX lookup (DnsClient) + optional SMTP probe.
- **Realtime:** SignalR for live job progress.
- **Exports:** ClosedXML (Excel) and CSV only. **No Google Sheets** (owner decision – no Google API at all).
- **Messaging:** Interakt integration module; credentials encrypted (DPAPI / AES) per tenant; approved-template sender.
- **Frontend:** Next.js + TypeScript, TanStack Table, per-aspect result tabs, filters, saved searches.
- **Geo data:** GeoNames dataset seeded via DbUp scripts (countries, cities, languages).
- **Tenancy:** single shared database, `TenantId` column on tenant-owned tables; shared global company cache (public data) across tenants.
- **Hosting:** API + Worker as Windows Services (Kestrel); Next.js as Node service behind IIS reverse proxy or standalone; same setup on PC and Windows Server.

### Solution structure (proposed)
```
DeepLead_RPA/
  PROJECT_NOTES.md
  backend/
    DeepLead.sln
    src/
      DeepLead.Api/          ASP.NET Core API, auth, SignalR hub
      DeepLead.Worker/       Windows Service: Hangfire server, pipeline stages
      DeepLead.Core/         domain models, interfaces, pipeline contracts
      DeepLead.Data/         Dapper repositories, SQL
      DeepLead.Migrator/     DbUp console + embedded SQL scripts (Scripts/0001_*.sql)
      DeepLead.Scrapers/     Playwright infra, Maps, Google/Bing/DDG, site parsers
      DeepLead.Enrichment/   OpenAI extraction, quote verification, phone/email validation, GSTIN/CIN, signals, scoring
      DeepLead.Export/       Excel/CSV
    tests/
      DeepLead.Scrapers.Tests/   parser tests on saved HTML snapshots
      DeepLead.Enrichment.Tests/
  frontend/                 Next.js + TypeScript
  data/pages/               gzip cleaned page text, 30-day retention (git-ignored)
```

## 8. Draft DB Tables
Tenants, Users, Roles, Countries, Cities, Languages, CountryLanguages, Searches, SearchAspects (country+city+keyword+language), Jobs, JobSteps, Leads, LeadSources, LeadContacts (people), LeadSocials, LeadProducts, LeadFieldEvidence (source+confidence), RawPages, ExportHistory, Integrations (Interakt etc.), OpenAiUsage, AuditLog.

## 9. Improvements & Feature Ideas (proposed 2026-10-03 – owner to pick)

### A. Data quality ("real figures" made visible)
- A1. **Evidence view** – click any field to see source URL + exact quote + date found.
- A2. **Verified badge** – value confirmed by 2+ independent sources.
- A3. **Company identity matching** – merge duplicates by phone, website domain, GSTIN/CIN, fuzzy name+address.
- A4. **Freshness** – "last seen" date per field; re-check stale leads on demand.
- A5. **Phone type** – mobile vs landline (libphonenumber); mobiles flagged as WhatsApp-capable candidates.

### B. India-specific free intelligence (real data, no API)
- B1. **GSTIN extraction + offline checksum validation** – GSTIN itself encodes state + PAN.
- B2. **CIN decoding** – gives listed/unlisted, industry (NIC) code, state, incorporation year, company type (Pvt/Public/LLP).
- B3. **Udyam/MSME number capture** where published.
- B4. **Director list via MCA-based sites** (names + DIN) feeding Owner/Core Members.

### C. Buying signals from data we already scrape
- C1. Google Maps extras: review count, recent review activity, photos count, claimed/unclaimed listing, opening hours, owner replies.
- C2. Website signals: has website or not, tech used (WordPress/Shopify/etc.), SSL, mobile-friendly, copyright year (outdated site = sales opportunity), domain age (WHOIS).
- C3. Social activity: last post date / follower count on public pages.
- C4. **Lead score** = completeness + validity + activity signals (configurable weights).

### D. Efficiency under "one IP, no proxies"
- D1. **Global company cache** – a company researched once is reused for every tenant/job (biggest speed + OpenAI-cost saving).
- D2. **Skip OpenAI when rules already found the value** – e.g., IndiaMART parser already gives turnover/employees → no OpenAI call for those fields.
- D3. **Adaptive rate limiter** – learns safe speed per source, auto cool-down on block, night-time scheduling.
- D4. **Incremental re-runs** – rerun an aspect and fetch only new/changed leads.
- D5. **Keep cleaned page text 30 days** – re-parse/re-verify without re-scraping (raw HTML not stored – hybrid decision).
- D6. **Time & cost estimate** before a job starts.

### E. User features
- E1. **Enrich my list** – upload an existing Excel of companies; run only enrichment stages.
- E2. Excel export with **one sheet per aspect** + column chooser + saved export templates.
- E3. "Exclude leads already exported" / do-not-contact list.
- E4. Mini-CRM: tags, notes, status (New/Contacted/Interested/Won), assign to team member.
- E5. Map view of leads (lat/long from Maps).
- E6. Saved searches + scheduled recurring runs + email notification on completion.
- E7. Interakt: map template variables to lead fields ({{name}}, {{city}}), send log, opt-out handling.

### F. Operations / maintainability
- F1. **Source health dashboard** – success/block rate per source; alert when a parser suddenly returns empty (site layout changed).
- F2. **Selectors stored in DB/config** – fix a broken parser without redeploying.
- F3. Parser regression tests using saved HTML snapshots.
- F4. Per-tenant usage dashboard (leads, jobs, OpenAI tokens), audit log, DB backups.

## 10. Phases (aligned with confirmed pipeline order)
1. **POC (Indore – Printing Companies):** Google Maps scraper (skip permanently closed) + Stage 2 (targeted searches + OpenAI text extraction) on ~20 leads – measure: results count, block rate, % of fields found with real citations, cost per lead.
2. **MVP:** auth + tenants, countries/cities/aspects, Stage 1 + Stage 2 in pipeline, Excel/CSV export, Next.js UI with live progress.
3. **Phase 2:** Stage 3 + 4 (Google Search, visit all URLs, IndiaMART/magicpin/social extractors), merge & verify, phone/email validation, Google Sheets export.
4. **Phase 3:** Interakt messaging, scheduling, scoring, other countries' languages tuning. (Billing later.)

## 11. Remaining Open Questions
1. Owner approved stack (section 7) but has doubts – collect and resolve them.

## 13. Build Progress
- 2026-10-03: Solution scaffolded (`backend/DeepLead.sln`, 8 projects + 2 test projects, all packages added, builds with 0 warnings). DbUp migrator with scripts 0001–0006 (tenancy, geo, search/aspects/stages, companies + channels/people/socials/products, pages/evidence/logs, seed IN/AE + Indore). Not yet run against a database.
- 2026-10-03: Schema updated for Search Console (sessions, keywords, cities, sequential aspects, ICP scores, user-added cities). Database `LeadScrapper` created, scripts 0001–0006 applied (27 tables). GeoNames import added (`DeepLead.Migrator --import-cities IN`): 7,075 Indian cities loaded with population + state.
- 2026-10-03: **Stage 1 Google Maps scraper working** (`DeepLead.Scrapers/Maps`). Live test Indore + "Printing Companies", 15 places in 75 s: name, phone, website, rating, review count, category, address, lat/long, place id all filled (0 missing). Permanently closed skipped from list card and place page. Blocked/CAPTCHA page -> `ScrapeBlockedException`. Dev CLI: `DeepLead.Cli maps --keyword ... --city ... [--max N] [--headful]`. 15 parser unit tests pass.
- Observations: Maps phones are local format (`09685251186`) -> normalize to E.164 in validation step; some "websites" are Instagram/Facebook links -> route to CompanySocials, not Website.
- 2026-10-03: **Web app working end-to-end** (login → Search Console → Worker runs aspects → live progress → leads table → Excel/CSV).
  - API (`DeepLead.Api`, http://localhost:5264): JWT login, geo (countries, city type-ahead, top-N), sessions (create/list/detail/pause/resume/cancel), leads (paged, filter), export (Excel: "All leads" + one sheet per aspect; CSV UTF-8 BOM).
  - Worker (`DeepLead.Worker`): polls DB for Pending sessions, runs aspects in sequence, saves to company cache (dedupe by Maps place id, else normalized name+city), phones -> E.164 via libphonenumber, Instagram/Facebook "websites" -> CompanySocials, Google block -> 20-min cool-down then retry, honours Pause/Cancel between listings, resumes interrupted sessions on restart. `Scraping:MaxResultsPerAspect` = test cap only.
  - Frontend (`frontend/`, Next.js 16 + Tailwind 4, dark theme from reference screenshots): login, Sessions list, Search Console (market, city chips with type-ahead + typed cities + Top 50/100/200/500, keywords, ICP box, run-plan preview), session page (stats, combinations list, leads table, pause/resume/cancel, export). Polls every 4–5 s instead of SignalR for now.
  - `run-dev.ps1` starts API + Worker + web in 3 windows.
  - Deviations from section 7 (deliberate, for now): Hangfire removed – a DB-polling BackgroundService is simpler for sequential runs (Hangfire can return for scheduled/recurring jobs); SignalR replaced by polling.
  - Known data issue: GeoNames population is wrong for some places (district totals, e.g. Kallakurichi, Nowrangapur, Rasapudipalem appear in Top 50). Cities now shown/searched by ASCII name ("Rajkot", not "Rājkot").
- 2026-10-03: **Stage 2 people discovery + Connected accounts built** (migration 0007).
  - Per aspect: Maps -> people discovery for each company (30-day company cache) -> aspect done; resume skips finished Maps stage.
  - Sources: company website (home + contact/about/team pages: emails w/ MX check, phones via libphonenumber, socials, "Proprietor: Mr X" mentions),
    IndiaMART seller profile (CEO/owner, employees, legal status, GSTIN, year est., forwarding number), DuckDuckGo search (best-effort) for
    IndiaMART pages, public LinkedIn profile titles (name + designation, kept only if company name matches), company social pages.
  - Every person/fact stored with source URL; Excel export has a "People" sheet; UI has owner/team column + company drawer.
  - Live test Indore: Burhani Offset Printers -> owner H Barodawala (Proprietor), Upto 10 people, GSTIN, est. 1997 (IndiaMART);
    Singh PhotoCopy -> 6 phones + email from website. DDG blocked throughout -> no LinkedIn people found in this test.
  - 38 unit tests (parsers, matcher, phones). Bugs found by tests/live runs and fixed: glued page text corrupting emails, '+' lost in tel: links.
- 2026-10-03: Owner chose options 1+2. Built:
  - **IndiaMART without search**: IndiaMART's own search returns nothing to automated browsers, so the seller page is found by
    slug guessing (`jai-ma-graphics`, `jaimagraphics`, `jai-ma-graphics-indore`) and verified by name + city. Paced client
    (~7 s, 429 -> 10 min cool-down). Owner also read from the verified-supplier block ("directorProprietor": "Rinku Sharma (Owner)").
    Live: owners found for 3/6 Indore companies with zero search-engine use (L Gupta, S Bhargava, H Barodawala) + Jai Ma Graphics -> Rinku Sharma.
  - **LinkedIn people search with the connected account** (migration 0008: daily usage counter): one search per company,
    25–50 s pacing, daily cap 60, visible browser by default, cookies re-saved after each search, login wall -> account marked Expired.
    Markup-agnostic extraction (profile links + card text); kept only when the card names the company. Empty result pages are
    snapshotted to `logs/linkedin/` for diagnosis. **Not yet verified against a real LinkedIn account** – owner to connect one.
  - Order per company: website / own IndiaMART page -> IndiaMART guess -> LinkedIn -> web search (only if still nobody).
- 2026-10-03: Alternatives discussed and **set aside – staying with the original free Playwright plan**:
  Google Places API (~$35/1,000 requests ≈ $1.75 per 1,000 businesses, max 60 results/query, no owner/email, storage limits in ToS),
  Gemini API grounding with Google Search ($14/1,000 after 5,000 free/month; Google "AI Mode" itself has no API),
  Chrome extension (Manifest V3) using the user's own logged-in browser (good for LinkedIn/Facebook, but needs Chrome open on a PC).
  Revisit if blocking becomes the bottleneck.
- 2026-10-03: **User management** – Settings > Users (add with temporary password, role, reset password, deactivate; can't remove own/last
  admin access), Settings > Customers for platform admins (create isolated workspace + first TenantAdmin, activate/deactivate),
  Account page (change own password). Deactivation is enforced on the next request (JWT OnTokenValidated DB check). API-tested 14 cases incl. isolation.
- 2026-10-03: **Full-list Maps test**: Indore "Printing Companies" -> 117 saved in 556 s (~4.8 s/place), list never showed
  "end of list" => Google's ~120 cap confirmed. **Area splitting built**: if the city list is capped (≥100 cards, no end-of-list),
  the worker re-searches over a grid of map centres (3×3, or 5×5 for cities ≥3M people, 4 km apart, zoom 15), skips places already
  saved at list level (no page open), drops results > (span×1.5 + 6 km) from the city centre. Live check: one area 4 km east returned
  results averaging 1.6 km from its centre, 2 of its top 12 were not in the 117-result city list. Settings: Scraping:AreaSplitting*.
  Cost: a full 3×3 split can add 30–90 min per capped combination (mostly list scrolling; known places are skipped).
- 2026-10-03: **Stage 4 Facebook + Stage 5 Verified & Lead score** (migration 0009).
  - Facebook business pages read without login (or with the connected Facebook account): intro phone/email, website, Instagram/YouTube
    links, follower count; English UI forced (locale=en_US). Page from website/Maps links, else name guesses verified by page name.
  - **Justdial parked**: listing/detail pages return an empty 14-byte response / HTTP2 reset to automated clients. Revisit via a connected
    Justdial account in a visible browser.
  - Verified: phones/emails/people keep the distinct sites that showed them (SourceDomains/SourceCount); 2+ sites = "✓ Verified";
    facts verified when the same value comes from 2+ sites. Live: Jai Ma Graphics mobile verified by Google Maps + its website.
  - Lead score 0–100 (rule-based): valid mobile 20 / other phone 12, valid email 15, owner 20, person with direct contact/LinkedIn 10,
    website 10, socials 5, rating ≥4 & ≥20 reviews 10 (any rating 4), anything verified 10. Recomputed on every save; existing 20 backfilled.
    UI: Score column, "Best leads first" sort, Verified tags in the drawer; Excel: Lead score + Verified items, People sheet "Confirmed by sites".
  - Bug found live and fixed: website name extraction joined text across lines ("Contact Us" + "H. Badri – Founder" -> "Us H. Badri");
    names can no longer cross a line break, may start with an initial, and any menu word rejects the candidate.
- Next: owner connects a dedicated LinkedIn account and runs a session (verify extraction); OpenAI key for page extraction + native language.

## 12. Decisions Log
- 2026-10-01: Requirements answers received; stack proposal drafted (section 7).
- 2026-10-01: Pipeline order fixed: Maps -> OpenAI intelligence -> Google Search -> visit all URLs. Real figures only (cited, never estimated). No proxies. India first. No billing for now.
- 2026-10-01: OpenAI (Stage 2) limited to 4 fields: Team Size, Turnover, Owner Name, Core Member Names.
- 2026-10-01: Language: **English search is mandatory for every aspect**; native-language search is a secondary pass (optional per job). Native keywords translated via OpenAI.
- 2026-10-01: Stage 4 extraction is **rule-based only, no OpenAI**:
  - Google results page (SERP) itself: structured – parse title, URL, snippet.
  - Known sites: dedicated parsers – IndiaMART, magicpin, Justdial, LinkedIn/Facebook/Instagram/YouTube public pages, Zauba/Tofler-type company sites.
  - Unknown company websites: regex for phones/emails, `tel:`/`mailto:` links, social-profile links, schema.org JSON-LD, crawl Contact/About/Team pages.
  - Products / decision-maker contacts on unknown websites = best effort (keyword heuristics); OpenAI page reading can be added later as an optional toggle if coverage is too low.
- 2026-10-01: Stack (section 7) approved in principle; owner has doubts to discuss.
- 2026-10-01: OpenAI built-in web search **rejected**.
- 2026-10-01: POC target: **Indore, India – "Printing Companies"**.
- 2026-10-03: Stage 2 approach approved: own targeted searches -> rules -> OpenAI text extraction (4 fields) -> quote verification.
- 2026-10-03: Google Maps: discard "Permanently closed" listings.
- 2026-10-03: Tech stack (section 7) **confirmed** by owner. Git repo initialised; owner allows pushing to a remote.
- 2026-10-03: SQL Server Express -> page content kept out of DB.
- 2026-10-03: Search input fully user-driven via Search Console (section 5a). OpenAI uses extended with **ICP lead scoring (per session)** and **related-keyword suggestions**. Top-N cities come from GeoNames population, not AI.
- 2026-10-03: **ICP scoring deferred** – not built for now (ICP prompt field + `SearchLeadScores` table stay in schema for later; batching 10–20 leads per OpenAI call when enabled).
- 2026-10-03: Git remote: https://github.com/bhumika-04/GoogleLeadScrapper (branch `main`).
- 2026-10-03: Database = `LeadScrapper` on owner's SQL Server (remote). Connection key `ConnectionStrings:DefaultConnection`, stored in .NET user secrets (id `deeplead`) – never committed.
- 2026-10-03: **Hybrid page handling approved** (replaces "Stage 4 rule-based only" and "store raw HTML"): rules first -> OpenAI JSON extraction only for relevant pages rules couldn't fully read -> JSON in DB -> cleaned text gzip on disk for 30 days, then deleted.
- 2026-10-03: Exports: Excel + CSV only (no Google Sheets / no Google API).
- 2026-10-03: "Temporarily closed" Maps listings kept with a status flag.
- 2026-10-03: v1 feature groups: **A** (data quality), **B** (India free intelligence), **C** (buying signals + lead score), **D** (efficiency). E and F later (except basic per-aspect export and minimal source-health logging).
