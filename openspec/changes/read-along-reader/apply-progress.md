# Apply Progress: read-along-reader

## F0 (sync map, sidecar writer, runner wiring): DONE (11/11), branch feat/read-along-reader-f0

Tasks 0.1-0.11 complete. Scoped tests green: Core 28 (Pipeline|SyncMap|ChunkHash|WavFile), Web 17 (ReaderSidecar|ConversionRunner|ConversionEndpoints). Cli and App (net10.0-windows) build.

### TDD evidence
| Task | Test file | RED | GREEN |
|------|-----------|-----|-------|
| 0.1 | Core.Tests/ChunkHashCharacterizationTests.cs (5) | pinned on current code (placeholder run, then literals) | pass |
| 0.2/0.3 | Core.Tests/WavFileTests.cs (6) | compile error: ReadPcmInfo missing | pass |
| 0.4/0.5 | Core.Tests/SyncMapBuilderTests.cs (5) | compile error: SyncMapBuilder missing | pass |
| 0.6/0.7 | Core.Tests/PipelineSyncCallbackTests.cs (5) | compile error: onChapterSynced missing | pass |
| 0.8/0.9 | Web.Tests/ReaderSidecarWriterTests.cs (6) | compile error: writer missing | pass |
| 0.10/0.11 | Web.Tests/ConversionRunnerReaderDataTests.cs (5) | compile error: ReaderDataPublisher missing | pass |

### Deviations / notes
- Runner logic extracted to `Web/Services/ReaderDataPublisher.cs` (testable without Piper/ffmpeg); runner calls it only after a successful export. M4B returns null without writing. Missing map (builder null) writes the rest and returns the warning.
- Chunk characterization tests pin CurrentCulture to invariant: `SentenceChunker` formats speed with the current culture ("1.00" vs "1,00"), so real hashes are machine-culture dependent (pre-existing, untouched).
- Sidecar chunk entries omit normalized text (order/startMs/endMs only, per design JSON). Paragraphs are `[]` until F1.
- Writer: malformed index is replaced; index with schemaVersion != 1 throws InvalidDataException (never overwritten) and the publisher turns it into the warning.
- Pipeline callback tests live in a new file instead of BookConversionPipelineTests.cs.
- A running ConversorLibros.Web process locks bin/; tests were run with `-p:BaseOutputPath=<scratch>`.
- Size: ~360 production lines, ~700 test lines (over the 400 budget as a whole).
- .gitignore line 13 `models/` ignores `src/ConversorLibros.Core/Models/` (Book.cs is untracked too): `ReaderModels.cs` will not be staged unless .gitignore is fixed or it is force-added.

## Delivery decision

- F0 accepted as `size:exception` by the user (2026-11-23): ~1035 changed lines (~360 production, ~700 tests) vs 400 budget. No split into Core/Web halves.
- Blocker resolved: `.gitignore` `models/` rule ignored `Core/Models` and `Web/Models`; fixed on main via PR #2 (e34dde6). `ReaderModels.cs` is now trackable.

## Verify follow-up (2026-11-23)

- W1 fixed: index entries carry `audioKind` ("opus"); legacy entries without it default to "opus" on merge.
- W2 fixed: export + publish extracted to `ReaderDataPublisher.ExportThenPublishAsync`; tests prove sidecars are written only after export and never when export throws.
- W3 (partial map at runner level) deferred as debt.
- Web scoped tests: 19/19 green.

## F1 (segmentation and alignment): DONE (5/5), branch feat/read-along-reader-f1

Mode: Strict TDD. Tasks 1.1-1.5 complete.

