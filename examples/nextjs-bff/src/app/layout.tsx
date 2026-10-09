import type { Metadata } from "next";
import "./globals.css";

export const metadata: Metadata = {
  title: "Dovepeak Identity — BFF example",
  description: "Next.js backend-for-frontend integration with Dovepeak Identity.",
};

export default function RootLayout({ children }: { children: React.ReactNode }) {
  return (
    <html lang="en">
      <body>
        <header>
          <strong>
            Dovepeak <span>Identity</span>
          </strong>
          <small>BFF example</small>
        </header>
        <main>{children}</main>
      </body>
    </html>
  );
}
