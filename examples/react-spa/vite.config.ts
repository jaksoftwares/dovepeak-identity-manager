import react from "@vitejs/plugin-react";
import { defineConfig } from "vite";

export default defineConfig({
  plugins: [react()],
  server: { port: 5173, strictPort: true },
  preview: {
    port: 5173,
    strictPort: true,
    // The same security headers a production host should send for a token-handling SPA.
    headers: {
      "Content-Security-Policy": "default-src 'self'; connect-src 'self' http://localhost:8080 http://localhost:5301; frame-ancestors 'none'; base-uri 'self'; object-src 'none'",
      "X-Frame-Options": "DENY",
      "Referrer-Policy": "same-origin",
      "X-Content-Type-Options": "nosniff",
    },
  },
});
