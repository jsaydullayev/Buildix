import { defineConfig } from 'vite';
import react from '@vitejs/plugin-react';
import path from 'node:path';

// The .NET API listens on http://localhost:8080 in dev (see Buildix.API/Program.cs).
// We proxy /api and /hubs so the browser talks to Vite's origin only — this keeps
// cookies/CORS simple and mirrors the production nginx layout (API under /api, /hubs).
const API_TARGET = process.env.VITE_API_PROXY_TARGET ?? 'http://localhost:8080';

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
