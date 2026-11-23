import { act, renderHook, waitFor } from "@testing-library/react";
import { beforeEach, describe, expect, it, vi } from "vitest";
import { useBookmarks } from "../useBookmarks";
import type { ReaderEntry } from "../entries";
import type { ReaderBookmarkDto } from "../../../shared/api/types";

const mocks = vi.hoisted(() => ({ get: vi.fn(), add: vi.fn(), del: vi.fn() }));
vi.mock("../../../shared/api/client", () => ({
  api: { getReaderBookmarks: mocks.get, addReaderBookmark: mocks.add, deleteReaderBookmark: mocks.del },
}));

const mark = (id: string, chapterIndex: number, ms: number): ReaderBookmarkDto => ({
  id,
  chapterIndex,
  ms,
  excerpt: null,
  note: null,
  createdAt: "2026-01-01T00:00:00Z",
});
const entry = (text: string): ReaderEntry => ({ index: 0, paragraphIndex: 0, text, startMs: 0, endMs: 9000 });
const chapters = [1, 2, 3].map((index) => ({ index, title: `C${index}`, durationMs: 5000, audioKind: "opus" as const }));

function deferred<T>() {
  let resolve!: (v: T) => void;
  const promise = new Promise<T>((r) => (resolve = r));
  return { promise, resolve };
}

function setup(over: Partial<Parameters<typeof useBookmarks>[0]> = {}) {
  const audio = document.createElement("audio");
  const props = {
    id: "b1",
    audio,
    chapters,
    current: 1 as number | null,
    ready: true,
    durationMs: 5000,
    entries: [entry("Hola.")],
    selectChapter: vi.fn(),
    seekTo: vi.fn(),
    ...over,
  };
  const hook = renderHook((p: typeof props) => useBookmarks(p), { initialProps: props });
  return { ...hook, props };
}

describe("useBookmarks", () => {
  beforeEach(() => {
    mocks.get.mockReset().mockResolvedValue([]);
    mocks.add.mockReset();
    mocks.del.mockReset();
  });

  it("orders bookmarks by chapter then ms after load and after an add that lands in the middle", async () => {
    mocks.get.mockResolvedValue([mark("c", 3, 100), mark("b", 1, 900), mark("a", 1, 100)]);
    mocks.add.mockResolvedValue(mark("n", 1, 500));
    const { result } = setup();
    await waitFor(() => expect(result.current.bookmarks.map((b) => b.id)).toEqual(["a", "b", "c"]));

    await act(() => result.current.add());
    expect(result.current.bookmarks.map((b) => b.id)).toEqual(["a", "n", "b", "c"]);
  });

  it("ignores a second add while the first request is pending", async () => {
    const d = deferred<ReaderBookmarkDto>();
    mocks.add.mockReturnValue(d.promise);
    const { result } = setup();
    await act(async () => {
      void result.current.add();
      void result.current.add();
    });
    expect(mocks.add).toHaveBeenCalledOnce();
    expect(result.current.adding).toBe(true);
    await act(async () => d.resolve(mark("n", 1, 0)));
    expect(result.current.adding).toBe(false);
    expect(result.current.bookmarks).toHaveLength(1);
  });

  it("does nothing while the chapter is not ready", async () => {
    const { result } = setup({ ready: false });
    await act(() => result.current.add());
    expect(mocks.add).not.toHaveBeenCalled();
  });

  it("caps the excerpt at 160 UTF-16 units without splitting a surrogate pair", async () => {
    mocks.add.mockResolvedValue(mark("n", 1, 0));
    const { result } = setup({ entries: [entry("a".repeat(159) + "😀tail")] });
    await act(() => result.current.add());
    expect(mocks.add.mock.calls[0][1].excerpt).toBe("a".repeat(159));
  });

  it("surfaces a delete failure and keeps the bookmark", async () => {
    mocks.get.mockResolvedValue([mark("a", 1, 0)]);
    mocks.del.mockRejectedValue(new Error("boom"));
    const { result } = setup();
    await waitFor(() => expect(result.current.bookmarks).toHaveLength(1));
    await act(() => result.current.remove(result.current.bookmarks[0]));
    expect(result.current.error).toBe("No se pudo eliminar el marcador.");
    expect(result.current.bookmarks).toHaveLength(1);
  });

  it("drops a pending cross-chapter jump when another chapter is picked by hand", async () => {
    const { result, rerender, props } = setup({ ready: false });
    act(() => result.current.jump(mark("a", 3, 4000)));
    expect(props.selectChapter).toHaveBeenCalledWith(3);

    rerender({ ...props, current: 2, ready: true });
    rerender({ ...props, current: 3, ready: true });
    expect(props.seekTo).not.toHaveBeenCalled();
  });

  it("ignores a late bookmark list after the book id changed", async () => {
    const old = deferred<ReaderBookmarkDto[]>();
    mocks.get.mockReturnValueOnce(old.promise).mockResolvedValueOnce([mark("new", 1, 0)]);
    const { result, rerender, props } = setup();
    rerender({ ...props, id: "b2" });
    await waitFor(() => expect(result.current.bookmarks.map((b) => b.id)).toEqual(["new"]));
    await act(async () => old.resolve([mark("old", 1, 0)]));
    expect(result.current.bookmarks.map((b) => b.id)).toEqual(["new"]);
  });

  it("ignores late add and remove responses after the book id changed", async () => {
    mocks.get.mockResolvedValueOnce([mark("x", 1, 0)]).mockResolvedValue([]);
    const addD = deferred<ReaderBookmarkDto>();
    const delD = deferred<void>();
    mocks.add.mockReturnValue(addD.promise);
    mocks.del.mockReturnValue(delD.promise);
    const { result, rerender, props } = setup();
    await waitFor(() => expect(result.current.bookmarks).toHaveLength(1));
    const first = result.current.bookmarks[0];
    await act(async () => {
      void result.current.add();
      void result.current.remove(first);
    });
    rerender({ ...props, id: "b2" });
    await waitFor(() => expect(result.current.bookmarks).toEqual([]));
    await act(async () => {
      addD.resolve(mark("late", 1, 0));
      delD.resolve();
    });
    expect(result.current.bookmarks).toEqual([]);
    expect(result.current.error).toBeNull();
  });
});
