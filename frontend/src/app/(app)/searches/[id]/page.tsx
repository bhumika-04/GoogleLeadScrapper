"use client";

import { useCallback, useEffect, useState } from "react";
import Link from "next/link";
import { useParams } from "next/navigation";
import { api } from "@/lib/api";
import { formatUtc } from "@/lib/format";
import type { Aspect, Lead, Paged, SearchDetail } from "@/lib/types";
import { ProgressBar, StatusBadge } from "@/components/StatusBadge";
import { LeadsTable } from "@/components/LeadsTable";
import { CompanyDrawer } from "@/components/CompanyDrawer";

const PAGE_SIZE = 50;
const LIVE_STATUSES = new Set(["Pending", "Running"]);

export default function SessionPage() {
  const { id: idParam } = useParams<{ id: string }>();
  const id = Number(idParam);

  const [detail, setDetail] = useState<SearchDetail | null>(null);
  const [leads, setLeads] = useState<Paged<Lead> | null>(null);
  const [aspectId, setAspectId] = useState<number | null>(null);
  const [query, setQuery] = useState("");
  const [debouncedQuery, setDebouncedQuery] = useState("");
  const [page, setPage] = useState(1);
  const [sort, setSort] = useState("");
  const [error, setError] = useState<string | null>(null);
  const [busy, setBusy] = useState<string | null>(null);
  const [openCompany, setOpenCompany] = useState<number | null>(null);

  const loadDetail = useCallback(() => api.search(id).then(setDetail).catch((e) => setError(e.message)), [id]);
  const loadLeads = useCallback(
    () => api.leads(id, { aspectId, q: debouncedQuery, page, pageSize: PAGE_SIZE, sort }).then(setLeads).catch((e) => setError(e.message)),
    [id, aspectId, debouncedQuery, page, sort],
  );

  useEffect(() => { loadDetail(); }, [loadDetail]);
  useEffect(() => { loadLeads(); }, [loadLeads]);

  useEffect(() => {
    const t = setTimeout(() => { setDebouncedQuery(query.trim()); setPage(1); }, 300);
    return () => clearTimeout(t);
  }, [query]);

  // Live refresh while the worker is on it.
  const live = detail ? LIVE_STATUSES.has(detail.summary.status) : false;
  useEffect(() => {
    if (!live) return;
    const t = setInterval(() => { loadDetail(); loadLeads(); }, 4000);
    return () => clearInterval(t);
  }, [live, loadDetail, loadLeads]);

  async function act(action: "pause" | "resume" | "cancel") {
    if (action === "cancel" && !confirm("Cancel this session? Leads found so far are kept.")) return;
    setBusy(action);
    try {
      await api[action](id);
      await loadDetail();
    } catch (e) {
      setError(e instanceof Error ? e.message : "Action failed");
    } finally {
      setBusy(null);
    }
  }

  async function download(format: "excel" | "csv") {
    setBusy(format);
    try {
      await api.download(id, format, aspectId);
    } catch (e) {
      setError(e instanceof Error ? e.message : "Export failed");
    } finally {
      setBusy(null);
    }
  }

  if (!detail) {
    return <div className="py-20 text-center text-sm text-muted">{error ?? "Loading…"}</div>;
  }

  const s = detail.summary;
  const selectedAspect = detail.aspects.find((a) => a.id === aspectId) ?? null;
  const running = detail.aspects.find((a) => a.status === "Running");

  return (
    <div className="space-y-5">
      <div className="flex flex-wrap items-start gap-4">
        <div className="min-w-0 flex-1">
          <Link href="/searches" className="text-xs text-muted hover:text-ink">← Sessions</Link>
          <div className="mt-1 flex flex-wrap items-center gap-3">
            <h1 className="truncate text-xl font-semibold">{s.name}</h1>
            <StatusBadge status={s.status} />
          </div>
          <p className="mt-1 text-sm text-muted">
            {s.countryName} · {s.cityCount} cit{s.cityCount === 1 ? "y" : "ies"} × {s.keywordCount} keyword{s.keywordCount === 1 ? "" : "s"} · created {formatUtc(s.createdAt)}
          </p>
        </div>
        <div className="flex flex-wrap gap-2">
          {(s.status === "Pending" || s.status === "Running") && (
            <button className="btn-ghost" disabled={!!busy} onClick={() => act("pause")}>Pause</button>
          )}
          {(s.status === "Paused" || s.status === "Failed") && (
            <button className="btn-primary" disabled={!!busy} onClick={() => act("resume")}>Resume</button>
          )}
          {["Pending", "Running", "Paused"].includes(s.status) && (
            <button className="btn-danger" disabled={!!busy} onClick={() => act("cancel")}>Cancel</button>
          )}
          <button className="btn-ghost" disabled={!!busy || s.leadCount === 0} onClick={() => download("excel")}>
            {busy === "excel" ? "Preparing…" : "Export Excel"}
          </button>
          <button className="btn-ghost" disabled={!!busy || s.leadCount === 0} onClick={() => download("csv")}>
            {busy === "csv" ? "Preparing…" : "CSV"}
          </button>
        </div>
      </div>

      {error && (
        <p className="rounded-lg border border-bad/30 bg-bad/10 px-3 py-2 text-sm text-bad">
          {error} <button className="ml-2 underline" onClick={() => setError(null)}>dismiss</button>
        </p>
      )}

      <div className="grid gap-4 sm:grid-cols-3">
        <Stat label="Combinations done" value={`${s.aspectsCompleted} / ${s.aspectCount}`}>
          <ProgressBar value={s.aspectsCompleted} max={s.aspectCount} />
        </Stat>
        <Stat label="Unique leads" value={s.leadCount.toLocaleString()} />
        <Stat label="Now running" value={running ? `${running.keyword} — ${running.city}` : s.status === "Pending" ? "Waiting for worker…" : "—"}
          small={running ? (running.peopleStatus === "Running"
            ? `Finding owners & teams: ${running.peopleDone} / ${running.peopleTotal ?? "?"} companies`
            : `Google Maps: ${running.itemsDone} places saved so far`) : undefined} />
      </div>

      <div className="grid gap-4 lg:grid-cols-[340px_minmax(0,1fr)]">
        <AspectList aspects={detail.aspects} selectedId={aspectId}
          onSelect={(a) => { setAspectId(a); setPage(1); }} total={s.leadCount} />

        <div className="card min-w-0">
          <div className="flex flex-wrap items-center gap-3 border-b border-line px-4 py-3">
            <h2 className="text-sm font-semibold">
              {selectedAspect ? `${selectedAspect.keyword} — ${selectedAspect.city}` : "All leads"}
              {leads && <span className="ml-2 font-normal text-muted">{leads.total.toLocaleString()}</span>}
            </h2>
            <select className="field ml-auto w-44 py-1.5 text-sm" value={sort} onChange={(e) => { setSort(e.target.value); setPage(1); }} aria-label="Sort leads">
              <option value="">Maps order</option>
              <option value="score">Best leads first</option>
              <option value="reviews">Most reviews first</option>
            </select>
            <input className="field w-64 py-1.5 text-sm" placeholder="Filter by name, category, address…"
              value={query} onChange={(e) => setQuery(e.target.value)} />
          </div>
          <LeadsTable leads={leads?.items ?? null} showAspect={!selectedAspect} onOpen={(l) => setOpenCompany(l.companyId)} />
          {leads && leads.total > PAGE_SIZE && (
            <div className="flex items-center justify-between border-t border-line px-4 py-2.5 text-sm text-muted">
              <span>{(page - 1) * PAGE_SIZE + 1}–{Math.min(page * PAGE_SIZE, leads.total)} of {leads.total.toLocaleString()}</span>
              <div className="flex gap-2">
                <button className="btn-ghost px-3 py-1" disabled={page === 1} onClick={() => setPage((p) => p - 1)}>Previous</button>
                <button className="btn-ghost px-3 py-1" disabled={page * PAGE_SIZE >= leads.total} onClick={() => setPage((p) => p + 1)}>Next</button>
              </div>
            </div>
          )}
        </div>
      </div>

      {openCompany !== null && <CompanyDrawer searchId={id} companyId={openCompany} onClose={() => setOpenCompany(null)} />}
    </div>
  );
}

