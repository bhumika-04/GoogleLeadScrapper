"use client";

import { useEffect, useMemo, useState } from "react";
import { useRouter } from "next/navigation";
import { api } from "@/lib/api";
import { splitList } from "@/lib/format";
import type { Country } from "@/lib/types";
import { CityPicker, type CityChip } from "@/components/CityPicker";
import { Logo } from "@/components/Logo";

const PREVIEW_LIMIT = 300;

export default function SearchConsolePage() {
  const router = useRouter();
  const [countries, setCountries] = useState<Country[]>([]);
  const [country, setCountry] = useState("IN");
  const [name, setName] = useState("");
  const [cities, setCities] = useState<CityChip[]>([]);
  const [keywordText, setKeywordText] = useState("");
  const [icp, setIcp] = useState("");
  const [icpOpen, setIcpOpen] = useState(false);
  const [submitting, setSubmitting] = useState(false);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    api.countries().then(setCountries).catch((e) => setError(e.message));
  }, []);

  const keywords = useMemo(() => splitList(keywordText), [keywordText]);
  const countryName = countries.find((c) => c.iso2 === country)?.name ?? country;

  // City-major order, same as the worker runs them: 1. K1 — C1, 2. K2 — C1, ...
  const combinations = useMemo(
    () => cities.flatMap((c) => keywords.map((k) => `${k} — ${c.name}`)),
    [cities, keywords],
  );

  function changeCountry(iso2: string) {
    if (iso2 === country) return;
    if (cities.length && !confirm("Changing the market clears the selected cities. Continue?")) return;
    setCountry(iso2);
    setCities([]);
  }

  async function submit() {
    setSubmitting(true);
    setError(null);
    try {
      const created = await api.createSearch({
        name: name.trim() || null,
        countryIso2: country,
        cityIds: cities.filter((c) => c.id !== null).map((c) => c.id!),
        newCityNames: cities.filter((c) => c.id === null).map((c) => c.name),
        keywords,
        icpPrompt: icp.trim() || null,
      });
      router.push(`/searches/${created.summary.id}`);
    } catch (e) {
      setError(e instanceof Error ? e.message : "Could not start the search");
      setSubmitting(false);
    }
  }

  const canSubmit = cities.length > 0 && keywords.length > 0 && !submitting;

  return (
    <div className="card mx-auto max-w-[1400px] overflow-hidden">
      <div className="h-0.5 bg-gradient-to-r from-accent via-violet to-accent" />
      <div className="space-y-6 p-5 sm:p-7">
        <div className="flex items-center gap-3">
          <Logo />
          <div>
            <h1 className="font-semibold">Search Console</h1>
            <p className="text-xs text-muted">Capture → validate → research, in one run</p>
          </div>
        </div>

        <div>
          <label className="label" htmlFor="session-name">Session name <span className="hint">— groups this run&apos;s leads, validation &amp; research in one workspace</span></label>
          <input id="session-name" className="field" value={name} onChange={(e) => setName(e.target.value)} maxLength={200}
            placeholder="e.g. Printing — Gujarat  ·  Pesticides — Punjab   (blank = auto-named)" />
        </div>

        <div>
          <label className="label" htmlFor="market">Market</label>
          <select id="market" className="field w-56 cursor-pointer" value={country} onChange={(e) => changeCountry(e.target.value)}>
            {countries.map((c) => (
              <option key={c.iso2} value={c.iso2}>{c.name} ({c.iso2})</option>
            ))}
          </select>
        </div>

        <CityPicker countryIso2={country} countryName={countryName} value={cities} onChange={setCities} />

        <div>
          <label className="label" htmlFor="keywords">Keyword / Business Category <span className="hint">— comma separated</span></label>
          <input id="keywords" className="field" value={keywordText} onChange={(e) => setKeywordText(e.target.value)}
            placeholder="Paper Trader, Paper Stockists, Paper Converters, Printing Companies" />
          {keywords.length > 0 && (
            <div className="mt-2 flex flex-wrap gap-1.5">
              {keywords.map((k) => (
                <span key={k} className="rounded-md border border-line bg-panel-2 px-2 py-0.5 text-xs text-ink-dim">{k}</span>
              ))}
            </div>
          )}
        </div>

        <div className="rounded-xl border border-line bg-panel-2/60">
          <button type="button" onClick={() => setIcpOpen((o) => !o)}
            className="flex w-full items-center gap-3 px-4 py-3 text-left">
            <span className="text-sm font-semibold text-violet">ICP for this session</span>
            {!icpOpen && <span className="truncate text-xs text-muted">{icp || "Describe your ideal customer (saved with this session; AI scoring comes in a later phase)"}</span>}
            <span className="ml-auto shrink-0 text-xs text-muted">{icpOpen ? "▴ collapse" : "▾ review / edit"}</span>
          </button>
          {icpOpen && (
            <div className="px-4 pb-4">
              <textarea className="field min-h-40 resize-y font-normal leading-relaxed" value={icp} onChange={(e) => setIcp(e.target.value)}
                placeholder={"You are a B2B lead qualification scorer for …\n\nMY BUSINESS:\n…\n\nReject (score 0) ONLY if …"} />
              <p className="mt-1.5 text-xs text-muted">Saved on this session only — each run keeps its own ICP. Lead scoring with it is not enabled yet.</p>
            </div>
          )}
        </div>

        <div className="rounded-xl border border-line bg-bg/50 p-4">
          {combinations.length === 0 ? (
            <p className="text-sm text-muted">Add at least one city and one keyword to see the run plan.</p>
          ) : (
            <>
              <p className="mb-3 text-sm font-semibold text-accent">
                Will search {combinations.length.toLocaleString()} combination{combinations.length === 1 ? "" : "s"} sequentially
                <span className="font-normal text-muted"> ({cities.length} cit{cities.length === 1 ? "y" : "ies"} × {keywords.length} keyword{keywords.length === 1 ? "" : "s"})</span>:
              </p>
              <div className="scroll-thin flex max-h-64 flex-wrap gap-1.5 overflow-auto">
                {combinations.slice(0, PREVIEW_LIMIT).map((c, i) => (
                  <span key={i} className="rounded-md border border-chip-line/70 bg-chip/70 px-2 py-0.5 text-xs text-ink-dim">{i + 1}. {c}</span>
                ))}
                {combinations.length > PREVIEW_LIMIT && (
                  <span className="px-2 py-0.5 text-xs text-muted">+ {(combinations.length - PREVIEW_LIMIT).toLocaleString()} more</span>
                )}
              </div>
            </>
          )}
        </div>

        {error && <p className="rounded-lg border border-bad/30 bg-bad/10 px-3 py-2 text-sm text-bad">{error}</p>}

        <div className="flex items-center justify-end gap-3 border-t border-line pt-5">
          <span className="mr-auto text-xs text-muted">Stage 1 (Google Maps) runs now; permanently closed places are skipped.</span>
          <button type="button" className="btn-primary px-6 py-2.5" disabled={!canSubmit} onClick={submit}>
            {submitting ? "Starting…" : "Start search"}
          </button>
        </div>
      </div>
    </div>
  );
}
