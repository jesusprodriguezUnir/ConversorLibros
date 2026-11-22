# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project

Offline EPUB (DRM-free) → audiobook converter, Spanish (`es-ES`) TTS. Personal use, Windows only. The current plan is `docs/plan-libro-a-audiolibro.md` (phases 0–4); `docs/plan-original-instinct.md` is the superseded Python plan, kept for reference only. Docs and user-facing strings are in Spanish; code and comments are in English.

## Commands

```bash
dotnet build ConversorLibros.sln
dotnet test tests/ConversorLibros.Core.Tests
dotnet test tests/ConversorLibros.Core.Tests --filter "FullyQualifiedName~SpanishTextNormalizerTests"

# CLI (Windows x64: references the sherpa-onnx win-x64 native runtime)
dotnet run --project src/ConversorLibros.Cli -- convert book.epub --dry-run --chapters 1-3 --out ./out
dotnet run --project src/ConversorLibros.Cli -- convert book.epub --model-dir <piper_model_dir> --voice 0 --speed 1.0 --format opus --out ./out
dotnet run --project src/ConversorLibros.Cli -- convert book.epub --model-dir <piper_model_dir> --format m4b --delete-wav --out ./out

# Web UI (ASP.NET Core + React/Vite, default http://localhost:5180)
cd src/ConversorLibros.Web/ClientApp && npm install && npm run build
dotnet run --project src/ConversorLibros.Web
# Dev mode with Vite hot-reload (http://localhost:5173 proxying /api -> 5180)
cd src/ConversorLibros.Web/ClientApp && npm run dev

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

Three-layer split: `ConversorLibros.Core` (net10.0, platform-agnostic) is reused by two Windows hosts — `ConversorLibros.Cli` (console) and `ConversorLibros.App` (.NET MAUI, `net10.0-windows10.0.19041.0`, MVVM with CommunityToolkit.Mvvm).

**Pipeline** (`Core/Abstractions/PipelineInterfaces.cs`, orchestrated by `Core/Services/BookConversionPipeline.cs`):

`IBookSource` (EpubBookSource: VersOne.Epub + AngleSharp, spine/TOC order) → `ITextNormalizer` (SpanishTextNormalizer: numbers, ordinals, roman numerals, abbreviations, dialogue dashes, optional per-book pronunciation dictionary) → optional `CachedChapterTranslator` (OllamaTranslator, source → `es`) → `IChunker` (SentenceChunker) → `IChunkCache` lookup → `ITtsEngine` (PiperSherpaTtsEngine via sherpa-onnx VITS) → `IChapterAssembler` (WavChapterAssembler, one WAV per chapter).

Packaging: `IAudiobookExporter` with `OpusFolderExporter` (default) and `M4bExporter`, both in Core. `AudiobookChapterResolver` maps chapter WAVs to titles (translated when the cache has them). `AudiobookLibrary.Scan` reads the output folder for the App's library screen.

Key invariants:
- `TextChunk.Hash` is SHA-256 of normalized text + voice + speed + model version. Model version is the `.onnx` file name, so swapping models invalidates the cache. `FileSystemChunkCache` (under `<out>/cache`) is what makes runs resumable — keep the hash deterministic.
- Translation cache keys (`OllamaTranslator.ModelVersion` / `PromptVersion`, `CachedChapterTranslator` key) are part of the same contract: changing a prompt or model requires bumping `PromptVersion`.
- Chapter assembly streams to disk with `FileStream`; do not buffer whole chapters in memory.
- Everything is `async` with `CancellationToken` and `IProgress<ConversionProgress>`.
- Process calls (ffmpeg) use `ProcessStartInfo.ArgumentList`, never a concatenated argument string.
- CLI composes the pipeline manually in `Cli/Program.cs`. The App registers its services in `App/MauiProgram.cs` via `builder.Services`; pipeline wiring lives in `App/Services/ConversionService.cs`. Changing a Core service constructor means updating both call sites.
- The App's Piper synthesis is synchronous, so `ConversionService` runs the whole pipeline inside `Task.Run`.
- The App reads its paths and Ollama settings from `AppSettings` (MAUI `Preferences`); the folder picker is the WinRT `FolderPicker` in `App/Platforms/Windows`.

Tests are xUnit in `tests/ConversorLibros.Core.Tests`, covering Core only. The plan requires the normalizer to stay fully covered by unit tests.
