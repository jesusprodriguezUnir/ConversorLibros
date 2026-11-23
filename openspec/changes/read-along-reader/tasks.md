# Tasks: Read-Along Reader

## Review Workload Forecast

| Field | Value |
|-------|-------|
| Estimated changed lines | ~2,100 total (F0 290, F1 320, F2 340, F3a 360, F3b 360, F3c 150, F4 280) |
| 400-line budget risk | High (total); each slice under 400 |
| Chained PRs recommended | Yes |
| Suggested split | F0 → F1 → F2 → F3a → F3b → F3c → F4 (7 PRs) |
| Delivery strategy | ask-on-risk |
| Chain strategy | pending |

Decision needed before apply: Yes
Chained PRs recommended: Yes
Chain strategy: pending
400-line budget risk: High

Chain options: stacked-to-main (each PR merges in order) or feature-branch-chain (PR1 base = tracker branch, PRn base = PRn-1 branch). F3a/F3b at ~360 leave little margin; if either grows, split it again.

Constraints for all slices: no Core constructor changes (only the optional trailing `onChapterSynced` on `ConvertAsync`), atomic `.partial` → `File.Move(overwrite)` writes, `ProcessStartInfo.ArgumentList` only, no new dependencies, strict TDD (RED test task before each GREEN task).

### Suggested Work Units

| Unit | Goal | Likely PR | Focused test command | Runtime harness | Rollback boundary |
|------|------|-----------|----------------------|-----------------|-------------------|
| F0 | Sync map + sidecar writer + runner wiring | PR 1 | `dotnet test tests/ConversorLibros.Core.Tests --filter "FullyQualifiedName~SyncMap|FullyQualifiedName~ChunkHash|FullyQualifiedName~WavFile"` and `tests/ConversorLibros.Web.Tests --filter "FullyQualifiedName~ReaderSidecar|FullyQualifiedName~ConversionRunner"` | Reconvert a small EPUB with Opus; check `<book>/reader/` | Revert PR; delete `reader/` |
| F1 | Segmenter + aligner | PR 2 | `dotnet test tests/ConversorLibros.Core.Tests --filter "FullyQualifiedName~ReaderText"` | Inspect a chapter JSON paragraphs/sentences | Revert PR (pure Core classes) |
| F2 | Reader API | PR 3 | `dotnet test tests/ConversorLibros.Web.Tests --filter "FullyQualifiedName~Reader"` | `curl -r 0-99` on `/audio`; progress roundtrip | Revert PR |
| F3a | Static paginated view + TOC | PR 4 | `npx vitest run src/features/reader` | Open `/biblioteca/:id/leer` in dev | Revert PR |
| F3b | Audio sync + follow | PR 5 | `npx vitest run src/features/reader` | Play a chapter, watch highlight | Revert PR |
| F3c | Appearance + spread | PR 6 | `npx vitest run src/features/reader` | Resize across 1100px; switch theme | Revert PR |
| F4 | Resume + bookmarks | PR 7 | `npx vitest run src/features/reader` | Reload mid-chapter; bookmark jump | Revert PR |

## F0: Sync map, sidecar writer, runner wiring (spec: reader-sync-map)

- [x] 0.1 RED: `tests/ConversorLibros.Core.Tests/ChunkHashCharacterizationTests.cs` pins `SentenceChunker` hashes on fixtures (abbreviations, numbers, long sentence, dialogue). Passes on current code. Scenario: Chunk hashes unchanged.
- [x] 0.2 RED: `WavFileTests` for `ReadPcmInfo` on generated WAVs (valid, extra RIFF chunks, truncated, non-PCM). Scenarios: Duration from header; Unsupported or truncated WAV.
- [x] 0.3 GREEN: add `ReadPcmInfo` to `Core/Services/WavFile.cs`; create `Core/Models/ReaderModels.cs` (records per design).
- [x] 0.4 RED: `SyncMapBuilderTests`: sum of chunk ms equals `ReadDuration(chapter wav)`; cache-hit chunks timed; byteRate mismatch returns null. Scenarios: Durations add up; Cache-hit chunks.
- [x] 0.5 GREEN: `Core/Services/SyncMapBuilder.cs` (data = fileLength − 44, cumulative bytes → ms).
- [x] 0.6 RED: `BookConversionPipelineTests`: callback called once per chapter with post-translation `RawText`/`Title`; not called when omitted or when Build returns null. Scenarios: Callback omitted; Translated book.
- [x] 0.7 GREEN: optional `Action<ChapterSyncMap>? onChapterSynced = null` on `BookConversionPipeline.ConvertAsync`. No ctor change; Cli/App untouched.
- [x] 0.8 RED: `tests/ConversorLibros.Web.Tests/ReaderSidecarWriterTests`: atomic write, no `.partial` left, `index.json` merge by chapter index, reconversion rewrites only affected files. Scenarios: Chapter subset merges; Reconversion of an existing chapter; Interrupted write.
- [x] 0.9 GREEN: `Web/Services/ReaderSidecarWriter.cs` (`schemaVersion:1`, camelCase).
- [x] 0.10 RED: runner tests: no sidecars when export fails or format is M4B; Build mismatch or writer exception leaves conversion successful with the warning "Audiolibro creado, pero no se pudieron generar los datos de lectura."; cancellation propagates. Scenario: M4B export.
- [x] 0.11 GREEN: wire `Web/Services/ConversionRunner.cs`: collect maps, after Opus success call writer (aligner is a stub until F1: emit paragraph-mode only if needed, or write chunks only).

