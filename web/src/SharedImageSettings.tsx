import { useRef, useState } from "react";
import { PlatformTagOptions } from "./GalleryBadges";
import { imageTags, selectableTags } from "./image-tags";
import type { Picture } from "./model";
import type { PlatformTag } from "./platform-tags";
import { Busy, Modal } from "./ui";

export type SharedImageMetadata = { title: string; tags: string[]; platformTags: PlatformTag[] };

export function SharedImageSettings({ picture, close, save }: {
  picture: Picture;
  close: () => void;
  save: (metadata: SharedImageMetadata) => Promise<boolean>;
}) {
  const [title, setTitle] = useState(picture.name);
  const [tags, setTags] = useState(() => imageTags(picture));
  const [platformTags, setPlatformTags] = useState<PlatformTag[]>(picture.platformTags ?? []);
  const [busy, setBusy] = useState(false);
  const pending = useRef(false);
  return <Modal title="이름 및 태그 수정" close={() => { if (!pending.current) close(); }}>
    <form className="shared-image-settings" onSubmit={async event => {
      event.preventDefault();
      if (!title.trim() || pending.current) return;
      pending.current = true;
      setBusy(true);
      try { if (await save({ title: title.trim(), tags, platformTags })) close(); }
      finally { pending.current = false; setBusy(false); }
    }}>
      <fieldset disabled={busy}>
        <label>이미지 이름<input value={title} onChange={event => setTitle(event.target.value)} maxLength={160} required /></label>
        <h3>상위 태그</h3>
        <PlatformTagOptions selected={platformTags} change={setPlatformTags} />
        <h3>하위 태그</h3>
        <div className="studio-tag-options" role="group" aria-label="하위 태그">
          {selectableTags.map(tag => <label key={tag}><input type="checkbox" checked={tags.includes(tag)}
            onChange={event => setTags(current => event.target.checked ? [...current, tag] : current.filter(item => item !== tag))} />
            <span>{tag}</span>
          </label>)}
        </div>
      </fieldset>
      <button className="button primary full" disabled={busy || !title.trim()}>{busy ? <Busy /> : "변경 저장"}</button>
    </form>
  </Modal>;
}
