import { defineConfig } from 'vite';
import react from '@vitejs/plugin-react';
import path from 'node:path';

// The .NET API listens on http://localhost:8080 in dev (see Buildix.API/Program.cs).
// We proxy /api and /hubs so the browser talks to Vite's origin only — this keeps
// cookies/CORS simple and mirrors the production nginx layout (API under /api, /hubs).
//
// 127.0.0.1, NOT localhost. On Windows, Node resolves `localhost` to ::1 first
// (dns.getDefaultResultOrder() === 'verbatim'), while Kestrel's default dev bind
// is IPv4-only. Anything else squatting ::1 on that port then answers the SPA's
// /api calls instead of the API — the requests succeed with the wrong server's
// 404s, so the register looks broken rather than disconnected. Pinning the family
// makes the target unambiguous.
const API_TARGET = process.env.VITE_API_PROXY_TARGET ?? 'http://127.0.0.1:8080';

export default defineConfig({
  plugins: [react()],
  resolve: {
    alias: {
      '@': path.resolve(__dirname, 'src'),
    },
  },
  server: {
    port: 5173,
    proxy: {
      '/api': {
        target: API_TARGET,
        changeOrigin: true,
      },
      // SignalR — needs WebSocket upgrade.
      '/hubs': {
        target: API_TARGET,
        changeOrigin: true,
        ws: true,
      },
    },
  },
  build: {
    outDir: 'dist',
    sourcemap: true,
  },
});
