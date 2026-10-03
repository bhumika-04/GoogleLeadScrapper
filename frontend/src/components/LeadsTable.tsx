import type { Lead } from "@/lib/types";

const socialLabel = (url: string) => {
  const u = url.toLowerCase();
  if (u.includes("instagram.")) return "Instagram";
  if (u.includes("facebook.") || u.includes("fb.com")) return "Facebook";
  if (u.includes("linkedin.")) return "LinkedIn";
  if (u.includes("youtube.") || u.includes("youtu.be")) return "YouTube";
  if (u.includes("twitter.") || u.includes("x.com")) return "X";
  return "Profile";
};

const shortUrl = (url: string) => url.replace(/^https?:\/\/(www\.)?/, "").replace(/\/$/, "");

export function LeadsTable({ leads, showAspect }: { leads: Lead[] | null; showAspect: boolean }) {
  if (leads === null) return <div className="px-4 py-12 text-center text-sm text-muted">Loading…</div>;
  if (leads.length === 0) return <div className="px-4 py-12 text-center text-sm text-muted">No leads yet.</div>;

  return (
    <div className="scroll-thin overflow-x-auto">
      <table className="w-full min-w-[1000px] text-sm">
        <thead>
          <tr className="border-b border-line text-left text-xs uppercase tracking-wide text-muted">
            <th className="px-4 py-2.5 font-medium">#</th>
            <th className="px-4 py-2.5 font-medium">Company</th>
            <th className="px-4 py-2.5 font-medium">Phone</th>
            <th className="px-4 py-2.5 font-medium">Website / social</th>
            <th className="px-4 py-2.5 font-medium">Address</th>
            <th className="px-4 py-2.5 text-right font-medium">Rating</th>
            {showAspect && <th className="px-4 py-2.5 font-medium">Found by</th>}
            <th className="px-4 py-2.5 font-medium">Maps</th>
          </tr>
        </thead>
        <tbody>
          {leads.map((l) => (
            <tr key={`${l.aspectId}-${l.companyId}`} className="border-b border-line/50 align-top last:border-0 hover:bg-panel-2/60">
              <td className="px-4 py-2.5 tabular-nums text-muted">{l.mapsRank ?? "—"}</td>
              <td className="max-w-[260px] px-4 py-2.5">
                <div className="font-medium text-ink">{l.name}</div>
                <div className="text-xs text-muted">
                  {l.category ?? "—"}
                  {l.businessStatus === "TemporarilyClosed" && <span className="ml-1.5 text-warn">· Temporarily closed</span>}
                </div>
              </td>
              <td className="whitespace-nowrap px-4 py-2.5 tabular-nums">
                {l.phones ? l.phones.split(", ").map((p) => (
                  <a key={p} href={`tel:${p}`} className="block text-ink-dim hover:text-accent">{p}</a>
                )) : <span className="text-muted">—</span>}
              </td>
              <td className="max-w-[240px] px-4 py-2.5">
                {l.website && (
                  <a href={l.website} target="_blank" rel="noreferrer" className="block truncate text-accent hover:underline">{shortUrl(l.website)}</a>
                )}
                {l.socials?.split(", ").map((s) => (
                  <a key={s} href={s} target="_blank" rel="noreferrer" className="mr-2 text-xs text-violet hover:underline">{socialLabel(s)}</a>
                ))}
                {!l.website && !l.socials && <span className="text-muted">—</span>}
              </td>
              <td className="max-w-[320px] px-4 py-2.5 text-ink-dim">{l.address ?? "—"}</td>
              <td className="whitespace-nowrap px-4 py-2.5 text-right tabular-nums">
                {l.rating != null ? <>★ {l.rating.toFixed(1)} <span className="text-muted">({l.reviewCount ?? 0})</span></> : <span className="text-muted">—</span>}
              </td>
              {showAspect && <td className="whitespace-nowrap px-4 py-2.5 text-xs text-muted">{l.keyword}<br />{l.city}</td>}
              <td className="px-4 py-2.5">
                {l.mapsUrl && <a href={l.mapsUrl} target="_blank" rel="noreferrer" className="text-xs text-accent hover:underline">Open ↗</a>}
              </td>
            </tr>
          ))}
        </tbody>
      </table>
    </div>
  );
}
