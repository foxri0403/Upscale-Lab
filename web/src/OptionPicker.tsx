import { useEffect, useId, useRef, useState, type ReactNode } from "react";
import { Check, ChevronDown } from "lucide-react";

export function OptionPicker({ label, value, options, change, icon, disabled, className = "" }: {
  label: string;
  value: string;
  options: { value: string; label: string }[];
  change: (value: string) => void;
  icon?: ReactNode;
  disabled?: boolean;
  className?: string;
}) {
  const id = useId();
  const root = useRef<HTMLDivElement>(null);
  const trigger = useRef<HTMLButtonElement>(null);
  const [open, setOpen] = useState(false);
  const expanded = open && !disabled;
  const selectedLabel = options.find(option => option.value === value)?.label ?? "선택 항목 없음";

  useEffect(() => {
    if (!expanded) return;
    const outside = (event: PointerEvent) => {
      if (!root.current?.contains(event.target as Node)) setOpen(false);
    };
    document.addEventListener("pointerdown", outside);
    return () => document.removeEventListener("pointerdown", outside);
  }, [expanded]);

  function close() {
    setOpen(false);
    trigger.current?.focus();
  }

  return <div className={`option-picker ${className}`} ref={root}
    onBlur={event => { if (!event.currentTarget.contains(event.relatedTarget)) setOpen(false); }}
    onKeyDown={event => {
      if (event.key === "Escape" && expanded) {
        event.preventDefault();
        event.stopPropagation();
        close();
      }
    }}>
    <button type="button" ref={trigger} className="option-picker-trigger" disabled={disabled || !options.length}
      aria-expanded={expanded} aria-controls={id} aria-label={`${label}, ${selectedLabel}`}
      onClick={() => setOpen(!open)}>
      {icon}<span>{selectedLabel}</span><ChevronDown size={17} aria-hidden="true" />
    </button>
    <div id={id} className="option-picker-list" role="group" aria-label={label} hidden={!expanded}>
      {options.map(option => <button type="button" key={option.value} aria-pressed={option.value === value}
        onClick={() => { change(option.value); close(); }}>
        <span>{option.label}</span>{option.value === value && <Check size={16} aria-hidden="true" />}
      </button>)}
    </div>
  </div>;
}
