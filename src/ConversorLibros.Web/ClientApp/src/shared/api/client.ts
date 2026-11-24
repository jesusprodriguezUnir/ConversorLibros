import type {
  CatalogBookDto,
  ChapterPreviewDto,
  ConversionStateDto,
  FileSystemBrowseResponse,
  FolderScanSummaryDto,
  ImportBooksResponse,
  InspectedBookDto,
  CreateReaderBookmarkRequest,
  LibraryCatalogResponse,
  LibraryItemDto,
  ReaderBookmarkDto,
  ReaderChapterDto,
  ReaderIndexDto,
  ReaderProgressDto,
  SaveReaderProgressRequest,
  SetLibraryFolderResponse,
  SettingsResponse,
  StartConversionRequest,
  TestOllamaResponse,
  UpdateBookMetadataRequest,
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

  async getLibraryCatalog(params?: {
    q?: string;
    author?: string;
    series?: string;
    tag?: string;
    audioStatus?: string;
    sort?: string;
    dir?: "asc" | "desc";
  }): Promise<LibraryCatalogResponse> {
    const sp = new URLSearchParams();
    if (params?.q) sp.set("q", params.q);
    if (params?.author) sp.set("author", params.author);
    if (params?.series) sp.set("series", params.series);
    if (params?.tag) sp.set("tag", params.tag);
    if (params?.audioStatus) sp.set("audioStatus", params.audioStatus);
    if (params?.sort) sp.set("sort", params.sort);
    if (params?.dir) sp.set("dir", params.dir);
    const qs = sp.toString();
    const res = await fetch(`/api/library/catalog${qs ? `?${qs}` : ""}`);
    return parseJson<LibraryCatalogResponse>(res);
  },

  async setLibraryFolder(path: string): Promise<SetLibraryFolderResponse> {
    const res = await fetch("/api/library/folder", {
      method: "PUT",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify({ path }),
    });
    return parseJson<SetLibraryFolderResponse>(res);
  },

  async scanLibraryFolder(): Promise<FolderScanSummaryDto> {
    const res = await fetch("/api/library/scan", { method: "POST" });
    return parseJson<FolderScanSummaryDto>(res);
  },

  async importBooks(files: File[]): Promise<ImportBooksResponse> {
    const form = new FormData();
    for (const file of files) {
      form.append("files", file);
    }
    const res = await fetch("/api/library/import", {
      method: "POST",
      body: form,
    });
    return parseJson<ImportBooksResponse>(res);
  },

  async updateBookMetadata(id: string, patch: UpdateBookMetadataRequest): Promise<CatalogBookDto> {
    const res = await fetch(`/api/library/${encodeURIComponent(id)}`, {
      method: "PATCH",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify(patch),
    });
    return parseJson<CatalogBookDto>(res);
  },

  async uploadBookCover(id: string, file: File): Promise<void> {
    const form = new FormData();
    form.append("file", file);
    const res = await fetch(`/api/library/${encodeURIComponent(id)}/cover`, {
      method: "PUT",
      body: form,
    });
    if (!res.ok) await parseJson(res);
  },

  async deleteBook(id: string): Promise<void> {
    const res = await fetch(`/api/library/${encodeURIComponent(id)}`, {
      method: "DELETE",
    });
    if (!res.ok) await parseJson(res);
  },

  libraryDownloadUrl(id: string): string {
    return `/api/library/${encodeURIComponent(id)}/download`;
  },

  libraryCoverUrl(id: string): string {
    return `/api/library/${encodeURIComponent(id)}/cover`;
  },

  async getReaderIndex(id: string): Promise<ReaderIndexDto> {
    const res = await fetch(`/api/library/${encodeURIComponent(id)}/reader`);
    return parseJson<ReaderIndexDto>(res);
  },

  async getReaderChapter(id: string, index: number): Promise<ReaderChapterDto> {
    const res = await fetch(`/api/library/${encodeURIComponent(id)}/reader/chapters/${index}`);
    return parseJson<ReaderChapterDto>(res);
  },

  /** Saved resume position, or null when none is stored (204). */
  async getReaderProgress(id: string): Promise<ReaderProgressDto | null> {
    const res = await fetch(`/api/library/${encodeURIComponent(id)}/reader/progress`);
    if (res.status === 204) return null;
    return parseJson<ReaderProgressDto>(res);
  },

  /** `keepalive` lets the request outlive the page (used when it is being hidden). */
  async saveReaderProgress(id: string, progress: SaveReaderProgressRequest, keepalive = false): Promise<void> {
    const res = await fetch(`/api/library/${encodeURIComponent(id)}/reader/progress`, {
      method: "PUT",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify(progress),
      ...(keepalive ? { keepalive: true } : {}),
    });
    if (!res.ok) await parseJson(res);
  },

  /** The server answers an empty list for a missing or corrupt bookmarks file. */
  async getReaderBookmarks(id: string): Promise<ReaderBookmarkDto[]> {
    const res = await fetch(`/api/library/${encodeURIComponent(id)}/reader/bookmarks`);
    return parseJson<ReaderBookmarkDto[]>(res);
  },

  /** 201 with the created bookmark; 400 when the chapter does not exist or `ms` is negative. */
  async addReaderBookmark(id: string, bookmark: CreateReaderBookmarkRequest): Promise<ReaderBookmarkDto> {
    const res = await fetch(`/api/library/${encodeURIComponent(id)}/reader/bookmarks`, {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify(bookmark),
    });
    return parseJson<ReaderBookmarkDto>(res);
  },

  async deleteReaderBookmark(id: string, bookmarkId: string): Promise<void> {
    const res = await fetch(
      `/api/library/${encodeURIComponent(id)}/reader/bookmarks/${encodeURIComponent(bookmarkId)}`,
      { method: "DELETE" }
    );
    if (!res.ok) await parseJson(res);
  },

  readerAudioUrl(id: string, index: number): string {
    return `/api/library/${encodeURIComponent(id)}/chapters/${index}/audio`;
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
