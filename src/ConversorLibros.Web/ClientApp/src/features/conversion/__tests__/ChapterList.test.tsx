import { render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { afterEach, describe, expect, it, vi } from "vitest";
import { api } from "../../../shared/api/client";
import type { ChapterSummaryDto } from "../../../shared/api/types";
import { ChapterList } from "../ChapterList";

const chapters: ChapterSummaryDto[] = [
  {
    index: 1,
    title: "Los psicohistoriadores",
    charCount: 4500,
    wordCount: 750,
    estimatedSeconds: 300,
    isLikelyFrontMatter: false,
    excerpt: "Hace doce mil años…",
  },
  {
    index: 2,
    title: "Dedicatoria",
    charCount: 120,
    wordCount: 20,
    estimatedSeconds: 8,
    isLikelyFrontMatter: true,
    excerpt: "Para mis lectores.",
  },
];

function renderList(overrides: Partial<Parameters<typeof ChapterList>[0]> = {}) {
  const props = {
    uploadId: "up-123",
    chapters,
    selected: new Set([1]),
    onToggle: vi.fn(),
    onSelectAll: vi.fn(),
    onSelectNone: vi.fn(),
    onInvert: vi.fn(),
    onDeselectFrontMatter: vi.fn(),
    ...overrides,
  };
  render(<ChapterList {...props} />);
  return props;
}

describe("ChapterList", () => {
  afterEach(() => {
    vi.restoreAllMocks();
  });

  it("shows the selection count and the estimate for selected chapters only", () => {
    renderList();

    expect(screen.getByText("1 de 2 seleccionados")).toBeInTheDocument();
    expect(screen.getByText("~5 min · 750 palabras")).toBeInTheDocument();
  });

  it("wires the quick-select buttons to their handlers", async () => {
    const user = userEvent.setup();
    const props = renderList();

    await user.click(screen.getByRole("button", { name: "Todos" }));
    await user.click(screen.getByRole("button", { name: "Ninguno" }));
    await user.click(screen.getByRole("button", { name: "Invertir" }));

    expect(props.onSelectAll).toHaveBeenCalledTimes(1);
    expect(props.onSelectNone).toHaveBeenCalledTimes(1);
    expect(props.onInvert).toHaveBeenCalledTimes(1);
  });

  it("enables removing front matter only when a front-matter chapter is selected", () => {
    const { unmount } = render(
      <ChapterList
        uploadId="up-123"
        chapters={chapters}
        selected={new Set([1])}
        onToggle={vi.fn()}
        onSelectAll={vi.fn()}
        onSelectNone={vi.fn()}
        onInvert={vi.fn()}
        onDeselectFrontMatter={vi.fn()}
      />
    );
    expect(screen.getByRole("button", { name: "Quitar preliminares" })).toBeDisabled();
    unmount();

    renderList({ selected: new Set([1, 2]) });
    expect(screen.getByRole("button", { name: "Quitar preliminares" })).toBeEnabled();
  });

  it("expands a row, fetches its preview once and renders the text", async () => {
    const user = userEvent.setup();
    const getPreview = vi.spyOn(api, "getChapterPreview").mockResolvedValue({
      index: 1,
      title: "Los psicohistoriadores",
      wordCount: 750,
      estimatedSeconds: 300,
      isLikelyFrontMatter: false,
      text: "Hace doce mil años, la humanidad ya era cuatro veces mayor.",
      truncated: false,
    });
    renderList();

    const expand = screen.getByRole("button", { name: "Ver fragmento de Los psicohistoriadores" });
    expect(expand).toHaveAttribute("aria-expanded", "false");

    await user.click(expand);

    await waitFor(() => {
      expect(
        screen.getByText("Hace doce mil años, la humanidad ya era cuatro veces mayor.")
      ).toBeInTheDocument();
    });
    expect(getPreview).toHaveBeenCalledWith("up-123", 1);
    expect(
      screen.getByRole("button", { name: "Ocultar fragmento de Los psicohistoriadores" })
    ).toHaveAttribute("aria-expanded", "true");
  });
});
