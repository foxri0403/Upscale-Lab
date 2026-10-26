import { spawnSync } from "node:child_process";
import { writeFile } from "node:fs/promises";
for (const args of [
  ["node_modules/typescript/bin/tsc", "-b"],
  ["node_modules/vite/bin/vite.js", "build"],
]) {
  const result = spawnSync(process.execPath, args, {
    stdio: "inherit",
    env: { ...process.env, VITE_APP_MODE: "cloud" },
  });
  if (result.status !== 0) process.exit(result.status || 1);
}
await writeFile(
  "dist/config.js",
  'window.__UPSCALE_CONFIG__ = { mode: "cloud", apiBaseUrl: "" };\n',
);
