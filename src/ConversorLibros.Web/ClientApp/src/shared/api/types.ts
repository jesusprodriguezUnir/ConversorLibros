export interface WebSettings {
  piperModelDir: string;
  outputRoot: string;
  ollamaUrl: string;
  ollamaModel: string;
  ffmpegOverride: string;
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
}

export interface ChapterSummaryDto {
  index: number;
  title: string;
  charCount: number;
}

export interface InspectedBookDto {
  uploadId: string;
  title: string;
  author: string;
  language: string;
  hasCover: boolean;
  coverUrl: string | null;
  chapters: ChapterSummaryDto[];
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
