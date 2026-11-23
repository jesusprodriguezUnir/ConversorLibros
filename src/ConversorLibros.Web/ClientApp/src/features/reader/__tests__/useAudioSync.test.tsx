import { act, render } from "@testing-library/react";
import { useRef } from "react";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import type { ReaderEntry } from "../entries";
import { FakeAudio } from "./fakeAudio";
import { useAudioSync } from "../useAudioSync";

const entry = (index: number, startMs: number, endMs: number): ReaderEntry => ({
  index,
  paragraphIndex: 0,
  text: `s${index}`,
  startMs,
  endMs,
});
const ENTRIES = [entry(0, 0, 1000), entry(1, 1000, 2500), entry(2, 2500, 4000)];

type Sync = ReturnType<typeof useAudioSync>;
interface HarnessProps {
  audio: FakeAudio;
  api: { current: Sync | null };
  src?: string | null;
  page?: number;
  following?: boolean;
  entries?: ReaderEntry[];
  onPage?: (page: number) => void;
  onEnded?: () => boolean;
}

function Harness({ audio, api, src = "/a.ogg", page = 0, following = true, entries = ENTRIES, onPage, onEnded }: HarnessProps) {
  const contentRef = useRef<HTMLDivElement>(null);
  const followingRef = useRef(following);
  followingRef.current = following;
  api.current = useAudioSync({
    audio: audio as unknown as HTMLAudioElement,
    src,
    entries,
    durationMs: 4000,
    contentRef,
    stride: 100,
    ppv: 1,
    page,
    followingRef,
    onPage: onPage ?? (() => {}),
    onEnded: onEnded ?? (() => false),
  });
  return (
    <div ref={contentRef}>
      {entries.map((e) => (
        <span key={e.index} data-entry={e.index}>
          {e.text}
        </span>
      ))}
    </div>
  );
}

let frames: FrameRequestCallback[] = [];
let lines: Record<string, number[]> = {};
let lastFrameId = 0;
let cancelledFrames: number[] = [];
const flushFrame = () => {
  const run = frames;
  frames = [];
  act(() => run.forEach((cb) => cb(0)));
};
const active = (container: HTMLElement) =>
  Array.from(container.querySelectorAll(".reader-active")).map((el) => (el as HTMLElement).dataset.entry);

