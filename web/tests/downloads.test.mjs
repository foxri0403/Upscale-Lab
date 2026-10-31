import test from "node:test";
import assert from "node:assert/strict";
import { createHash } from "node:crypto";
import { mkdtemp, rm, writeFile } from "node:fs/promises";
import os from "node:os";
import path from "node:path";
import { verifyDownloads } from "../scripts/verify-downloads.mjs";

async function fixture(run) {
  const directory = await mkdtemp(path.join(os.tmpdir(), "upscale-downloads-"));
  try {
    const releases = [];
    for (const [platform, extension] of [["Windows", "exe"], ["Android", "apk"]]) {
      const content = Buffer.from(`${platform} fixture`);
      const name = `UpscaleLab-1.2.2-test.${extension}`;
      await writeFile(path.join(directory, name), content);
      releases.push({ platform, version: "1.2.2", url: `/downloads/${name}`,
        sha256: createHash("sha256").update(content).digest("hex"), sizeBytes: content.length });
    }
    const manifest = { schemaVersion: 1, releases };
    const save = () => writeFile(path.join(directory, "manifest.json"), JSON.stringify(manifest));
    await save();
    await run(directory, manifest, save);
  } finally { await rm(directory, { recursive: true, force: true }); }
}

test("cloud releases retain both companion installers with matching bytes and hashes", () => fixture(async directory => {
  assert.equal((await verifyDownloads(directory)).releases.length, 2);
}));

test("a missing installer or an equally sized corrupted installer blocks a release", () => fixture(async (directory, manifest) => {
  const file = path.join(directory, path.basename(manifest.releases[0].url));
  await writeFile(file, Buffer.alloc(manifest.releases[0].sizeBytes));
  await assert.rejects(verifyDownloads(directory), /SHA-256/);
  await rm(file);
  await assert.rejects(verifyDownloads(directory), /ENOENT/);
}));

test("a manifest cannot omit a platform or reference a different version or external file", () => fixture(async (directory, manifest, save) => {
  const release = manifest.releases.pop();
  await save();
  await assert.rejects(verifyDownloads(directory), /Android/);
  manifest.releases.push(release);
  release.version = "1.2.3";
  await save();
  await assert.rejects(verifyDownloads(directory), /버전/);
  release.version = "1.2.2";
  release.url = "/downloads/../../private.apk";
  await save();
  await assert.rejects(verifyDownloads(directory), /경로/);
}));
