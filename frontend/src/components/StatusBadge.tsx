import type { SearchStatus } from "@/lib/types";

const styles: Record<SearchStatus, string> = {
  Pending: "border-line-strong bg-panel-2 text-ink-dim",
  Running: "border-accent/40 bg-accent/15 text-accent",
  Paused: "border-warn/40 bg-warn/10 text-warn",
  Blocked: "border-warn/40 bg-warn/10 text-warn",
  Completed: "border-ok/40 bg-ok/10 text-ok",
  Failed: "border-bad/40 bg-bad/10 text-bad",
  Cancelled: "border-line bg-panel-2 text-muted",
};

export function StatusBadge({ status }: { status: SearchStatus }) {
  return (
    <span className={`inline-flex items-center gap-1.5 rounded-full border px-2.5 py-0.5 text-xs font-semibold ${styles[status]}`}>
      {status === "Running" && <span className="h-1.5 w-1.5 animate-pulse rounded-full bg-accent" />}
      {status}
    </span>
  );
}

export function ProgressBar({ value, max }: { value: number; max: number }) {
  const pct = max > 0 ? Math.min(100, Math.round((value / max) * 100)) : 0;
  return (
    <div className="h-1.5 w-full overflow-hidden rounded-full bg-panel-2" role="progressbar" aria-valuenow={pct} aria-valuemin={0} aria-valuemax={100}>
      <div className="h-full rounded-full bg-accent transition-all" style={{ width: `${pct}%` }} />
    </div>
  );
}
