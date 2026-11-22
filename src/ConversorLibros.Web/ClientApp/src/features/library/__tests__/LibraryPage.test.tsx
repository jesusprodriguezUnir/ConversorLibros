import { render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { MemoryRouter } from "react-router-dom";
import { describe, expect, it, vi, beforeEach, afterEach } from "vitest";
import { ToastProvider } from "../../../shared/components/Toast";
import { LibraryPage } from "../LibraryPage";
import type { LibraryItemDto } from "../../../shared/api/types";

const sampleBooks: LibraryItemDto[] = [
  {
    id: "book-1",
    title: "Dune",
    author: "Frank Herbert",
    format: "opus",
    chapterCount: 24,
    durationSeconds: 7320,
    hasCover: true,
    coverUrl: "/api/library/book-1/cover",
  },
  {
    id: "book-2",
    title: "El Quijote",
    author: "Miguel de Cervantes",
    format: "m4b",
    chapterCount: 0,
    durationSeconds: null,
    hasCover: false,
    coverUrl: null,
  },
];

describe("LibraryPage", () => {
  beforeEach(() => {
    vi.restoreAllMocks();
  });

  afterEach(() => {
    vi.unstubAllGlobals();
  });

  it("renders actionable empty state linking to /convertir when library is empty", async () => {
    vi.stubGlobal(
      "fetch",
      vi.fn().mockResolvedValue({
        ok: true,
        json: async () => [],
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
      expect(screen.getByText(/tu biblioteca está vacía/i)).toBeInTheDocument();
    });

    const link = screen.getByRole("link", { name: /convertir tu primer libro/i });
    expect(link).toHaveAttribute("href", "/convertir");
  });

  it("renders audiobook cards and filters by search text and format (Opus/M4B)", async () => {
    const user = userEvent.setup();
    vi.stubGlobal(
      "fetch",
      vi.fn().mockResolvedValue({
        ok: true,
        json: async () => sampleBooks,
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
      expect(screen.getByText("Dune")).toBeInTheDocument();
      expect(screen.getByText("El Quijote")).toBeInTheDocument();
    });

    await user.click(screen.getByRole("button", { name: "M4B" }));
    expect(screen.queryByText("Dune")).not.toBeInTheDocument();
    expect(screen.getByText("El Quijote")).toBeInTheDocument();

    await user.click(screen.getByRole("button", { name: "Todos" }));
    const search = screen.getByPlaceholderText(/buscar por título o autor/i);
    await user.type(search, "Herbert");

    expect(screen.getByText("Dune")).toBeInTheDocument();
    expect(screen.queryByText("El Quijote")).not.toBeInTheDocument();
  });

  it("calls POST /api/library/{id}/open when clicking Abrir carpeta", async () => {
    const user = userEvent.setup();
    const fetchMock = vi
      .fn()
      .mockResolvedValueOnce({
        ok: true,
        json: async () => sampleBooks,
      })
      .mockResolvedValueOnce({
        ok: true,
        status: 204,
      });

    vi.stubGlobal("fetch", fetchMock);

    render(
      <MemoryRouter>
        <ToastProvider>
          <LibraryPage />
        </ToastProvider>
      </MemoryRouter>
    );

    await waitFor(() => expect(screen.getByText("Dune")).toBeInTheDocument());

    const openButtons = screen.getAllByRole("button", { name: /abrir carpeta/i });
    await user.click(openButtons[0]);

    expect(fetchMock).toHaveBeenCalledWith("/api/library/book-1/open", {
      method: "POST",
    });
  });
});
