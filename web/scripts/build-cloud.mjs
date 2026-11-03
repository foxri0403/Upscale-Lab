import { spawnSync } from "node:child_process";
import { writeFile } from "node:fs/promises";
import { loadEnv } from "vite";
import { verifyDownloads } from "./verify-downloads.mjs";
import { paymentClientKey } from "./payment-config.mjs";
// Missing public checkout configuration must stop a deployment instead of
// silently rebuilding an application whose payment buttons are disabled.
const clientKey = paymentClientKey({ ...loadEnv("production", process.cwd(), "VITE_"), ...process.env });
// A production bundle must keep the existing signed companion installers.
await verifyDownloads("public/downloads");
for (const args of [
  ["node_modules/typescript/bin/tsc", "-b"],
  ["node_modules/vite/bin/vite.js", "build"],
]) {
  const result = spawnSync(process.execPath, args, {
    stdio: "inherit",
    env: { ...process.env, VITE_APP_MODE: "cloud", VITE_TOSS_CLIENT_KEY: clientKey },
  });
  if (result.status !== 0) process.exit(result.status || 1);
}
await writeFile(
  "dist/config.js",
  'window.__UPSCALE_CONFIG__ = { mode: "cloud", apiBaseUrl: "" };\n',
);
await verifyDownloads("dist/downloads");
