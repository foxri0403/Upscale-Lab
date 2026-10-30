import { defineConfig, loadEnv } from "vite";
import react from "@vitejs/plugin-react";

export default defineConfig(({ mode }) => ({
  plugins: [react()],
  server: {
    host: "127.0.0.1",
    proxy: {
      "/__separation-lab": {
        target: loadEnv(mode, ".", "SEPARATION_LAB_").SEPARATION_LAB_API || "http://127.0.0.1:5091",
        rewrite: path => path.replace(/^\/__separation-lab/, ""),
      },
    },
  },
}));
