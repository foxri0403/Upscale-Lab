import { createHash } from "node:crypto";
import { createReadStream } from "node:fs";
import { readFile, stat } from "node:fs/promises";
import path from "node:path";
import { fileURLToPath } from "node:url";

export async function verifyDownloads(directory) {
  const manifest = JSON.parse(await readFile(path.join(directory, "manifest.json"), "utf8"));
  if (manifest.schemaVersion !== 1 || !Array.isArray(manifest.releases))
    throw new Error("연결 앱 배포 manifest 형식이 올바르지 않습니다.");
  if (manifest.localTest === true || manifest.releases.some(release => /-local(?:-|\.)/.test(release.url || "")))
    throw new Error("로컬 테스트 연결 앱은 운영 웹에 배포할 수 없습니다.");
  for (const platform of ["Windows", "Android"]) {
    const matches = manifest.releases.filter(release => release.platform === platform);
    if (matches.length !== 1) throw new Error(`${platform} 연결 앱 배포 정보가 필요합니다.`);
    const release = matches[0];
    const extension = platform === "Windows" ? "exe" : "apk";
    if (!/^\d+\.\d+\.\d+$/.test(release.version) ||
        !new RegExp(`^/downloads/[A-Za-z0-9_.-]+\\.${extension}$`).test(release.url) ||
        !path.basename(release.url).includes(`-${release.version}-`) ||
        !/^[a-f0-9]{64}$/.test(release.sha256) ||
        !Number.isSafeInteger(release.sizeBytes) || release.sizeBytes <= 0)
      throw new Error(`${platform} 연결 앱의 버전·파일 경로·검증 정보가 올바르지 않습니다.`);
    const file = path.join(directory, path.basename(release.url));
    if ((await stat(file)).size !== release.sizeBytes)
      throw new Error(`${platform} 연결 앱 파일 크기가 manifest와 다릅니다.`);
    const hash = createHash("sha256");
    for await (const chunk of createReadStream(file)) hash.update(chunk);
    if (hash.digest("hex") !== release.sha256)
      throw new Error(`${platform} 연결 앱 SHA-256이 manifest와 다릅니다.`);
  }
  return manifest;
}

if (process.argv[1] && path.resolve(process.argv[1]) === fileURLToPath(import.meta.url)) {
  const manifest = await verifyDownloads(process.argv[2] || "public/downloads");
  console.log(`연결 앱 배포 검증 완료: ${manifest.releases.map(release => `${release.platform} ${release.version}`).join(", ")}`);
}
