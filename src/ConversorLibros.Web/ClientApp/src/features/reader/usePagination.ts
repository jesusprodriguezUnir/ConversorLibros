import { useLayoutEffect, useState, type RefObject } from "react";
import { columnMetrics, pageCount, pagesPerView, type ColumnMetrics } from "./pagination";

export const COLUMN_GAP = 48;

export interface PaginationState {
  ppv: 1 | 2;
  metrics: ColumnMetrics;
  count: number;
}

/** Measures the viewport and the rendered columns; `contentKey` re-measures when the content changes. */
export function usePagination(
  viewportRef: RefObject<HTMLElement | null>,
  contentRef: RefObject<HTMLElement | null>,
  contentKey: unknown
): PaginationState {
  const [width, setWidth] = useState(0);
  const [count, setCount] = useState(1);
  const ppv = pagesPerView(width);
  const metrics = columnMetrics(width, COLUMN_GAP, ppv);

  useLayoutEffect(() => {
    const el = viewportRef.current;
    if (!el) return;
    const read = () => setWidth(el.clientWidth);
    read();
    if (typeof ResizeObserver === "undefined") return;
    const observer = new ResizeObserver(read);
    observer.observe(el);
    return () => observer.disconnect();
  }, [viewportRef]);

  useLayoutEffect(() => {
    const content = contentRef.current;
    if (content) setCount(pageCount(content.scrollWidth, metrics, COLUMN_GAP));
  }, [contentRef, contentKey, metrics.colW]); // eslint-disable-line react-hooks/exhaustive-deps

  return { ppv, metrics, count };
}
