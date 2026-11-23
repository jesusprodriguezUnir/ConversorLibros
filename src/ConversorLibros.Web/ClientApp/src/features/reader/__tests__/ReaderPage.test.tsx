import { act, render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { MemoryRouter, Route, Routes } from "react-router-dom";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { ReaderPage } from "../ReaderPage";
import type { ReaderChapterDto, ReaderIndexDto } from "../../../shared/api/types";

const index = (over: Partial<ReaderIndexDto> = {}): ReaderIndexDto => ({
  available: true,
  reason: null,
  title: "Dune",
  hasCover: true,
  chapters: [
    { index: 1, title: "Uno", durationMs: 3000, audioKind: "opus" },
    { index: 3, title: "Tres", durationMs: 3000, audioKind: "opus" },
  ],
  ...over,
});

const chapter = (n: number, title: string): ReaderChapterDto => ({
  schemaVersion: 1,
  chapterIndex: n,
  title,
  durationMs: 3000,
  chunks: [],
  paragraphs: [{ mode: "sentence", sentences: [{ text: `Texto ${title}.`, startMs: 0, endMs: 3000 }] }],
});

function stubFetch(idx: ReaderIndexDto | number, chapters: Record<number, ReaderChapterDto | number> = {}) {
  const fn = vi.fn(async (url: string) => {
    const m = /reader\/chapters\/(\d+)$/.exec(url);
    const body = m ? chapters[Number(m[1])] : idx;
    if (typeof body === "number" || body === undefined) {
      return { ok: false, status: typeof body === "number" ? body : 404, json: async () => ({}) };
    }
    return { ok: true, status: 200, json: async () => body };
  });
  vi.stubGlobal("fetch", fn);
  return fn;
}

function renderReader() {
  return render(
    <MemoryRouter initialEntries={["/biblioteca/b1/leer"]}>
      <Routes>
        <Route path="/biblioteca/:id/leer" element={<ReaderPage />} />
      </Routes>
    </MemoryRouter>
  );
}

const content = () => screen.getByTestId("chapter-content");

describe("ReaderPage", () => {
  beforeEach(() => {
    // 1000 px viewport, 3 single pages: 3 * (1000 + 48) - 48.
    Object.defineProperty(HTMLElement.prototype, "clientWidth", { configurable: true, get: () => 1000 });
    Object.defineProperty(HTMLElement.prototype, "scrollWidth", { configurable: true, get: () => 3096 });
  });
  afterEach(() => {
    delete (HTMLElement.prototype as unknown as Record<string, unknown>).clientWidth;
    delete (HTMLElement.prototype as unknown as Record<string, unknown>).scrollWidth;
    vi.unstubAllGlobals();
  });

  it("loads the index and the first available chapter with the cover as first page", async () => {
    const fetchFn = stubFetch(index(), { 1: chapter(1, "Uno"), 3: chapter(3, "Tres") });
    renderReader();

    expect(await screen.findByText("Texto Uno.")).toBeInTheDocument();
    expect(fetchFn).toHaveBeenCalledWith("/api/library/b1/reader");
    expect(fetchFn).toHaveBeenCalledWith("/api/library/b1/reader/chapters/1");
    expect(screen.getByAltText(/portada de dune/i)).toBeInTheDocument();
    expect(screen.getByText(/página 1 de 3/i)).toBeInTheDocument();
  });

  it("shows a loading status and disables page buttons while a chapter loads", async () => {
    stubFetch(index());
    vi.stubGlobal(
      "fetch",
      vi.fn((url: string) =>
        /reader\/chapters\//.test(url)
          ? new Promise(() => {})
          : Promise.resolve({ ok: true, status: 200, json: async () => index() })
      )
    );
    renderReader();

    expect(await screen.findByRole("status")).toHaveTextContent(/cargando capítulo/i);
    expect(screen.getByRole("button", { name: "Página siguiente" })).toBeDisabled();
    expect(screen.getByRole("button", { name: "Página anterior" })).toBeDisabled();
  });

  it("collapses a run of absent chapters into one disabled TOC entry", async () => {
    stubFetch(
      index({
        chapters: [
          { index: 1, title: "Uno", durationMs: 3000, audioKind: "opus" },
          { index: 5000, title: "Final", durationMs: 3000, audioKind: "opus" },
        ],
      }),
      { 1: chapter(1, "Uno") }
    );
    renderReader();
    await screen.findByText("Texto Uno.");

    const toc = screen.getByRole("navigation", { name: "Capítulos" });
    expect(toc.querySelectorAll("button")).toHaveLength(3);
    expect(screen.getByRole("button", { name: "Capítulos 2–4999" })).toBeDisabled();
  });

  it("renders a book without title using a generic cover label", async () => {
    stubFetch(index({ title: null }), { 1: chapter(1, "Uno") });
    renderReader();

    await screen.findByText("Texto Uno.");
    expect(screen.getByAltText("Portada del libro")).toBeInTheDocument();
  });

  it("omits the cover page when the book has no cover", async () => {
    stubFetch(index({ hasCover: false }), { 1: chapter(1, "Uno") });
    renderReader();

    await screen.findByText("Texto Uno.");
    expect(screen.queryByRole("img")).not.toBeInTheDocument();
  });

  it("opens a chapter from the TOC at page 1 and shows absent chapters disabled", async () => {
    const user = userEvent.setup();
    stubFetch(index(), { 1: chapter(1, "Uno"), 3: chapter(3, "Tres") });
    renderReader();
    await screen.findByText("Texto Uno.");

    expect(screen.getByRole("button", { name: /capítulo 2/i })).toBeDisabled();
    await user.click(screen.getByRole("button", { name: "Página siguiente" }));
    expect(content().style.transform).toBe("translateX(-1048px)");

    await user.click(screen.getByRole("button", { name: "Tres" }));
    expect(await screen.findByText("Texto Tres.")).toBeInTheDocument();
    expect(screen.getByText(/página 1 de 3/i)).toBeInTheDocument();
    expect(content().style.transform).toBe("translateX(0px)");
  });

  it("clamps page navigation at the chapter bounds", async () => {
    const user = userEvent.setup();
    stubFetch(index(), { 1: chapter(1, "Uno"), 3: chapter(3, "Tres") });
    renderReader();
    await screen.findByText("Texto Uno.");

    await user.click(screen.getByRole("button", { name: "Página anterior" }));
    expect(screen.getByText(/página 1 de 3/i)).toBeInTheDocument();
    await user.click(screen.getByRole("button", { name: "Página siguiente" }));
    await user.click(screen.getByRole("button", { name: "Página siguiente" }));
    await user.click(screen.getByRole("button", { name: "Página siguiente" }));
    expect(screen.getByText(/página 3 de 3/i)).toBeInTheDocument();
  });

  it("disables the page buttons at the chapter bounds", async () => {
    const user = userEvent.setup();
    stubFetch(index(), { 1: chapter(1, "Uno"), 3: chapter(3, "Tres") });
    renderReader();
    await screen.findByText("Texto Uno.");
    const prev = screen.getByRole("button", { name: "Página anterior" });
    const next = screen.getByRole("button", { name: "Página siguiente" });

    expect(prev).toBeDisabled();
    expect(next).toBeEnabled();
    await user.click(next);
    expect(prev).toBeEnabled();
    await user.click(next);
    expect(next).toBeDisabled();
    expect(prev).toBeEnabled();
  });

  it("announces the page indicator politely", async () => {
    stubFetch(index(), { 1: chapter(1, "Uno") });
    renderReader();
    expect(await screen.findByText(/página 1 de 3/i)).toHaveAttribute("aria-live", "polite");
  });

  it("ignores a chapter response that arrives after another chapter was selected", async () => {
    const user = userEvent.setup();
    let resolveOne!: (r: unknown) => void;
    const ok = (body: unknown) => ({ ok: true, status: 200, json: async () => body });
    vi.stubGlobal(
      "fetch",
      vi.fn((url: string) => {
        if (/chapters\/1$/.test(url)) return new Promise((r) => (resolveOne = r));
        return Promise.resolve(ok(/chapters\/3$/.test(url) ? chapter(3, "Tres") : index()));
      })
    );
    renderReader();
    await user.click(await screen.findByRole("button", { name: "Tres" }));
    expect(await screen.findByText("Texto Tres.")).toBeInTheDocument();

    await act(async () => resolveOne(ok(chapter(1, "Uno"))));

    expect(screen.getByText("Texto Tres.")).toBeInTheDocument();
    expect(screen.queryByText("Texto Uno.")).not.toBeInTheDocument();
  });

  it("recalculates the page count when the viewport is resized", async () => {
    const callbacks: Array<() => void> = [];
    vi.stubGlobal(
      "ResizeObserver",
      class {
        constructor(cb: () => void) {
          callbacks.push(cb);
        }
        observe() {}
        disconnect() {}
      }
    );
    stubFetch(index(), { 1: chapter(1, "Uno") });
    renderReader();
    expect(await screen.findByText(/página 1 de 3/i)).toBeInTheDocument();

    // 800 px viewport, 4 single pages: 4 * (800 + 48) - 48.
    Object.defineProperty(HTMLElement.prototype, "clientWidth", { configurable: true, get: () => 800 });
    Object.defineProperty(HTMLElement.prototype, "scrollWidth", { configurable: true, get: () => 3344 });
    act(() => callbacks.forEach((cb) => cb()));

    expect(await screen.findByText(/página 1 de 4/i)).toBeInTheDocument();
  });

  it("shows an error for a chapter that cannot be loaded and keeps the TOC usable", async () => {
    const user = userEvent.setup();
    stubFetch(index(), { 1: 422, 3: chapter(3, "Tres") });
    renderReader();

    expect(await screen.findByRole("alert")).toBeInTheDocument();
    await user.click(screen.getByRole("button", { name: "Tres" }));
    expect(await screen.findByText("Texto Tres.")).toBeInTheDocument();
  });

  it.each([
    ["no data", index({ available: false, reason: "no-data", chapters: [] })],
    ["unknown book", 404],
  ])("shows the reconvert hint and a library link (%s)", async (_name, idx) => {
    stubFetch(idx);
    renderReader();

    await waitFor(() =>
      expect(screen.getByText(/reconvierte el libro para habilitar el lector/i)).toBeInTheDocument()
    );
    expect(screen.getByRole("link", { name: /volver a la biblioteca/i })).toHaveAttribute("href", "/biblioteca");
  });
});