function Stat({ label, value, small, children }: { label: string; value: string; small?: string; children?: React.ReactNode }) {
  return (
    <div className="card p-4">
      <div className="text-xs uppercase tracking-wide text-muted">{label}</div>
      <div className="mt-1 truncate text-lg font-semibold">{value}</div>
      {small && <div className="text-xs text-muted">{small}</div>}
      {children && <div className="mt-2">{children}</div>}
    </div>
  );
}

const dot: Record<string, string> = {
  Pending: "bg-line-strong",
  Running: "bg-accent animate-pulse",
  Paused: "bg-warn",
  Completed: "bg-ok",
  Failed: "bg-bad",
  Cancelled: "bg-muted",
};

function AspectList({ aspects, selectedId, onSelect, total }: {
  aspects: Aspect[]; selectedId: number | null; onSelect: (id: number | null) => void; total: number;
}) {
  return (
    <div className="card flex max-h-[70vh] flex-col">
      <div className="border-b border-line px-4 py-3 text-sm font-semibold">Combinations <span className="font-normal text-muted">{aspects.length}</span></div>
      <ul className="scroll-thin flex-1 overflow-auto p-1.5">
        <li>
          <button onClick={() => onSelect(null)}
            className={`flex w-full items-center justify-between rounded-md px-3 py-2 text-left text-sm ${selectedId === null ? "bg-chip" : "hover:bg-panel-2"}`}>
            <span className="font-medium">All leads</span>
            <span className="tabular-nums text-muted">{total}</span>
          </button>
        </li>
        {aspects.map((a) => (
          <li key={a.id}>
            <button onClick={() => onSelect(a.id)} title={a.lastError ?? undefined}
              className={`flex w-full items-center gap-2.5 rounded-md px-3 py-2 text-left text-sm ${selectedId === a.id ? "bg-chip" : "hover:bg-panel-2"}`}>
              <span className={`h-2 w-2 shrink-0 rounded-full ${dot[a.status] ?? "bg-line-strong"}`} />
              <span className="min-w-0 flex-1">
                <span className="block truncate">{a.sequence}. {a.keyword} — {a.city}</span>
                {(a.status === "Running" || a.peopleStatus === "Running") && (
                  <span className="block text-xs text-muted">
                    {a.peopleStatus === "Running" ? `People ${a.peopleDone}/${a.peopleTotal ?? "?"}` : `Maps ${a.itemsDone} found`}
                  </span>
                )}
                {a.lastError && <span className="block truncate text-xs text-warn">{a.lastError}</span>}
              </span>
              <span className="tabular-nums text-muted">{a.leadCount}</span>
            </button>
          </li>
        ))}
      </ul>
    </div>
  );
}
