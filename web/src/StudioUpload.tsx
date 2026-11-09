import { useEffect, useRef, useState } from "react";
import { ImagePlus, Upload } from "lucide-react";
import { useDemo } from "./Workspace";
import { uploadImage } from "./model";
import { cloud } from "./api";
import { discardDraft, uploadProject, prepareEditableProject } from "./cloud-store";
import { Busy, navigate } from "./ui";
import { imageAccept, imageUploadDescription, validateStudioImageUpload } from "./image-upload";

export function StudioUpload() {
  const { library, update, toast } = useDemo();
  const mounted = useRef(true);
  useEffect(() => { mounted.current = true; return () => { mounted.current = false; }; }, []);
  const savedImages = library.images.filter(p => !p.isDraft && !p.isReadOnly);
  const input = useRef<HTMLInputElement>(null);
  const [busy, setBusy] = useState(false);
  async function add(file?: File) {
    if (!file || busy) return;
    setBusy(true);
    try {
      await validateStudioImageUpload(file);
      if (!mounted.current) return;
      const picture = cloud ? await uploadProject(file) : { ...await uploadImage(file), isDraft: true };
      if (!mounted.current) { if (cloud) void discardDraft(picture).catch(() => {}); return; }
      if (!update((value) => ({
        ...value,
        images: value.images.some((p) => p.id === picture.id)
          ? value.images : [picture, ...value.images],
      }))) return;
      navigate(`/app/studio?id=${encodeURIComponent(picture.id)}`, true);
      toast("편집을 시작했습니다. 보관함에 저장하면 내 이미지에 반영됩니다.");
    } catch (error) {
      toast((error as Error).message);
    } finally {
      setBusy(false);
      if (input.current) input.current.value = "";
    }
  }
  async function openSaved(picture: (typeof savedImages)[number]) {
    if (busy) return;
    if (!cloud || !picture.assetId || picture.projectId) {
      navigate(`/app/studio?id=${encodeURIComponent(picture.id)}`);
      return;
    }
    setBusy(true);
    try {
      const editable = await prepareEditableProject(picture);
      if (!mounted.current) return;
      update(value => ({ ...value, images: [editable, ...value.images] }));
      navigate(`/app/studio?id=${encodeURIComponent(editable.id)}`);
    } catch (error) { toast((error as Error).message); }
    finally { setBusy(false); }
  }
  return (
    <section className="studio-upload" aria-busy={busy}>
      <div className="page-heading">
        <div><h1>새 이미지 작업실</h1><p>편집할 이미지를 추가해 주세요.</p></div>
      </div>
      <div
        className="studio-upload-zone"
        onDragOver={(event) => event.preventDefault()}
        onDrop={(event) => {
          event.preventDefault();
          void add(event.dataTransfer.files[0]);
        }}
      >
        <ImagePlus size={46} />
        <h2>이곳에서 새 이미지를 시작하세요</h2>
        <p>정적 이미지를 끌어 놓거나 파일을 선택해 주세요.</p>
        <button className="button primary" disabled={busy} onClick={() => input.current?.click()}>
          {busy ? <Busy /> : <Upload size={18} />}
          {busy ? "이미지 추가 중…" : "이미지 파일 선택"}
        </button>
        <small>{imageUploadDescription}</small>
        <input
          ref={input}
          type="file"
          className="visually-hidden"
          aria-label="작업실 이미지 파일"
          accept={imageAccept}
          disabled={busy}
          onChange={(event) => void add(event.target.files?.[0])}
        />
      </div>
      {!!savedImages.length && (
        <div className="studio-recent-images">
          <h2>내 이미지에서 열기</h2>
          <div className="filmstrip">
            {savedImages.slice(0, 8).map((picture) => (
              <button key={picture.id} title={picture.name}
                aria-label={`${picture.name} 작업실에서 열기`}
                disabled={busy}
                onClick={() => void openSaved(picture)}>
                <img src={picture.src} alt={picture.name} />
              </button>
            ))}
          </div>
        </div>
      )}
    </section>
  );
}
