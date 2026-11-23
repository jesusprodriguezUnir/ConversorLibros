import { render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { MemoryRouter, Route, Routes } from "react-router-dom";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { ReaderPage } from "../ReaderPage";
import type { ReaderBookmarkDto, ReaderChapterDto, ReaderIndexDto } from "../../../shared/api/types";
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

const longSentence = "x".repeat(200);
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
        { text: n === 1 ? longSentence : `Final ${title}.`, startMs: 1500, endMs: 3000 },
      ],
    },
  ],
});

const stored = (id: string, chapterIndex: number, ms: number): ReaderBookmarkDto => ({
  id,
  chapterIndex,
  ms,
  excerpt: `Marca ${id}`,
  note: null,
  createdAt: "2026-01-01T00:00:00Z",
});

describe("ReaderPage bookmarks", () => {
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

  function stub(list: ReaderBookmarkDto[] | "fail") {
    fetchFn = vi.fn(async (url: string, init?: RequestInit) => {
      const ok = (body: unknown, status = 200) => ({ ok: true, status, json: async () => body });
      if (/reader\/bookmarks$/.test(url)) {
        if (init?.method === "POST") {
          const req = JSON.parse(init.body as string);
          return ok({ id: "new", note: null, createdAt: "2026-01-02T00:00:00Z", ...req }, 201);
        }
        return list === "fail" ? { ok: false, status: 500, json: async () => ({ error: "x" }) } : ok(list);
      }
      if (/reader\/bookmarks\/[\w-]+$/.test(url)) return ok(null, 204);
      if (/reader\/progress$/.test(url)) return init?.method === "PUT" ? ok({}) : ok({}, 204);
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

  const openPanel = async (user: ReturnType<typeof userEvent.setup>) =>
    user.click(await screen.findByRole("button", { name: "Marcadores" }));

  it("adds a bookmark at the audio position with the active sentence (at most 160 chars)", async () => {
    const user = userEvent.setup();
    stub([]);
    await screen.findByText("Texto Uno.");
    audio.currentTime = 2;
    await openPanel(user);
    await user.click(screen.getByRole("button", { name: "Añadir marcador" }));

    const post = fetchFn.mock.calls.find(([, init]) => init?.method === "POST");
    expect(post?.[0]).toBe("/api/library/b1/reader/bookmarks");
    const body = JSON.parse(post?.[1].body);
    expect(body).toMatchObject({ chapterIndex: 1, ms: 2000 });
    expect(body.excerpt).toHaveLength(160);
    expect(await screen.findByRole("button", { name: /^Uno · 0:02/ })).toBeInTheDocument();
  });

  it("lists stored bookmarks after a reload and deletes one", async () => {
    const user = userEvent.setup();
    stub([stored("a", 1, 1000), stored("b", 3, 2000)]);
    await openPanel(user);

    expect(await screen.findByText("Marca a")).toBeInTheDocument();
    await user.click(screen.getByRole("button", { name: "Eliminar marcador de Uno (0:01)" }));
    await waitFor(() => expect(screen.queryByText("Marca a")).not.toBeInTheDocument());
    const del = fetchFn.mock.calls.find(([, init]) => init?.method === "DELETE");
    expect(del?.[0]).toBe("/api/library/b1/reader/bookmarks/a");
    expect(screen.getByText("Marca b")).toBeInTheDocument();
  });

  it("jumps within the same chapter via seek", async () => {
    const user = userEvent.setup();
    stub([stored("a", 1, 2000)]);
    await screen.findByText("Texto Uno.");
    await openPanel(user);
    await user.click(await screen.findByRole("button", { name: /^Uno · 0:02/ }));

    expect(audio.currentTime).toBe(2);
  });

  it("opens another chapter and seeks once it is ready, clamping ms past the chapter end", async () => {
    const user = userEvent.setup();
    stub([stored("a", 3, 99_000)]);
    await screen.findByText("Texto Uno.");
    await openPanel(user);
    await user.click(await screen.findByRole("button", { name: /^Tres · 1:39/ }));

    expect(await screen.findByText("Texto Tres.")).toBeInTheDocument();
    await waitFor(() => expect(audio.currentTime).toBe(3));
    expect(audio.src).toBe("/api/library/b1/chapters/3/audio");
  });

  it("disables a bookmark of a chapter that is no longer in the index", async () => {
    const user = userEvent.setup();
    stub([stored("a", 9, 1000)]);
    await openPanel(user);

    expect(await screen.findByRole("button", { name: /^Capítulo no disponible/ })).toBeDisabled();
  });

  it("treats an unreadable bookmarks list as empty without breaking the reader", async () => {
    const user = userEvent.setup();
    stub("fail");
    expect(await screen.findByText("Texto Uno.")).toBeInTheDocument();
    await openPanel(user);

    expect(screen.getByText("Aún no hay marcadores.")).toBeInTheDocument();
  });
});
