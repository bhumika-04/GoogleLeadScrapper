"use client";

import { useEffect, useRef, useState } from "react";
import { api } from "@/lib/api";
import type { City } from "@/lib/types";

/** A selected city: either a known city (id) or a name the user typed that isn't in our list yet. */
export type CityChip = { key: string; name: string; region: string | null; id: number | null };

type Props = {
  countryIso2: string;
  countryName: string;
  value: CityChip[];
  onChange: (cities: CityChip[]) => void;
};

const TOP_OPTIONS = [50, 100, 200, 500];

export function CityPicker({ countryIso2, countryName, value, onChange }: Props) {
  const [query, setQuery] = useState("");
  const [suggestions, setSuggestions] = useState<City[]>([]);
  const [highlight, setHighlight] = useState(0);
  const [open, setOpen] = useState(false);
  const [loadingTop, setLoadingTop] = useState<number | null>(null);
  const inputRef = useRef<HTMLInputElement>(null);
  const boxRef = useRef<HTMLDivElement>(null);

  const selectedIds = new Set(value.filter((c) => c.id !== null).map((c) => c.id));
  const selectedNames = new Set(value.map((c) => c.name.toLowerCase()));

  // Debounced type-ahead.
  useEffect(() => {
    if (!open) return;
    const handle = setTimeout(() => {
      api.cities(countryIso2, query.trim(), 12)
        .then((cities) => {
          setSuggestions(cities.filter((c) => !selectedIds.has(c.id)));
          setHighlight(0);
        })
        .catch(() => setSuggestions([]));
    }, 180);
    return () => clearTimeout(handle);
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [query, countryIso2, open, value.length]);

  // Close the dropdown on outside click.
  useEffect(() => {
    const onDown = (e: MouseEvent) => {
      if (boxRef.current && !boxRef.current.contains(e.target as Node)) setOpen(false);
    };
    document.addEventListener("mousedown", onDown);
    return () => document.removeEventListener("mousedown", onDown);
  }, []);

  function addCity(city: City) {
    if (selectedIds.has(city.id)) return;
    onChange([...value, { key: `id:${city.id}`, name: city.name, region: city.region, id: city.id }]);
    setQuery("");
    inputRef.current?.focus();
  }

  function addTyped(name: string) {
    const clean = name.trim();
    if (!clean || selectedNames.has(clean.toLowerCase())) return;
    onChange([...value, { key: `new:${clean.toLowerCase()}`, name: clean, region: null, id: null }]);
    setQuery("");
  }

  function remove(key: string) {
    onChange(value.filter((c) => c.key !== key));
  }

  async function addTop(n: number) {
    setLoadingTop(n);
    try {
      const top = await api.topCities(countryIso2, n);
      const fresh = top.filter((c) => !selectedIds.has(c.id) && !selectedNames.has(c.name.toLowerCase()));
      onChange([...value, ...fresh.map((c) => ({ key: `id:${c.id}`, name: c.name, region: c.region, id: c.id }))]);
    } finally {
      setLoadingTop(null);
    }
  }

  function onKeyDown(e: React.KeyboardEvent<HTMLInputElement>) {
    if (e.key === "ArrowDown") {
      e.preventDefault();
      setHighlight((h) => Math.min(h + 1, suggestions.length - 1));
    } else if (e.key === "ArrowUp") {
      e.preventDefault();
      setHighlight((h) => Math.max(h - 1, 0));
    } else if (e.key === "Enter") {
      e.preventDefault();
      const exact = suggestions.find((s) => s.name.toLowerCase() === query.trim().toLowerCase());
      if (exact) addCity(exact);
      else if (open && suggestions[highlight] && query.trim()) addCity(suggestions[highlight]);
      else addTyped(query);
    } else if (e.key === "Backspace" && query === "" && value.length > 0) {
      remove(value[value.length - 1].key);
    } else if (e.key === "Escape") {
      setOpen(false);
    }
  }

  const typedIsNew = query.trim() && !suggestions.some((s) => s.name.toLowerCase() === query.trim().toLowerCase());

  return (
    <div>
      <div className="mb-1.5 flex items-baseline justify-between">
        <span className="label mb-0">City <span className="hint">— {countryName}</span></span>
        {value.length > 0 && <span className="text-sm text-accent">{value.length} cit{value.length === 1 ? "y" : "ies"} selected</span>}
      </div>

      <div ref={boxRef} className="relative">
        <div
          className="field flex min-h-[52px] cursor-text flex-wrap items-center gap-1.5 px-2.5 py-2"
          onClick={() => { inputRef.current?.focus(); setOpen(true); }}
        >
          {value.map((c) => (
            <span key={c.key}
              title={c.id === null ? "Not in our city list — will be searched as typed" : c.region ?? undefined}
              className={`inline-flex items-center gap-1 rounded-md border px-2 py-0.5 text-sm ${c.id === null
                ? "border-dashed border-violet/60 bg-violet/10 text-ink"
                : "border-chip-line bg-chip text-ink"}`}>
              {c.name}
              <button type="button" aria-label={`Remove ${c.name}`} onClick={(e) => { e.stopPropagation(); remove(c.key); }}
                className="ml-0.5 text-muted hover:text-ink">×</button>
            </span>
          ))}
          <input
            ref={inputRef}
            value={query}
            onChange={(e) => { setQuery(e.target.value); setOpen(true); }}
            onFocus={() => setOpen(true)}
            onKeyDown={onKeyDown}
            placeholder={value.length ? "Add more…" : "Search a city or type any name and press Enter"}
            className="min-w-[180px] flex-1 bg-transparent px-1 py-1 text-[15px] outline-none placeholder:text-muted"
          />
          {value.length > 0 && (
            <button type="button" onClick={(e) => { e.stopPropagation(); onChange([]); }}
              className="ml-auto px-1 text-lg leading-none text-muted hover:text-ink" aria-label="Clear all cities">×</button>
          )}
        </div>

        {open && (suggestions.length > 0 || typedIsNew) && (
          <ul className="scroll-thin absolute z-30 mt-1 max-h-72 w-full overflow-auto rounded-lg border border-line-strong bg-panel-2 py-1 shadow-xl">
            {suggestions.map((c, i) => (
              <li key={c.id}>
                <button type="button" onMouseDown={(e) => e.preventDefault()} onClick={() => addCity(c)}
                  onMouseEnter={() => setHighlight(i)}
                  className={`flex w-full items-center justify-between px-3 py-2 text-left text-sm ${i === highlight ? "bg-chip" : ""}`}>
                  <span>{c.name}{c.region && <span className="text-muted"> · {c.region}</span>}</span>
                  {c.population ? <span className="text-xs tabular-nums text-muted">{c.population.toLocaleString()}</span> : null}
                </button>
              </li>
            ))}
            {typedIsNew && (
              <li>
                <button type="button" onMouseDown={(e) => e.preventDefault()} onClick={() => addTyped(query)}
                  className="w-full px-3 py-2 text-left text-sm text-violet hover:bg-chip">
                  + Add “{query.trim()}” as typed
                </button>
              </li>
            )}
          </ul>
        )}
      </div>

      <p className="mt-1.5 text-xs text-muted">Select from list or type any city name and press Enter. Multiple cities run sequentially.</p>

      <div className="mt-2.5 flex flex-wrap items-center gap-2 text-xs text-muted">
        <span>Quick-add top cities by population:</span>
        {TOP_OPTIONS.map((n) => (
          <button key={n} type="button" className="pill" disabled={loadingTop !== null} onClick={() => addTop(n)}>
            {loadingTop === n ? "Adding…" : `Top ${n}`}
          </button>
        ))}
        <button type="button" className="px-2 py-1 text-sm text-muted hover:text-ink" onClick={() => onChange([])}>Clear</button>
      </div>
    </div>
  );
}
