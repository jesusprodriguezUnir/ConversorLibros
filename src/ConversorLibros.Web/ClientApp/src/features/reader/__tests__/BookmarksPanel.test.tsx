import { render, screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { describe, expect, it, vi } from "vitest";
import { BookmarksPanel } from "../BookmarksPanel";
import type { ReaderBookmarkDto, ReaderIndexChapterDto } from "../../../shared/api/types";

const chapters: ReaderIndexChapterDto[] = [
  { index: 1, title: "Uno", durationMs: 3000, audioKind: "opus" },
  { index: 3, title: "Tres", durationMs: 3000, audioKind: "opus" },
];
const mark = (id: string, chapterIndex: number, ms: number, excerpt: string | null): ReaderBookmarkDto => ({
  id,
  chapterIndex,
  ms,
  excerpt,
  note: null,
  createdAt: "2026-01-01T00:00:00Z",
});

function setup(bookmarks: ReaderBookmarkDto[], extra: Partial<Parameters<typeof BookmarksPanel>[0]> = {}) {
  const props = { onAdd: vi.fn(), onJump: vi.fn(), onDelete: vi.fn(), ...extra };
  render(<BookmarksPanel bookmarks={bookmarks} chapters={chapters} error={null} canAdd {...props} />);
  return props;
}

describe("BookmarksPanel", () => {
  it("toggles with aria-expanded/aria-controls and Escape returns focus to the toggle", async () => {
    const user = userEvent.setup();
    setup([]);
    const toggle = screen.getByRole("button", { name: "Marcadores" });
    expect(toggle).toHaveAttribute("aria-expanded", "false");

    await user.click(toggle);
    expect(toggle).toHaveAttribute("aria-expanded", "true");
    const panelId = toggle.getAttribute("aria-controls");
    expect(document.getElementById(panelId!)).toBeInTheDocument();

    await user.keyboard("{Escape}");
    expect(toggle).toHaveAttribute("aria-expanded", "false");
    expect(toggle).toHaveFocus();
  });

  it("shows an empty message and adds a bookmark", async () => {
    const user = userEvent.setup();
    const { onAdd } = setup([]);
    await user.click(screen.getByRole("button", { name: "Marcadores" }));

    expect(screen.getByText("Aún no hay marcadores.")).toBeInTheDocument();
    await user.click(screen.getByRole("button", { name: "Añadir marcador" }));
    expect(onAdd).toHaveBeenCalledOnce();
  });

  it("jumps to and deletes a bookmark, closing the panel after a jump", async () => {
    const user = userEvent.setup();
    const b = mark("a", 3, 65_000, "Frase marcada.");
    const { onJump, onDelete } = setup([b]);
    const toggle = screen.getByRole("button", { name: "Marcadores" });
    await user.click(toggle);

    expect(screen.getByText("Frase marcada.")).toBeInTheDocument();
    await user.click(screen.getByRole("button", { name: /^Tres · 1:05/ }));
    expect(onJump).toHaveBeenCalledWith(b);
    expect(toggle).toHaveAttribute("aria-expanded", "false");

    await user.click(toggle);
    await user.click(screen.getByRole("button", { name: "Eliminar marcador de Tres (1:05)" }));
    expect(onDelete).toHaveBeenCalledWith(b);
  });

  it("disables bookmarks whose chapter is not in the index, but still lets them be deleted", async () => {
    const user = userEvent.setup();
    const b = mark("gone", 9, 1000, "Texto antiguo.");
    const { onJump, onDelete } = setup([b]);
    await user.click(screen.getByRole("button", { name: "Marcadores" }));

    const jump = screen.getByRole("button", { name: /^Capítulo no disponible/ });
    expect(jump).toBeDisabled();
    await user.click(jump);
    expect(onJump).not.toHaveBeenCalled();
    await user.click(screen.getByRole("button", { name: /Eliminar marcador/ }));
    expect(onDelete).toHaveBeenCalledWith(b);
  });

  it("moves focus to the next (else previous, else add) item after deleting the focused one", async () => {
    const user = userEvent.setup();
    const list = [mark("a", 1, 1000, "A"), mark("b", 1, 2000, "B"), mark("c", 3, 3000, "C")];
    const onDelete = vi.fn();
    const { rerender } = render(
      <BookmarksPanel bookmarks={list} chapters={chapters} error={null} canAdd onAdd={vi.fn()} onJump={vi.fn()} onDelete={onDelete} />
    );
    const again = (items: ReaderBookmarkDto[]) =>
      rerender(
        <BookmarksPanel bookmarks={items} chapters={chapters} error={null} canAdd onAdd={vi.fn()} onJump={vi.fn()} onDelete={onDelete} />
      );
    await user.click(screen.getByRole("button", { name: "Marcadores" }));

    await user.click(screen.getByRole("button", { name: "Eliminar marcador de Uno (0:01)" }));
    again(list.slice(1));
    expect(screen.getByRole("button", { name: "Eliminar marcador de Uno (0:02)" })).toHaveFocus();

    await user.click(screen.getByRole("button", { name: "Eliminar marcador de Tres (0:03)" }));
    again(list.slice(1, 2));
    expect(screen.getByRole("button", { name: "Eliminar marcador de Uno (0:02)" })).toHaveFocus();

    await user.click(screen.getByRole("button", { name: "Eliminar marcador de Uno (0:02)" }));
    again([]);
    expect(screen.getByRole("button", { name: "Añadir marcador" })).toHaveFocus();
  });

  it("disables add when not allowed and announces errors", async () => {
    const user = userEvent.setup();
    setup([], { canAdd: false, error: "No se pudo guardar el marcador." });
    await user.click(screen.getByRole("button", { name: "Marcadores" }));

    expect(screen.getByRole("button", { name: "Añadir marcador" })).toBeDisabled();
    expect(screen.getByRole("alert")).toHaveTextContent("No se pudo guardar el marcador.");
  });
});