### TDD evidence
| Task | Test file | Safety net | RED | GREEN | Triangulation | REFACTOR |
|------|-----------|------------|-----|-------|---------------|----------|
| 1.1/1.2 | Core.Tests/ReaderTextSegmenterTests.cs (7) | ChunkHash/SentenceChunker/SyncMap/PipelineSync 16/16 green before changes | compile error CS0103: ReaderTextSegmenter missing | 7/7 + ChunkHash 5/5 | abbreviation+lowercase merge, plain split, blank paragraphs, 4-fixture parity theory vs real SentenceChunker(50) | none needed |
| 1.3/1.4 | Core.Tests/ReaderTextAlignerTests.cs (7) | same | compile error CS0103: ReaderTextAligner missing | 7/7 | one-chunk interpolation (0-250/250-1000), spanning chunks, mismatch (different length, neighbours stay sentence), Roman across break (trailing paragraph stays sentence), leftover chunks, dictionary + original text, blank paragraphs | removed unused parameter from BuildParagraphs; re-run green |
| 1.5 | Web.Tests/ConversionRunnerReaderDataTests.cs (+1) | 7/7 green before change | Assert.Single failure: paragraphs empty | 8/8 | existing Opus/M4B/warning/cancel tests still green | none needed |

### Work unit evidence
| Evidence | Value |
|---|---|
| Focused tests | `dotnet test tests/ConversorLibros.Core.Tests --filter "ReaderText|ChunkHash|SyncMapBuilder|PipelineSyncCallback|SentenceChunker|SpanishTextNormalizer"` → 36/36 pass; `dotnet test tests/ConversorLibros.Web.Tests --filter "ReaderSidecarWriter|ConversionRunnerReaderData|ConversionEndpoints"` → 20/20 pass (both with `-p:BaseOutputPath=<scratch>`) |
| Runtime harness | `PublishAsync_Opus_WritesAlignedParagraphs` writes and parses `reader/chapter_001.json` through the real writer. Manual check on a real EPUB conversion not run (needs Piper model + ffmpeg). |
| Rollback boundary | Delete `Core/Services/ReaderText{Segmenter,Aligner}.cs` and their tests; restore `[]` paragraphs in `ReaderDataPublisher`; regex visibility in `SentenceChunker` back to private. Sidecar schema unchanged. |

### Deviations / notes
- Aligner wired in `Web/Services/ReaderDataPublisher.cs` (static `SpanishTextNormalizer`, null dictionary), not in `ConversionRunner` directly: the runner delegates reader data to the publisher since F0.
- Segmenter reuses `SentenceChunker.SentenceSplitRegex()` (made `internal`, behaviour unchanged; ChunkHash tests green) instead of a copy.
- Merge rule extended: besides "normalized accumulation does not end in [.!?…]", a piece starting with a lowercase letter is merged too. Needed for the spec scenario "5 p. m. y se fue." (one sentence). Sentence boundaries never affect verification.
- Alignment refined vs design ("consume chunks until length is reached"): paragraphs match the chunk stream anchored at chunk boundaries (start at a chunk start, end at a chunk end). On a mismatch, alignment resyncs at the nearest later paragraph (lookahead 8) that matches anchored; the skipped paragraphs share that range in `paragraph` mode, split by normalized length. Length-based consumption cascaded the Roman-heading mismatch into the following paragraphs.
- Timing: per chunk, characters are weighted only inside entries (inter-entry spaces excluded), so the spec numbers hold exactly (100/300 chars → 250 ms). Entry start = previous entry end (no gaps), first start 0, last end = DurationMs (covers leftover chunks).
- Size: ~482 authored changed lines in src+tests (new production 264, new tests 193, modified 22+/3-) vs 400 budget / ~450 soft cap; openspec docs excluded. Needs user size:exception before PR (as F0).
- Known limit: if a mismatched paragraph finds no anchored resync within 8 paragraphs, the rest of the chapter degrades to paragraph mode (coverage stays correct).

- F1 accepted as `size:exception` by the user (2026-11-23): ~480 changed lines vs 400 budget.

### F1 verify follow-up (2026-11-23)

- W1 fixed: `ChunkStream` lookups use binary search over the strictly ascending chunk starts/ends; `FindAnchored` starts at the first chunk at or after the cursor. RED: `Align_LargeChapterWithManyMismatches_CompletesQuickly` (10k paragraphs, 1k mismatches) took 9.8 s; GREEN well under the 3 s bound.
- W2 fixed: `BuildParagraphs` visits only the chunks inside each entry's range (`FirstChunkEndingAfter`), same results.
- W4 fixed: pinning tests for blank raw text, no chunks, resync never found and paragraph boundaries on chunk boundaries (they passed on first run: they characterize existing behavior).
- W3 (short duplicated paragraph anchoring at an earlier duplicate) deferred as tech debt: timing-only degradation.
- Scoped runs: Core 34/34, Web 14/14.

