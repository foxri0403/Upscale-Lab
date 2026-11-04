import { createRoot } from "react-dom/client";
import "../src/styles.css";
import "../src/responsive.css";

// Dedicated development entry point; these fake credentials cannot reach a
// real API, and this page is excluded from the production Vite entry points.
const base = "/__device-ui-fixture";
window.__UPSCALE_CONFIG__ = { mode: "cloud", apiBaseUrl: base };
sessionStorage.setItem(`upscale.cloud.tokens.v1.${base}`, JSON.stringify({ accessToken: "fixture", refreshToken: "fixture", expiresAt: "2099-01-01T00:00:00Z" }));
const account = { id: "device-ui-fixture", email: "preview@example.test", username: "preview", name: "preview" };
const display = (id: string, width: number, height: number, scale: number, primary = true) => ({
  id, name: primary ? "내장 화면" : "모니터 2", width, height, scale, isPrimary: primary,
  isConnected: true, refreshRateHz: 120, canApplyWallpaper: true, wallpapers: [],
});
const devices = [
  { id: "pc", name: "내 PC", platform: "Windows", displays: [display("monitor-1", 2560, 1440, 1.5), display("monitor-2", 1920, 1080, 1, false)] },
  { id: "phone", name: "내 휴대폰", platform: "Android", displays: [display("phone-screen", 1080, 2400, 3)] },
  { id: "tablet", name: "내 태블릿", platform: "Android", displays: [display("tablet-screen", 2560, 1600, 2)] },
].map((d, i) => ({ ...d, deviceName: d.name, osVersion: d.platform === "Windows" ? "Windows 11" : "Android", isLinked: true,
  recentlySeen: true, supportsWallpaperRemoval: true, createdAt: `2026-11-03T00:00:0${i}Z` }));
const nativeFetch = window.fetch.bind(window);
const json = (value: unknown, status = 200) => new Response(JSON.stringify(value), { status, headers: { "Content-Type": "application/json" } });
window.fetch = async (input, init) => {
  const url = new URL(String(input), location.origin);
  if (url.pathname === "/downloads/manifest.json") return json({ releases: ["Windows", "Android"].map(platform => ({
    platform, version: "1.3.0", url: `/downloads/fixture-preview.${platform === "Windows" ? "exe" : "apk"}`, sha256: "0".repeat(64), sizeBytes: 1024 * 1024,
  })) });
  if (!url.pathname.startsWith(base)) return nativeFetch(input, init);
  const path = url.pathname.slice(base.length);
  if (path === "/api/account") return json(account);
  if (path === "/api/devices/updates") {
    await new Promise<void>((resolve, reject) => {
      const timer = setTimeout(resolve, 20000);
      init?.signal?.addEventListener("abort", () => { clearTimeout(timer); reject(new DOMException("Aborted", "AbortError")); }, { once: true });
    });
    return json({ cursor: "fixture" });
  }
  if (path === "/api/devices") return json(devices);
  const device = devices.find(d => path === `/api/devices/${d.id}/state`);
  if (device) return json(device);
  if (["/api/projects", "/api/wallpapers", "/api/wallpapers/shared", "/api/gallery"].includes(path)) return json([]);
  return json({ message: "이 개발 페이지는 기기 UI 조회만 지원합니다." }, 400);
};
const { Workspace } = await import("../src/Workspace");
createRoot(document.getElementById("root")!).render(<Workspace session={{ id: account.id, email: account.email, name: account.name }} route="/app/devices" logout={() => {}} />);