## F1: Segmentation and alignment (spec: reader-text-alignment)

- [x] 1.1 RED: `ReaderTextSegmenterTests`: regex parity with chunker on fixtures; abbreviation merge (`Sr.`); paragraph split on `\n\n`; empty/whitespace paragraphs dropped. Scenarios: Abbreviation does not split; Empty and whitespace paragraphs.
- [x] 1.2 GREEN: `Core/Services/ReaderTextSegmenter.cs` (`SplitParagraphs`, `SplitSentences`).
- [x] 1.3 RED: `ReaderTextAlignerTests`: sentences within one chunk; sentence spanning chunks (linear interpolation); mismatch → `mode:"paragraph"`; Roman heading across paragraphs → fallback; leftover chunks extend last paragraph; times monotonic and within chunk bounds; display text is original raw; dictionary passed through. Scenarios: Sentences inside one chunk; Sentence spanning two chunks; Mismatch in one paragraph; Roman numeral across paragraph break; Displayed text is original; Pronunciation dictionary.
- [x] 1.4 GREEN: `Core/Services/ReaderTextAligner.cs`.
- [x] 1.5 Wire aligner into `ConversionRunner` (`new SpanishTextNormalizer()`, null dictionary); extend F0 runner test to assert paragraphs in the sidecar.

## F2: Reader API (spec: reader-api)

- [x] 2.1 RED: `ReaderEndpointsTests` (WebApplicationFactory) for index/chapter 200, no-data 404, missing chapter 404, corrupt JSON handled, unknown or encoded id (`..%2F`) 404, negative or non-int `n` 404, non-guid bookmark id 404. Scenarios: Book with reader data; Book without reader data; Missing chapter; Corrupt JSON; Unknown book or invalid id; Path containment.
- [x] 2.2 RED: audio tests: full 200, Range 206, unsatisfiable 416, M4B/missing 404. Scenarios: Full request; Range request; Unsatisfiable Range; M4B or missing audio.
- [x] 2.3 RED: progress and bookmark tests: GET empty → 204; PUT roundtrip; invalid payload/unknown chapter/ms<0 → 400; malformed file → empty, next write replaces; concurrent PUTs stay valid JSON; bookmark create 201, list, delete unknown 404, note >500 → 400; unknown schemaVersion → 409. Scenarios: First save and read; Invalid payload; Concurrent writes; Create and list; Delete unknown id; Note too long.
- [x] 2.4 RED: `LibraryEndpointsTests`: `hasReader` true only for Opus with `reader/index.json`.
- [x] 2.5 GREEN: create `Web/Services/LibraryIds.cs` (extract from `LibraryScanner`, same ids), `LibraryBookResolver.cs`, `ReaderStore.cs` (per-book `SemaphoreSlim`).
- [x] 2.6 GREEN: `Web/Endpoints/ReaderEndpoints.cs` (every file passes `LibraryScanner.IsInside`); register in `Program.cs`; add `HasReader` to `LibraryItemDto` in `Web/Models/ApiDtos.cs` and `LibraryEndpoints.cs`.

## F3a: Static view + TOC (spec: reader-ui)

