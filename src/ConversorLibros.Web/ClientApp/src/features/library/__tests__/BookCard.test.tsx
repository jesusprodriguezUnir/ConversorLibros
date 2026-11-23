import { render, screen } from "@testing-library/react";
import { MemoryRouter } from "react-router-dom";
import { describe, expect, it } from "vitest";
import { BookCard } from "../BookCard";
import type { LibraryItemDto } from "../../../shared/api/types";

const book = (over: Partial<LibraryItemDto> = {}): LibraryItemDto => ({
  id: "b1",
  title: "Dune",
  author: "Frank Herbert",
  format: "opus",
  chapterCount: 3,
  durationSeconds: 100,
  hasCover: false,
  coverUrl: null,
  hasReader: true,
  ...over,
});

const renderCard = (b: LibraryItemDto) =>
  render(
    <MemoryRouter>
      <BookCard book={b} onOpen={() => {}} />
    </MemoryRouter>
  );

describe("BookCard reader entry point", () => {
  it("enables Leer and links to the reader when the book has reader data", () => {
    renderCard(book());
    expect(screen.getByRole("link", { name: /leer/i })).toHaveAttribute("href", "/biblioteca/b1/leer");
    expect(screen.queryByText(/reconvierte el libro/i)).not.toBeInTheDocument();
  });

  it.each([
    ["no reader data", book({ hasReader: false })],
    ["M4B", book({ format: "m4b", hasReader: false })],
  ])("disables Leer with a hint (%s)", (_n, b) => {
    renderCard(b);
    expect(screen.getByRole("button", { name: /leer/i })).toBeDisabled();
    expect(screen.getByText(/reconvierte el libro para habilitar el lector/i)).toBeInTheDocument();
    expect(screen.queryByRole("link", { name: /leer/i })).not.toBeInTheDocument();
  });
});
