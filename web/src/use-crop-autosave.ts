import { useEffect, useRef, useState } from "react";
import { LatestSaveQueue } from "./latest-save-queue";
import type { CropEdit } from "./crop-geometry";
import type { Picture } from "./model";

export type CropSave = { picture: Picture; crop: CropEdit };
export function useCropAutosave(write: (value: CropSave) => Promise<void>, failed: (error: Error) => void,
  pictureId: string | undefined, active: boolean) {
  const [pending, setPending] = useState(false), [error, setError] = useState("");
  const callbacks = useRef({ write, failed }); callbacks.current = { write, failed };
  const activePicture = useRef(pictureId); activePicture.current = pictureId;
  const mounted = useRef(true), queue = useRef<LatestSaveQueue<CropSave> | null>(null);
  queue.current ||= new LatestSaveQueue(value => callbacks.current.write(value), value => { if (mounted.current) setPending(value); },
    (reason, value) => { if (mounted.current && value.picture.id === activePicture.current) setError(reason.message); callbacks.current.failed(reason); });
  useEffect(() => { if (!active) void queue.current!.flush(); }, [active]);
  useEffect(() => { setError(""); void queue.current!.flush(); }, [pictureId]);
  useEffect(() => {
    mounted.current = true;
    const flush = () => { void queue.current!.flush(); };
    window.addEventListener("pagehide", flush);
    return () => { mounted.current = false; window.removeEventListener("pagehide", flush); flush(); };
  }, []);
  return { pending, error, enqueue: (value: CropSave) => { setError(""); queue.current!.enqueue(value.picture.id, value); },
    flush: () => queue.current!.flush() };
}
