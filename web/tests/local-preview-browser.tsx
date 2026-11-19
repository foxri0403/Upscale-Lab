import { useState } from "react";
import { createRoot } from "react-dom/client";
import { demoEmail, demoPassword, signIn } from "../src/auth";
import { defaultEdit, read, sampleLibrary, write, type Library, type Picture } from "../src/model";
import { showcaseCharacter, showcaseRig } from "../src/showcase-character";
import "../src/styles.css";
import "./local-preview.css";

function Preview() {
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState("");
  async function open(path: string) {
    if (busy) return;
    setBusy(true); setError("");
    try {
      const session = await signIn(demoEmail, demoPassword, false);
      const key = `upscale.demo.library.v1.${session.id}`;
      const library = read<Library>(key, sampleLibrary());
      const created = "2026-11-20T00:00:00Z";
      const layers = [...showcaseCharacter.layers].sort((a, b) => a.renderOrder - b.renderOrder);
      const character: Picture = {
        id: "local-preview-character", name: "Live 2D 테스트 캐릭터", src: "/images/live2d-character-v2.png",
        width: showcaseCharacter.width, height: showcaseCharacter.height, genre: "애니", tags: ["애니"],
        created, features: ["Live 2D"],
        layers: layers.map((layer, layerOrder) => ({ id: layer.name, layerType: layer.semanticType,
          layerOrder, automaticOrder: layerOrder, imageUrl: layer.src, movementX: 0, movementY: 0 })),
        edit: { ...defaultEdit, rig: { ...showcaseRig, faceEnabled: false, bodyMode: "fixed",
          partAssignments: Object.fromEntries(layers.map(layer => [layer.name, layer.part])) } },
      };
      const saved: Picture = { id: "local-preview-saved", name: "다른 사용자가 만든 저장 이미지", src: "/images/forest.jpg",
        width: 1600, height: 900, genre: "풍경", tags: ["풍경"], creator: "ForestStudio", isReadOnly: true, created };
      write(key, { ...library, images: [...library.images, ...[character, saved].filter(p => !library.images.some(old => old.id === p.id))] });
      const shared = read<Picture[]>("upscale.demo.shared.v1", []);
      const recommendations = [80, 42, 5].map((likes, index): Picture => ({
        id: `local-preview-recommendation-${index}`, name: `추천 테스트 · 좋아요 ${likes}개`, ownerId: "local-preview-author",
        src: ["/images/lake.jpg", "/images/forest.jpg", "/images/mountain.jpg"][index],
        genre: "풍경", tags: ["풍경"], creator: "PreviewStudio", likes, downloads: 0, created,
      }));
      write("upscale.demo.shared.v1", [...shared, ...recommendations.filter(p => !shared.some(old => old.id === p.id))]);
      location.assign(path);
    } catch (cause) { setError((cause as Error).message); setBusy(false); }
  }
  return <main className="local-preview">
    <h1>Upscale Lab 로컬 테스트</h1>
    <p>샘플 계정과 저장된 이미지로 변경된 화면을 확인합니다. 이미지 처리는 아래 가상 진행률 화면에서 재생할 수 있습니다.</p>
    <section className="local-preview-links" aria-label="테스트 화면">
      <button className="button primary" disabled={busy} onClick={() => void open("/app/studio?id=local-preview-character&tool=live")}>Live 2D 작업실 열기</button>
      <button className="button secondary" disabled={busy} onClick={() => void open("/app/explore")}>추천 이미지 좋아요순 확인</button>
      <button className="button secondary" disabled={busy} onClick={() => void open("/app/images")}>내 이미지 클릭 동작 확인</button>
      <a className="button secondary" href="/tests/processing-ui.html">처리 팝업 가상 진행률 테스트</a>
      <a className="button secondary" href="/tests/signup-flow.html?scenario=expired">회원가입 만료 화면 확인</a>
    </section>
    {error && <p role="alert">{error}</p>}
    <ul>
      <li>작업실: 얼굴·몸 반응 추가 후 세부 설정 활성화, 5:5 버튼 폭과 회색 글씨를 확인합니다.</li>
      <li>자르기: 비율 설정에 Live 2D 기능과 설명이 없는지 확인합니다.</li>
      <li>탐색: 좋아요 80·42·5개 샘플의 추천 순서를 확인합니다.</li>
      <li>내 이미지: ‘다른 사용자가 만든 저장 이미지’의 사진·제목을 눌러도 창이 열리지 않습니다. 기기 적용은 이미지 설정 메뉴에서 확인합니다.</li>
    </ul>
    <p>샘플 데이터는 이 브라우저에 저장됩니다. DB의 가입 만료·계정 삭제는 <code>AuthServiceTests</code>와 <code>AccountServiceTests</code>로 검증합니다.</p>
  </main>;
}
createRoot(document.getElementById("root")!).render(<Preview />);
