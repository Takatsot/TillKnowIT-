import react from "@vitejs/plugin-react";
import { defineConfig } from "vitest/config";
import { VitePWA } from "vite-plugin-pwa";

export default defineConfig({
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
        start_url: "/",
        background_color: "#f7f7f4",
        theme_color: "#0b6b4a",
        icons: [{ src: "icon.svg", sizes: "any", type: "image/svg+xml", purpose: "any" }],
      },
    }),
  ],
  test: { environment: "node", testTimeout: 30000 },
});
