import type { ReaderChapterDto } from "../../shared/api/types";

export interface ReaderEntry {
  index: number;
  paragraphIndex: number;
  text: string;
  startMs: number;
  endMs: number;
}

export function flattenEntries(chapter: ReaderChapterDto): ReaderEntry[] {
  const entries: ReaderEntry[] = [];
  chapter.paragraphs.forEach((paragraph, paragraphIndex) => {
    for (const s of paragraph.sentences) {
      entries.push({
        index: entries.length,
        paragraphIndex,
        text: s.text,
        startMs: s.startMs,
        endMs: s.endMs,
      });
    }
  });
  return entries;
}

/** Index of the entry containing `ms` (binary search); clamps to the last entry; -1 when empty. */
export function entryAtMs(entries: readonly ReaderEntry[], ms: number): number {
  let lo = 0;
  let hi = entries.length - 1;
  let found = -1;
  while (lo <= hi) {
    const mid = (lo + hi) >> 1;
    if (entries[mid].startMs <= ms) {
      found = mid;
      lo = mid + 1;
    } else {
      hi = mid - 1;
    }
  }
  return found;
}
