"use client";

import Link from "next/link";
import { usePathname } from "next/navigation";

/** Sidebar link marked as current for its own page and, unless exact, every page beneath it. */
export function NavLink({ href, children, exact = false }: { href: string; children: React.ReactNode; exact?: boolean }) {
  const pathname = usePathname();
  const current = exact ? pathname === href : pathname === href || pathname.startsWith(`${href}/`);
  return (
    <Link href={href} aria-current={current ? "page" : undefined}>
      {children}
    </Link>
  );
}
