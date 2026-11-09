import { useEffect, useState } from "react";
import { fetchExplore } from "./cloud-store";
import type { Picture } from "./model";

export function useExploreResults(query: string, revision: Picture[], enabled: boolean) {
  const [result, setResult] = useState<{ query: string; pictures?: Picture[]; error?: string }>();
  useEffect(() => {
    if (!enabled || !query) return;
    const controller = new AbortController();
    const timer = window.setTimeout(() => {
      fetchExplore(new URLSearchParams(query), controller.signal).then(
        pictures => { if (!controller.signal.aborted) setResult({ query, pictures }); },
        (error: Error) => { if (!controller.signal.aborted) setResult({ query, error: error.message }); },
      );
    }, 180);
    return () => { window.clearTimeout(timer); controller.abort(); };
  }, [query, revision, enabled]);
  return enabled && result?.query === query ? result : undefined;
}
