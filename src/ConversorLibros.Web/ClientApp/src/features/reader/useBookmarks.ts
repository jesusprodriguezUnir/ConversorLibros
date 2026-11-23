import { useCallback, useEffect, useRef, useState } from "react";
import { api } from "../../shared/api/client";
import type { ReaderBookmarkDto, ReaderIndexChapterDto } from "../../shared/api/types";
import { entryAtMs, type ReaderEntry } from "./entries";

export const MAX_EXCERPT_LENGTH = 160;

interface Options {
  id: string;
  audio: HTMLAudioElement;
  chapters: readonly ReaderIndexChapterDto[];
  /** Chapter currently open; null until the resume position has been resolved. */
  current: number | null;
  /** True once the audio and text of `current` are loaded. */
  ready: boolean;
  /** Duration of `current`, used to clamp a jump past the chapter end. */
  durationMs: number;
  entries: readonly ReaderEntry[];
  selectChapter: (chapterIndex: number) => void;
  seekTo: (ms: number) => void;
}

/** Chapter, then position; `sort` is stable so equal keys keep their order. */
const sortBookmarks = (list: readonly ReaderBookmarkDto[]) =>
  [...list].sort((a, b) => a.chapterIndex - b.chapterIndex || a.ms - b.ms);

/** The server caps at 160 UTF-16 units: never leave half a surrogate pair at the cut. */
function truncateExcerpt(text: string): string {
  const cut = text.slice(0, MAX_EXCERPT_LENGTH);
  const last = cut.charCodeAt(cut.length - 1);
  return cut.length < text.length && last >= 0xd800 && last <= 0xdbff ? cut.slice(0, -1) : cut;
}

/** Bookmark list plus add / delete / jump. A jump into another chapter waits for it to load, then seeks once. */
export function useBookmarks({ id, audio, chapters, current, ready, durationMs, entries, selectChapter, seekTo }: Options) {
  const [bookmarks, setBookmarks] = useState<ReaderBookmarkDto[]>([]);
  const [error, setError] = useState<string | null>(null);
  const pendingRef = useRef<{ chapter: number; ms: number } | null>(null);
  /** Jump already applied, kept until `loadedmetadata` in case the browser dropped the seek. */
  const appliedRef = useRef<{ chapter: number; ms: number } | null>(null);
  const currentRef = useRef(current);
  currentRef.current = current;
  const idRef = useRef(id);
  idRef.current = id;
  const addingRef = useRef(false);
  const [adding, setAdding] = useState(false);

  // A missing or unreadable list simply shows no bookmarks.
  useEffect(() => {
    let cancelled = false;
    setBookmarks([]);
    api
      .getReaderBookmarks(id)
      .then((list) => !cancelled && setBookmarks(Array.isArray(list) ? sortBookmarks(list) : []))
      .catch(() => undefined);
    return () => {
      cancelled = true;
    };
  }, [id]);

  const add = useCallback(async () => {
    if (current === null || !ready || addingRef.current) return;
    const ms = Math.round(audio.currentTime * 1000);
    const text = entries[entryAtMs(entries, ms)]?.text.trim();
    addingRef.current = true;
    setAdding(true);
    setError(null);
    try {
      const created = await api.addReaderBookmark(id, {
        chapterIndex: current,
        ms,
        excerpt: text ? truncateExcerpt(text) : null,
      });
      if (idRef.current === id) setBookmarks((list) => sortBookmarks([...list, created]));
    } catch {
      if (idRef.current === id) setError("No se pudo guardar el marcador.");
    } finally {
      addingRef.current = false;
      setAdding(false);
    }
  }, [id, audio, current, ready, entries]);

  const remove = useCallback(
    async (bookmark: ReaderBookmarkDto) => {
      setError(null);
      try {
        await api.deleteReaderBookmark(id, bookmark.id);
        if (idRef.current === id) setBookmarks((list) => list.filter((b) => b.id !== bookmark.id));
      } catch {
        if (idRef.current === id) setError("No se pudo eliminar el marcador.");
      }
    },
    [id]
  );

  const jump = useCallback(
    (bookmark: ReaderBookmarkDto) => {
      if (!chapters.some((c) => c.index === bookmark.chapterIndex)) return;
      const ms = Math.max(0, bookmark.ms);
      if (bookmark.chapterIndex === current && ready) {
        const clamped = Math.min(ms, Math.max(0, durationMs));
        appliedRef.current = null;
        seekTo(clamped);
        return;
      }
      pendingRef.current = { chapter: bookmark.chapterIndex, ms };
      if (bookmark.chapterIndex !== current) selectChapter(bookmark.chapterIndex);
    },
    [chapters, current, ready, durationMs, seekTo, selectChapter]
  );

  // Pending jump: once its chapter is loaded, seek (clamped to the chapter end) exactly once.
  useEffect(() => {
    const pending = pendingRef.current;
    if (!ready || !pending || pending.chapter !== current) return;
    const ms = Math.min(pending.ms, Math.max(0, durationMs));
    pendingRef.current = null;
    appliedRef.current = { chapter: pending.chapter, ms };
    seekTo(ms);
  }, [ready, current, durationMs, seekTo]);

  // The browser may drop a seek made before the metadata is known: apply it once more.
  useEffect(() => {
    const onMetadata = () => {
      const applied = appliedRef.current;
      appliedRef.current = null;
      if (applied && applied.chapter === currentRef.current && Math.abs(audio.currentTime * 1000 - applied.ms) > 1) {
        audio.currentTime = applied.ms / 1000;
      }
    };
    audio.addEventListener("loadedmetadata", onMetadata);
    return () => audio.removeEventListener("loadedmetadata", onMetadata);
  }, [audio]);

  // A jump belongs to its chapter: picking another one by hand discards it.
  useEffect(() => {
    if (pendingRef.current && pendingRef.current.chapter !== current) pendingRef.current = null;
    if (appliedRef.current && appliedRef.current.chapter !== current) appliedRef.current = null;
  }, [current]);

  return { bookmarks, error, adding, add, remove, jump };
}
