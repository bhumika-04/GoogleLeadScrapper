"use client";

import { useCallback, useEffect, useState } from "react";
import { api } from "@/lib/api";
import { useAuth } from "@/lib/auth";
import { formatUtc } from "@/lib/format";
import type { Role, UserListItem } from "@/lib/types";

const ROLE_LABEL: Record<Role, string> = { Admin: "Platform admin", TenantAdmin: "Admin", User: "User" };

export default function UsersPage() {
  const { user } = useAuth();
  const [users, setUsers] = useState<UserListItem[] | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [notice, setNotice] = useState<string | null>(null);
  const [form, setForm] = useState({ fullName: "", email: "", role: "User" as Role, password: "" });
  const [saving, setSaving] = useState(false);

  const load = useCallback(() => api.users().then(setUsers).catch((e) => setError(e.message)), []);
  useEffect(() => { load(); }, [load]);

  const roles: Role[] = user?.role === "Admin" ? ["User", "TenantAdmin", "Admin"] : ["User", "TenantAdmin"];

  async function run(action: () => Promise<unknown>, success?: string) {
    setError(null);
    setNotice(null);
    try {
      await action();
      if (success) setNotice(success);
      await load();
    } catch (e) {
      setError(e instanceof Error ? e.message : "Action failed");
    }
  }

  async function addUser(e: React.FormEvent) {
    e.preventDefault();
    setSaving(true);
    await run(async () => {
      await api.createUser({ ...form, email: form.email.trim(), fullName: form.fullName.trim() });
      setForm({ fullName: "", email: "", role: "User", password: "" });
    }, `User added. Share the password with them securely; they can change it under Account.`);
    setSaving(false);
  }

  function resetPassword(u: UserListItem) {
    const pw = prompt(`New password for ${u.fullName} (min 8 characters):`);
    if (pw) run(() => api.resetPassword(u.id, pw), `Password for ${u.fullName} was reset.`);
  }

  return (
    <div className="space-y-6">
      {error && <p className="rounded-lg border border-bad/30 bg-bad/10 px-3 py-2 text-sm text-bad">{error}</p>}
      {notice && <p className="rounded-lg border border-ok/30 bg-ok/10 px-3 py-2 text-sm text-ok">{notice}</p>}

      <form onSubmit={addUser} className="card grid gap-3 p-5 sm:grid-cols-2 lg:grid-cols-[1fr_1.2fr_150px_1fr_auto] lg:items-end">
        <div>
          <label className="label" htmlFor="u-name">Name</label>
          <input id="u-name" className="field" required value={form.fullName} onChange={(e) => setForm({ ...form, fullName: e.target.value })} />
        </div>
        <div>
          <label className="label" htmlFor="u-email">Email</label>
          <input id="u-email" type="email" className="field" required value={form.email} onChange={(e) => setForm({ ...form, email: e.target.value })} />
        </div>
        <div>
          <label className="label" htmlFor="u-role">Role</label>
          <select id="u-role" className="field" value={form.role} onChange={(e) => setForm({ ...form, role: e.target.value as Role })}>
            {roles.map((r) => <option key={r} value={r}>{ROLE_LABEL[r]}</option>)}
          </select>
        </div>
        <div>
          <label className="label" htmlFor="u-pw">Temporary password</label>
          <input id="u-pw" type="text" minLength={8} className="field" required value={form.password} onChange={(e) => setForm({ ...form, password: e.target.value })} />
        </div>
        <button type="submit" className="btn-primary h-[46px]" disabled={saving}>{saving ? "Adding…" : "Add user"}</button>
      </form>

      <div className="card overflow-x-auto">
        <table className="w-full min-w-[720px] text-sm">
          <thead>
            <tr className="border-b border-line text-left text-xs uppercase tracking-wide text-muted">
              <th className="px-4 py-3 font-medium">User</th>
              <th className="px-4 py-3 font-medium">Role</th>
              <th className="px-4 py-3 font-medium">Status</th>
              <th className="px-4 py-3 font-medium">Last login</th>
              <th className="px-4 py-3 text-right font-medium">Actions</th>
            </tr>
          </thead>
          <tbody>
            {users === null && <tr><td colSpan={5} className="px-4 py-8 text-center text-muted">Loading…</td></tr>}
            {users?.map((u) => {
              const self = u.id === user?.id;
              const locked = u.role === "Admin" && user?.role !== "Admin";
              return (
                <tr key={u.id} className="border-b border-line/60 last:border-0">
                  <td className="px-4 py-3">
                    <div className="font-medium">{u.fullName}{self && <span className="ml-2 text-xs text-muted">(you)</span>}</div>
                    <div className="text-xs text-muted">{u.email}</div>
                  </td>
                  <td className="px-4 py-3">
                    {self || locked ? <span className="text-ink-dim">{ROLE_LABEL[u.role]}</span> : (
                      <select className="field w-40 py-1.5 text-sm" value={u.role}
                        onChange={(e) => run(() => api.updateUser(u.id, { role: e.target.value as Role }))}>
                        {roles.map((r) => <option key={r} value={r}>{ROLE_LABEL[r]}</option>)}
                      </select>
                    )}
                  </td>
                  <td className="px-4 py-3">
                    <span className={u.isActive ? "text-ok" : "text-muted"}>{u.isActive ? "Active" : "Deactivated"}</span>
                  </td>
                  <td className="px-4 py-3 text-xs text-muted">{u.lastLoginAt ? formatUtc(u.lastLoginAt) : "Never"}</td>
                  <td className="px-4 py-3 text-right">
                    {!locked && (
                      <div className="flex justify-end gap-2">
                        <button className="btn-ghost px-3 py-1" onClick={() => resetPassword(u)}>Reset password</button>
                        {!self && (
                          <button className={u.isActive ? "btn-danger px-3 py-1" : "btn-ghost px-3 py-1"}
                            onClick={() => run(() => api.updateUser(u.id, { isActive: !u.isActive }))}>
                            {u.isActive ? "Deactivate" : "Activate"}
                          </button>
                        )}
                      </div>
                    )}
                  </td>
                </tr>
              );
            })}
          </tbody>
        </table>
      </div>
    </div>
  );
}