## F2 (reader API): DONE (6/6), branch feat/read-along-reader-f2

Mode: Strict TDD. Tasks 2.1-2.6 complete.

### TDD evidence
| Task | Test file | RED | GREEN |
|------|-----------|-----|-------|
| 2.1-2.3 | Web.Tests/ReaderEndpointsTests.cs (48 cases incl. theories) | After adding only the `HasReader` DTO field: 23+ tests failed (404 from missing routes) | 48/48 |
| 2.4 | Web.Tests/LibraryAndBookEndpointsTests.cs (+1) | Compile error CS1061: `HasReader` missing, then runtime failure | pass |
| 2.5/2.6 | `LibraryIds`, `LibraryBookResolver`, `ReaderStore`, `ReaderEndpoints`, DI + route registration | n/a | `dotnet test tests/ConversorLibros.Web.Tests --filter "Reader|Library|ConversionEndpoints"` → 78/78 (with `-p:BaseOutputPath=<scratch>`) |

### Deviations / notes
- Design said `GET /reader` returns 404 when there is no index; implemented per spec: 200 with `available:false` and reason `no-data` (or `unreadable` for a corrupt/unknown-schema index). 404 only for unknown id or non-Opus book.
- Corrupt chapter JSON returns 422 with an `ErrorResponse`; the chapter is validated with `JsonDocument` before serving.
- Index response adds `hasCover` (spec: "cover availability").
- Bookmark `excerpt` is accepted in POST and truncated to 160 chars server-side; blank notes are stored as null.
- Unknown schemaVersion: GET treats the file as empty (204 / empty list); PUT/POST/DELETE return 409 and never overwrite. Validation errors (400) take precedence over 409 on progress/bookmark create.
- All reads and writes of progress/bookmarks share the per-book semaphore; reads open with FileShare.ReadWrite|Delete so File.Move never collides.
- `LibraryBookResolver` only matches direct subfolders of OutputRoot that contain top-level `*.opus`; `LibraryIds.Compute` is a verbatim extraction (existing library tests green).
- Size: ~475 new production lines in src (442 new files + 33 modified), ~490 test lines. Total ~965 vs 400 budget; needs size:exception like F0/F1.
- Not run: full suite, manual curl harness against a real book.

- F2 accepted as `size:exception` by the user (2026-11-23): ~965 changed lines vs 400 budget.

### F2 verify follow-up (W1 + null bookmarks)
- `LibraryScanner.IsInside` now rejects any existing junction/symlink component below the root (not just path text). Covers the resolver, the audio endpoint and the pre-existing download endpoint (`LibraryEndpoints.cs`).
- `LibraryBookResolver` also rejects a book whose `reader/` folder is a link.
- `ReaderStore` drops `null` entries from `bookmarks.json` on load (list and mutations).
- Tests (RED then GREEN): `Audio_BookFolderIsJunctionOutsideOutputRoot_ReturnsNotFound`, `Reader_ReaderFolderIsJunctionOutsideOutputRoot_ReturnsNotFound`, `Bookmarks_FileWithNullEntry_ListsOnlyValidBookmarks`. Scoped run `Reader|LibraryLayout|LibraryAndBookEndpoints` → 55/55.
- Not covered by a dedicated test: download endpoint over a junction (same `IsInside` path), file-level symlinks inside `reader/` (need Developer Mode to create).

## F3a (static view + TOC): DONE (5/5), branch feat/read-along-reader-f3a

Mode: Strict TDD. Tasks 3a.1-3a.5 complete.

### TDD evidence
| Task | Test file | RED | GREEN |
|------|-----------|-----|-------|
| 3a.1/3a.2 | reader/__tests__/pagination.test.ts, entries.test.ts | Import resolution failure (modules missing), 2 files failed | 10/10 |
| 3a.3/3a.4 | reader/__tests__/ReaderPage.test.tsx (scrollWidth/clientWidth stubbed) | Import of `ReaderPage` failed | 8/8 (one fix: `translateX(-0px)` normalised to `0px`) |
| 3a.5 | library/__tests__/BookCard.test.tsx | 3 failed (no "Leer") | 3/3 |

