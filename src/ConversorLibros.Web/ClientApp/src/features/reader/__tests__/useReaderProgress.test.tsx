import { act, renderHook } from "@testing-library/react";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import type { ReaderIndexChapterDto } from "../../../shared/api/types";
import { useReaderProgress } from "../useReaderProgress";
import { FakeAudio } from "./fakeAudio";

const chapters: ReaderIndexChapterDto[] = [
  { index: 1, title: "Uno", durationMs: 30000, audioKind: "opus" },
  { index: 3, title: "Tres", durationMs: 30000, audioKind: "opus" },
];

type Saved = { chapterIndex: number; ms: number; updatedAt: string } | null;

/** Routes GET to the given saved position (null = 204) and records every PUT. */
function stubProgress(saved: Saved, putFails = false) {
  const puts: { body: Record<string, number>; keepalive: boolean | undefined }[] = [];
  const fn = vi.fn(async (_url: string, init?: RequestInit) => {
    if (init?.method === "PUT") {
      if (putFails) throw new TypeError("offline");
      puts.push({ body: JSON.parse(init.body as string), keepalive: init.keepalive });
      return { ok: true, status: 200, json: async () => ({}) };
    }
    if (saved === null) return { ok: true, status: 204, json: async () => ({}) };
    return { ok: true, status: 200, json: async () => saved };
  });
  vi.stubGlobal("fetch", fn);
  return { fn, puts };
}

interface Props {
  current: number | null;
  playing: boolean;
  ready: boolean;
  durationMs: number;
}

function setup(initial: Partial<Props> = {}) {
  const audio = new FakeAudio();
  const seekTo = vi.fn();
  const hook = renderHook(
    (props: Props) => useReaderProgress({ id: "b1", audio: audio as unknown as HTMLAudioElement, seekTo, ...props }),
    { initialProps: { current: null, playing: false, ready: false, durationMs: 30000, ...initial } as Props }
  );
  return { audio, seekTo, ...hook };
}

const flush = () => act(async () => {});

/** Loads the resume position and opens the resolved chapter, as the page does. */
async function open(h: ReturnType<typeof setup>, over: Partial<Props> = {}) {
  let chapter = -1;
  await act(async () => {
    chapter = await h.result.current.loadResume(chapters);
  });
  h.rerender({ current: chapter, playing: false, ready: true, durationMs: 30000, ...over });
  await flush();
  return chapter;
}

