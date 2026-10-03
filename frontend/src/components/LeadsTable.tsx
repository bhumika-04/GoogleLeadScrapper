import type { Lead } from "@/lib/types";
import { ScoreBadge } from "@/components/ScoreBadge";

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

type Props = { leads: Lead[] | null; showAspect: boolean; onOpen: (lead: Lead) => void };

export function LeadsTable({ leads, showAspect, onOpen }: Props) {
  if (leads === null) return <div className="px-4 py-12 text-center text-sm text-muted">Loading…</div>;
  if (leads.length === 0) return <div className="px-4 py-12 text-center text-sm text-muted">No leads yet.</div>;

  return (
    <div className="scroll-thin overflow-x-auto">
      <table className="w-full min-w-[1150px] text-sm">
        <thead>
          <tr className="border-b border-line text-left text-xs uppercase tracking-wide text-muted">
            <th className="px-4 py-2.5 font-medium">#</th>
            <th className="px-3 py-2.5 font-medium" title="Lead score 0–100">Score</th>
            <th className="px-4 py-2.5 font-medium">Company</th>
            <th className="px-4 py-2.5 font-medium">Owner &amp; team</th>
            <th className="px-4 py-2.5 font-medium">Phone / email</th>
            <th className="px-4 py-2.5 font-medium">Website / social</th>
            <th className="px-4 py-2.5 font-medium">Address</th>
            <th className="px-4 py-2.5 text-right font-medium">Rating</th>
            {showAspect && <th className="px-4 py-2.5 font-medium">Found by</th>}
          </tr>
        </thead>
        <tbody>
          {leads.map((l) => {
            const team = l.people?.split("; ").filter((p) => !l.ownerName || !p.startsWith(l.ownerName)) ?? [];
            return (
              <tr key={`${l.aspectId}-${l.companyId}`} onClick={() => onOpen(l)}
                className="cursor-pointer border-b border-line/50 align-top last:border-0 hover:bg-panel-2/60">
                <td className="px-4 py-2.5 tabular-nums text-muted">{l.mapsRank ?? "—"}</td>
                <td className="px-3 py-2.5">
                  <ScoreBadge score={l.leadScore} />
                  {l.verifiedCount > 0 && <div className="mt-1 text-[10px] font-semibold text-ok" title="Phones/emails/people confirmed by 2+ sites">✓ {l.verifiedCount}</div>}
                </td>
                <td className="max-w-[240px] px-4 py-2.5">
                  <div className="font-medium text-ink">{l.isNew && <span className="mr-1.5 rounded bg-violet/20 px-1.5 py-0.5 align-middle text-[10px] font-bold text-violet">NEW</span>}{l.name}</div>
                  <div className="text-xs text-muted">
                    {l.category ?? "—"}
                    {l.businessStatus === "TemporarilyClosed" && <span className="ml-1.5 text-warn">· Temporarily closed</span>}
                  </div>
                  {(l.teamSize || l.turnover) && (
                    <div className="mt-0.5 text-[11px] text-ink-dim">{[l.teamSize, l.turnover].filter(Boolean).join(" · ")}</div>
                  )}
                </td>
                <td className="max-w-[240px] px-4 py-2.5">
                  {l.ownerName ? (
                    <div className="font-medium text-violet">{l.ownerName}</div>
                  ) : l.peopleEnrichedAt ? <span className="text-xs text-muted">Not found</span>
                    : l.lastEnrichedAt ? <span className="text-xs text-warn" title="Website/IndiaMART checked; web search was rate-limited">Not found (partial)</span>
                    : <span className="text-xs text-muted">Pending…</span>}
                  {team.length > 0 && <div className="truncate text-xs text-ink-dim" title={team.join("; ")}>{team.slice(0, 2).join("; ")}{team.length > 2 && ` +${team.length - 2}`}</div>}
                </td>
                <td className="whitespace-nowrap px-4 py-2.5 tabular-nums">
                  {l.phones?.split(", ").slice(0, 2).map((p) => (
                    <a key={p} href={`tel:${p}`} onClick={(e) => e.stopPropagation()} className="block text-ink-dim hover:text-accent">{p}</a>
                  ))}
                  {l.emails?.split(", ").slice(0, 1).map((e) => (
                    <a key={e} href={`mailto:${e}`} onClick={(ev) => ev.stopPropagation()} className="block text-xs text-ink-dim hover:text-accent">{e}</a>
                  ))}
                  {!l.phones && !l.emails && <span className="text-muted">—</span>}
                </td>
                <td className="max-w-[220px] px-4 py-2.5">
                  {l.website && (
                    <a href={l.website} target="_blank" rel="noreferrer" onClick={(e) => e.stopPropagation()} className="block truncate text-accent hover:underline">{shortUrl(l.website)}</a>
                  )}
                  {l.socials?.split(", ").map((s) => (
                    <a key={s} href={s} target="_blank" rel="noreferrer" onClick={(e) => e.stopPropagation()} className="mr-2 text-xs text-violet hover:underline">{socialLabel(s)}</a>
                  ))}
                  {!l.website && !l.socials && <span className="text-muted">—</span>}
                </td>
                <td className="max-w-[280px] px-4 py-2.5 text-ink-dim">{l.address ?? "—"}</td>
                <td className="whitespace-nowrap px-4 py-2.5 text-right tabular-nums">
                  {l.rating != null ? <>★ {l.rating.toFixed(1)} <span className="text-muted">({l.reviewCount ?? 0})</span></> : <span className="text-muted">—</span>}
                </td>
                {showAspect && <td className="whitespace-nowrap px-4 py-2.5 text-xs text-muted">{l.keyword}<br />{l.city}</td>}
              </tr>
            );
          })}
        </tbody>
      </table>
    </div>
  );
}
