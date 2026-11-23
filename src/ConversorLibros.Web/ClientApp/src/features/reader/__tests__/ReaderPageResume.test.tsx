import { act, render, screen, waitFor } from "@testing-library/react";
import { MemoryRouter, Route, Routes } from "react-router-dom";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { ReaderPage } from "../ReaderPage";
import type { ReaderChapterDto, ReaderIndexDto } from "../../../shared/api/types";
import { FakeAudio } from "./fakeAudio";

const index: ReaderIndexDto = {
  available: true,
  reason: null,
  title: "Dune",
  hasCover: false,
  chapters: [
    { index: 1, title: "Uno", durationMs: 3000, audioKind: "opus" },
    { index: 3, title: "Tres", durationMs: 3000, audioKind: "opus" },
  ],
};

const chapter = (n: number, title: string): ReaderChapterDto => ({
  schemaVersion: 1,
  chapterIndex: n,
  title,
  durationMs: 3000,
  chunks: [],
  paragraphs: [
    {
      mode: "sentence",
      sentences: [
        { text: `Texto ${title}.`, startMs: 0, endMs: 1500 },
        { text: `Final ${title}.`, startMs: 1500, endMs: 3000 },
      ],
    },
  ],
});

describe("ReaderPage resume", () => {
  let audio: FakeAudio;
  let fetchFn: ReturnType<typeof vi.fn>;
  beforeEach(() => {
    audio = new FakeAudio();
    vi.stubGlobal("Audio", function () {
      return audio;
    } as unknown as typeof Audio);
    Object.defineProperty(HTMLElement.prototype, "clientWidth", { configurable: true, get: () => 1000 });
    Object.defineProperty(HTMLElement.prototype, "scrollWidth", { configurable: true, get: () => 3096 });
  });
  afterEach(() => {
    delete (HTMLElement.prototype as unknown as Record<string, unknown>).clientWidth;
    delete (HTMLElement.prototype as unknown as Record<string, unknown>).scrollWidth;
    vi.unstubAllGlobals();
  });

  function stub(saved: { chapterIndex: number; ms: number } | null) {
    fetchFn = vi.fn(async (url: string, init?: RequestInit) => {
      const ok = (body: unknown, status = 200) => ({ ok: true, status, json: async () => body });
      if (/reader\/progress$/.test(url)) return init?.method === "PUT" ? ok({}) : saved ? ok(saved) : ok({}, 204);
      const m = /reader\/chapters\/(\d+)$/.exec(url);
      if (m) return ok(chapter(Number(m[1]), m[1] === "1" ? "Uno" : "Tres"));
      return ok(index);
    });
    vi.stubGlobal("fetch", fetchFn);
    render(
      <MemoryRouter initialEntries={["/biblioteca/b1/leer"]}>
        <Routes>
          <Route path="/biblioteca/:id/leer" element={<ReaderPage />} />
        </Routes>
      </MemoryRouter>
    );
  }

  it("reopens at the saved chapter and ms, paused", async () => {
    stub({ chapterIndex: 3, ms: 1500 });

    expect(await screen.findByText("Texto Tres.")).toBeInTheDocument();
    expect(audio.currentTime).toBe(1.5);
    expect(audio.src).toBe("/api/library/b1/chapters/3/audio");
    expect(audio.play).not.toHaveBeenCalled();
    expect(fetchFn).not.toHaveBeenCalledWith("/api/library/b1/reader/chapters/1");
    expect(fetchFn.mock.calls.some(([, init]) => init?.method === "PUT")).toBe(false);
  });

  it("opens the first chapter at 0 ms when nothing is saved", async () => {
    stub(null);

    expect(await screen.findByText("Texto Uno.")).toBeInTheDocument();
    expect(audio.currentTime).toBe(0);
  });

  it("shows the page that contains the saved sentence while paused", async () => {
    // The second sentence sits one column to the right; the first one on page 1.
    vi.spyOn(HTMLElement.prototype, "getBoundingClientRect").mockReturnValue({ left: 0 } as DOMRect);
    vi.spyOn(HTMLElement.prototype, "getClientRects").mockImplementation(function (this: HTMLElement) {
      return (this.dataset.entry === undefined ? [] : [{ left: this.dataset.entry === "1" ? 1500 : 0 }]) as unknown as DOMRectList;
    });
    stub({ chapterIndex: 3, ms: 2000 });

    expect(await screen.findByText("Final Tres.")).toBeInTheDocument();
    const indicator = document.querySelector(".reader-page-indicator");
    await waitFor(() => expect(indicator?.textContent).toMatch(/^Página 2 de/));
    expect(audio.play).not.toHaveBeenCalled();
    vi.restoreAllMocks();
  });

  it("saves the position when the audio pauses", async () => {
    stub({ chapterIndex: 3, ms: 1500 });
    await screen.findByText("Texto Tres.");

    audio.currentTime = 2;
    await act(async () => void audio.dispatchEvent(new Event("pause")));

    const put = fetchFn.mock.calls.find(([, init]) => init?.method === "PUT");
    expect(put?.[0]).toBe("/api/library/b1/reader/progress");
    expect(JSON.parse(put?.[1].body)).toEqual({ chapterIndex: 3, ms: 2000 });
  });
});
