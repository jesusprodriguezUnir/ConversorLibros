import { describe, expect, it } from "vitest";
import type { ReaderEntry } from "../entries";
import { bookPercent, clampSpeed, findActiveEntry, pageAtProgress, skipMs } from "../sync";

const entry = (index: number, startMs: number, endMs: number): ReaderEntry => ({
  index,
  paragraphIndex: 0,
  text: `s${index}`,
  startMs,
  endMs,
});
const entries = [entry(0, 0, 1000), entry(1, 1000, 2500), entry(2, 2500, 4000)];

describe("findActiveEntry", () => {
  it.each([
    [0, 0],
    [999, 0],
    [1000, 1],
    [3999, 2],
    [9000, 2],
  ])("ms %i -> entry %i", (ms, expected) => expect(findActiveEntry(entries, ms)).toBe(expected));

  it("returns -1 for no entries or before the first entry", () => {
    expect(findActiveEntry([], 100)).toBe(-1);
    expect(findActiveEntry([entry(0, 500, 900)], 100)).toBe(-1);
  });
});

describe("pageAtProgress", () => {
  it("picks the page of the line reached by the playback progress", () => {
    const pages = [3, 3, 4, 4];
    expect(pageAtProgress(pages, 0)).toBe(3);
    expect(pageAtProgress(pages, 0.49)).toBe(3);
    expect(pageAtProgress(pages, 0.5)).toBe(4);
    expect(pageAtProgress(pages, 1.2)).toBe(4);
  });

  it("clamps negative progress and handles a single rect", () => {
    expect(pageAtProgress([7], -1)).toBe(7);
    expect(pageAtProgress([7], 0.9)).toBe(7);
  });
});

describe("skipMs", () => {
  it("never goes below zero", () => expect(skipMs(5000, -15000, 60000)).toBe(0));
  it("never goes past the chapter end", () => expect(skipMs(55000, 15000, 60000)).toBe(60000));
  it("moves within bounds", () => expect(skipMs(20000, 15000, 60000)).toBe(35000));
});

describe("clampSpeed", () => {
  it.each([
    [0.5, 0.75],
    [1.25, 1.25],
    [2.5, 2],
  ])("%d -> %d", (input, expected) => expect(clampSpeed(input)).toBe(expected));
});

describe("bookPercent", () => {
  const durations = [1000, 3000];
  it("adds the finished chapters and the position inside the current one", () => {
    expect(bookPercent(durations, 0, 0)).toBe(0);
    expect(bookPercent(durations, 1, 1500)).toBe(63);
    expect(bookPercent(durations, 1, 99999)).toBe(100);
  });
  it("is 0 without durations", () => expect(bookPercent([], 0, 100)).toBe(0));
});
