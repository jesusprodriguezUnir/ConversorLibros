import { render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { MemoryRouter } from "react-router-dom";
import { describe, expect, it, vi, beforeEach, afterEach } from "vitest";
import { ToastProvider } from "../../../shared/components/Toast";
import { ConversionProvider } from "../../../app/ConversionContext";
import { NewConversionPage } from "../NewConversionPage";
import type { ConversionStateDto, InspectedBookDto, SettingsResponse } from "../../../shared/api/types";

class StubEventSource {
  addEventListener() {}
  close() {}
}

const idleConversion: ConversionStateDto = {
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

const validSettings: SettingsResponse = {
  settings: {
    piperModelDir: "C:\\models\\piper",
    outputRoot: "C:\\out",
    ollamaUrl: "http://localhost:11434",
    ollamaModel: "qwen3:8b",
    ffmpegOverride: "",
  },
  piper: { isValid: true, message: "Modelo Piper listo para sintetizar." },
  ffmpeg: { found: true, resolvedPath: "ffmpeg.exe", message: "Usando: ffmpeg.exe" },
};

const inspectedBook: InspectedBookDto = {
  uploadId: "up-123",
  title: "Fundación",
  author: "Isaac Asimov",
  language: "en",
  hasCover: true,
  coverUrl: "/api/books/up-123/cover",
  chapters: [
    { index: 1, title: "Los psicohistoriadores", charCount: 4500 },
    { index: 2, title: "Los enciclopedistas", charCount: 6200 },
  ],
};

function renderPage() {
  return render(
    <MemoryRouter initialEntries={["/convertir"]}>
      <ToastProvider>
        <ConversionProvider>
          <NewConversionPage />
        </ConversionProvider>
      </ToastProvider>
    </MemoryRouter>
  );
}

describe("NewConversionPage", () => {
  beforeEach(() => {
    vi.stubGlobal("EventSource", StubEventSource);
  });

  afterEach(() => {
    vi.unstubAllGlobals();
    vi.restoreAllMocks();
  });

  it("shows warning banner linking to /ajustes when Piper model is invalid", async () => {
    const invalidSettings: SettingsResponse = {
      ...validSettings,
      piper: { isValid: false, message: "La carpeta del modelo Piper está vacía." },
    };

    vi.stubGlobal(
      "fetch",
      vi.fn().mockImplementation((url: string) => {
        if (url === "/api/settings") {
          return Promise.resolve({ ok: true, json: async () => invalidSettings });
        }
        return Promise.resolve({ ok: true, json: async () => idleConversion });
      })
    );

    renderPage();

    await waitFor(() => {
      expect(screen.getByText(/modelo piper no configurado/i)).toBeInTheDocument();
    });
    expect(screen.getByRole("link", { name: /ir a ajustes/i })).toHaveAttribute("href", "/ajustes");
  });

  it("uploading an EPUB displays book metadata, cover and selects all chapters by default", async () => {
    const user = userEvent.setup();
    vi.stubGlobal(
      "fetch",
      vi.fn().mockImplementation((url: string) => {
        if (url === "/api/settings") {
          return Promise.resolve({ ok: true, json: async () => validSettings });
        }
        if (url === "/api/books/inspect") {
          return Promise.resolve({ ok: true, json: async () => inspectedBook });
        }
        return Promise.resolve({ ok: true, json: async () => idleConversion });
      })
    );

    renderPage();

    const input = screen.getByLabelText(/seleccionar archivo epub/i) as HTMLInputElement;
    const file = new File(["epub"], "fundacion.epub", { type: "application/epub+zip" });
    await user.upload(input, file);

    await waitFor(() => {
      expect(screen.getByText("Fundación")).toBeInTheDocument();
      expect(screen.getByText("Isaac Asimov")).toBeInTheDocument();
      expect(screen.getByText("Los psicohistoriadores")).toBeInTheDocument();
      expect(screen.getByText("2 de 2 seleccionados")).toBeInTheDocument();
    });
  });

  it("Ninguno/Todos buttons update selected chapter count and disable Empezar when none selected", async () => {
    const user = userEvent.setup();
    vi.stubGlobal(
      "fetch",
      vi.fn().mockImplementation((url: string) => {
        if (url === "/api/settings") {
          return Promise.resolve({ ok: true, json: async () => validSettings });
        }
        if (url === "/api/books/inspect") {
          return Promise.resolve({ ok: true, json: async () => inspectedBook });
        }
        return Promise.resolve({ ok: true, json: async () => idleConversion });
      })
    );

    renderPage();

    const input = screen.getByLabelText(/seleccionar archivo epub/i) as HTMLInputElement;
    await user.upload(input, new File(["epub"], "fundacion.epub", { type: "application/epub+zip" }));
    await waitFor(() => expect(screen.getByText("2 de 2 seleccionados")).toBeInTheDocument());

    await user.click(screen.getByRole("button", { name: "Ninguno" }));
    expect(screen.getByText("0 de 2 seleccionados")).toBeInTheDocument();
    expect(screen.getByRole("button", { name: /empezar/i })).toBeDisabled();

    await user.click(screen.getByRole("button", { name: "Todos" }));
    expect(screen.getByText("2 de 2 seleccionados")).toBeInTheDocument();
    expect(screen.getByRole("button", { name: /empezar/i })).toBeEnabled();
  });

  it("submits selected chapters, speed, translation and format to startConversion", async () => {
    const user = userEvent.setup();
    const fetchMock = vi.fn().mockImplementation((url: string) => {
      if (url === "/api/settings") {
        return Promise.resolve({ ok: true, json: async () => validSettings });
      }
      if (url === "/api/books/inspect") {
        return Promise.resolve({ ok: true, json: async () => inspectedBook });
      }
      if (url === "/api/conversions") {
        return Promise.resolve({
          ok: true,
          status: 202,
          json: async () => ({ ...idleConversion, status: "running", stage: "Iniciando" }),
        });
      }
      return Promise.resolve({ ok: true, json: async () => idleConversion });
    });
    vi.stubGlobal("fetch", fetchMock);

    renderPage();

    const input = screen.getByLabelText(/seleccionar archivo epub/i) as HTMLInputElement;
    await user.upload(input, new File(["epub"], "fundacion.epub", { type: "application/epub+zip" }));
    await waitFor(() => expect(screen.getByText("Fundación")).toBeInTheDocument());

    await user.click(screen.getByRole("button", { name: "1.15x" }));
    await user.click(screen.getByRole("button", { name: /m4b/i }));
    await user.click(screen.getByRole("button", { name: /empezar/i }));

    await waitFor(() => {
      const postCall = fetchMock.mock.calls.find(
        ([url, opts]) => url === "/api/conversions" && opts?.method === "POST"
      );
      expect(postCall).toBeDefined();
      const body = JSON.parse(postCall![1].body as string);
      expect(body).toEqual({
        uploadId: "up-123",
        chapterIndexes: [1, 2],
        speed: 1.15,
        translate: false,
        sourceLanguage: null,
        format: "m4b",
      });
    });
  });
});
