# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project

Offline EPUB (DRM-free) → audiobook converter, Spanish (`es-ES`) TTS. Personal use, Windows only. The current plan is `docs/plan-libro-a-audiolibro.md` (phases 0–4); `docs/plan-original-instinct.md` is the superseded Python plan, kept for reference only. Docs and user-facing strings are in Spanish; code and comments are in English.

## Commands

```bash
dotnet build ConversorLibros.sln
dotnet test tests/ConversorLibros.Core.Tests
dotnet test tests/ConversorLibros.Web.Tests
dotnet test tests/ConversorLibros.Core.Tests --filter "FullyQualifiedName~SpanishTextNormalizerTests"

# CLI (Windows x64: references the sherpa-onnx win-x64 native runtime)
dotnet run --project src/ConversorLibros.Cli -- convert book.epub --dry-run --chapters 1-3 --out ./out
dotnet run --project src/ConversorLibros.Cli -- convert book.epub --model-dir <piper_model_dir> --voice 0 --speed 1.0 --format opus --out ./out
dotnet run --project src/ConversorLibros.Cli -- convert book.epub --model-dir <piper_model_dir> --format m4b --delete-wav --out ./out

# Web UI (ASP.NET Core + React/Vite, default http://localhost:5180)
# Dev mode (starts backend on :5180 + Vite on :5173 and opens the browser):
.\dev.ps1
# Production build (single server on http://localhost:5180):
cd src/ConversorLibros.Web/ClientApp && npm install && npm run build
dotnet run --project src/ConversorLibros.Web

# App (MAUI, Windows only; NOT in the .sln, build it explicitly)
dotnet build src/ConversorLibros.App -f net10.0-windows10.0.19041.0
```

- `--dry-run` writes `out/dry-run.txt` (normalized + chunked text) without loading any model — use it to check reading errors.
- Non-dry runs require `--model-dir` containing a Piper `*.onnx`, `tokens.txt` and `espeak-ng-data/`.
- `--voice` is a numeric sherpa-onnx speaker id for Piper (default `"0"`).
- `--format` is `opus` (default) or `m4b`. `--delete-wav` removes the chapter WAVs after a successful export.
- Models, audio (`*.wav/*.mp3/*.m4b/*.opus`), `*.epub`, `cache/` and `out/` are git-ignored; never commit them.

## Output format

- **Opus (default):** a folder `<out>/<sanitized title>/` with one `NNN - <chapter title>.opus` per chapter (16 kbps mono, `-application voip`, Ogg container, Vorbis tags) plus `cover.jpg` or `cover.png` when the EPUB has a cover. Chapter titles are sanitized for Windows (reserved names such as `CON` are prefixed, 120-character cap).
- **M4B:** a single `<out>/<sanitized title>.m4b` (AAC 64k mono, chapter markers from an ffmetadata file, optional cover as attached picture). Needs ffmpeg.
- Exports are atomic per file: encode to `<file>.partial`, then move into place.

## ffmpeg

`FfmpegLocator.Locate()` resolves ffmpeg in this order: `PATH`, then the WinGet package `Gyan.FFmpeg*\*\bin\ffmpeg.exe` (latest version last), then the WinGet `Links` folder. The lookup is hermetic: `PATH` and `LOCALAPPDATA` are parameters, so tests do not depend on the machine. If ffmpeg is missing, the CLI prints a warning, keeps the WAVs and exits 0; the App shows the same warning and the WAV folder. Install with `winget install Gyan.FFmpeg`.

## Dependency pins

- `org.k2fsa.sherpa.onnx` (Core) and `org.k2fsa.sherpa.onnx.runtime.win-x64` (CLI and App) are pinned to **1.13.8**. sherpa-onnx 2.x produced silent audio with the Piper models used here. Upgrade only after verifying non-silent output on a real chapter, and bump both packages together.

## Architecture

