"use client";

import { useCallback, useEffect, useState } from "react";
import { api } from "@/lib/api";
import { useAuth } from "@/lib/auth";
import { formatUtc } from "@/lib/format";
import type { Tenant } from "@/lib/types";

export default function CustomersPage() {
  const { user } = useAuth();
  const [tenants, setTenants] = useState<Tenant[] | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [notice, setNotice] = useState<string | null>(null);
  const [form, setForm] = useState({ name: "", adminName: "", adminEmail: "", adminPassword: "" });
  const [saving, setSaving] = useState(false);

  const load = useCallback(() => api.tenants().then(setTenants).catch((e) => setError(e.message)), []);
  useEffect(() => { load(); }, [load]);

  if (user && user.role !== "Admin") {
    return <p className="py-16 text-center text-sm text-muted">Only platform admins can manage customers.</p>;
  }

  async function create(e: React.FormEvent) {
    e.preventDefault();
    setSaving(true);
    setError(null);
    setNotice(null);
    try {
      await api.createTenant(form);
      setNotice(`Workspace "${form.name}" created. ${form.adminEmail} can sign in with the password you set.`);
      setForm({ name: "", adminName: "", adminEmail: "", adminPassword: "" });
      await load();
    } catch (err) {
      setError(err instanceof Error ? err.message : "Could not create workspace");
    } finally {
      setSaving(false);
    }
  }

  async function toggle(t: Tenant) {
    if (t.isActive && !confirm(`Deactivate "${t.name}"? Its users are signed out immediately; data is kept.`)) return;
    setError(null);
    try {
      await api.updateTenant(t.id, { isActive: !t.isActive });
      await load();
    } catch (err) {
      setError(err instanceof Error ? err.message : "Action failed");
    }
  }

  return (
    <div className="space-y-6">
      <p className="text-sm text-muted">Each customer gets an isolated workspace: their own users, sessions, leads and connected accounts.</p>
      {error && <p className="rounded-lg border border-bad/30 bg-bad/10 px-3 py-2 text-sm text-bad">{error}</p>}
      {notice && <p className="rounded-lg border border-ok/30 bg-ok/10 px-3 py-2 text-sm text-ok">{notice}</p>}

      <form onSubmit={create} className="card grid gap-3 p-5 sm:grid-cols-2 lg:grid-cols-[1.2fr_1fr_1.2fr_1fr_auto] lg:items-end">
        <div>
          <label className="label" htmlFor="t-name">Customer / company</label>
          <input id="t-name" className="field" required value={form.name} onChange={(e) => setForm({ ...form, name: e.target.value })} />
        </div>
        <div>
          <label className="label" htmlFor="t-admin">Admin name</label>
          <input id="t-admin" className="field" required value={form.adminName} onChange={(e) => setForm({ ...form, adminName: e.target.value })} />
        </div>
        <div>
          <label className="label" htmlFor="t-email">Admin email</label>
          <input id="t-email" type="email" className="field" required value={form.adminEmail} onChange={(e) => setForm({ ...form, adminEmail: e.target.value })} />
        </div>
        <div>
          <label className="label" htmlFor="t-pw">Temporary password</label>
          <input id="t-pw" minLength={8} className="field" required value={form.adminPassword} onChange={(e) => setForm({ ...form, adminPassword: e.target.value })} />
        </div>
        <button type="submit" className="btn-primary h-[46px]" disabled={saving}>{saving ? "Creating…" : "Create"}</button>
      </form>

      <div className="card overflow-x-auto">
        <table className="w-full min-w-[640px] text-sm">
          <thead>
            <tr className="border-b border-line text-left text-xs uppercase tracking-wide text-muted">
              <th className="px-4 py-3 font-medium">Workspace</th>
              <th className="px-4 py-3 text-right font-medium">Users</th>
              <th className="px-4 py-3 text-right font-medium">Sessions</th>
              <th className="px-4 py-3 text-right font-medium">Leads</th>
              <th className="px-4 py-3 font-medium">Created</th>
              <th className="px-4 py-3 text-right font-medium">Status</th>
            </tr>
          </thead>
          <tbody>
            {tenants === null && <tr><td colSpan={6} className="px-4 py-8 text-center text-muted">Loading…</td></tr>}
            {tenants?.map((t) => (
              <tr key={t.id} className="border-b border-line/60 last:border-0">
                <td className="px-4 py-3 font-medium">{t.name}{t.id === user?.tenantId && <span className="ml-2 text-xs text-muted">(yours)</span>}</td>
                <td className="px-4 py-3 text-right tabular-nums">{t.userCount}</td>
                <td className="px-4 py-3 text-right tabular-nums">{t.sessionCount}</td>
                <td className="px-4 py-3 text-right tabular-nums">{t.leadCount.toLocaleString()}</td>
                <td className="px-4 py-3 text-xs text-muted">{formatUtc(t.createdAt)}</td>
                <td className="px-4 py-3 text-right">
                  {t.id === user?.tenantId ? <span className="text-ok">Active</span> : (
                    <button className={t.isActive ? "btn-danger px-3 py-1" : "btn-ghost px-3 py-1"} onClick={() => toggle(t)}>
                      {t.isActive ? "Deactivate" : "Activate"}
                    </button>
                  )}
                </td>
              </tr>
            ))}
          </tbody>
        </table>
      </div>
    </div>
  );
}
