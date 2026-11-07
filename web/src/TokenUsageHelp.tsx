import { useId, useLayoutEffect, useRef, useState } from "react";
import { TokenUsageTable } from "./TokenUsageTable";

export function TokenUsageHelp() {
  const id = useId();
  const root = useRef<HTMLDivElement>(null);
  const trigger = useRef<HTMLButtonElement>(null);
  const panel = useRef<HTMLDivElement>(null);
  const [open, setOpen] = useState(false);
  useLayoutEffect(() => {
    if (!open) return;
    const popup = panel.current!;
    const position = () => {
      const anchor = trigger.current!.getBoundingClientRect();
      const bounds = popup.getBoundingClientRect();
      const left = Math.max(12, Math.min(anchor.left, innerWidth - bounds.width - 12));
      const below = anchor.bottom + 8;
      const top = below + bounds.height <= innerHeight - 12 ? below : anchor.top - bounds.height - 8;
      popup.style.left = `${left}px`;
      popup.style.top = `${Math.max(12, Math.min(top, innerHeight - bounds.height - 12))}px`;
    };
    const dismissFocus = (event: FocusEvent) => {
      if (event.target instanceof Node && !root.current?.contains(event.target)) popup.hidePopover();
    };
    const dismissEscape = (event: KeyboardEvent) => {
      if (event.key !== "Escape") return;
      event.preventDefault(); popup.hidePopover(); trigger.current?.focus();
    };
    const dismissScroll = (event: Event) => {
      if (!(event.target instanceof Node) || !popup.contains(event.target)) popup.hidePopover();
    };
    position();
    window.addEventListener("resize", position);
    window.addEventListener("scroll", dismissScroll, true);
    document.addEventListener("focusin", dismissFocus);
    document.addEventListener("keydown", dismissEscape);
    return () => {
      window.removeEventListener("resize", position);
      window.removeEventListener("scroll", dismissScroll, true);
      document.removeEventListener("focusin", dismissFocus);
      document.removeEventListener("keydown", dismissEscape);
    };
  }, [open]);
  return <div className="token-usage-help" ref={root}>
    <button ref={trigger} type="button" className="token-usage-link" popoverTarget={id}
      aria-haspopup="dialog" aria-expanded={open} aria-controls={id}>
      소모 토큰량 확인
    </button>
    <div ref={panel} id={id} popover="auto" className="token-usage-popover" role="dialog"
      aria-labelledby={`${id}-title`} style={{ visibility: open ? "visible" : "hidden" }}
      onToggle={() => setOpen(!!panel.current?.matches(":popover-open"))}>
      <h2 id={`${id}-title`}>작업별 토큰 사용량</h2>
      <TokenUsageTable compact />
    </div>
  </div>;
}
