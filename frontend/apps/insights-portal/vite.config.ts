import react from "@vitejs/plugin-react";
import { defineConfig } from "vitest/config";

// In development the API runs on its own port; the app talks to /api/v1 and Vite forwards it.
export default defineConfig({
  plugins: [react()],
  server: { proxy: { "/api": { target: "http://localhost:5080", changeOrigin: true } } },
  test: { environment: "jsdom" },
});