- [x] 3a.1 RED: `features/reader/pagination.test.ts` (`pagesPerView`, `columnMetrics`, `pageCount`, `pageOfRect`, `clampPage`, `spreadStart`) and `entries.test.ts` (`flattenEntries`, `entryAtMs`). Scenario: Spread threshold.
- [x] 3a.2 GREEN: `pagination.ts`, `entries.ts`.
- [x] 3a.3 RED: `ReaderPage.test.tsx` (stubbed `scrollWidth`): loads index + chapter, cover page present/absent, TOC navigation, chapter without data, page bounds, direct URL without data. Scenarios: Open chapter from TOC; Chapter without data; Bounds; Missing cover; Direct URL without data.
- [x] 3a.4 GREEN: `ReaderPage`, `PaginatedChapter`, `CoverPage`, `ReaderToc`, `PageIndicator`, `usePagination`, `reader.css`; types and client in `shared/api/{types,client}.ts`; route `/biblioteca/:id/leer` in `app/App.tsx`.
- [x] 3a.5 RED/GREEN: `BookCard.test.tsx` "Leer" enabled when `hasReader`, disabled with hint otherwise; edit `features/library/BookCard.tsx`. Scenarios: Reader available; Reader unavailable.

## F3b: Audio sync (spec: reader-ui)

- [x] 3b.1 RED: `sync.test.ts` (`findActiveEntry`, `pageTurnFraction`, `skipMs` bounds, `clampSpeed` 0.75–2, `bookPercent`). Scenarios: Skip bounds; Speed range.
- [x] 3b.2 GREEN: `sync.ts`.
- [x] 3b.3 RED: `useAudioSync.test.tsx` (fake rAF/audio): highlight follows, paragraph fallback highlights the whole paragraph, sentence spanning pages turns by fraction, chapter end, click-to-seek, audio load error. Scenarios: Highlight follows audio; Paragraph fallback; Sentence spanning two pages; Chapter ends; Click to seek; Audio load error.
- [x] 3b.4 RED: `useAutoFollow.test.tsx`: manual navigation suspends follow and shows "Volver al audio"; button and click-to-seek resume. Scenarios: Suspend; Resume follow; Click-to-seek resumes follow.
- [x] 3b.5 GREEN: `useAudioSync`, `useAutoFollow`, `AudioControls`, `BackToAudioButton`, `ReaderToolbar` (classList toggle on `[data-entry]`).

## F3c: Appearance + spread (spec: reader-ui)

- [x] 3c.1 RED: appearance reducer/storage tests (`reader.appearance.v1`), re-anchor by entry on resize/font change/theme change during playback. Scenarios: Resize keeps position; Change theme during playback; Font size change.
- [x] 3c.2 GREEN: `ReaderAppearanceContext`, `AppearancePanel`, `[data-theme]` CSS variables, `ResizeObserver` and `document.fonts.ready` re-anchor.

## F3d: Follow-ups (spec: reader-ui)

- [x] 3d.1 RED: page buttons disabled at the chapter bounds; `aria-live="polite"` on the page indicator; stale chapter fetch ignored; ResizeObserver recalculates the page count; re-anchor to the sentence spanning into a page with no sentence start; AppearancePanel a11y (Escape closes and refocuses the toggle, `aria-controls`, focus moves when a font button hits a bound, font size announced).
- [x] 3d.2 GREEN: `ReaderPage`, `PageIndicator`, `useReanchor`, `AppearancePanel` changes for the items above.

## F4: Resume + bookmarks (spec: reader-progress-bookmarks)

- [x] 4.1 RED: `useReaderProgress.test.tsx` (fake timers): PUT on pause, chapter change, every 10 s, keepalive on hide; no saved position → chapter 0; resume loads paused; ms clamped to chapter end; removed chapter falls back to 0; save failure non-blocking. Scenarios: Resume after reload; No saved position; Layout independence; Position beyond chapter end; Saved chapter no longer available; Save failure.
- [x] 4.2 GREEN: `useReaderProgress` and resume seek in `ReaderPage`.
- [ ] 4.3 RED: `BookmarksPanel.test.tsx`: add while playing (excerpt ≤160), jump, delete, reload persists, ms clamped, removed-chapter bookmark disabled, corrupt storage → empty list. Scenarios: Add while playing; Jump to bookmark; Bookmarks survive reload; Bookmark beyond chapter end; Bookmark of removed chapter; Corrupt storage file.
- [ ] 4.4 GREEN: `useBookmarks`, `BookmarksPanel`.
- [ ] 4.5 Update `CLAUDE.md` (reader endpoints and `reader/` layout). Run `dotnet build ConversorLibros.sln` and scoped tests; build App separately.
