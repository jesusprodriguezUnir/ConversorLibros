import { render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { describe, expect, it, vi, beforeEach, afterEach } from "vitest";
import { ToastProvider } from "../../../shared/components/Toast";
import { SettingsPage } from "../SettingsPage";
import type { FileSystemBrowseResponse, SettingsResponse, TestOllamaResponse } from "../../../shared/api/types";

const initialSettings: SettingsResponse = {
  settings: {
    piperModelDir: "C:\\models\\piper",
    outputRoot: "C:\\Users\\demo\\Documents\\ConversorLibros",
    ollamaUrl: "http://localhost:11434",
    ollamaModel: "qwen3:8b",
    ffmpegOverride: "",
    defaultSpeed: 1.0,
    defaultFormat: "opus",
  },
  piper: { isValid: true, message: "Modelo Piper listo para sintetizar." },
  ffmpeg: { found: true, resolvedPath: "C:\\ffmpeg\\bin\\ffmpeg.exe", message: "Usando: C:\\ffmpeg\\bin\\ffmpeg.exe" },
  effectivePiperModelDir: "C:\\models\\piper",
  piperAutoDetected: false,
};

describe("SettingsPage", () => {
  beforeEach(() => {
    vi.restoreAllMocks();
  });

  afterEach(() => {
    vi.unstubAllGlobals();
  });

  it("renders health badges for Piper and ffmpeg from GET /api/settings", async () => {
    vi.stubGlobal(
      "fetch",
      vi.fn().mockResolvedValue({
        ok: true,
        json: async () => initialSettings,
      })
    );

    render(
      <ToastProvider>
        <SettingsPage />
      </ToastProvider>
    );

    await waitFor(() => {
      expect(screen.getByDisplayValue("C:\\models\\piper")).toBeInTheDocument();
      expect(screen.getAllByText("Listo").length).toBeGreaterThanOrEqual(2);
    });
  });

  it("opens FolderBrowserModal, navigates into a folder and applies selected path", async () => {
    const user = userEvent.setup();
    const drivesResponse: FileSystemBrowseResponse = {
      currentPath: null,
      parentPath: null,
      directories: [{ name: "D:", path: "D:\\" }],
    };
    const subResponse: FileSystemBrowseResponse = {
      currentPath: "D:\\",
      parentPath: null,
      directories: [{ name: "Modelos", path: "D:\\Modelos" }],
    };
    const modelosResponse: FileSystemBrowseResponse = {
      currentPath: "D:\\Modelos",
      parentPath: "D:\\",
      directories: [],
    };

    vi.stubGlobal(
      "fetch",
      vi.fn().mockImplementation((url: string) => {
        if (url === "/api/settings") {
          return Promise.resolve({ ok: true, json: async () => initialSettings });
        }
        if (url === "/api/fs/browse") {
          return Promise.resolve({ ok: true, json: async () => drivesResponse });
        }
        if (url.includes(encodeURIComponent("D:\\Modelos"))) {
          return Promise.resolve({ ok: true, json: async () => modelosResponse });
        }
        return Promise.resolve({ ok: true, json: async () => subResponse });
      })
    );

    render(
      <ToastProvider>
        <SettingsPage />
      </ToastProvider>
    );

    await waitFor(() => expect(screen.getByDisplayValue("C:\\models\\piper")).toBeInTheDocument());

    const browseButtons = screen.getAllByRole("button", { name: /examinar/i });
    await user.click(browseButtons[0]);

    await waitFor(() => expect(screen.getByText("Modelos")).toBeInTheDocument());
    await user.click(screen.getByText("Modelos"));

    await waitFor(() => expect(screen.getByText("D:\\Modelos")).toBeInTheDocument());
    await user.click(screen.getByRole("button", { name: /seleccionar esta carpeta/i }));

    expect(screen.getByDisplayValue("D:\\Modelos")).toBeInTheDocument();
  });

  it("Probar conexión populates detected Ollama models selector", async () => {
    const user = userEvent.setup();
    const ollamaRes: TestOllamaResponse = {
      connected: true,
      modelInstalled: true,
      models: ["qwen3:8b", "llama3.1:8b"],
      message: "Conectado. El modelo 'qwen3:8b' está disponible.",
    };

    vi.stubGlobal(
      "fetch",
      vi.fn().mockImplementation((url: string) => {
        if (url === "/api/settings/test-ollama") {
          return Promise.resolve({ ok: true, json: async () => ollamaRes });
        }
        return Promise.resolve({ ok: true, json: async () => initialSettings });
      })
    );

    render(
      <ToastProvider>
        <SettingsPage />
      </ToastProvider>
    );

    await waitFor(() => expect(screen.getByDisplayValue("qwen3:8b")).toBeInTheDocument());

    await user.click(screen.getByRole("button", { name: /probar conexión/i }));

    await waitFor(() => {
      expect(screen.getByText(/el modelo 'qwen3:8b' está disponible/i)).toBeInTheDocument();
      expect(screen.getByRole("option", { name: "llama3.1:8b" })).toBeInTheDocument();
    });
  });

  it("shows the auto-detected Piper model badge", async () => {
    const autoDetected: SettingsResponse = {
      ...initialSettings,
      effectivePiperModelDir: "D:\\Modelos\\piper-es",
      piperAutoDetected: true,
    };
    vi.stubGlobal(
      "fetch",
      vi.fn().mockResolvedValue({ ok: true, json: async () => autoDetected })
    );

    render(
      <ToastProvider>
        <SettingsPage />
      </ToastProvider>
    );

    await waitFor(() => {
      expect(screen.getByText(/detectado automáticamente: d:\\modelos\\piper-es/i)).toBeInTheDocument();
    });
  });

  it("Guardar persists settings and displays validation feedback", async () => {
    const user = userEvent.setup();
    const savedResponse: SettingsResponse = {
      ...initialSettings,
      piper: { isValid: false, message: "Falta un fichero .onnx." },
    };

    const fetchMock = vi.fn().mockImplementation((url: string, opts?: RequestInit) => {
      if (url === "/api/settings" && opts?.method === "PUT") {
        return Promise.resolve({ ok: true, json: async () => savedResponse });
      }
      return Promise.resolve({ ok: true, json: async () => initialSettings });
    });
    vi.stubGlobal("fetch", fetchMock);

    render(
      <ToastProvider>
        <SettingsPage />
      </ToastProvider>
    );

    await waitFor(() => expect(screen.getByDisplayValue("C:\\models\\piper")).toBeInTheDocument());

    await user.click(screen.getByRole("button", { name: /guardar cambios/i }));

    await waitFor(() => {
      expect(screen.getByText("Falta un fichero .onnx.")).toBeInTheDocument();
    });
  });
});
