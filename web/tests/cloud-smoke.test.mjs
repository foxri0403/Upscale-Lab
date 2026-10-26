import test from "node:test";
import assert from "node:assert/strict";
import { mergeExploreFeed } from "../src/explore-feed.ts";
const base = process.env.WEB_TEST_URL;
const run = (name, action) => test(name, { skip: !base }, action);
const get = (path) =>
  fetch(`${base}${path}`, { signal: AbortSignal.timeout(15000) });
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
