import react from "@vitejs/plugin-react";
import { defineConfig } from "vitest/config";
import { VitePWA } from "vite-plugin-pwa";

export default defineConfig({
  // On GitHub Pages each app lives under its own path (the build sets PAGES_BASE, for example /TillKnowIT-/till/).
  base: process.env["PAGES_BASE"] ?? "/",
  plugins: [
    react(),
    VitePWA({
      registerType: "autoUpdate",
      includeAssets: ["icon.svg"],
      manifest: {
        name: "TillKnowIT",
        short_name: "TillKnowIT",
        description: "A simple offline till for your shop",
        display: "standalone",
        start_url: process.env["PAGES_BASE"] ?? "/",
        background_color: "#f4efe3",
        theme_color: "#0b2a1f",
        icons: [{ src: "icon.svg", sizes: "any", type: "image/svg+xml", purpose: "any" }],
      },
    }),
  ],
  // In development the API runs on its own port; the till talks to /api/v1 and Vite forwards it.
  server: { proxy: { "/api": { target: "http://localhost:5080", changeOrigin: true } } },
  test: { setupFiles: ["./test-setup.ts"], environment: "node", testTimeout: 30000 },
});
