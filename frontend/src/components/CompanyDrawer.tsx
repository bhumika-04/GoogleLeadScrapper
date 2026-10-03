"use client";

import { useEffect, useState } from "react";
import { api } from "@/lib/api";
import { formatUtc } from "@/lib/format";
import type { CompanyDetail, Person } from "@/lib/types";

const FACT_LABELS: Record<string, string> = {
  OwnerName: "Owner",
  TeamSize: "Team size",
  Turnover: "Annual turnover",
  Gstin: "GSTIN",
  LegalStatus: "Legal status",
  YearEstablished: "Established",
  NatureOfBusiness: "Nature of business",
};

const host = (url: string) => {
  try {
    const u = new URL(url);
    if (u.hostname.includes("google.") && u.pathname.startsWith("/maps")) return "Google Maps";
    return u.hostname.replace(/^www\./, "");
  } catch {
    return url;
  }
};

export function CompanyDrawer({ searchId, companyId, onClose }: { searchId: number; companyId: number; onClose: () => void }) {
  const [company, setCompany] = useState<CompanyDetail | null>(null);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    let active = true;
    api.company(searchId, companyId)
      .then((c) => active && setCompany(c))
      .catch((e) => active && setError(e.message));
    return () => { active = false; };
  }, [searchId, companyId]);

  useEffect(() => {
    const onKey = (e: KeyboardEvent) => e.key === "Escape" && onClose();
    window.addEventListener("keydown", onKey);
    return () => window.removeEventListener("keydown", onKey);
  }, [onClose]);

  return (
    <div className="fixed inset-0 z-40 flex justify-end bg-black/50" onClick={onClose}>
      <aside className="scroll-thin h-full w-full max-w-xl overflow-y-auto border-l border-line bg-panel shadow-2xl" onClick={(e) => e.stopPropagation()}>
        <div className="sticky top-0 z-10 flex items-start gap-3 border-b border-line bg-panel px-5 py-4">
          <div className="min-w-0 flex-1">
            <h2 className="text-lg font-semibold leading-snug">{company?.name ?? "Loading…"}</h2>
            {company && <p className="text-xs text-muted">{company.category ?? "—"}{company.address && ` · ${company.address}`}</p>}
          </div>
          <button onClick={onClose} className="btn-ghost px-2.5 py-1" aria-label="Close">✕</button>
        </div>

        {error && <p className="m-5 rounded-lg border border-bad/30 bg-bad/10 px-3 py-2 text-sm text-bad">{error}</p>}

        {company && (
          <div className="space-y-6 p-5">
            <div className="flex flex-wrap gap-2 text-xs">
              {company.website && <a className="pill" href={company.website} target="_blank" rel="noreferrer">{host(company.website)} ↗</a>}
              {company.mapsUrl && <a className="pill" href={company.mapsUrl} target="_blank" rel="noreferrer">Google Maps ↗</a>}
              {company.rating != null && <span className="pill">★ {company.rating.toFixed(1)} ({company.reviewCount ?? 0})</span>}
              {company.socials.map((s) => (
                <a key={s.url} className="pill" href={s.url} target="_blank" rel="noreferrer">{s.platform} ↗</a>
              ))}
            </div>

            <Section title={`Owner & core team (${company.people.length})`}>
              {company.people.length === 0 ? (
                <Empty text={company.peopleEnrichedAt ? "No people found in public sources."
                  : company.lastEnrichedAt ? "Partially researched: website / IndiaMART were checked, but web search was rate-limited. No people found yet."
                  : "People research hasn't run for this company yet."} />
              ) : (
                <ul className="space-y-2.5">{company.people.map((p) => <PersonCard key={p.id} person={p} />)}</ul>
              )}
            </Section>

            <Section title="Phones & emails">
              {company.channels.length === 0 ? <Empty text="None found." /> : (
                <ul className="divide-y divide-line/60 rounded-lg border border-line">
                  {company.channels.map((c) => (
                    <li key={c.channelType + c.normalizedValue} className="flex flex-wrap items-center gap-x-3 gap-y-1 px-3 py-2 text-sm">
                      <span className="w-12 text-xs text-muted">{c.channelType}</span>
                      <a href={c.channelType === "Phone" ? `tel:${c.normalizedValue}` : `mailto:${c.normalizedValue}`} className="font-medium tabular-nums hover:text-accent">
                        {c.normalizedValue}
                      </a>
                      {c.phoneKind && <span className="rounded bg-panel-2 px-1.5 py-0.5 text-[11px] text-ink-dim">{c.phoneKind}</span>}
                      {c.isValid === true && <span className="text-[11px] text-ok">✓ valid</span>}
                      {c.isValid === false && <span className="text-[11px] text-bad">✕ {c.validationNote ?? "invalid"}</span>}
                      {c.sourceUrl && <a href={c.sourceUrl} target="_blank" rel="noreferrer" className="ml-auto text-[11px] text-muted hover:text-accent">{host(c.sourceUrl)}</a>}
                    </li>
                  ))}
                </ul>
              )}
            </Section>

            <Section title="Company facts (sourced)">
              {company.facts.length === 0 ? <Empty text="No sourced facts yet." /> : (
                <dl className="divide-y divide-line/60 rounded-lg border border-line">
                  {company.facts.map((f, i) => (
                    <div key={i} className="grid grid-cols-[130px_1fr] gap-3 px-3 py-2 text-sm">
                      <dt className="text-muted">{FACT_LABELS[f.fieldName] ?? f.fieldName}</dt>
                      <dd>
                        <div className="font-medium">{f.value}</div>
                        <a href={f.sourceUrl} target="_blank" rel="noreferrer" className="text-[11px] text-muted hover:text-accent">
                          {host(f.sourceUrl)} · {formatUtc(f.foundAt)}
                        </a>
                      </dd>
                    </div>
                  ))}
                </dl>
              )}
            </Section>
          </div>
        )}
      </aside>
    </div>
  );
}

