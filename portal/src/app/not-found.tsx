import Link from "next/link";

export default function NotFound() {
  return (
    <main className="hero">
      <h1>Not found</h1>
      <p>This page does not exist, or you do not have access to it.</p>
      <Link className="button" href="/orgs">Back to your organizations</Link>
    </main>
  );
}
