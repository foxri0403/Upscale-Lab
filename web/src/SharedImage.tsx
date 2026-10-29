import { useEffect, useState } from "react";
import { Download } from "lucide-react";
import { cloud, request, type Manifest } from "./api";
import { defaultEdit, downloadImage, read, type Picture } from "./model";
import { Brand, Busy, Notice, PictureView } from "./ui";
import { imageFeatures } from "./image-features";

export function SharedImage({ token }: { token: string }) {
  const [picture, setPicture] = useState<Picture | null>(null);
  const [error, setError] = useState("");
  const [busy, setBusy] = useState(false);
  useEffect(() => {
    let active = true;
    setPicture(null); setError("");
    const load = async () => {
      try {
        let value: Picture;
        if (cloud) {
          const link = await request<{ title: string; manifest: Manifest; features?: string[] }>(
            `/api/wallpapers/links/${encodeURIComponent(token)}`, {}, false);
          const files = [...link.manifest.files].sort((a, b) => a.order - b.order);
          value = { id: token, name: link.title, src: files[0]?.source || "", genre: "장르 설정되지 않음", created: "",
            width: link.manifest.width, height: link.manifest.height, features: link.features,
            exportSources: { image: `/api/wallpapers/links/${encodeURIComponent(token)}/content/${files[0]?.order ?? 0}`,
              background: link.manifest.mode === "Live2D" ? `/api/wallpapers/links/${encodeURIComponent(token)}/content/${files.find(file => file.role === "Background")?.order ?? 0}` : undefined,
              foregrounds: link.manifest.mode === "Live2D" ? files.filter(file => file.role !== "Background").map(file => `/api/wallpapers/links/${encodeURIComponent(token)}/content/${file.order}`) : undefined },
            ...(link.manifest.mode === "Live2D" ? {
              background: files.find(file => file.role === "Background")?.source,
              foregrounds: files.filter(file => file.role !== "Background").map(file => file.source),
              edit: { ...defaultEdit, liveEnabled: link.features?.includes("Live 2D") ?? false },
            } : {}) };
        } else {
          const local = read<Picture | null>(`upscale.demo.link.${token}`, null);
          if (!local) throw new Error("공유 링크를 찾을 수 없습니다.");
          value = local;
        }
        if (active) { setPicture(value); setError(""); }
      } catch (e) { if (active) setError((e as Error).message); }
    };
    void load();
    const timer = setInterval(() => void load(), 10 * 60 * 1000);
    return () => { active = false; clearInterval(timer); };
  }, [token]);
  return <main className="shared-image-page">
    <Brand to="/" />
    {error ? <Notice>{error}</Notice> : !picture ? <Busy /> : <>
      <h1>{picture.name}</h1>
      <div className="shared-image-preview"><PictureView picture={picture} /></div>
      <div className="card-meta">{imageFeatures(picture).map(feature => <span className="type-badge" key={feature}>{feature}</span>)}</div>
      <button className="button primary" disabled={busy} onClick={async () => {
        setBusy(true);
        try { await downloadImage(picture, picture.edit || defaultEdit); }
        catch (e) { setError((e as Error).message); }
        finally { setBusy(false); }
      }}><Download size={18} />다운로드</button>
      {!cloud && <Notice>이 미리보기 링크는 같은 브라우저에서만 열 수 있습니다.</Notice>}
    </>}
  </main>;
}
