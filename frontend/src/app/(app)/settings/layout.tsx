"use client";

import Link from "next/link";
import { usePathname } from "next/navigation";
import { useAuth } from "@/lib/auth";

const tabs = [
  { href: "/settings", label: "Connected accounts" },
  { href: "/settings/users", label: "Users" },
  { href: "/settings/customers", label: "Customers", platformAdminOnly: true },
];

export default function SettingsLayout({ children }: { children: React.ReactNode }) {
  const { user } = useAuth();
  const pathname = usePathname();

  if (user?.role === "User") {
    return <p className="py-16 text-center text-sm text-muted">Only admins can open Settings.</p>;
  }

  return (
    <div className="mx-auto max-w-5xl space-y-6">
      <div>
        <h1 className="text-xl font-semibold">Settings</h1>
        <p className="text-sm text-muted">{user?.tenantName} workspace</p>
      </div>
      <nav className="flex gap-1 border-b border-line">
        {tabs.filter((t) => !t.platformAdminOnly || user?.role === "Admin").map((t) => {
          const active = pathname === t.href;
          return (
            <Link key={t.href} href={t.href}
              className={`-mb-px border-b-2 px-3 py-2 text-sm transition ${active ? "border-accent text-ink" : "border-transparent text-ink-dim hover:text-ink"}`}>
              {t.label}
            </Link>
          );
        })}
      </nav>
      {children}
    </div>
  );
}
