import { useEffect, useMemo, useRef, type MouseEvent, type ReactNode, type RefObject } from "react";
import type { ReaderChapterDto } from "../../shared/api/types";
import { flattenEntries, type ReaderEntry } from "./entries";
import { COLUMN_GAP, usePagination } from "./usePagination";
import { spreadStart } from "./pagination";

interface Props {
  chapter: ReaderChapterDto;
  cover: ReactNode;
  page: number;
  contentRef: RefObject<HTMLDivElement | null>;
  onLayout: (layout: { count: number; ppv: 1 | 2; stride: number }) => void;
  onEntryClick: (entryIndex: number) => void;
}

export function PaginatedChapter({ chapter, cover, page, contentRef, onLayout, onEntryClick }: Props) {
  const viewportRef = useRef<HTMLDivElement>(null);
  const { ppv, metrics, count } = usePagination(viewportRef, contentRef, chapter);
  const { stride } = metrics;

  useEffect(() => onLayout({ count, ppv, stride }), [count, ppv, stride, onLayout]);

  const handleClick = (event: MouseEvent<HTMLElement>) => {
    const target = (event.target as HTMLElement).closest<HTMLElement>("[data-entry]");
    if (target) onEntryClick(Number(target.dataset.entry));
  };

  const paragraphs = useMemo(() => {
    const groups: ReaderEntry[][] = [];
    for (const entry of flattenEntries(chapter)) (groups[entry.paragraphIndex] ??= []).push(entry);
    return groups.filter(Boolean);
  }, [chapter]);

  return (
    <div ref={viewportRef} className="reader-viewport">
      <div
        ref={contentRef}
        data-testid="chapter-content"
        className="reader-content"
        onClick={handleClick}
        style={{
          width: metrics.contentWidth,
          columnWidth: metrics.colW,
          columnGap: COLUMN_GAP,
          transform: `translateX(${-spreadStart(page, ppv) * metrics.stride || 0}px)`,
        }}
      >
        {cover}
        <h2>{chapter.title}</h2>
        {paragraphs.map((entries) => (
          <p key={entries[0].paragraphIndex}>
            {entries.map((e) => (
              <span key={e.index} data-entry={e.index}>
                {e.text}{" "}
              </span>
            ))}
          </p>
        ))}
      </div>
    </div>
  );
}
