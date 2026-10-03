/** API timestamps are UTC without an offset (SQL DATETIME2); show them in the viewer's local time. */
export function formatUtc(value: string | null | undefined): string {
  if (!value) return "—";
  const hasZone = /[zZ]|[+-]\d{2}:?\d{2}$/.test(value);
  return new Date(hasZone ? value : `${value}Z`).toLocaleString();
}

/** Splits "a, b,c" into trimmed, de-duplicated (case-insensitive) values. */
export function splitList(text: string): string[] {
  const seen = new Set<string>();
  const out: string[] = [];
  for (const raw of text.split(/[,\n]/)) {
    const v = raw.trim();
    if (v && !seen.has(v.toLowerCase())) {
      seen.add(v.toLowerCase());
      out.push(v);
    }
  }
  return out;
}
