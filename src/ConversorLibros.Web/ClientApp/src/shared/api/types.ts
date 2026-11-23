export interface WebSettings {
  piperModelDir: string;
  outputRoot: string;
  ollamaUrl: string;
  ollamaModel: string;
  ffmpegOverride: string;
  defaultSpeed: number;
  defaultFormat: "opus" | "m4b";
}

export interface PiperValidationStatus {
  isValid: boolean;
  message: string;
}

export interface FfmpegStatus {
  found: boolean;
  resolvedPath: string | null;
  message: string;
}

export interface SettingsResponse {
  settings: WebSettings;
  piper: PiperValidationStatus;
  ffmpeg: FfmpegStatus;
  effectivePiperModelDir: string;
  piperAutoDetected: boolean;
}

export interface TestOllamaResponse {
  connected: boolean;
  modelInstalled: boolean;
  models: string[];
  message: string;
}

export interface DirectoryEntryDto {
  name: string;
  path: string;
}

export interface FileSystemBrowseResponse {
  currentPath: string | null;
  parentPath: string | null;
  directories: DirectoryEntryDto[];
}

export interface LibraryItemDto {
  id: string;
  title: string;
  author: string;
  format: "opus" | "m4b";
  chapterCount: number;
  durationSeconds: number | null;
  hasCover: boolean;
  coverUrl: string | null;
  hasReader: boolean;
}

export interface ReaderIndexChapterDto {
  index: number;
  title: string;
  durationMs: number;
  audioKind: string | null;
}

export interface ReaderIndexDto {
  available: boolean;
  reason: "no-data" | "unreadable" | null;
  title: string | null;
  hasCover: boolean;
  chapters: ReaderIndexChapterDto[];
}

/** Single resume position: chapter + ms only, so it is independent of layout. */
export interface ReaderProgressDto {
  chapterIndex: number;
  ms: number;
  updatedAt: string;
}

export interface SaveReaderProgressRequest {
  chapterIndex: number;
  ms: number;
}

export interface ReaderBookmarkDto {
  id: string;
  chapterIndex: number;
  ms: number;
  /** The server truncates it to 160 characters. */
  excerpt: string | null;
  note: string | null;
  createdAt: string;
}

export interface CreateReaderBookmarkRequest {
  chapterIndex: number;
  ms: number;
  excerpt?: string | null;
  note?: string | null;
}

export interface ReaderSentenceDto {
  text: string;
  startMs: number;
  endMs: number;
}

export interface ReaderParagraphDto {
  mode: "sentence" | "paragraph";
  sentences: ReaderSentenceDto[];
}

export interface ReaderChapterDto {
  schemaVersion: number;
  chapterIndex: number;
  title: string;
  durationMs: number;
  chunks: { order: number; startMs: number; endMs: number }[];
  paragraphs: ReaderParagraphDto[];
}

export interface ChapterSummaryDto {
  index: number;
  title: string;
  charCount: number;
  wordCount: number;
  estimatedSeconds: number;
  isLikelyFrontMatter: boolean;
  excerpt: string;
}

export interface BookMetadataDto {
  publisher: string | null;
  publishedDate: string | null;
  description: string | null;
  isbn: string | null;
  subjects: string[];
  series: string | null;
  seriesIndex: number | null;
}

export interface ChapterPreviewDto {
  index: number;
  title: string;
  wordCount: number;
  estimatedSeconds: number;
  isLikelyFrontMatter: boolean;
  text: string;
  truncated: boolean;
}

export interface InspectedBookDto {
  uploadId: string;
  title: string;
  author: string;
  language: string;
  hasCover: boolean;
  coverUrl: string | null;
  chapters: ChapterSummaryDto[];
  folderName: string;
  metadata: BookMetadataDto | null;
  totalWordCount: number;
  totalEstimatedSeconds: number;
}

export interface StartConversionRequest {
  uploadId: string;
  chapterIndexes: number[];
  speed: number;
  translate: boolean;
  sourceLanguage: string | null;
  format: "opus" | "m4b";
}

export interface ConversionStateDto {
  status: "idle" | "running" | "completed" | "cancelled" | "error";
  stage: string;
  progressFraction: number;
  currentChapter: string;
  timing: string;
  bookTitle: string | null;
  outputPath: string | null;
  warning: string | null;
  error: string | null;
  logs: string[];
}
