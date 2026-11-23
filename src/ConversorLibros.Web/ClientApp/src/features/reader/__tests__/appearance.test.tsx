import { act, render, renderHook, screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import type { ReactNode } from "react";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import {
  APPEARANCE_KEY,
  DEFAULT_APPEARANCE,
  FONT_SIZES_REM,
  appearanceReducer,
  loadAppearance,
} from "../appearance";
import { AppearancePanel } from "../AppearancePanel";
import { ReaderAppearanceProvider, useReaderAppearance } from "../ReaderAppearanceContext";

const MAX = FONT_SIZES_REM.length - 1;

describe("appearanceReducer", () => {
  it("sets the theme", () => {
    expect(appearanceReducer(DEFAULT_APPEARANCE, { type: "theme", theme: "dark" }).theme).toBe("dark");
  });

  it("moves the font step and clamps it at both ends", () => {
    const top = { ...DEFAULT_APPEARANCE, fontStep: MAX };
    const bottom = { ...DEFAULT_APPEARANCE, fontStep: 0 };
    expect(appearanceReducer(top, { type: "font", delta: 1 }).fontStep).toBe(MAX);
    expect(appearanceReducer(bottom, { type: "font", delta: -1 }).fontStep).toBe(0);
    expect(appearanceReducer(DEFAULT_APPEARANCE, { type: "font", delta: 1 }).fontStep).toBe(
      DEFAULT_APPEARANCE.fontStep + 1
    );
  });
});

describe("loadAppearance", () => {
  beforeEach(() => localStorage.clear());
  afterEach(() => vi.restoreAllMocks());

  it("returns defaults when nothing is stored", () => {
    expect(loadAppearance()).toEqual(DEFAULT_APPEARANCE);
  });

  it("reads a valid stored value", () => {
    localStorage.setItem(APPEARANCE_KEY, JSON.stringify({ theme: "sepia", fontStep: 0 }));
    expect(loadAppearance()).toEqual({ theme: "sepia", fontStep: 0 });
  });

  it.each([
    ["malformed JSON", "{nope"],
    ["a non-object", "42"],
    ["null", "null"],
  ])("falls back to defaults on %s", (_n, raw) => {
    localStorage.setItem(APPEARANCE_KEY, raw);
    expect(loadAppearance()).toEqual(DEFAULT_APPEARANCE);
  });

  it("validates each field on its own", () => {
    localStorage.setItem(APPEARANCE_KEY, JSON.stringify({ theme: "neon", fontStep: 1 }));
    expect(loadAppearance()).toEqual({ theme: DEFAULT_APPEARANCE.theme, fontStep: 1 });
    localStorage.setItem(APPEARANCE_KEY, JSON.stringify({ theme: "dark", fontStep: 99 }));
    expect(loadAppearance()).toEqual({ theme: "dark", fontStep: DEFAULT_APPEARANCE.fontStep });
    localStorage.setItem(APPEARANCE_KEY, JSON.stringify({ theme: "dark", fontStep: 1.5 }));
    expect(loadAppearance().fontStep).toBe(DEFAULT_APPEARANCE.fontStep);
  });

  it("survives a storage that throws", () => {
    vi.spyOn(Storage.prototype, "getItem").mockImplementation(() => {
      throw new Error("denied");
    });
    expect(loadAppearance()).toEqual(DEFAULT_APPEARANCE);
  });
});

const wrapper = ({ children }: { children: ReactNode }) => (
  <ReaderAppearanceProvider>{children}</ReaderAppearanceProvider>
);

describe("ReaderAppearanceProvider", () => {
  beforeEach(() => localStorage.clear());
  afterEach(() => vi.restoreAllMocks());

  it("persists changes and restores them", () => {
    const { result, unmount } = renderHook(() => useReaderAppearance(), { wrapper });
    act(() => result.current.setTheme("dark"));
    expect(JSON.parse(localStorage.getItem(APPEARANCE_KEY)!)).toEqual({
      theme: "dark",
      fontStep: DEFAULT_APPEARANCE.fontStep,
    });
    unmount();

    const again = renderHook(() => useReaderAppearance(), { wrapper });
    expect(again.result.current.theme).toBe("dark");
  });

  it("keeps working when saving fails", () => {
    vi.spyOn(Storage.prototype, "setItem").mockImplementation(() => {
      throw new Error("quota");
    });
    const { result } = renderHook(() => useReaderAppearance(), { wrapper });
    act(() => result.current.changeFont(1));
    expect(result.current.fontStep).toBe(DEFAULT_APPEARANCE.fontStep + 1);
  });

  it("requires the provider", () => {
    vi.spyOn(console, "error").mockImplementation(() => {});
    expect(() => renderHook(() => useReaderAppearance())).toThrow(/ReaderAppearanceProvider/);
  });
});

describe("AppearancePanel", () => {
  beforeEach(() => localStorage.clear());

  it("opens from its button, switches theme and changes the font size", async () => {
    const user = userEvent.setup();
    render(<AppearancePanel />, { wrapper });
    expect(screen.queryByRole("radio", { name: "Oscuro" })).not.toBeInTheDocument();

    await user.click(screen.getByRole("button", { name: "Apariencia" }));
    await user.click(screen.getByRole("radio", { name: "Oscuro" }));
    expect(screen.getByRole("radio", { name: "Oscuro" })).toBeChecked();

    await user.click(screen.getByRole("button", { name: "Aumentar texto" }));
    expect(JSON.parse(localStorage.getItem(APPEARANCE_KEY)!)).toEqual({
      theme: "dark",
      fontStep: DEFAULT_APPEARANCE.fontStep + 1,
    });
  });

  it("disables the font buttons at the bounds", async () => {
    const user = userEvent.setup();
    localStorage.setItem(APPEARANCE_KEY, JSON.stringify({ theme: "light", fontStep: MAX }));
    render(<AppearancePanel />, { wrapper });
    await user.click(screen.getByRole("button", { name: "Apariencia" }));

    expect(screen.getByRole("button", { name: "Aumentar texto" })).toBeDisabled();
    expect(screen.getByRole("button", { name: "Reducir texto" })).toBeEnabled();
  });
});
