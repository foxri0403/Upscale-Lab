import { useEffect, useRef } from "react";
import { setNavigationGuard } from "./navigation";

export function useUnsavedWarning(active: boolean, discard: () => void) {
  const current = useRef({ active, discard });
  current.current = { active, discard };
  useEffect(() => {
    const confirm = () => {
      if (!current.current.active) return true;
      if (!window.confirm("저장하지 않은 이미지 또는 변경 사항이 있습니다. 저장하지 않고 나가시겠습니까?")) return false;
      current.current.discard(); return true;
    };
    const stop = setNavigationGuard(confirm);
    const unload = (event: BeforeUnloadEvent) => {
      if (current.current.active) { event.preventDefault(); event.returnValue = ""; }
    };
    const hide = () => { if (current.current.active) current.current.discard(); };
    const external = (event: MouseEvent) => {
      const link = (event.target as Element)?.closest?.("a");
      if (!link || link.hasAttribute("data-spa-link") || link.download || link.target === "_blank" ||
          event.ctrlKey || event.metaKey || event.shiftKey || event.button !== 0 || !link.href) return;
      if (!confirm()) { event.preventDefault(); event.stopImmediatePropagation(); }
    };
    window.addEventListener("beforeunload", unload);
    window.addEventListener("pagehide", hide);
    document.addEventListener("click", external, true);
    return () => {
      stop(); window.removeEventListener("beforeunload", unload); window.removeEventListener("pagehide", hide);
      document.removeEventListener("click", external, true);
    };
  }, []);
}
