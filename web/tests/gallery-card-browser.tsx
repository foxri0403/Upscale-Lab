import { createRoot } from "react-dom/client";
import { ImageCard } from "../src/ImageCard";
import { projectPicture } from "../src/cloud-store";
import { galleryPicture } from "../src/explore-feed";
import { selectableTags } from "../src/image-tags";
import { defaultEdit } from "../src/model";
import "../src/styles.css";
import "../src/responsive.css";
import "../src/gallery-discovery.css";

const createdAt = "2026-11-13T00:00:00Z";
const saved = projectPicture({
  id: "saved",
  title: "저장한 배경화면",
  originalImageUrl: "/images/forest.jpg",
  status: "Uploaded",
  createdAt,
  edit: { ...defaultEdit, savedAt: createdAt, tags: [...selectableTags], isReadOnly: true, sourceUploaderName: "ForestStudio" },
});
const legacy = { ...saved, id: "legacy", name: "이전에 저장한 배경화면", src: "/images/lake.jpg", creator: undefined, tags: [] };
const published = galleryPicture({
  id: "published",
  userId: "author",
  username: "ForestStudio",
  title: "공개 배경화면",
  imageUrl: "/images/forest.jpg",
  isPublic: true,
  tags: [...selectableTags],
  downloadCount: 4,
  createdAt,
});
const cardActions = { open: () => {}, editMetadata: () => {}, remove: () => {} };

createRoot(document.getElementById("root")!).render(<>
  <section aria-labelledby="saved-title">
    <h1 id="saved-title">내 이미지</h1>
    <div className="image-grid">
      <ImageCard picture={saved} explore={false} {...cardActions} />
      <ImageCard picture={legacy} explore={false} {...cardActions} />
    </div>
  </section>
  <section aria-labelledby="explore-title" className="explore-page" style={{ marginTop: 32 }}>
    <h2 id="explore-title">탐색</h2>
    <div className="image-grid">
      <ImageCard picture={published} explore {...cardActions} />
    </div>
  </section>
</>);
