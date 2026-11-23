import { describe, expect, it } from "vitest";
import {
  clampPage,
  columnMetrics,
  pageCount,
  pageOfRect,
  pagesPerView,
  spreadStart,
} from "../pagination";

describe("pagesPerView", () => {
  it("shows a spread at 1100 px and a single page at 1099 px", () => {
    expect(pagesPerView(1100)).toBe(2);
    expect(pagesPerView(1099)).toBe(1);
  });
});

describe("columnMetrics", () => {
  it("uses integer column widths and an explicit content width", () => {
    expect(columnMetrics(1000, 48, 1)).toEqual({ colW: 1000, stride: 1048, contentWidth: 1000 });
    // floor((1101 - 48) / 2) = 526
    expect(columnMetrics(1101, 48, 2)).toEqual({ colW: 526, stride: 574, contentWidth: 1100 });
  });
});

describe("pageCount", () => {
  const m = columnMetrics(1000, 48, 1);
  it("derives pages from the scroll width", () => {
    expect(pageCount(1000, m, 48)).toBe(1);
    expect(pageCount(3 * 1048 - 48, m, 48)).toBe(3);
  });
  it("never returns less than one page", () => {
    expect(pageCount(0, m, 48)).toBe(1);
  });
});

describe("pageOfRect", () => {
  it("measures relative to the content origin", () => {
    expect(pageOfRect(100, 100, 1048)).toBe(0);
    expect(pageOfRect(100 + 1048 * 2 + 5, 100, 1048)).toBe(2);
    expect(pageOfRect(50, 100, 1048)).toBe(0);
  });
});

describe("clampPage", () => {
  it("keeps the page inside 0..count-1", () => {
    expect(clampPage(-1, 5)).toBe(0);
    expect(clampPage(9, 5)).toBe(4);
    expect(clampPage(2, 5)).toBe(2);
  });
});

describe("spreadStart", () => {
  it("aligns to the first page of the spread", () => {
    expect(spreadStart(3, 2)).toBe(2);
    expect(spreadStart(4, 2)).toBe(4);
    expect(spreadStart(3, 1)).toBe(3);
  });
});
