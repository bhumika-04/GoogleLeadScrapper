"use client";

import { useEffect, useState } from "react";
import Link from "next/link";
import { useRouter } from "next/navigation";
import { api } from "@/lib/api";
import { formatUtc } from "@/lib/format";
import type { SearchSummary } from "@/lib/types";
import { ProgressBar, StatusBadge } from "@/components/StatusBadge";

export default function SessionsPage() {
  const router = useRouter();
  const [sessions, setSessions] = useState<SearchSummary[] | null>(null);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    let active = true;
    const load = () =>
      api.searches()
        .then((s) => active && setSessions(s))
        .catch((e) => active && setError(e.message));
    load();
    const timer = setInterval(load, 5000);   // keeps running sessions' progress fresh
    return () => {
      active = false;
      clearInterval(timer);
    };
  }, []);

  return (
    <div>
      <div className="mb-5 flex items-center justify-between">
        <div>
          <h1 className="text-xl font-semibold">Sessions</h1>
          <p className="text-sm text-muted">Each session groups one run&apos;s cities × keywords and its leads.</p>
        </div>
        <Link href="/searches/new" className="btn-primary">+ New search</Link>
      </div>

      {error && <p className="mb-4 rounded-lg border border-bad/30 bg-bad/10 px-3 py-2 text-sm text-bad">{error}</p>}

      <div className="card overflow-x-auto">
        <table className="w-full min-w-[760px] text-sm">
          <thead>
            <tr className="border-b border-line text-left text-xs uppercase tracking-wide text-muted">
              <th className="px-4 py-3 font-medium">Session</th>
              <th className="px-4 py-3 font-medium">Market</th>
              <th className="px-4 py-3 font-medium">Status</th>
              <th className="w-56 px-4 py-3 font-medium">Progress</th>
              <th className="px-4 py-3 text-right font-medium">Leads</th>
              <th className="px-4 py-3 text-right font-medium">Created</th>
            </tr>
          </thead>
          <tbody>
            {sessions === null && !error && (
              <tr><td colSpan={6} className="px-4 py-10 text-center text-muted">Loading…</td></tr>
            )}
            {sessions?.length === 0 && (
              <tr>
                <td colSpan={6} className="px-4 py-12 text-center text-muted">
                  No sessions yet. <Link href="/searches/new" className="text-accent hover:underline">Start your first search</Link>.
                </td>
              </tr>
            )}
            {sessions?.map((s) => (
              <tr key={s.id} onClick={() => router.push(`/searches/${s.id}`)}
                className="cursor-pointer border-b border-line/60 transition last:border-0 hover:bg-panel-2">
                <td className="px-4 py-3">
                  <div className="font-medium text-ink">{s.name}</div>
                  <div className="text-xs text-muted">{s.keywordCount} keyword{s.keywordCount === 1 ? "" : "s"} · {s.cityCount} cit{s.cityCount === 1 ? "y" : "ies"}</div>
                </td>
                <td className="px-4 py-3 text-ink-dim">{s.countryName}</td>
                <td className="px-4 py-3"><StatusBadge status={s.status} /></td>
                <td className="px-4 py-3">
                  <ProgressBar value={s.aspectsCompleted} max={s.aspectCount} />
                  <div className="mt-1 text-xs text-muted">{s.aspectsCompleted} / {s.aspectCount} combinations</div>
                </td>
                <td className="px-4 py-3 text-right font-semibold tabular-nums">{s.leadCount.toLocaleString()}</td>
                <td className="px-4 py-3 text-right text-xs text-muted">{formatUtc(s.createdAt)}</td>
              </tr>
            ))}
          </tbody>
        </table>
      </div>
    </div>
  );
}
