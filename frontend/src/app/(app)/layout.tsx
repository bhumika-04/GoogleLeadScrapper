"use client";

import { useEffect } from "react";
import Link from "next/link";
import { usePathname, useRouter } from "next/navigation";
import { useAuth } from "@/lib/auth";
import { Logo } from "@/components/Logo";

const nav = [
  { href: "/searches/new", label: "New search" },
  { href: "/searches", label: "Sessions" },
  { href: "/settings", label: "Settings", adminOnly: true },
];

export default function AppLayout({ children }: { children: React.ReactNode }) {
  const { user, loading, logout } = useAuth();
  const router = useRouter();
  const pathname = usePathname();

  useEffect(() => {
    if (!loading && !user) router.replace("/login");
  }, [loading, user, router]);

  if (loading || !user) {
    return <div className="flex min-h-screen items-center justify-center text-sm text-muted">Loading…</div>;
  }

  return (
    <div className="min-h-screen">
      <header className="sticky top-0 z-20 border-b border-line bg-bg/90 backdrop-blur">
        <div className="mx-auto flex h-14 max-w-[1600px] items-center gap-6 px-4 sm:px-6">
          <Link href="/searches" className="flex items-center gap-2.5">
            <Logo size={30} />
            <span className="font-semibold">DeepLead</span>
          </Link>
          <nav className="flex gap-1">
            {nav.filter((item) => !item.adminOnly || user.role !== "User").map((item) => {
              const active = item.href === "/searches"
                ? pathname === "/searches" || (/^\/searches\/\d+/.test(pathname))
                : pathname.startsWith(item.href);
              return (
                <Link key={item.href} href={item.href}
                  className={`rounded-md px-3 py-1.5 text-sm transition ${active ? "bg-panel-2 text-ink" : "text-ink-dim hover:text-ink"}`}>
                  {item.label}
                </Link>
              );
            })}
          </nav>
          <div className="ml-auto flex items-center gap-3 text-sm">
            <span className="hidden text-ink-dim sm:inline">{user.fullName} · <span className="text-muted">{user.tenantName}</span></span>
            <button onClick={logout} className="btn-ghost px-3 py-1.5">Sign out</button>
          </div>
        </div>
      </header>
      <main className="mx-auto max-w-[1600px] px-4 py-6 sm:px-6">{children}</main>
    </div>
  );
}
