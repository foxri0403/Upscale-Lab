import { useEffect, useRef, useState, type PointerEvent } from "react";

export function usePreviewPan(enabled: boolean, primary = true) {
  const root = useRef<HTMLDivElement>(null), drag = useRef<{ id: number; x: number; y: number; left: number; top: number } | null>(null);
  const [space, setSpace] = useState(false), [dragging, setDragging] = useState(false);
  const moved = useRef(false);
  useEffect(() => {
    const down = (e: KeyboardEvent) => {
      if (e.code !== "Space" || /INPUT|TEXTAREA|SELECT|BUTTON/.test((e.target as HTMLElement)?.tagName) || (e.target as HTMLElement)?.isContentEditable) return;
      e.preventDefault(); setSpace(true);
    };
    const up = (e: KeyboardEvent) => { if (e.code === "Space") setSpace(false); };
    const blur = () => { setSpace(false); drag.current = null; setDragging(false); };
    window.addEventListener("keydown", down); window.addEventListener("keyup", up); window.addEventListener("blur", blur);
    return () => { window.removeEventListener("keydown", down); window.removeEventListener("keyup", up); window.removeEventListener("blur", blur); };
  }, []);
  useEffect(() => { if (!enabled) { root.current?.scrollTo(0, 0); drag.current = null; setDragging(false); } }, [enabled]);
  const finish = (e: PointerEvent<HTMLDivElement>) => {
    if (drag.current?.id !== e.pointerId) return;
    e.stopPropagation(); drag.current = null; setDragging(false);
    if (e.currentTarget.hasPointerCapture(e.pointerId)) e.currentTarget.releasePointerCapture(e.pointerId);
  };
  return { root, canPan: enabled && (primary || space), dragging, events: {
    onPointerDownCapture: (e: PointerEvent<HTMLDivElement>) => {
      if (!enabled || !e.isPrimary || !(e.button === 1 || (e.button === 0 && (primary || space)))) return;
      if ((e.target as HTMLElement).closest("button,select,input") && !(e.target as HTMLElement).closest(".studio-palette-toggle")) return;
      e.preventDefault(); e.stopPropagation();
      moved.current = false;
      drag.current = { id: e.pointerId, x: e.clientX, y: e.clientY, left: e.currentTarget.scrollLeft, top: e.currentTarget.scrollTop };
      e.currentTarget.setPointerCapture(e.pointerId); setDragging(true);
    },
    onPointerMoveCapture: (e: PointerEvent<HTMLDivElement>) => {
      const d = drag.current; if (!d || d.id !== e.pointerId) return;
      if (Math.hypot(d.x - e.clientX, d.y - e.clientY) > 4) moved.current = true;
      e.preventDefault(); e.stopPropagation(); e.currentTarget.scrollLeft = d.left + d.x - e.clientX; e.currentTarget.scrollTop = d.top + d.y - e.clientY;
    }, onPointerUpCapture: finish, onPointerCancelCapture: finish, onLostPointerCapture: finish,
    onClickCapture: (e: React.MouseEvent) => { if (moved.current) { e.preventDefault(); e.stopPropagation(); moved.current = false; } },
    onDragStart: (e: React.DragEvent) => e.preventDefault(),
  } };
}
