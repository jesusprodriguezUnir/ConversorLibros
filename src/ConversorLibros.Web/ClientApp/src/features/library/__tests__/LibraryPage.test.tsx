import { render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { MemoryRouter } from "react-router-dom";
import { describe, expect, it, vi, beforeEach, afterEach } from "vitest";
import { ToastProvider } from "../../../shared/components/Toast";
import { LibraryPage } from "../LibraryPage";
import type { LibraryCatalogResponse } from "../../../shared/api/types";

const sampleCatalog: LibraryCatalogResponse = {
  booksRoot: "D:\\Libros",
  outputRoot: "D:\\Audiolibros",
  totalCount: 2,
  facets: {
    counts: { all: 2, withAudio: 1, textOnly: 1, converting: 0 },
    authors: [
      { name: "Frank Herbert", count: 1 },
      { name: "Brandon Sanderson", count: 1 },
    ],
    series: [{ name: "Nacidos de la Bruma", count: 1 }],
    tags: [{ name: "Fantasía", count: 1 }],
  },
  items: [
    {
      id: "book-1",
      title: "Dune",
      author: "Frank Herbert",
      series: null,
      seriesIndex: null,
      publisher: "Debolsillo",
      publishedDate: "2021",
      isbn: null,
      description: "Arrakis y la especia.",
      language: "es",
      tags: ["Ciencia Ficción"],
      epubPath: "D:\\Libros\\Dune.epub",
      hasEpub: true,
      hasCover: true,
      coverUrl: "/api/library/book-1/cover",
      hasReader: true,
      audioStatus: "ready",
      audioFormat: "opus",
      audioDurationSeconds: 7320,
      chapterCount: 24,
      readingChapter: 1,
      readingPercent: 25,
      lastReadAt: null,
      isLegacy: false,
      createdAt: "2026-01-01T00:00:00Z",
    },
    {
      id: "book-2",
      title: "El Imperio Final",
      author: "Brandon Sanderson",
      series: "Nacidos de la Bruma",
      seriesIndex: 1,
      publisher: "Nova",
      publishedDate: "2019",
      isbn: null,
      description: "Alomancia y ceniza.",
      language: "es",
      tags: ["Fantasía"],
      epubPath: "D:\\Libros\\Sanderson\\Imperio.epub",
      hasEpub: true,
      hasCover: false,
      coverUrl: null,
      hasReader: true,
      audioStatus: "none",
      audioFormat: null,
      audioDurationSeconds: null,
      chapterCount: 38,
      readingChapter: null,
      readingPercent: 0,
      lastReadAt: null,
      isLegacy: false,
      createdAt: "2026-01-02T00:00:00Z",
    },
  ],
};

describe("LibraryPage (Calibre 3-pane)", () => {
  beforeEach(() => {
    vi.restoreAllMocks();
  });

  afterEach(() => {
    vi.unstubAllGlobals();
  });

  it("renders folder onboarding empty state when catalog has no books", async () => {
    vi.stubGlobal(
      "fetch",
      vi.fn().mockResolvedValue({
        ok: true,
        json: async () => ({
          ...sampleCatalog,
          booksRoot: "",
          totalCount: 0,
          items: [],
          facets: {
            counts: { all: 0, withAudio: 0, textOnly: 0, converting: 0 },
            authors: [],
            series: [],
            tags: [],
          },
        }),
      })
    );

    render(
      <MemoryRouter>
        <ToastProvider>
          <LibraryPage />
        </ToastProvider>
      </MemoryRouter>
    );

    await waitFor(() => {
      expect(screen.getByText(/selecciona tu carpeta de libros/i)).toBeInTheDocument();
    });
  });

  it("renders 3-pane Calibre view, updates inspector on row click, and toggles table/grid", async () => {
    const user = userEvent.setup();
    vi.stubGlobal(
      "fetch",
      vi.fn().mockResolvedValue({
        ok: true,
        json: async () => sampleCatalog,
      })
    );

    render(
      <MemoryRouter>
        <ToastProvider>
          <LibraryPage />
        </ToastProvider>
      </MemoryRouter>
    );

    await waitFor(() => {
      expect(screen.getByText("Arrakis y la especia.")).toBeInTheDocument();
    });

    // Click second row -> inspector switches to El Imperio Final
    await user.click(screen.getByText("El Imperio Final"));
    expect(screen.getByText("Alomancia y ceniza.")).toBeInTheDocument();
    expect(screen.getByRole("link", { name: /^leer$/i })).toHaveAttribute(
      "href",
      "/biblioteca/book-2/leer"
    );

    // Toggle to cover grid view
    await user.click(screen.getByRole("button", { name: /vista portadas/i }));
    expect(screen.getAllByText("El Imperio Final").length).toBeGreaterThanOrEqual(2);
  });

  it("calls POST /api/library/scan when clicking Escanear", async () => {
    const user = userEvent.setup();
    const fetchMock = vi
      .fn()
      .mockResolvedValueOnce({ ok: true, json: async () => sampleCatalog })
      .mockResolvedValueOnce({
        ok: true,
        json: async () => ({ added: 1, updated: 0, unchanged: 2, removed: 0, errors: [] }),
      })
      .mockResolvedValueOnce({ ok: true, json: async () => sampleCatalog });

    vi.stubGlobal("fetch", fetchMock);

    render(
      <MemoryRouter>
        <ToastProvider>
          <LibraryPage />
        </ToastProvider>
      </MemoryRouter>
    );

    await waitFor(() => expect(screen.getByText("Arrakis y la especia.")).toBeInTheDocument());

    await user.click(screen.getByRole("button", { name: /escanear/i }));

    await waitFor(() => {
      expect(fetchMock).toHaveBeenCalledWith("/api/library/scan", { method: "POST" });
    });
  });
});
