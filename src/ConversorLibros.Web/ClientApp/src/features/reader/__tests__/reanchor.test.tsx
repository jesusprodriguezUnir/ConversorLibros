import { act, render, screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { MemoryRouter, Route, Routes } from "react-router-dom";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { ReaderPage } from "../ReaderPage";
import { APPEARANCE_KEY } from "../appearance";
import { FakeAudio } from "./fakeAudio";

const chapterBody = {
  schemaVersion: 1,
  chapterIndex: 1,
  title: "Uno",
  durationMs: 4000,
  chunks: [],
  paragraphs: [
    {
      mode: "sentence",
      sentences: [0, 1, 2, 3].map((n) => ({ text: `Frase ${n}.`, startMs: n * 1000, endMs: (n + 1) * 1000 })),
    },
  ],
};
const indexBody = {
  available: true,
  reason: null,
  title: "Dune",
  hasCover: false,
  chapters: [{ index: 1, title: "Uno", durationMs: 4000, audioKind: "opus" }],
};

/** Fake layout: entry n sits on page floor(n * factor); the page stride follows the viewport width. */
const model = { width: 1000, factor: 1, spans: {} as Record<number, number[]> };
const stride = () => {
  const ppv = model.width >= 1100 ? 2 : 1;
  return Math.floor((model.width - 48 * (ppv - 1)) / ppv) + 48;
};
const lastPage = () => Math.floor(3 * model.factor);

let roCallbacks: Array<() => void> = [];

class FakeResizeObserver {
  constructor(cb: () => void) {
    roCallbacks.push(cb);
  }
  observe() {}
  disconnect() {}
}

type User = ReturnType<typeof userEvent.setup>;

describe("ReaderPage re-anchoring", () => {
  let audio: FakeAudio;
  beforeEach(() => {
    model.width = 1000;
    model.factor = 1;
    model.spans = {};
    roCallbacks = [];
    localStorage.clear();
    audio = new FakeAudio();
    vi.stubGlobal("Audio", function () {
      return audio;
    } as unknown as typeof Audio);
    vi.stubGlobal("ResizeObserver", FakeResizeObserver);
    vi.stubGlobal(
      "fetch",
      vi.fn(async (url: string) => ({
        ok: true,
        status: 200,
        json: async () => (/chapters\/\d+$/.test(url) ? chapterBody : indexBody),
      }))
    );
    Object.defineProperty(HTMLElement.prototype, "clientWidth", { configurable: true, get: () => model.width });
    Object.defineProperty(HTMLElement.prototype, "scrollWidth", {
      configurable: true,
      get: () => (lastPage() + 1) * stride() - 48,
    });
    vi.spyOn(HTMLElement.prototype, "getBoundingClientRect").mockReturnValue({ left: 0 } as DOMRect);
    vi.spyOn(HTMLElement.prototype, "getClientRects").mockImplementation(function (this: HTMLElement) {
      if (this.dataset.entry === undefined) return [] as unknown as DOMRectList;
      const n = Number(this.dataset.entry);
      const pages = model.spans[n] ?? [Math.floor(n * model.factor)];
      return pages.map((p) => ({ left: p * stride() })) as unknown as DOMRectList;
    });
  });
  afterEach(() => {
    delete (document as unknown as Record<string, unknown>).fonts;
    delete (HTMLElement.prototype as unknown as Record<string, unknown>).clientWidth;
    delete (HTMLElement.prototype as unknown as Record<string, unknown>).scrollWidth;
    vi.restoreAllMocks();
    vi.unstubAllGlobals();
  });

  async function open() {
    const user = userEvent.setup();
    render(
      <MemoryRouter initialEntries={["/biblioteca/b1/leer"]}>
        <Routes>
          <Route path="/biblioteca/:id/leer" element={<ReaderPage />} />
        </Routes>
      </MemoryRouter>
    );
    await screen.findByText("Frase 0.");
    return user;
  }
  const nextPage = (user: User, times = 1) =>
    (async () => {
      for (let i = 0; i < times; i++) await user.click(screen.getByRole("button", { name: "Página siguiente" }));
    })();
  const increaseFont = async (user: User) => {
    await user.click(screen.getByRole("button", { name: "Apariencia" }));
    await user.click(screen.getByRole("button", { name: "Aumentar texto" }));
  };

  it("keeps the first visible entry on screen when the font size changes", async () => {
    const user = await open();
    await nextPage(user, 2);
    expect(screen.getByText(/página 3 de 4/i)).toBeInTheDocument();

    model.factor = 2; // entry 2 now sits on page 4 of 7
    await increaseFont(user);

    expect(screen.getByText(/página 5 de 7/i)).toBeInTheDocument();
  });

  it("anchors to the active entry while following during playback", async () => {
    vi.stubGlobal("requestAnimationFrame", () => 0);
    vi.stubGlobal("cancelAnimationFrame", () => {});
    const user = await open();
    audio.currentTime = 1.5; // entry 1 is active; page 1 still shows entry 0 first
    await user.click(screen.getByRole("button", { name: "Reproducir" }));

    model.factor = 2; // entry 1 -> page 2
    await increaseFont(user);

    expect(screen.getByText(/página 3 de 7/i)).toBeInTheDocument();
  });

  it("uses the first visible entry when auto-follow is suspended", async () => {
    vi.stubGlobal("requestAnimationFrame", () => 0);
    vi.stubGlobal("cancelAnimationFrame", () => {});
    const user = await open();
    audio.currentTime = 1.5;
    await user.click(screen.getByRole("button", { name: "Reproducir" }));
    await nextPage(user, 2); // suspends follow; page 3 shows entry 2

    model.factor = 2;
    await increaseFont(user);

    expect(screen.getByText(/página 5 de 7/i)).toBeInTheDocument();
  });

  it("re-anchors on resize and switches to a spread from 1100 px", async () => {
    const user = await open();
    await nextPage(user, 2);

    model.width = 1200;
    model.factor = 2; // entry 2 on page 4; spreads start on even pages
    act(() => roCallbacks.forEach((cb) => cb()));

    expect(await screen.findByText(/página 5 de 7/i)).toBeInTheDocument();
  });

  it("re-anchors when web fonts finish loading", async () => {
    let resolve!: () => void;
    Object.defineProperty(document, "fonts", {
      configurable: true,
      value: { ready: new Promise<void>((r) => (resolve = r)) },
    });
    const user = await open();
    await nextPage(user, 2);

    model.factor = 2;
    await act(async () => resolve());

    expect(screen.getByText(/página 5 de 7/i)).toBeInTheDocument();
  });

  it("does not drift backward across consecutive relayouts", async () => {
    const user = await open();
    await nextPage(user, 1); // entry 1 on page 1

    model.factor = 0.5; // entries 0 and 1 now share page 0
    await increaseFont(user);
    expect(screen.getByText(/página 1 de 2/i)).toBeInTheDocument();

    model.factor = 1; // the anchor must still be entry 1 (page 1), not entry 0
    await user.click(screen.getByRole("button", { name: "Aumentar texto" }));

    expect(screen.getByText(/página 2 de 4/i)).toBeInTheDocument();
  });

  it("falls back to the last anchor when the spread holds no entry start", async () => {
    model.factor = 2; // entries start on even pages; page 1 holds none
    const user = await open();
    await nextPage(user, 1);
    expect(screen.getByText(/página 2 de 7/i)).toBeInTheDocument();

    model.factor = 1;
    model.spans = {};
    await increaseFont(user);

    expect(screen.getByText(/página 1 de 4/i)).toBeInTheDocument();
  });

  it("anchors to the sentence spanning into a page that starts none", async () => {
    model.spans = { 0: [0], 1: [0], 2: [0], 3: [0, 1, 2] }; // entry 3 runs over pages 0-2
    const user = await open();
    await user.click(screen.getByRole("button", { name: "+15 s" })); // seeks to the end of entry 3 -> page 2
    expect(screen.getByText(/página 3 de 4/i)).toBeInTheDocument();

    model.spans = {};
    model.factor = 2; // entry 3 now starts on page 6 of 7; entry 0 would send the reader back to page 1
    await increaseFont(user);

    expect(screen.getByText(/página 7 de 7/i)).toBeInTheDocument();
  });

  it.each([
    ["loading", () => new Promise(() => {})],
    ["unavailable", async () => ({ ok: false, status: 500, json: async () => ({}) })],
  ])("applies the stored theme to the %s shell", async (_name, respond) => {
    localStorage.setItem(APPEARANCE_KEY, JSON.stringify({ theme: "dark", fontStep: 2 }));
    vi.stubGlobal("fetch", vi.fn(respond));
    const { container } = render(
      <MemoryRouter initialEntries={["/biblioteca/b1/leer"]}>
        <Routes>
          <Route path="/biblioteca/:id/leer" element={<ReaderPage />} />
        </Routes>
      </MemoryRouter>
    );
    await act(async () => {});
    const shell = container.querySelector(".reader-shell") as HTMLElement;
    expect(shell).toHaveAttribute("data-theme", "dark");
    expect(shell.style.getPropertyValue("--reader-font-size")).not.toBe("");
  });

  it("switches theme during playback without touching the audio or the page", async () => {
    const user = await open();
    await user.click(screen.getByRole("button", { name: "Reproducir" }));
    await nextPage(user, 1);

    await user.click(screen.getByRole("button", { name: "Apariencia" }));
    await user.click(screen.getByRole("radio", { name: "Oscuro" }));

    expect(document.querySelector("[data-theme]")).toHaveAttribute("data-theme", "dark");
    expect(audio.pause).not.toHaveBeenCalled();
    expect(audio.play).toHaveBeenCalledTimes(1);
    expect(screen.getByText(/página 2 de 4/i)).toBeInTheDocument();
  });
});
