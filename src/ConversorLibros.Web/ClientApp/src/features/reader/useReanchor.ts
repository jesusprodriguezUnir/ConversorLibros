import { useLayoutEffect, useRef, type RefObject } from "react";
import { entryPages } from "./useAudioSync";

interface Options {
  contentRef: RefObject<HTMLElement | null>;
  /** Identity of the rendered chapter; an anchor never crosses chapters. */
  chapterKey: unknown;
  /** Current (unclamped) page, ppv and column stride. */
  page: number;
  ppv: number;
  stride: number;
  count: number;
  /** Changes whenever something other than the viewport width reflows the text (font size, web fonts). */
  relayoutKey: string;
  /** Entry to keep in view (the active one while following playback), or -1 to use the first visible entry. */
  followedEntry: () => number;
  onPage: (page: number) => void;
}

/** First entry that starts on the current spread; null when the spread holds none (e.g. the cover). */
function firstVisibleEntry(content: HTMLElement, page: number, ppv: number, stride: number): number | null {
  const first = page - (page % ppv);
  for (const el of content.querySelectorAll<HTMLElement>("[data-entry]")) {
    const index = Number(el.dataset.entry);
    const [start] = entryPages(content, index, stride);
    if (start === undefined || start < first) continue;
    return start < first + ppv ? index : null;
  }
  return null;
}

/**
 * Keeps the reader on the same text when the layout changes: remembers the first visible entry per page and,
 * when the page geometry changes (resize, font size, web fonts), moves to the page that now holds the entry.
 */
export function useReanchor(options: Options) {
  const optsRef = useRef(options);
  optsRef.current = options;
  const anchor = useRef<{ chapter: unknown; entry: number | null }>({ chapter: undefined, entry: null });
  const signature = useRef("");
  const { chapterKey, page, ppv, stride, count, relayoutKey } = options;
  const current = `${stride}:${ppv}:${count}:${relayoutKey}`;

  useLayoutEffect(() => {
    const { contentRef, followedEntry, onPage } = optsRef.current;
    const content = contentRef.current;
    if (!content) return;
    const previous = signature.current;
    signature.current = current;
    if (previous !== current && anchor.current.chapter === chapterKey) {
      const followed = followedEntry();
      const entry = followed >= 0 ? followed : anchor.current.entry;
      const [target] = entry === null ? [] : entryPages(content, entry, stride);
      anchor.current = { chapter: chapterKey, entry };
      if (target !== undefined) onPage(target);
      return;
    }
    // Within a chapter the previous anchor survives spreads that start no entry, and stays put while it is still
    // visible, so a re-anchor never drifts back to the spread's first entry.
    const kept = anchor.current.chapter === chapterKey ? anchor.current.entry : null;
    const [keptStart] = kept === null ? [] : entryPages(content, kept, stride);
    const first = page - (page % ppv);
    const stillVisible = keptStart !== undefined && keptStart >= first && keptStart < first + ppv;
    const visible = stillVisible ? kept : firstVisibleEntry(content, page, ppv, stride);
    anchor.current = { chapter: chapterKey, entry: visible ?? kept };
  }, [current, chapterKey, page, ppv, stride]);
}
