/** Lead score 0–100: completeness + validity + verification (see LeadScore in the backend). */
export function ScoreBadge({ score }: { score: number | null }) {
  if (score === null) return <span className="text-xs text-muted">—</span>;
  const style = score >= 70 ? "border-ok/40 bg-ok/10 text-ok" : score >= 40 ? "border-warn/40 bg-warn/10 text-warn" : "border-line bg-panel-2 text-ink-dim";
  return (
    <span title="Lead score: valid phone/email, owner found, website, socials, rating, verified data"
      className={`inline-flex min-w-9 justify-center rounded-md border px-1.5 py-0.5 text-xs font-bold tabular-nums ${style}`}>
      {score}
    </span>
  );
}

/** "Verified" = the same value was shown by 2+ different sites. */
export function VerifiedTag({ count, domains }: { count: number; domains?: string | null }) {
  if (count < 2) return null;
  return (
    <span title={domains ? `Seen on: ${domains.split(",").join(", ")}` : undefined}
      className="rounded bg-ok/15 px-1.5 py-0.5 text-[11px] font-semibold text-ok">
      ✓ Verified · {count} sites
    </span>
  );
}
