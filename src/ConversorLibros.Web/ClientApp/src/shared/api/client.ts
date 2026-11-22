import type {
  ChapterPreviewDto,
  ConversionStateDto,
  FileSystemBrowseResponse,
  InspectedBookDto,
  LibraryItemDto,
  SettingsResponse,
  StartConversionRequest,
  TestOllamaResponse,
  WebSettings,
} from "./types";

export class ApiError extends Error {
  status: number;

  constructor(status: number, message: string) {
    super(message);
    this.name = "ApiError";
    this.status = status;
  }
}

async function parseJson<T>(response: Response): Promise<T> {
  if (!response.ok) {
    let message = `Error HTTP ${response.status}`;
    try {
      const data = (await response.json()) as { error?: string };
      if (data.error) {
        message = data.error;
      }
    } catch {
      // Fall back to generic HTTP status message.
    }
    throw new ApiError(response.status, message);
  }
  return (await response.json()) as T;
}

export const api = {
  async getLibrary(): Promise<LibraryItemDto[]> {
    const res = await fetch("/api/library");
    return parseJson<LibraryItemDto[]>(res);
  },

  libraryDownloadUrl(id: string): string {
    return `/api/library/${encodeURIComponent(id)}/download`;
  },

  async openLibraryItem(id: string): Promise<void> {
    const res = await fetch(`/api/library/${encodeURIComponent(id)}/open`, {
      method: "POST",
    });
    if (!res.ok) {
      await parseJson(res);
    }
  },

  async inspectBook(file: File): Promise<InspectedBookDto> {
    const form = new FormData();
    form.append("file", file);
    const res = await fetch("/api/books/inspect", {
      method: "POST",
      body: form,
    });
    return parseJson<InspectedBookDto>(res);
  },

  async getChapterPreview(uploadId: string, index: number): Promise<ChapterPreviewDto> {
    const res = await fetch(
      `/api/books/${encodeURIComponent(uploadId)}/chapters/${index}`
    );
    return parseJson<ChapterPreviewDto>(res);
  },

  async getCurrentConversion(): Promise<ConversionStateDto> {
    const res = await fetch("/api/conversions/current");
    return parseJson<ConversionStateDto>(res);
  },

  async startConversion(request: StartConversionRequest): Promise<ConversionStateDto> {
    const res = await fetch("/api/conversions", {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify(request),
    });
    return parseJson<ConversionStateDto>(res);
  },

  async cancelConversion(): Promise<ConversionStateDto> {
    const res = await fetch("/api/conversions/cancel", { method: "POST" });
    return parseJson<ConversionStateDto>(res);
  },

  async getSettings(): Promise<SettingsResponse> {
    const res = await fetch("/api/settings");
    return parseJson<SettingsResponse>(res);
  },

  async saveSettings(settings: WebSettings): Promise<SettingsResponse> {
    const res = await fetch("/api/settings", {
      method: "PUT",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify(settings),
    });
    return parseJson<SettingsResponse>(res);
  },

  async testOllama(url: string, model: string): Promise<TestOllamaResponse> {
    const res = await fetch("/api/settings/test-ollama", {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify({ url, model }),
    });
    return parseJson<TestOllamaResponse>(res);
  },

  async browseFileSystem(path?: string): Promise<FileSystemBrowseResponse> {
    const query = path ? `?path=${encodeURIComponent(path)}` : "";
    const res = await fetch(`/api/fs/browse${query}`);
    return parseJson<FileSystemBrowseResponse>(res);
  },
};
