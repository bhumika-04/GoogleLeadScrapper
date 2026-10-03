"use client";

import { useState } from "react";
import { api } from "@/lib/api";
import { useAuth } from "@/lib/auth";

export default function AccountPage() {
  const { user } = useAuth();
  const [current, setCurrent] = useState("");
  const [next, setNext] = useState("");
  const [confirmPw, setConfirmPw] = useState("");
  const [message, setMessage] = useState<{ ok: boolean; text: string } | null>(null);
  const [saving, setSaving] = useState(false);

  async function submit(e: React.FormEvent) {
    e.preventDefault();
    if (next !== confirmPw) {
      setMessage({ ok: false, text: "The new passwords don't match." });
      return;
    }
    setSaving(true);
    setMessage(null);
    try {
      await api.changePassword(current, next);
      setMessage({ ok: true, text: "Password changed." });
      setCurrent("");
      setNext("");
      setConfirmPw("");
    } catch (err) {
      setMessage({ ok: false, text: err instanceof Error ? err.message : "Could not change password" });
    } finally {
      setSaving(false);
    }
  }

  return (
    <div className="mx-auto max-w-md space-y-6">
      <div>
        <h1 className="text-xl font-semibold">Account</h1>
        <p className="text-sm text-muted">{user?.fullName} · {user?.email} · {user?.tenantName}</p>
      </div>
      <form onSubmit={submit} className="card space-y-4 p-6">
        <h2 className="font-semibold">Change password</h2>
        <div>
          <label className="label" htmlFor="cur">Current password</label>
          <input id="cur" type="password" autoComplete="current-password" className="field" required value={current} onChange={(e) => setCurrent(e.target.value)} />
        </div>
        <div>
          <label className="label" htmlFor="new">New password <span className="hint">— at least 8 characters</span></label>
          <input id="new" type="password" autoComplete="new-password" minLength={8} className="field" required value={next} onChange={(e) => setNext(e.target.value)} />
        </div>
        <div>
          <label className="label" htmlFor="confirm">Confirm new password</label>
          <input id="confirm" type="password" autoComplete="new-password" minLength={8} className="field" required value={confirmPw} onChange={(e) => setConfirmPw(e.target.value)} />
        </div>
        {message && (
          <p className={`rounded-lg border px-3 py-2 text-sm ${message.ok ? "border-ok/30 bg-ok/10 text-ok" : "border-bad/30 bg-bad/10 text-bad"}`}>{message.text}</p>
        )}
        <button type="submit" className="btn-primary w-full" disabled={saving}>{saving ? "Saving…" : "Change password"}</button>
      </form>
    </div>
  );
}