Three-layer split: `ConversorLibros.Core` (net10.0, platform-agnostic) is reused by three hosts — `ConversorLibros.Cli` (console), `ConversorLibros.App` (.NET MAUI, `net10.0-windows10.0.19041.0`, MVVM with CommunityToolkit.Mvvm) and `ConversorLibros.Web` (ASP.NET Core, see [Web host](#web-host)).

**Pipeline** (`Core/Abstractions/PipelineInterfaces.cs`, orchestrated by `Core/Services/BookConversionPipeline.cs`):

`IBookSource` (EpubBookSource: VersOne.Epub + AngleSharp, spine/TOC order) → `ITextNormalizer` (SpanishTextNormalizer: numbers, ordinals, roman numerals, abbreviations, dialogue dashes, optional per-book pronunciation dictionary) → optional `CachedChapterTranslator` (OllamaTranslator, source → `es`) → `IChunker` (SentenceChunker) → `IChunkCache` lookup → `ITtsEngine` (PiperSherpaTtsEngine via sherpa-onnx VITS) → `IChapterAssembler` (WavChapterAssembler, one WAV per chapter).

Packaging: `IAudiobookExporter` with `OpusFolderExporter` (default) and `M4bExporter`, both in Core. `AudiobookChapterResolver` maps chapter WAVs to titles (translated when the cache has them). `AudiobookLibrary.Scan` reads the output folder for the App's library screen.

Key invariants:
- `TextChunk.Hash` is SHA-256 of normalized text + voice + speed + model version. Model version is the `.onnx` file name, so swapping models invalidates the cache. `FileSystemChunkCache` (under `<out>/cache`) is what makes runs resumable — keep the hash deterministic.
- Translation cache keys (`OllamaTranslator.ModelVersion` / `PromptVersion`, `CachedChapterTranslator` key) are part of the same contract: changing a prompt or model requires bumping `PromptVersion`.
- Chapter assembly streams to disk with `FileStream`; do not buffer whole chapters in memory.
- Everything is `async` with `CancellationToken` and `IProgress<ConversionProgress>`.
- Process calls (ffmpeg) use `ProcessStartInfo.ArgumentList`, never a concatenated argument string.
- CLI composes the pipeline manually in `Cli/Program.cs`. The App registers its services in `App/MauiProgram.cs` via `builder.Services`; pipeline wiring lives in `App/Services/ConversionService.cs`. `ConversionRunner` in `Web/Services` is the third pipeline composition root. Changing a Core service constructor means updating all three call sites: `Cli/Program.cs`, `App/Services/ConversionService.cs` and `Web/Services/ConversionRunner.cs`.
- The App's Piper synthesis is synchronous, so `ConversionService` runs the whole pipeline inside `Task.Run`.
- The App reads its paths and Ollama settings from `AppSettings` (MAUI `Preferences`); the folder picker is the WinRT `FolderPicker` in `App/Platforms/Windows`.

## Web host

`ConversorLibros.Web` is an ASP.NET Core host (default `http://localhost:5180`) that serves the React/Vite UI from `ClientApp` (built into `wwwroot`) and reuses Core through `ConversionRunner`. Progress is streamed over SSE at `/api/conversions/events`.

- **Per-book layout:** `<OutputRoot>/<SanitizeComponent(title)>/`. The final `.opus` files and cover, or the `.m4b`, sit directly in the book folder. Intermediate files live in `work/` (chapter WAVs, `cache/`, `translation-cache/`). `LibraryScanner` lists this layout and the legacy top-level ones, and ignores `work/`.
- **Model auto-detection:** `PiperModelLocator.Resolve` uses the configured `piperModelDir` when valid; otherwise it probes `models/` folders near the executable and content root, then `%LOCALAPPDATA%\ConversorLibros\models`. The result is exposed as `effectivePiperModelDir` / `piperAutoDetected` in `SettingsResponse` and is never written to `settings.json`.
- **Download:** `GET /api/library/{id}/download` streams an `.m4b` file, or zips the `.opus` files and cover of an opus book. The path is checked for containment under `OutputRoot`.
- **Book metadata:** `EpubBookSource.BuildMetadata` fills `Book.Metadata` (`BookMetadata`: publisher, publication date, plain-text description, ISBN, subjects, series and index). Calibre's `0101-01-01` date is dropped, ISBN is accepted only from ISBN-scheme or `urn:isbn:` identifiers, and series indexes parse with `InvariantCulture`. The inspect response returns it as `metadata`.
- **Reader:** `ReaderEndpoints` (sidecar data in `<book>/reader/`, written by `ReaderSidecarWriter`, read through `ReaderStore`) exposes under `/api/library/{id}`: `GET /reader` (index), `GET /reader/chapters/{n}` (chapter JSON, 422 if corrupt), `GET /chapters/{n}/audio` (opus, range requests), `GET`/`PUT /reader/progress` (one chapter + ms, 204 when none) and `GET`/`POST /reader/bookmarks` plus `PUT`/`DELETE /reader/bookmarks/{guid}` (excerpt capped at 160, corrupt file reads as empty, newer schema version answers 409). Frontend in `ClientApp/src/features/reader/`: `ReaderPage` composes `useAudioSync` (audio + sentence highlight), `useAutoFollow`, `usePagination`/`useReanchor` (column pagination), `useReaderProgress` (resume) and `useBookmarks`; UI pieces are `ReaderToc`, `ReaderToolbar`, `AppearancePanel` and `BookmarksPanel`.
- **Chapter stats and preview:** `BookDtoMapper` normalizes text with `SpanishTextNormalizer` for word counts, so the numbers match the audio. `ChapterTextStatistics` holds the 150 wpm estimate and the front-matter heuristic (under 150 words, or a whole-title match). `GET /api/books/{uploadId}/chapters/{index}` returns the normalized text, capped at 4000 characters.

Tests are xUnit in `tests/ConversorLibros.Core.Tests` (Core) and `tests/ConversorLibros.Web.Tests` (Web host). Frontend tests use Vitest (`npx vitest run <path>`). The plan requires the normalizer to stay fully covered by unit tests.
