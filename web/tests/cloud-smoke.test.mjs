import test from "node:test";
import assert from "node:assert/strict";
import { mergeExploreFeed } from "../src/explore-feed.ts";
const base = process.env.WEB_TEST_URL;
const run = (name, action) => test(name, { skip: !base }, action);
const get = (path) =>
  fetch(`${base}${path}`, { signal: AbortSignal.timeout(15000) });
run("companion release manifest points to real Windows and signed Android binaries", async () => {
  const response = await get("/downloads/manifest.json");
  assert.equal(response.status, 200);
  assert.match(response.headers.get("content-type"), /json/);
  assert.match(response.headers.get("cache-control"), /no-store/);
  const manifest = await response.json();
  assert.deepEqual(manifest.releases.map(r => r.platform).sort(), ["Android", "Windows"]);
  for (const release of manifest.releases) {
    assert.match(release.sha256, /^[a-f0-9]{64}$/);
    assert.ok(release.sizeBytes > 1000000);
    assert.match(release.url, /^\/downloads\/[A-Za-z0-9_.-]+\.(exe|apk)$/);
    const head = await fetch(`${base}${release.url}`, { method: "HEAD", signal: AbortSignal.timeout(15000) });
    assert.equal(head.status, 200);
    assert.equal(Number(head.headers.get("content-length")), release.sizeBytes);
    assert.doesNotMatch(head.headers.get("content-type"), /text\/html/);
    const sample = await fetch(`${base}${release.url}`, { headers: { Range: "bytes=0-3" }, signal: AbortSignal.timeout(15000) });
    assert.equal(sample.status, 206);
    const magic = Buffer.from(await sample.arrayBuffer());
    assert.equal(magic.subarray(0, 2).toString("ascii"), release.platform === "Windows" ? "MZ" : "PK");
  }
});
run("missing companion download returns 404 instead of the SPA shell", async () => {
  assert.equal((await get("/downloads/not-a-release.apk")).status, 404);
});
run(
  "cloud serves the landing page and a real compiled JavaScript bundle",
  async () => {
    const page = await get("/");
    assert.equal(page.status, 200);
    const html = await page.text();
    assert.match(html, /<html lang="ko">/);
    assert.match(html, /id="root"/);
    const src = html.match(/src="(\/assets\/[^\"]+\.js)"/)?.[1];
    assert.ok(src);
    const bundle = await get(src);
    assert.equal(bundle.status, 200);
    assert.match(bundle.headers.get("content-type"), /javascript/);
    const code = await bundle.text();
    assert.match(code, /\/api\/gallery/, "deployed UI must query the existing mobile gallery");
    assert.match(code, /\/api\/auth\/find-id/, "deployed UI must include mobile ID recovery");
    assert.ok(code.includes("/app/studio?new=1"), "image addition must open a new studio");
    assert.ok(code.includes("originalContentType"), "studio must read authoritative image format metadata");
  },
);
run("runtime configuration enables same-origin cloud API", async () => {
  const r = await get("/config.js");
  assert.equal(r.status, 200);
  assert.match(await r.text(), /mode:\s*"cloud"/);
});
run("direct navigation to every page returns the SPA", async () => {
  for (const path of [
    "/login",
    "/signup",
    "/app/images",
    "/app/explore",
    "/app/studio",
    "/app/devices",
  ]) {
    const r = await get(path);
    assert.equal(r.status, 200, path);
    assert.match(await r.text(), /id="root"/);
  }
});
run("backend health stays available behind frontend routing", async () => {
  const r = await get("/health");
  assert.equal(r.status, 200);
  assert.equal((await r.text()).trim(), "Healthy");
});
run("private API routes still reject unauthenticated requests", async () => {
  for (const path of [
    "/api/projects",
    "/api/devices",
    "/api/wallpapers",
    "/api/auth/me",
  ]) {
    const r = await get(path);
    assert.equal(r.status, 401, path);
    assert.doesNotMatch(r.headers.get("content-type") || "", /text\/html/);
  }
});
run("shared wallpaper API returns JSON, never the SPA fallback", async () => {
  const r = await get("/api/wallpapers/shared");
  assert.equal(r.status, 200);
  assert.ok(Array.isArray(await r.json()));
});
run("image settings PATCH routes exist and require authentication", async () => {
  for (const resource of ["projects", "wallpapers"]) {
    const path = `/api/${resource}/00000000-0000-0000-0000-000000000001`;
    const options = await fetch(`${base}${path}`, {
      method: "OPTIONS", signal: AbortSignal.timeout(15000),
    });
    assert.equal(options.status, 405, resource);
    assert.match(options.headers.get("allow"), /PATCH/, resource);
    const patch = await fetch(`${base}${path}`, {
      method: "PATCH", headers: { "Content-Type": "application/json" },
      body: JSON.stringify({ title: "Unauthenticated test" }),
      signal: AbortSignal.timeout(15000),
    });
    assert.equal(patch.status, 401, resource);
  }
});
run(
  "the explore feed includes existing mobile posts alongside new wallpapers",
  async () => {
    const [galleryResponse, wallpaperResponse] = await Promise.all([
      get("/api/gallery"),
      get("/api/wallpapers/shared"),
    ]);
    assert.equal(galleryResponse.status, 200);
    assert.equal(wallpaperResponse.status, 200);
    const gallery = await galleryResponse.json();
    const wallpapers = await wallpaperResponse.json();
    assert.ok(Array.isArray(gallery));
    const feed = mergeExploreFeed(
      wallpapers.map((a) => ({ id: `asset:${a.id}`, assetId: a.id })),
      gallery,
    );
    const visible = new Set(feed.map((p) => p.galleryPostId).filter(Boolean));
    assert.deepEqual(
      visible,
      new Set(gallery.filter((p) => p.isPublic).map((p) => p.id)),
    );
  },
);
run(
  "invalid login is validated by the actual API without creating a session",
  async () => {
    const r = await fetch(`${base}/api/auth/login`, {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify({ email: "", password: "" }),
      signal: AbortSignal.timeout(15000),
    });
    assert.equal(r.status, 400);
    assert.match(r.headers.get("content-type"), /json/);
  },
);
run(
  "mobile account recovery endpoints validate requests without sending email or changing passwords",
  async () => {
    for (const path of ["find-id", "forgot-password", "reset-password"]) {
      const response = await fetch(`${base}/api/auth/${path}`, {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify({ email: "", code: "", newPassword: "" }),
        signal: AbortSignal.timeout(15000),
      });
      assert.equal(response.status, 400, path);
      assert.match(response.headers.get("content-type"), /json/);
    }
  },
);
run(
  "missing asset and API endpoints return 404 instead of the HTML shell",
  async () => {
    assert.equal((await get("/assets/missing-test.js")).status, 404);
    const r = await get("/api/does-not-exist");
    assert.equal(r.status, 404);
    assert.doesNotMatch(r.headers.get("content-type") || "", /text\/html/);
  },
);
