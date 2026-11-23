import { useCallback, useEffect, useRef, useState, type RefObject } from "react";
import type { ReaderEntry } from "./entries";
import { pageOfRect, spreadStart } from "./pagination";
import { clampSpeed, findActiveEntry, pageAtProgress, skipMs } from "./sync";

export const ACTIVE_CLASS = "reader-active";
const AUDIO_ERROR = "No se pudo reproducir el audio de este capítulo.";

interface Options {
  audio: HTMLAudioElement;
  /** Chapter audio URL; null while the chapter is not ready. */
  src: string | null;
  entries: readonly ReaderEntry[];
  durationMs: number;
  contentRef: RefObject<HTMLElement | null>;
  stride: number;
  ppv: number;
  page: number;
  followingRef: RefObject<boolean>;
  onPage: (page: number) => void;
  /** Called when the audio ends; returns true when a next chapter takes over (it then autoplays). */
  onEnded: () => boolean;
}

function toggleActive(content: HTMLElement, index: number, on: boolean) {
  if (index >= 0) content.querySelector(`[data-entry="${index}"]`)?.classList.toggle(ACTIVE_CLASS, on);
}

/** Page of every line (client rect) of an entry; rects are measured against the transformed content. */
export function entryPages(content: HTMLElement, index: number, stride: number): number[] {
  const el = content.querySelector(`[data-entry="${index}"]`);
  if (!el) return [];
  const left = content.getBoundingClientRect().left;
  return Array.from(el.getClientRects(), (r) => pageOfRect(r.left, left, stride));
}

export function useAudioSync(options: Options) {
  const optsRef = useRef(options);
  optsRef.current = options;
  const activeRef = useRef(-1);
  const autoplayRef = useRef(false);
  const [playing, setPlaying] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [speed, setSpeed] = useState(1);
  const speedRef = useRef(speed);
  speedRef.current = speed;
  const [positionMs, setPositionMs] = useState(0);
  const { audio, src } = options;

  const tick = useCallback(() => {
    const { audio, entries, contentRef, stride, ppv, page, followingRef, onPage } = optsRef.current;
    const content = contentRef.current;
    if (!content) return;
    const ms = audio.currentTime * 1000;
    const active = findActiveEntry(entries, ms);
    if (active !== activeRef.current) {
      toggleActive(content, activeRef.current, false);
      toggleActive(content, active, true);
      activeRef.current = active;
    }
    if (active < 0 || !followingRef.current) return;
    const { startMs, endMs } = entries[active];
    const pages = entryPages(content, active, stride);
    if (pages.length === 0) return;
    const target = pageAtProgress(pages, (ms - startMs) / Math.max(1, endMs - startMs));
    const first = spreadStart(page, ppv);
    if (target < first || target >= first + ppv) onPage(target);
  }, []);

  const play = useCallback(() => {
    optsRef.current.audio.play().catch(() => setError(AUDIO_ERROR));
  }, []);

  useEffect(() => {
    activeRef.current = -1;
    setError(null);
    if (src === null) return;
    // Loading a new source resets the rate and pauses without firing `pause`, so restore both by hand.
    audio.src = src;
    audio.playbackRate = speedRef.current;
    setPlaying(false);
    setPositionMs(0);
    if (autoplayRef.current) {
      autoplayRef.current = false;
      play();
    }
  }, [audio, src, play]);

  useEffect(() => {
    const on = (type: string, handler: () => void) => audio.addEventListener(type, handler);
    const off = (type: string, handler: () => void) => audio.removeEventListener(type, handler);
    const onPlay = () => setPlaying(true);
    const onPause = () => setPlaying(false);
    const onEnd = () => {
      setPlaying(false);
      autoplayRef.current = optsRef.current.onEnded();
    };
    const onFail = () => setError(AUDIO_ERROR);
    const onTime = () => setPositionMs(Math.floor(audio.currentTime) * 1000);
    const handlers: [string, () => void][] = [
      ["play", onPlay],
      ["pause", onPause],
      ["ended", onEnd],
      ["error", onFail],
      ["timeupdate", onTime],
    ];
    handlers.forEach(([type, h]) => on(type, h));
    return () => {
      handlers.forEach(([type, h]) => off(type, h));
      if (!audio.paused) audio.pause();
    };
  }, [audio]);

  useEffect(() => {
    if (!playing) return;
    let id = 0;
    const loop = () => {
      tick();
      id = requestAnimationFrame(loop);
    };
    id = requestAnimationFrame(loop);
    return () => cancelAnimationFrame(id);
  }, [playing, tick]);

  useEffect(() => {
    audio.playbackRate = speed;
  }, [audio, speed]);

  const toggle = useCallback(() => {
    if (audio.paused) play();
    else audio.pause();
  }, [audio, play]);

  const seekTo = useCallback(
    (ms: number) => {
      audio.currentTime = ms / 1000;
      setPositionMs(ms);
      tick();
    },
    [audio, tick]
  );

  const skip = useCallback(
    (seconds: number) => seekTo(skipMs(audio.currentTime * 1000, seconds * 1000, optsRef.current.durationMs)),
    [audio, seekTo]
  );

  const changeSpeed = useCallback((delta: number) => setSpeed((s) => clampSpeed(s + delta)), []);

  /** Forgets an autoplay requested by `onEnded`, e.g. when the user picks a chapter by hand. */
  const cancelAutoplay = useCallback(() => {
    autoplayRef.current = false;
  }, []);

  return { playing, error, speed, positionMs, toggle, seekTo, skip, changeSpeed, cancelAutoplay, syncNow: tick };
}
