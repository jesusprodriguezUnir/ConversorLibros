import { useEffect, useMemo, useRef, type ReactNode } from "react";
import type { ReaderChapterDto } from "../../shared/api/types";
import { flattenEntries, type ReaderEntry } from "./entries";
import { COLUMN_GAP, usePagination } from "./usePagination";
import { spreadStart } from "./pagination";

interface Props {
  chapter: ReaderChapterDto;
  cover: ReactNode;
  page: number;
  onLayout: (layout: { count: number; ppv: 1 | 2 }) => void;
}

export function PaginatedChapter({ chapter, cover, page, onLayout }: Props) {
  const viewportRef = useRef<HTMLDivElement>(null);
  const contentRef = useRef<HTMLDivElement>(null);
  const { ppv, metrics, count } = usePagination(viewportRef, contentRef, chapter);

  useEffect(() => onLayout({ count, ppv }), [count, ppv, onLayout]);

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
