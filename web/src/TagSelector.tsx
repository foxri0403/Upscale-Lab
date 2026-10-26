import { useEffect, useRef } from "react";
import { ChevronDown, Tag } from "lucide-react";
import { allTagsSelected, selectableTags, tagSummary } from "./image-tags";

export function TagSelector({
  selected,
  change,
}: {
  selected: string[];
  change: (tags: string[]) => void;
}) {
  const ref = useRef<HTMLDetailsElement>(null);
  const allRef = useRef<HTMLInputElement>(null);
  const all = allTagsSelected(selected);
  useEffect(() => {
    if (allRef.current)
      allRef.current.indeterminate = selected.length > 0 && !all;
  }, [selected, all]);
  useEffect(() => {
    const outside = (event: PointerEvent) => {
      if (ref.current && !ref.current.contains(event.target as Node))
        ref.current.open = false;
    };
    document.addEventListener("pointerdown", outside);
    return () => document.removeEventListener("pointerdown", outside);
  }, []);
  return (
    <details
      className="tag-selector"
      ref={ref}
      onKeyDown={(event) => {
        if (event.key === "Escape" && ref.current) {
          ref.current.open = false;
          ref.current.querySelector("summary")?.focus();
        }
      }}
    >
      <summary
        aria-label="장르 선택"
        title={selected.join(" · ") || "장르 없음"}
      >
        <Tag size={18} />
        <span>{all ? "전체 장르" : tagSummary(selected)}</span>
        <ChevronDown size={17} />
      </summary>
      <div className="tag-popover" role="group" aria-label="장르 필터">
        <label className="tag-option tag-select-all">
          <span>전체 선택</span>
          <input
            ref={allRef}
            type="checkbox"
            checked={all}
            onChange={(e) =>
              change(e.target.checked ? [...selectableTags] : [])
            }
          />
        </label>
        <div className="tag-options">
          {selectableTags.map((tag) => (
            <label className="tag-option" key={tag}>
              <span>{tag}</span>
              <input
                type="checkbox"
                checked={selected.includes(tag)}
                onChange={(e) =>
                  change(
                    e.target.checked
                      ? [...selected, tag]
                      : selected.filter((t) => t !== tag),
                  )
                }
              />
            </label>
          ))}
        </div>
        <div className="tag-selection-footer">
          <span aria-live="polite">
            {selected.length ? `${selected.length}개 선택` : "장르 없음"}
          </span>
          <button className="text-button" onClick={() => change([])}>
            선택 해제
          </button>
          <button
            className="button primary small"
            onClick={() => {
              if (ref.current) ref.current.open = false;
            }}
          >
            적용
          </button>
        </div>
      </div>
    </details>
  );
}
