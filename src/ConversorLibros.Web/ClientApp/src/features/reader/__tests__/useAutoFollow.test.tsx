import { act, render, renderHook, screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { MemoryRouter, Route, Routes } from "react-router-dom";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { ReaderPage } from "../ReaderPage";
import { useAutoFollow } from "../useAutoFollow";
import { FakeAudio } from "./fakeAudio";

describe("useAutoFollow", () => {
  it("keeps the state and the synchronous ref in step", () => {
    const { result } = renderHook(() => useAutoFollow());
    expect(result.current.following).toBe(true);

    act(() => result.current.suspend());
    expect(result.current.following).toBe(false);
    expect(result.current.followingRef.current).toBe(false);

    act(() => result.current.resume());
    expect(result.current.following).toBe(true);
    expect(result.current.followingRef.current).toBe(true);
  });
});

const body = (url: string) =>
  /reader\/chapters\/\d+$/.test(url)
    ? {
        schemaVersion: 1,
        chapterIndex: 1,
        title: "Uno",
        durationMs: 3000,
        chunks: [],
        paragraphs: [
          {
            mode: "sentence",
            sentences: [
              { text: "Primera frase.", startMs: 0, endMs: 1500 },
              { text: "Segunda frase.", startMs: 1500, endMs: 3000 },
            ],
          },
        ],
      }
    : {
        available: true,
        reason: null,
        title: "Dune",
        hasCover: false,
        chapters: [{ index: 1, title: "Uno", durationMs: 3000, audioKind: "opus" }],
      };

describe("ReaderPage auto-follow", () => {
  let audio: FakeAudio;
  beforeEach(() => {
    audio = new FakeAudio();
    vi.stubGlobal(
      "Audio",
      function () {
        return audio;
      } as unknown as typeof Audio
    );
    vi.stubGlobal(
      "fetch",
      vi.fn(async (url: string) => ({ ok: true, status: 200, json: async () => body(url) }))
    );
    Object.defineProperty(HTMLElement.prototype, "clientWidth", { configurable: true, get: () => 1000 });
    Object.defineProperty(HTMLElement.prototype, "scrollWidth", { configurable: true, get: () => 3096 });
  });
  afterEach(() => {
    delete (HTMLElement.prototype as unknown as Record<string, unknown>).clientWidth;
    delete (HTMLElement.prototype as unknown as Record<string, unknown>).scrollWidth;
    vi.unstubAllGlobals();
  });

  async function openPlaying() {
    const user = userEvent.setup();
    render(
      <MemoryRouter initialEntries={["/biblioteca/b1/leer"]}>
        <Routes>
          <Route path="/biblioteca/:id/leer" element={<ReaderPage />} />
        </Routes>
      </MemoryRouter>
    );
    await screen.findByText("Primera frase.");
    return user;
  }

  it("suspends follow on manual navigation while playing and shows 'Volver al audio'", async () => {
    const user = await openPlaying();
    expect(audio.src).toBe("/api/library/b1/chapters/1/audio");
    expect(screen.queryByRole("button", { name: "Volver al audio" })).not.toBeInTheDocument();

    await user.click(screen.getByRole("button", { name: "Reproducir" }));
    await user.click(screen.getByRole("button", { name: "Página siguiente" }));

    expect(screen.getByRole("button", { name: "Volver al audio" })).toBeInTheDocument();
    expect(audio.pause).not.toHaveBeenCalled();
    expect(audio.paused).toBe(false);
  });

  it("does not suspend when the audio is paused", async () => {
    const user = await openPlaying();
    await user.click(screen.getByRole("button", { name: "Página siguiente" }));

    expect(screen.queryByRole("button", { name: "Volver al audio" })).not.toBeInTheDocument();
  });

  it("resumes follow with 'Volver al audio' and jumps to the page of the active sentence", async () => {
    // Every sentence sits on the first page: its single line starts at the content's left edge.
    vi.spyOn(HTMLElement.prototype, "getBoundingClientRect").mockReturnValue({ left: 0 } as DOMRect);
    vi.spyOn(HTMLElement.prototype, "getClientRects").mockImplementation(function (this: HTMLElement) {
      return (this.dataset.entry === undefined ? [] : [{ left: 0 }]) as unknown as DOMRectList;
    });
    const user = await openPlaying();
    await user.click(screen.getByRole("button", { name: "Reproducir" }));
    await user.click(screen.getByRole("button", { name: "Página siguiente" }));
    expect(screen.getByText(/página 2 de 3/i)).toBeInTheDocument();

    await user.click(screen.getByRole("button", { name: "Volver al audio" }));

    expect(screen.queryByRole("button", { name: "Volver al audio" })).not.toBeInTheDocument();
    expect(screen.getByText(/página 1 de 3/i)).toBeInTheDocument();
    vi.restoreAllMocks();
  });

  it("seeks and resumes follow when a sentence is clicked", async () => {
    const user = await openPlaying();
    await user.click(screen.getByRole("button", { name: "Reproducir" }));
    await user.click(screen.getByRole("button", { name: "Página siguiente" }));
    await user.click(screen.getByText("Segunda frase."));

    expect(audio.currentTime).toBe(1.5);
    expect(screen.queryByRole("button", { name: "Volver al audio" })).not.toBeInTheDocument();
    expect(screen.getByText("Segunda frase.")).toHaveClass("reader-active");
  });

  it("wires the playback controls: skip, speed and error message", async () => {
    const user = await openPlaying();
    audio.currentTime = 1;
    await user.click(screen.getByRole("button", { name: "−15 s" }));
    expect(audio.currentTime).toBe(0);
    await user.click(screen.getByRole("button", { name: "+15 s" }));
    expect(audio.currentTime).toBe(3);

    await user.click(screen.getByRole("button", { name: "Más rápida" }));
    expect(audio.playbackRate).toBe(1.25);

    act(() => audio.dispatchEvent(new Event("error")));
    expect(await screen.findByRole("alert")).toHaveTextContent(/audio/i);
    expect(screen.getByText("Primera frase.")).toBeInTheDocument();
  });
});