Scoped run `npx vitest run src/features/reader src/features/library src/app` -> 27/27; `npx tsc --noEmit -p .` clean.

### Deviations / notes
- Tests live in `__tests__/` folders (repo convention), not next to the sources as the task text implied.
- `LibraryItemDto.hasReader` added to the frontend type (required); `LibraryPage.test.tsx` fixtures updated.
- Cover is the first column of the first chapter's content (`break-after: column`), shown only for the first indexed chapter; image error falls back to the title.
- TOC lists every index between min and max indexed chapters; gaps render disabled as "Capitulo N".
- Page controls clamp at chapter bounds (no auto-advance to the next chapter); chapter prev/next buttons exist. Indicator shows the first page of the spread.
- Fixed styling only (single light theme, 1.125rem font); appearance is F3c. `ResizeObserver` is guarded for jsdom; `document.fonts.ready` re-measure is F3c.
- Size: 784 added lines incl. tests (18 files) vs 400 budget / 600 hard stop; needs size:exception.
- Not run: full frontend suite, `npm run build`, manual browser check of real column layout.

- F3a accepted as `size:exception` by the user (2026-11-23): ~812 changed lines (incl. tests and docs) vs 400 budget.

### F3a verify follow-up (suggestions 1, 4, 5)
- Chapter loading shows `role=status` "Cargando capítulo…" and disables page prev/next until the chapter is ready (the layout still describes the previous chapter).
- `ReaderIndexDto.title` is `string | null` (mirrors `ApiDtos.cs`); `CoverPage` falls back to "Portada del libro" / "Sin título".
- `ReaderToc` collapses each run of missing indexes into one disabled entry ("Capítulo N" or "Capítulos N–M"), so the list is bounded by the indexed chapter count.
- Tests (RED then GREEN): loading status, sparse TOC (1 and 5000 → 3 entries), null title. Scoped `vitest run src/features/reader src/features/library src/app` → 30/30; `tsc --noEmit` clean.
- Deferred to F3c: suggestion 2 (bound-disabled page buttons, `aria-live` indicator) and 3 (stale-fetch race and ResizeObserver tests).

## F3b: Audio sync + follow

Mode: Strict TDD. Tasks 3b.1-3b.5 complete.

### Files
- New: `reader/sync.ts`, `useAudioSync.ts`, `useAutoFollow.ts`, `AudioControls.tsx`, `BackToAudioButton.tsx`, `ReaderToolbar.tsx`; tests `sync.test.ts`, `useAudioSync.test.tsx`, `useAutoFollow.test.tsx`, `fakeAudio.ts` (shared fake).
- Modified: `ReaderPage.tsx` (hooks, toolbar, manual-navigation suspend, click-to-seek), `PaginatedChapter.tsx` (`contentRef`, `onEntryClick`, `stride` in `onLayout`), `reader.css`, `shared/api/client.ts` (`readerAudioUrl` -> `/api/library/{id}/chapters/{n}/audio`).

### TDD evidence
| Task | RED | GREEN |
|------|-----|-------|
| 3b.1/3b.2 | `sync.test.ts` failed: `../sync` unresolved | 16/16 |
| 3b.3/3b.4 | `useAudioSync.test.tsx`, `useAutoFollow.test.tsx` failed: hook modules unresolved | 9 hook tests + 6 follow/integration tests green |
| 3b.5 | (implementation) | scoped run 61/61 (9 files), `tsc --noEmit` clean, `npm run build` OK, no build artifacts in `git status` |

