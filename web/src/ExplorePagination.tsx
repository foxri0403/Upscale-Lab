import { useLayoutEffect, useState } from "react";

export function useExploreColumns(grid: HTMLDivElement | null, detailsOpen: boolean) {
  const [columns, setColumns] = useState(1);
  useLayoutEffect(() => {
    if (!grid) return;
    const measure = () => {
      // Resolved grid tracks reflect responsive CSS and the space taken by the inspector.
      const tracks = getComputedStyle(grid).gridTemplateColumns.trim();
      setColumns(tracks && tracks !== "none" ? tracks.split(/\s+/).length : 1);
    };
    measure();
    const observer = new ResizeObserver(measure);
    observer.observe(grid);
    window.addEventListener("resize", measure);
    return () => {
      observer.disconnect();
      window.removeEventListener("resize", measure);
    };
  }, [grid, detailsOpen]);
  return columns;
}

export function ExplorePagination({ page, pageCount, numbers, change }: {
  page: number;
  pageCount: number;
  numbers: number[];
  change: (page: number) => void;
}) {
  if (!pageCount) return null;
  return (
    <nav className="explore-pagination" aria-label="공유 이미지 페이지">
      <button type="button" className="button secondary" aria-label="첫 페이지"
        disabled={page === 1} onClick={() => change(1)}>{"<<"}</button>
      <button type="button" className="button secondary" aria-label="이전 페이지"
        disabled={page === 1} onClick={() => change(page - 1)}>{"<"}</button>
      {numbers[0] > 1 && <span aria-hidden="true">…</span>}
      {numbers.map(number => (
        <button type="button" className={"button " + (number === page ? "primary" : "secondary")}
          key={number} aria-label={number + "페이지"} aria-current={number === page ? "page" : undefined}
          onClick={() => change(number)}>{number}</button>
      ))}
      {numbers[numbers.length - 1] < pageCount && <span aria-hidden="true">…</span>}
      <button type="button" className="button secondary" aria-label="다음 페이지"
        disabled={page === pageCount} onClick={() => change(page + 1)}>{">"}</button>
      <button type="button" className="button secondary" aria-label="마지막 페이지"
        disabled={page === pageCount} onClick={() => change(pageCount)}>{">>"}</button>
    </nav>
  );
}
