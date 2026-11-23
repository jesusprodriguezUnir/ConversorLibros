import { describe, expect, it } from "vitest";
import { entryAtMs, flattenEntries } from "../entries";
import type { ReaderChapterDto } from "../../../shared/api/types";

const chapter: ReaderChapterDto = {
  schemaVersion: 1,
  chapterIndex: 1,
  title: "Uno",
  durationMs: 3000,
  chunks: [],
  paragraphs: [
    {
      mode: "sentence",
      sentences: [
        { text: "A.", startMs: 0, endMs: 1000 },
        { text: "B.", startMs: 1000, endMs: 2000 },
      ],
    },
    { mode: "paragraph", sentences: [{ text: "C.", startMs: 2000, endMs: 3000 }] },
  ],
};

describe("flattenEntries", () => {
  it("flattens paragraphs into ordered entries keeping the paragraph index", () => {
    const entries = flattenEntries(chapter);
    expect(entries.map((e) => [e.index, e.paragraphIndex, e.text])).toEqual([
      [0, 0, "A."],
      [1, 0, "B."],
      [2, 1, "C."],
    ]);
  });
});

describe("entryAtMs", () => {
  const entries = flattenEntries(chapter);
  it("finds the entry whose range contains the time", () => {
    expect(entryAtMs(entries, 0)).toBe(0);
    expect(entryAtMs(entries, 1500)).toBe(1);
    expect(entryAtMs(entries, 2000)).toBe(2);
  });
  it("clamps past the end and returns -1 when empty", () => {
    expect(entryAtMs(entries, 99999)).toBe(2);
    expect(entryAtMs([], 10)).toBe(-1);
  });
});