### Deviations / notes
- `pageTurnFraction` is implemented as `pageAtProgress(rectPages, progress)`: it returns the page of the line reached by the progress, which is the same rule (turn when progress reaches the share of lines on the visible spread) without a snap-back when the spread changes.
- `findActiveEntry` is an alias of `entryAtMs` (already a binary search).
- Skip past the chapter end clamps at the end (no jump to the next chapter). Chapter end auto-advances and autoplays.
- Speed uses "Más lenta"/"Más rápida" buttons (0.25 step, clamped 0.75-2).
- Whole-book percent in the toolbar refreshes about once per second (`timeupdate`, whole seconds).
- Size: ~795 added/changed lines in code and tests (plus docs), over the 600 hard stop; F3b was not split because the work was already complete when measured.
- Not done: real-browser check of column rects and page turns; F3c (appearance, aria-live, bound-disabled buttons) and F4.
- F3b accepted as `size:exception` by the user (2026-11-23): ~855 changed lines (incl. tests and docs) vs 600 hard stop; split into F3b-1/F3b-2 declined.

### F3b verify follow-up (warnings 1-3, pending autoplay)
- `FakeAudio.src` now mirrors the media load algorithm (rate back to default, paused without `pause`, position 0).
- `useAudioSync` re-applies the chosen speed and resets `playing`/`positionMs` after every source change; new `cancelAutoplay()`, called by `ReaderPage.selectChapter` so a manual pick never inherits a pending autoplay (`onEnded` re-arms it after the call).
- Tests: speed/play state on source change and cancelled autoplay (RED then GREEN); rAF loop cancelled on pause and unmount, and "Volver al audio" jumping to the active sentence's page (coverage, green on first run).
- Scoped vitest 64/64 (one unreproduced failure of "loads the index and the first available chapter" in the first run, then 8 green runs); `tsc` clean; build OK.

## F3c: Appearance + re-anchor (item 1 only)

Mode: Strict TDD. Tasks 3c.1 and 3c.2 complete. Item 2 (F3a follow-ups) NOT done: the diff already exceeded the ~500-line stop (587 insertions / 16 deletions in code and tests, before docs).

### Files
- New: `reader/appearance.ts` (reducer, storage `reader.appearance.v1`, validation), `ReaderAppearanceContext.tsx`, `AppearancePanel.tsx`, `useReanchor.ts`; tests `__tests__/appearance.test.tsx`, `__tests__/reanchor.test.tsx`.
- Modified: `ReaderPage.tsx` (provider wrapper, `data-theme`, `--reader-font-size`, fonts.ready tick, `useReanchor`), `PaginatedChapter.tsx` and `usePagination.ts` (`relayoutKey`), `ReaderToolbar.tsx` (panel), `useAudioSync.ts` (`entryPages` exported), `reader.css` (theme variables, panel).

### Design notes
- `ReaderPage` now wraps its view in `ReaderAppearanceProvider`, so `App.tsx` is untouched.
- `useReanchor` records the first visible entry on every page change; when stride, ppv, count or `relayoutKey` (font step, fonts.ready) changes it moves to the page of the active entry (following during playback) or of the recorded entry. Width changes arrive through the existing `ResizeObserver` in `usePagination`. A theme change is not a relayout trigger.
- Font sizes: 0.9, 1, 1.125 (default), 1.25, 1.5, 1.75 rem. Invalid stored fields fall back to their defaults independently.

### TDD evidence
| Task | RED | GREEN |
|------|-----|-------|
| 3c.1/3c.2 | `appearance.test.tsx` suite failed (modules missing); 6 `reanchor.test.tsx` tests failed (no "Apariencia" control) | scoped run 84/84 (11 files) |

`tsc --noEmit` clean; `npm run build` OK.

### Open / not done
- Item 2 pending: page buttons disabled at the bounds, `aria-live="polite"` on the page indicator, tests for stale chapter fetch and ResizeObserver recalculation (the latter is partly covered by the resize re-anchor test).
- Real-browser check of column rects, spread switch at 1100 px and web-font reflow not performed.

## F3c verify follow-up

- `useReanchor`: a re-anchor now keeps the anchored entry (no overwrite with the spread's first entry, so repeated relayouts do not drift back); a null anchor falls back to the last non-null one within the same chapter.
- `ReaderPage`: loading and unavailable shells carry `data-theme` and `--reader-font-size`.
- `reanchor.test.tsx`: `document.fonts` stub is restored in `afterEach`; new tests for drift, null-anchor fallback and shell theme (RED first for all three).
