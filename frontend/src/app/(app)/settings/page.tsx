"use client";

import { useCallback, useEffect, useState } from "react";
import { api } from "@/lib/api";
import { useAuth } from "@/lib/auth";
import { formatUtc } from "@/lib/format";
import type { AccountStatus, ConnectedAccount, Platform } from "@/lib/types";

const PLATFORM_INFO: Record<Platform, { label: string; color: string; use: string; autoDetect: boolean }> = {
  LinkedIn: { label: "LinkedIn", color: "#0a66c2", use: "Owner & core team profiles, designations, company people list", autoDetect: true },
  Facebook: { label: "Facebook", color: "#1877f2", use: "Business page About (phone, email), owner profiles", autoDetect: true },
  Instagram: { label: "Instagram", color: "#d6249f", use: "Business profile bio, contact buttons, linked owner accounts", autoDetect: true },
  IndiaMart: { label: "IndiaMART", color: "#2e3192", use: "Full seller contact numbers visible to logged-in buyers", autoDetect: false },
  Justdial: { label: "Justdial", color: "#ff6f00", use: "Business contact numbers and owner names", autoDetect: false },
};

const STATUS_TEXT: Record<AccountStatus, string> = {
  Disconnected: "Not connected",
  ConnectRequested: "Opening login window…",
  WaitingForLogin: "Waiting for you to log in",
  SaveRequested: "Saving session…",
  Connected: "Connected",
  Expired: "Session expired — reconnect",
  Failed: "Connection failed",
};

const BUSY: AccountStatus[] = ["ConnectRequested", "WaitingForLogin", "SaveRequested"];

export default function SettingsPage() {
  const { user } = useAuth();
  const [accounts, setAccounts] = useState<ConnectedAccount[] | null>(null);
  const [labels, setLabels] = useState<Partial<Record<Platform, string>>>({});
  const [error, setError] = useState<string | null>(null);

  const load = useCallback(() => api.accounts().then(setAccounts).catch((e) => setError(e.message)), []);
  useEffect(() => { load(); }, [load]);

  // Poll quickly while a login window is open so the card updates as soon as the session is saved.
  const anyBusy = accounts?.some((a) => BUSY.includes(a.status)) ?? false;
  useEffect(() => {
    if (!anyBusy) return;
    const t = setInterval(load, 2000);
    return () => clearInterval(t);
  }, [anyBusy, load]);

  async function run(action: () => Promise<void>) {
    setError(null);
    try {
      await action();
      await load();
    } catch (e) {
      setError(e instanceof Error ? e.message : "Action failed");
    }
  }

  if (user && user.role === "User") {
    return <p className="py-16 text-center text-sm text-muted">Only admins can manage connected accounts.</p>;
  }

  return (
    <div className="mx-auto max-w-5xl space-y-6">
      <div>
        <h1 className="text-xl font-semibold">Settings</h1>
        <p className="text-sm text-muted">Connected accounts let the worker read pages that need a login.</p>
      </div>

      <div className="card space-y-2 border-warn/30 bg-warn/5 p-4 text-sm text-ink-dim">
        <p className="font-semibold text-warn">How connecting works</p>
        <ul className="list-disc space-y-1 pl-5">
          <li>Click <b>Connect</b>: a browser window opens <b>on the computer running the DeepLead Worker</b>. Log in there yourself (OTP / 2FA included).</li>
          <li>DeepLead saves only the login session (cookies), encrypted. Your password is never seen or stored.</li>
          <li>Use <b>dedicated accounts</b>, not personal ones. LinkedIn and Meta can restrict accounts that browse automatically; the worker keeps volumes low and human-paced.</li>
          <li>When a site logs the session out, the card shows <i>Session expired</i> — click Reconnect.</li>
        </ul>
      </div>

      {error && <p className="rounded-lg border border-bad/30 bg-bad/10 px-3 py-2 text-sm text-bad">{error}</p>}

      <div className="grid gap-4 md:grid-cols-2">
        {accounts === null && <p className="text-sm text-muted">Loading…</p>}
        {accounts?.map((a) => {
          const info = PLATFORM_INFO[a.platform];
          const busy = BUSY.includes(a.status);
          return (
            <div key={a.platform} className="card flex flex-col gap-3 p-5">
              <div className="flex items-center gap-3">
                <span className="flex h-10 w-10 items-center justify-center rounded-lg text-sm font-bold text-white" style={{ background: info.color }}>
                  {info.label.slice(0, 2)}
                </span>
                <div className="min-w-0 flex-1">
                  <div className="font-semibold">{info.label}</div>
                  <div className="truncate text-xs text-muted">{info.use}</div>
                </div>
                <StatusPill status={a.status} />
              </div>

              <div className="text-xs text-muted">
                {STATUS_TEXT[a.status]}
                {a.status === "Connected" && a.connectedAt && <> · since {formatUtc(a.connectedAt)}</>}
                {a.lastUsedAt && <> · last used {formatUtc(a.lastUsedAt)}</>}
                {a.status === "Connected" && a.platform === "LinkedIn" && <> · <span className="text-ink-dim">{a.usageToday} searches today</span> (daily cap 60)</>}
                {a.accountLabel && <> · <span className="text-ink-dim">{a.accountLabel}</span></>}
              </div>
              {a.lastError && <p className="rounded-md bg-bad/10 px-2.5 py-1.5 text-xs text-bad">{a.lastError}</p>}

              {a.status === "WaitingForLogin" && (
                <div className="rounded-md border border-accent/30 bg-accent/10 px-3 py-2 text-xs text-ink-dim">
                  Log in in the browser window that opened{info.autoDetect ? " — it is saved automatically once you're in." : ", then click the button below."}
                </div>
              )}

              {!busy && a.status !== "Connected" && (
                <input className="field py-1.5 text-sm" placeholder="Account note (optional), e.g. leadbot@yourcompany.com"
                  value={labels[a.platform] ?? a.accountLabel ?? ""} maxLength={200}
                  onChange={(e) => setLabels((l) => ({ ...l, [a.platform]: e.target.value }))} />
              )}

              <div className="mt-auto flex flex-wrap gap-2">
                {!busy && (
                  <button className="btn-primary" onClick={() => run(() => api.connectAccount(a.platform, labels[a.platform]?.trim() || null))}>
                    {a.status === "Connected" ? "Reconnect" : a.status === "Expired" || a.status === "Failed" ? "Reconnect" : "Connect"}
                  </button>
                )}
                {a.status === "WaitingForLogin" && (
                  <button className="btn-primary" onClick={() => run(() => api.saveAccount(a.platform))}>I&apos;ve logged in — save session</button>
                )}
                {a.status === "Connected" && (
                  <button className="btn-danger" onClick={() => confirm(`Disconnect ${info.label}? The saved session is deleted.`) && run(() => api.disconnectAccount(a.platform))}>
                    Disconnect
                  </button>
                )}
              </div>
            </div>
          );
        })}
      </div>
    </div>
  );
}

function StatusPill({ status }: { status: AccountStatus }) {
  const style =
    status === "Connected" ? "border-ok/40 bg-ok/10 text-ok"
      : status === "Expired" || status === "Failed" ? "border-bad/40 bg-bad/10 text-bad"
      : BUSY.includes(status) ? "border-accent/40 bg-accent/15 text-accent"
      : "border-line bg-panel-2 text-muted";
  return <span className={`shrink-0 rounded-full border px-2.5 py-0.5 text-xs font-semibold ${style}`}>{status === "Disconnected" ? "Off" : status === "Connected" ? "On" : "…"}</span>;
}