describe("useReaderProgress", () => {
  beforeEach(() => vi.useFakeTimers());
  afterEach(() => {
    vi.useRealTimers();
    vi.unstubAllGlobals();
  });

  it("starts at the first chapter and 0 ms when nothing is saved", async () => {
    const { puts } = stubProgress(null);
    const h = setup();
    expect(await open(h)).toBe(1);
    expect(h.seekTo).not.toHaveBeenCalled();
    expect(puts).toHaveLength(0);
  });

  it("resumes the saved chapter and seeks to the saved ms without autoplay", async () => {
    const { puts } = stubProgress({ chapterIndex: 3, ms: 12500, updatedAt: "x" });
    const h = setup();
    expect(await open(h)).toBe(3);
    expect(h.seekTo).toHaveBeenCalledWith(12500);
    expect(h.audio.play).not.toHaveBeenCalled();
    expect(puts).toHaveLength(0);
  });

  it("waits for the chapter to be ready before seeking", async () => {
    stubProgress({ chapterIndex: 3, ms: 12500, updatedAt: "x" });
    const h = setup();
    await act(async () => {
      await h.result.current.loadResume(chapters);
    });
    h.rerender({ current: 3, playing: false, ready: false, durationMs: 0 });
    expect(h.seekTo).not.toHaveBeenCalled();
    h.rerender({ current: 3, playing: false, ready: true, durationMs: 30000 });
    expect(h.seekTo).toHaveBeenCalledTimes(1);
    h.rerender({ current: 3, playing: false, ready: true, durationMs: 30000 });
    expect(h.seekTo).toHaveBeenCalledTimes(1);
  });

  it("clamps a position beyond the chapter end to the chapter duration", async () => {
    stubProgress({ chapterIndex: 3, ms: 999999, updatedAt: "x" });
    const h = setup();
    expect(await open(h)).toBe(3);
    expect(h.seekTo).toHaveBeenCalledWith(30000);
  });

  it("falls back to the first chapter when the saved chapter is gone", async () => {
    stubProgress({ chapterIndex: 9, ms: 5000, updatedAt: "x" });
    const h = setup();
    expect(await open(h)).toBe(1);
    expect(h.seekTo).not.toHaveBeenCalled();
  });

  it("falls back to the first chapter when the progress request fails", async () => {
    vi.stubGlobal("fetch", vi.fn().mockRejectedValue(new TypeError("offline")));
    const h = setup();
    expect(await open(h)).toBe(1);
  });

  it("saves only chapter and ms on pause, never page numbers", async () => {
    const { puts } = stubProgress(null);
    const h = setup();
    await open(h);
    h.audio.currentTime = 7.4;
    act(() => h.audio.dispatchEvent(new Event("pause")));
    await flush();
    expect(puts).toEqual([{ body: { chapterIndex: 1, ms: 7400 }, keepalive: undefined }]);
  });

  it("does not repeat a PUT for an identical position", async () => {
    const { puts } = stubProgress(null);
    const h = setup();
    await open(h);
    h.audio.currentTime = 7;
    act(() => h.audio.dispatchEvent(new Event("pause")));
    act(() => h.audio.dispatchEvent(new Event("pause")));
    await flush();
    expect(puts).toHaveLength(1);
  });

  it("saves the new chapter at 0 ms on chapter change", async () => {
    const { puts } = stubProgress(null);
    const h = setup();
    await open(h);
    h.rerender({ current: 3, playing: false, ready: true, durationMs: 30000 });
    await flush();
    expect(puts.map((p) => p.body)).toEqual([{ chapterIndex: 3, ms: 0 }]);
  });

  it("saves every 10 seconds while playing and stops when paused", async () => {
    const { puts } = stubProgress(null);
    const h = setup();
    await open(h, { playing: true });
    h.audio.currentTime = 4;
    await act(async () => void vi.advanceTimersByTime(10000));
    h.audio.currentTime = 14;
    await act(async () => void vi.advanceTimersByTime(10000));
    expect(puts.map((p) => p.body.ms)).toEqual([4000, 14000]);

    h.rerender({ current: 1, playing: false, ready: true, durationMs: 30000 });
    await act(async () => void vi.advanceTimersByTime(30000));
    expect(puts).toHaveLength(2);
  });

  it.each([
    ["visibilitychange", () => document.dispatchEvent(new Event("visibilitychange"))],
    ["pagehide", () => window.dispatchEvent(new Event("pagehide"))],
  ])("saves with keepalive on %s", async (_name, fire) => {
    const { puts } = stubProgress(null);
    const h = setup();
    await open(h);
    h.audio.currentTime = 9;
    Object.defineProperty(document, "visibilityState", { configurable: true, value: "hidden" });
    try {
      act(() => fire());
    } finally {
      delete (document as unknown as Record<string, unknown>).visibilityState;
    }
    await flush();
    expect(puts).toEqual([{ body: { chapterIndex: 1, ms: 9000 }, keepalive: true }]);
  });

  it("does not save when the page becomes visible", async () => {
    const { puts } = stubProgress(null);
    const h = setup();
    await open(h);
    h.audio.currentTime = 9;
    act(() => document.dispatchEvent(new Event("visibilitychange")));
    await flush();
    expect(puts).toHaveLength(0);
  });

  it("does not overwrite the saved position before the resume seek has happened", async () => {
    const { puts } = stubProgress({ chapterIndex: 3, ms: 12500, updatedAt: "x" });
    const h = setup();
    await act(async () => {
      await h.result.current.loadResume(chapters);
    });
    h.rerender({ current: 3, playing: false, ready: false, durationMs: 0 });
    act(() => h.audio.dispatchEvent(new Event("pause")));
    await flush();
    expect(puts).toHaveLength(0);
  });

  it("drops the pending resume ms when another chapter is opened first, and saving works again", async () => {
    const { puts } = stubProgress({ chapterIndex: 3, ms: 12500, updatedAt: "x" });
    const h = setup();
    await act(async () => {
      await h.result.current.loadResume(chapters);
    });
    h.rerender({ current: 3, playing: false, ready: false, durationMs: 0 }); // resume chapter fails to load
    h.rerender({ current: 1, playing: false, ready: true, durationMs: 30000 }); // user picks another one
    await flush();
    expect(h.seekTo).not.toHaveBeenCalled();
    h.audio.currentTime = 7;
    act(() => h.audio.dispatchEvent(new Event("pause")));
    await flush();
    expect(puts.map((p) => p.body)).toEqual([
      { chapterIndex: 1, ms: 0 },
      { chapterIndex: 1, ms: 7000 },
    ]);
  });

  it("does not save the old audio position under a new chapter before it is ready", async () => {
    const { puts } = stubProgress(null);
    const h = setup();
    await open(h, { playing: true });
    h.audio.currentTime = 25;
    h.rerender({ current: 3, playing: true, ready: false, durationMs: 0 });
    act(() => h.audio.dispatchEvent(new Event("pause")));
    await act(async () => void vi.advanceTimersByTime(10000));
    Object.defineProperty(document, "visibilityState", { configurable: true, value: "hidden" });
    try {
      act(() => document.dispatchEvent(new Event("visibilitychange")));
    } finally {
      delete (document as unknown as Record<string, unknown>).visibilityState;
    }
    await flush();
    expect(puts.map((p) => p.body)).toEqual([{ chapterIndex: 3, ms: 0 }]);
  });

  it("saves the last known position synchronously on unmount, with keepalive", async () => {
    const { puts } = stubProgress(null);
    const h = setup();
    await open(h, { playing: true });
    h.audio.currentTime = 5;
    h.unmount();
    await flush();
    expect(puts).toEqual([{ body: { chapterIndex: 1, ms: 5000 }, keepalive: true }]);
  });

  it("does not save on unmount while the resume seek is still pending", async () => {
    const { puts } = stubProgress({ chapterIndex: 3, ms: 12500, updatedAt: "x" });
    const h = setup();
    await act(async () => {
      await h.result.current.loadResume(chapters);
    });
    h.rerender({ current: 3, playing: false, ready: false, durationMs: 0 });
    h.unmount();
    await flush();
    expect(puts).toHaveLength(0);
  });

  it("re-applies the resume seek on loadedmetadata when the browser dropped it", async () => {
    stubProgress({ chapterIndex: 3, ms: 12500, updatedAt: "x" });
    const h = setup();
    await open(h);
    h.audio.currentTime = 0; // the browser reset the position while loading
    act(() => h.audio.dispatchEvent(new Event("loadedmetadata")));
    expect(h.audio.currentTime).toBe(12.5);
    h.audio.currentTime = 20; // later loads or user seeks are left alone
    act(() => h.audio.dispatchEvent(new Event("loadedmetadata")));
    expect(h.audio.currentTime).toBe(20);
  });

  it("keeps working when saving fails", async () => {
    const { puts } = stubProgress(null, true);
    const h = setup();
    await open(h);
    h.audio.currentTime = 3;
    expect(() => act(() => h.audio.dispatchEvent(new Event("pause")))).not.toThrow();
    await flush();
    expect(puts).toHaveLength(0);
    expect(h.result.current).toBeTruthy();
  });
});