describe("useAudioSync", () => {
  let audio: FakeAudio;
  let api: { current: Sync | null };

  beforeEach(() => {
    frames = [];
    lines = {};
    audio = new FakeAudio();
    api = { current: null };
    lastFrameId = 0;
    cancelledFrames = [];
    vi.stubGlobal("requestAnimationFrame", (cb: FrameRequestCallback) => {
      frames.push(cb);
      return ++lastFrameId;
    });
    vi.stubGlobal("cancelAnimationFrame", (id: number) => cancelledFrames.push(id));
    vi.spyOn(HTMLElement.prototype, "getBoundingClientRect").mockReturnValue({ left: 0 } as DOMRect);
    vi.spyOn(HTMLElement.prototype, "getClientRects").mockImplementation(function (this: HTMLElement) {
      return (lines[this.dataset.entry ?? ""] ?? []).map((left) => ({ left })) as unknown as DOMRectList;
    });
  });
  afterEach(() => {
    vi.restoreAllMocks();
    vi.unstubAllGlobals();
  });

  it("loads the chapter audio and highlights the entry under the playback time frame by frame", async () => {
    const { container } = render(<Harness audio={audio} api={api} />);
    expect(audio.src).toBe("/a.ogg");

    audio.currentTime = 1.2;
    await act(() => api.current!.toggle());
    flushFrame();
    expect(active(container)).toEqual(["1"]);

    audio.currentTime = 0.5;
    flushFrame();
    expect(active(container)).toEqual(["0"]);
  });

  it("highlights a whole paragraph held as a single entry", async () => {
    const whole = [entry(0, 0, 4000)];
    const { container } = render(<Harness audio={audio} api={api} entries={whole} />);
    audio.currentTime = 3;
    await act(() => api.current!.toggle());
    flushFrame();

    expect(active(container)).toEqual(["0"]);
  });

  it("turns the page by fraction when a sentence spans two pages", async () => {
    lines["1"] = [300, 300, 400, 400]; // pages 3, 3, 4, 4 (stride 100)
    const onPage = vi.fn();
    const view = render(<Harness audio={audio} api={api} onPage={onPage} />);
    audio.currentTime = 1.0;
    await act(() => api.current!.toggle());
    flushFrame();
    expect(onPage).toHaveBeenLastCalledWith(3);

    view.rerender(<Harness audio={audio} api={api} onPage={onPage} page={3} />);
    onPage.mockClear();
    audio.currentTime = 1.5; // 33 % of the entry: still page 3
    flushFrame();
    expect(onPage).not.toHaveBeenCalled();
    audio.currentTime = 1.8; // 53 %: second half of the lines
    flushFrame();
    expect(onPage).toHaveBeenLastCalledWith(4);
  });

  it("highlights but does not turn pages while follow is suspended", async () => {
    lines["1"] = [300];
    const onPage = vi.fn();
    const { container } = render(<Harness audio={audio} api={api} onPage={onPage} following={false} />);
    audio.currentTime = 1.2;
    await act(() => api.current!.toggle());
    flushFrame();

    expect(active(container)).toEqual(["1"]);
    expect(onPage).not.toHaveBeenCalled();
  });

  it("continues with the next chapter when the audio ends, and stops when there is none", async () => {
    const view = render(<Harness audio={audio} api={api} onEnded={() => true} />);
    await act(() => api.current!.toggle());
    act(() => {
      audio.paused = true;
      audio.dispatchEvent(new Event("ended"));
    });
    expect(api.current!.playing).toBe(false);

    view.rerender(<Harness audio={audio} api={api} onEnded={() => true} src="/b.ogg" />);
    expect(audio.src).toBe("/b.ogg");
    expect(audio.play).toHaveBeenCalledTimes(2);

    view.rerender(<Harness audio={audio} api={api} onEnded={() => false} src="/b.ogg" />);
    act(() => audio.dispatchEvent(new Event("ended")));
    view.rerender(<Harness audio={audio} api={api} onEnded={() => false} src="/c.ogg" />);
    expect(audio.play).toHaveBeenCalledTimes(2);
  });

  it("keeps the chosen speed and resets the play state when the source changes", async () => {
    const view = render(<Harness audio={audio} api={api} />);
    act(() => api.current!.changeSpeed(0.25));
    await act(() => api.current!.toggle());
    audio.currentTime = 2;
    act(() => audio.dispatchEvent(new Event("timeupdate")));

    view.rerender(<Harness audio={audio} api={api} src="/b.ogg" />);

    expect(audio.playbackRate).toBe(1.25);
    expect(api.current!.playing).toBe(false);
    expect(api.current!.positionMs).toBe(0);
  });

  it("drops a pending autoplay when it is cancelled before the next source arrives", async () => {
    const view = render(<Harness audio={audio} api={api} onEnded={() => true} />);
    await act(() => api.current!.toggle());
    act(() => audio.dispatchEvent(new Event("ended")));
    view.rerender(<Harness audio={audio} api={api} onEnded={() => true} src={null} />);

    act(() => api.current!.cancelAutoplay());
    view.rerender(<Harness audio={audio} api={api} onEnded={() => true} src="/b.ogg" />);

    expect(audio.play).toHaveBeenCalledTimes(1);
  });

  it("stops the frame loop on pause and on unmount", async () => {
    const view = render(<Harness audio={audio} api={api} />);
    await act(() => api.current!.toggle());
    const playingFrame = lastFrameId;
    act(() => audio.pause());
    expect(cancelledFrames).toContain(playingFrame);

    await act(() => api.current!.toggle());
    const resumedFrame = lastFrameId;
    view.unmount();
    expect(cancelledFrames).toContain(resumedFrame);
  });

  it("seeks to an entry start and highlights it immediately while paused", () => {
    const { container } = render(<Harness audio={audio} api={api} />);
    act(() => api.current!.seekTo(2500));

    expect(audio.currentTime).toBe(2.5);
    expect(active(container)).toEqual(["2"]);
  });

  it("reports an audio load error without crashing and keeps the text", () => {
    const { container } = render(<Harness audio={audio} api={api} />);
    act(() => audio.dispatchEvent(new Event("error")));

    expect(api.current!.error).toMatch(/audio/i);
    expect(container.textContent).toContain("s0");
  });

  it("reports an error when play() is rejected", async () => {
    audio.play.mockRejectedValueOnce(new Error("NotSupported"));
    render(<Harness audio={audio} api={api} />);
    await act(async () => api.current!.toggle());

    expect(api.current!.error).toMatch(/audio/i);
  });

  it("skips within bounds and clamps the speed to 0.75-2", () => {
    render(<Harness audio={audio} api={api} />);
    audio.currentTime = 5;
    act(() => api.current!.skip(-15));
    expect(audio.currentTime).toBe(0);
    act(() => api.current!.skip(15));
    expect(audio.currentTime).toBe(4);

    act(() => api.current!.changeSpeed(5));
    expect(audio.playbackRate).toBe(2);
    act(() => api.current!.changeSpeed(-5));
    expect(audio.playbackRate).toBe(0.75);
  });
});
