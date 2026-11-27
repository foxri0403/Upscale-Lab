import { useEffect, useRef, useState } from "react";
import { cloud, request } from "./api";
import type { Picture } from "./model";

type WebpView = { source: string; background?: string | null; foregrounds?: string[] | null };

export function useStoredWebp(picture: Picture, enabled: boolean) {
  const element = useRef<HTMLSpanElement>(null);
  const path = enabled && cloud && (picture.projectId ? ["projects", picture.projectId]
    : picture.assetId ? ["wallpapers", picture.assetId] : undefined);
  const endpoint = path ? `/api/web-images/${path[0]}/${encodeURIComponent(path[1])}` : "";
  const revision = endpoint ? `${endpoint}:${picture.jobId ?? ""}` : "";
  const [result, setResult] = useState<{ revision: string; view?: WebpView }>();

  useEffect(() => {
    if (!endpoint) return;
    const controller = new AbortController();
    const load = () => {
      const signal = AbortSignal.any([controller.signal, AbortSignal.timeout(45000)]);
      request<WebpView>(endpoint, { signal }).then(
        view => { if (!controller.signal.aborted) setResult({ revision, view }); },
        () => { if (!controller.signal.aborted) setResult({ revision }); },
      );
    };
    const target = element.current;
    if (!target || typeof IntersectionObserver === "undefined") {
      load();
      return () => controller.abort();
    }
    const observer = new IntersectionObserver(entries => {
      if (!entries.some(entry => entry.isIntersecting)) return;
      observer.disconnect();
      load();
    }, { rootMargin: "240px" });
    observer.observe(target);
    return () => { observer.disconnect(); controller.abort(); };
  }, [endpoint, revision]);

  return {
    element,
    pending: !!revision && result?.revision !== revision,
    view: revision && result?.revision === revision ? result.view : undefined,
    fallback: () => setResult({ revision }),
  };
}
