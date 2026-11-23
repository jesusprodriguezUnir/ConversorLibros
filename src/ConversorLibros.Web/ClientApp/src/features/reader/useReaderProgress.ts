import { useCallback, useEffect, useRef } from "react";
import { api } from "../../shared/api/client";
import type { ReaderIndexChapterDto, ReaderProgressDto } from "../../shared/api/types";

const SAVE_INTERVAL_MS = 10_000;

interface Options {
  id: string;
  audio: HTMLAudioElement;
  /** Chapter currently open; null until the resume position has been resolved. */
  current: number | null;
  playing: boolean;
  /** True once the audio and text of `current` are loaded. */
  ready: boolean;
  /** Duration of `current`, used to clamp the resume position. */
  durationMs: number;
  seekTo: (ms: number) => void;
}

/** Picks the chapter to open; a saved chapter missing from the index falls back to the first one. */
function resolveResume(chapters: readonly ReaderIndexChapterDto[], saved: ReaderProgressDto | null) {
  if (saved && typeof saved.chapterIndex === "number" && chapters.some((c) => c.index === saved.chapterIndex)) {
    return { chapter: saved.chapterIndex, ms: Math.max(0, Number(saved.ms) || 0) };
  }
  return { chapter: chapters[0].index, ms: 0 };
}

/** Restores and persists the single resume position (chapter + ms). Saving never blocks reading. */
export function useReaderProgress({ id, audio, current, playing, ready, durationMs, seekTo }: Options) {
  const loadedRef = useRef(false);
  /** Resume position still to be applied; only valid for its own chapter. */
  const pendingRef = useRef<{ chapter: number; ms: number } | null>(null);
  /** Resume seek already applied, kept until `loadedmetadata` in case the browser dropped it. */
  const appliedRef = useRef<{ chapter: number; ms: number } | null>(null);
  const lastSavedRef = useRef("");
  const currentRef = useRef(current);
  currentRef.current = current;
  const readyRef = useRef(ready);
  readyRef.current = ready;
  const previousChapterRef = useRef<number | null>(null);

  const save = useCallback(
    (chapterIndex: number, ms: number, keepalive = false) => {
      const key = `${chapterIndex}:${ms}`;
      if (key === lastSavedRef.current) return;
      lastSavedRef.current = key;
      api.saveReaderProgress(id, { chapterIndex, ms }, keepalive).catch(() => {
        // Non-blocking: forget the key so the next trigger retries.
        if (lastSavedRef.current === key) lastSavedRef.current = "";
      });
    },
    [id]
  );

  /** Saves where the audio is now; skipped until the chapter is loaded and its resume seek applied. */
  const saveNow = useCallback(
    (keepalive = false) => {
      const chapter = currentRef.current;
      if (chapter === null || !loadedRef.current || !readyRef.current) return;
      if (pendingRef.current?.chapter === chapter) return;
      save(chapter, Math.round(audio.currentTime * 1000), keepalive);
    },
    [audio, save]
  );

  /** Reads the saved position and returns the chapter to open; never rejects. */
  const loadResume = useCallback(
    async (chapters: readonly ReaderIndexChapterDto[]): Promise<number> => {
      const saved = await api.getReaderProgress(id).catch(() => null);
      const { chapter, ms } = resolveResume(chapters, saved);
      pendingRef.current = ms > 0 ? { chapter, ms } : null;
      lastSavedRef.current = `${chapter}:${ms}`;
      loadedRef.current = true;
      return chapter;
    },
    [id]
  );

  // Resume seek: the audio stays paused, the position is clamped to the chapter end.
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

  useEffect(() => {
    if (current === null) return;
    const previous = previousChapterRef.current;
    previousChapterRef.current = current;
    // A resume position belongs to its chapter: opening another one discards it.
    if (pendingRef.current && pendingRef.current.chapter !== current) pendingRef.current = null;
    if (appliedRef.current && appliedRef.current.chapter !== current) appliedRef.current = null;
    if (previous !== null && previous !== current && loadedRef.current) save(current, 0);
  }, [current, save]);

  useEffect(() => {
    const onPause = () => saveNow();
    audio.addEventListener("pause", onPause);
    return () => audio.removeEventListener("pause", onPause);
  }, [audio, saveNow]);

  useEffect(() => {
    if (!playing) return;
    const timer = setInterval(() => saveNow(), SAVE_INTERVAL_MS);
    return () => clearInterval(timer);
  }, [playing, saveNow]);

  useEffect(() => {
    const onHide = () => saveNow(true);
    const onVisibility = () => document.visibilityState === "hidden" && onHide();
    document.addEventListener("visibilitychange", onVisibility);
    window.addEventListener("pagehide", onHide);
    return () => {
      document.removeEventListener("visibilitychange", onVisibility);
      window.removeEventListener("pagehide", onHide);
    };
  }, [saveNow]);

  // SPA navigation fires neither visibilitychange nor pagehide, so save when the reader unmounts.
  useEffect(() => () => saveNow(true), [saveNow]);

  return { loadResume };
}
