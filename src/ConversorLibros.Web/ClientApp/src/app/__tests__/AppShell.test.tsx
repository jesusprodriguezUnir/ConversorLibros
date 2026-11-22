import { render, screen, waitFor, act } from "@testing-library/react";
import { MemoryRouter } from "react-router-dom";
import { describe, expect, it, vi, beforeEach, afterEach } from "vitest";
import { ConversionProvider } from "../ConversionContext";
import { AppShell } from "../AppShell";
import type { ConversionStateDto } from "../../shared/api/types";

class MockEventSource {
  static instances: MockEventSource[] = [];
  listeners = new Map<string, Array<(e: MessageEvent) => void>>();
  url: string;
  closed = false;

  constructor(url: string) {
    this.url = url;
    MockEventSource.instances.push(this);
  }

  addEventListener(type: string, cb: (e: MessageEvent) => void) {
    const list = this.listeners.get(type) ?? [];
    list.push(cb);
    this.listeners.set(type, list);
  }

  emit(type: string, payload: unknown) {
    const list = this.listeners.get(type) ?? [];
    const event = { data: JSON.stringify(payload) } as MessageEvent;
    for (const cb of list) cb(event);
  }

  close() {
    this.closed = true;
  }
}

const idleState: ConversionStateDto = {
  status: "idle",
  stage: "Inactivo",
  progressFraction: 0,
  currentChapter: "",
  timing: "",
  bookTitle: null,
  outputPath: null,
  warning: null,
  error: null,
  logs: [],
};

describe("AppShell", () => {
  beforeEach(() => {
    MockEventSource.instances = [];
    vi.stubGlobal("EventSource", MockEventSource);
  });

  afterEach(() => {
    vi.unstubAllGlobals();
    vi.restoreAllMocks();
  });

  it("renders sidebar links for Biblioteca, Nueva conversión and Ajustes", async () => {
    vi.stubGlobal(
      "fetch",
      vi.fn().mockResolvedValue({
        ok: true,
        json: async () => idleState,
      })
    );

    render(
      <MemoryRouter initialEntries={["/biblioteca"]}>
        <ConversionProvider>
          <AppShell />
        </ConversionProvider>
      </MemoryRouter>
    );

    expect(screen.getByRole("link", { name: /biblioteca/i })).toBeInTheDocument();
    expect(screen.getByRole("link", { name: /nueva conversión/i })).toBeInTheDocument();
    expect(screen.getByRole("link", { name: /ajustes/i })).toBeInTheDocument();
  });

  it("hydrates active conversion from GET /api/conversions/current and displays sidebar progress widget", async () => {
    const runningState: ConversionStateDto = {
      ...idleState,
      status: "running",
      stage: "Sintetizando",
      progressFraction: 0.42,
      currentChapter: "Capítulo 2 (2/5)",
      bookTitle: "Dune — Frank Herbert",
    };

    vi.stubGlobal(
      "fetch",
      vi.fn().mockResolvedValue({
        ok: true,
        json: async () => runningState,
      })
    );

    render(
      <MemoryRouter initialEntries={["/biblioteca"]}>
        <ConversionProvider>
          <AppShell />
        </ConversionProvider>
      </MemoryRouter>
    );

    await waitFor(() => {
      expect(screen.getByText(/Dune — Frank Herbert/i)).toBeInTheDocument();
      expect(screen.getByText(/42%/i)).toBeInTheDocument();
    });
  });

  it("updates progress widget when SSE progress event arrives", async () => {
    vi.stubGlobal(
      "fetch",
      vi.fn().mockResolvedValue({
        ok: true,
        json: async () => idleState,
      })
    );

    render(
      <MemoryRouter initialEntries={["/biblioteca"]}>
        <ConversionProvider>
          <AppShell />
        </ConversionProvider>
      </MemoryRouter>
    );

    await waitFor(() => expect(MockEventSource.instances.length).toBe(1));

    act(() => {
      MockEventSource.instances[0].emit("progress", {
        ...idleState,
        status: "running",
        stage: "Exportando",
        progressFraction: 0.87,
        bookTitle: "El Quijote — Cervantes",
      });
    });

    expect(screen.getByText(/El Quijote — Cervantes/i)).toBeInTheDocument();
    expect(screen.getByText(/87%/i)).toBeInTheDocument();
  });
});
