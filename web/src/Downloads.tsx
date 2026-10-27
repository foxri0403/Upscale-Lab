import { useEffect, useState } from "react";
import { Download, Monitor, Smartphone, Apple } from "lucide-react";
import { Notice } from "./ui";

type Release = { platform: "Windows" | "Android"; version: string; url: string; sha256: string; sizeBytes: number };
export function Downloads() {
  const [releases, setReleases] = useState<Release[]>([]);
  const [error, setError] = useState("");
  useEffect(() => {
    const controller = new AbortController();
    fetch("/downloads/manifest.json", { cache: "no-store", signal: controller.signal })
      .then(async (response) => {
        if (!response.ok) throw new Error("다운로드 정보를 불러오지 못했습니다.");
        const data = await response.json();
        if (!Array.isArray(data.releases)) throw new Error("배포 정보가 올바르지 않습니다.");
        setReleases(data.releases.filter((r: Release) =>
          ["Windows", "Android"].includes(r.platform) && /^\/downloads\/[A-Za-z0-9_.-]+\.(exe|apk)$/.test(r.url) &&
          /^[a-f0-9]{64}$/.test(r.sha256) && r.sizeBytes > 0));
      }).catch((failure) => { if (!controller.signal.aborted) setError(failure.message); });
    return () => controller.abort();
  }, []);
  return <>
    <p>연결 앱에서 웹과 같은 계정으로 로그인하면 기기가 자동으로 등록됩니다. 웹의 기기 관리에서 적용할 화면을 선택하세요.</p>
    {(["Windows", "Android"] as const).map((platform) => {
      const release = releases.find((r) => r.platform === platform);
      return <div className="download-platform" key={platform}>
        {platform === "Windows" ? <Monitor /> : <Smartphone />}
        <div><strong>{platform} 연결 앱</strong><small>{release ? `v${release.version} · ${(release.sizeBytes / 1024 / 1024).toFixed(1)} MB · ${platform === "Windows" ? "Windows 10/11 · x64" : "Android 7 이상"}` : "배포 정보 확인 중"}</small>
          {release && <details className="download-checksum"><summary>SHA-256 확인</summary><code>{release.sha256}</code></details>}
        </div>
        {release && <a className="button primary" href={release.url} download><Download size={16} />{platform === "Windows" ? "EXE" : "APK"} 다운로드</a>}
      </div>;
    })}
    <div className="download-platform"><Apple /><div><strong>iOS</strong><small>준비 중</small></div></div>
    {error && <Notice>{error} 잠시 후 다시 열어 주세요.</Notice>}
    <Notice>연결 중인 기기의 화면 개수·해상도·주사율·기기 정보와 배경화면 적용 상태를 표시합니다. Android Live 2D의 첫 적용은 기기에서 승인해야 합니다.</Notice>
  </>;
}
