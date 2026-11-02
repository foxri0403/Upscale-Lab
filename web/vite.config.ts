import { defineConfig, loadEnv } from "vite";
import react from "@vitejs/plugin-react";

export default defineConfig(({ mode }) => {
  const apiTarget = loadEnv(mode, ".", "LOCAL_").LOCAL_API_TARGET || "http://127.0.0.1:5080";
  const apiProxy = {
    "/api": { target: apiTarget, changeOrigin: true },
    "/health": { target: apiTarget, changeOrigin: true },
    "/hubs": { target: apiTarget, changeOrigin: true, ws: true },
  };
  return {
    plugins: [react()],
    // Reuse the deployed cloud UI without local sample accounts or LOCAL PREVIEW labels.
    define: mode === "production-ui" ? { "import.meta.env.VITE_APP_MODE": JSON.stringify("cloud") } : {},
    server: {
      host: "127.0.0.1",
      proxy: {
        ...apiProxy,
        "/__separation-lab": {
          target: loadEnv(mode, ".", "SEPARATION_LAB_").SEPARATION_LAB_API || "http://127.0.0.1:5091",
          rewrite: path => path.replace(/^\/__separation-lab/, ""),
        },
      },
    },
    preview: { proxy: apiProxy },
  };
});