function PersonCard({ person: p }: { person: Person }) {
  return (
    <li className="rounded-lg border border-line bg-panel-2/60 p-3">
      <div className="flex flex-wrap items-center gap-2">
        <span className="font-semibold">{p.fullName}</span>
        {p.isOwner && <span className="rounded-full bg-violet/20 px-2 py-0.5 text-[11px] font-semibold text-violet">Owner</span>}
        {!p.isOwner && p.isDecisionMaker && <span className="rounded-full bg-accent/15 px-2 py-0.5 text-[11px] font-semibold text-accent">Decision maker</span>}
      </div>
      {p.designation && <div className="text-sm text-ink-dim">{p.designation}</div>}
      <div className="mt-1.5 flex flex-wrap gap-x-3 gap-y-1 text-xs">
        {p.phone && <a href={`tel:${p.phone}`} className="text-ink-dim hover:text-accent">{p.phone}</a>}
        {p.email && <a href={`mailto:${p.email}`} className="text-ink-dim hover:text-accent">{p.email}</a>}
        {p.linkedInUrl && <a href={p.linkedInUrl} target="_blank" rel="noreferrer" className="text-accent hover:underline">LinkedIn ↗</a>}
        {p.facebookUrl && <a href={p.facebookUrl} target="_blank" rel="noreferrer" className="text-accent hover:underline">Facebook ↗</a>}
        {p.instagramUrl && <a href={p.instagramUrl} target="_blank" rel="noreferrer" className="text-accent hover:underline">Instagram ↗</a>}
      </div>
      <a href={p.sourceUrl} target="_blank" rel="noreferrer" className="mt-1 block text-[11px] text-muted hover:text-accent">
        via {p.source ?? "source"} · {host(p.sourceUrl)}
      </a>
    </li>
  );
}

function Section({ title, children }: { title: string; children: React.ReactNode }) {
  return (
    <section>
      <h3 className="mb-2 text-xs font-semibold uppercase tracking-wide text-muted">{title}</h3>
      {children}
    </section>
  );
}

function Empty({ text }: { text: string }) {
  return <p className="rounded-lg border border-dashed border-line px-3 py-3 text-sm text-muted">{text}</p>;
}
