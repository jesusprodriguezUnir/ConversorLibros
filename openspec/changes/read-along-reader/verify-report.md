# Verify Report: read-along-reader

## F0 (tasks 0.1-0.11) - Verdict: PASS WITH WARNINGS

Mode: Strict TDD. Tasks 11/11 checked. Scope: F0 only.

### Runtime evidence
- Core.Tests (ChunkHash|WavFile|SyncMapBuilder|PipelineSyncCallback|BookConversionPipeline): 22/22 passed, exit 0.
- Web.Tests (ReaderSidecarWriter|ConversionRunner|ConversionEndpoints): 17/17 passed, exit 0.
- Built with -p:BaseOutputPath=<scratch>/verify-bin (dev process lock avoidance). Build warnings pre-existing (NU1902, CS8631).

### Spec compliance (reader-sync-map)
| Scenario | Test | Status |
|---|---|---|
| Duration from header | WavFileTests.ReadPcmInfo_ValidMonoPcm16_ReportsHeaderFields | COMPLIANT |
| Unsupported or truncated WAV | WavFileTests ShorterThanHeader/NonPcm/NotRiff; SyncMapBuilderTests.Build_InvalidWav_ReturnsNull; PipelineSyncCallbackTests.BuilderReturnsNull_SkipsCallbackButKeepsAudio | COMPLIANT |
| Durations add up | SyncMapBuilderTests.Build_SumOfChunkDurations_EqualsAssembledChapterDuration (vs ReadDuration) | COMPLIANT |
| Cache-hit chunks | PipelineSyncCallbackTests.SecondRunFromCache_ProducesIdenticalMapWithoutSynthesis | COMPLIANT |
| Callback omitted | PipelineSyncCallbackTests.CallbackOmitted_StillConverts (Cli/App untouched per git) | COMPLIANT |
| Chunk hashes unchanged | ChunkHashCharacterizationTests (5, pinned literals) | COMPLIANT |
| Chapter subset merges | ReaderSidecarWriterTests.ChapterSubset_Merges... | COMPLIANT |
| Reconversion of existing chapter | ReaderSidecarWriterTests.ReconvertedChapter_Replaces... | COMPLIANT |
| Translated book | PipelineSyncCallbackTests.Translated_CallbackCarriesSpokenText | COMPLIANT |
| Interrupted write | ReaderSidecarWriterTests.FailureMidWrite_LeavesPreviousFileIntact | COMPLIANT |
| M4B export | ConversionRunnerReaderDataTests.PublishAsync_M4b_WritesNothing | COMPLIANT (spec says MAY) |
Task 0.10 extras: warning on missing map / writer throw / cancellation propagate: covered (Publisher tests).

### Invariants
No Core ctor change (only trailing optional param); Cli/App untouched (git diff empty); no csproj changes; writes .partial -> File.Move(overwrite); hashes pinned; cancellation rethrown in publisher; publisher called only after successful export in ConversionRunner.cs:134-138, returns null for non-Opus.

### TDD compliance
Evidence table present, all 6 test files exist and pass. No tautologies. Triangulation adequate. Apply table lacks Safety Net/Triangulate columns (format only).

### Known deviations
- Runner logic in ReaderDataPublisher: ACCEPTABLE (testable, runner is thin).
- Export-failure covered structurally only: ACCEPTABLE (WARNING, see below).
- index.json schemaVersion != 1 throws -> warning: ACCEPTABLE, matches the "never overwrite newer schema" intent; tested.

### Issues
CRITICAL: none.
WARNING:
1. ReaderSidecarWriter.cs:102,104 - spec "index.json listing (index, title, durationMs, audio kind)": IndexEntry has no audio kind. Harmless in F0 (Opus only); decide in F2 (HasReader/audio endpoint) or add field.
2. ConversionRunner.cs:132-138 - no test drives CoreConversionRunner end to end (needs Piper/ffmpeg); "no sidecars when export fails" is guaranteed only by call ordering.
3. Writer wiring when maps.Count < expected: partial index is written plus warning; acceptable but untested at runner level.
SUGGESTION:
1. SyncMapBuilder.cs:35-45 - IOException from ReadPcmInfo/FileInfo is not caught (only InvalidDataException); assembler would already fail, so low risk.
2. Task 0.6 said BookConversionPipelineTests; tests live in PipelineSyncCallbackTests.cs (note only).
3. Chunk hash tests pin invariant culture (pre-existing culture-dependent speed formatting); consider a separate fix.

---

## F1 verification (segmentation and alignment) - branch feat/read-along-reader-f1, uncommitted

Mode: Strict TDD. Tasks 1.1-1.5 all [x]. Scope: F1 only; size:exception accepted.

### Runtime evidence
- Core: ReaderTextSegmenter, ReaderTextAligner, ChunkHashCharacterization, SentenceChunker, SpanishTextNormalizer, SyncMapBuilder, PipelineSyncCallback -> 36/36 pass.
- Web: ReaderSidecarWriter, ConversionRunnerReaderData -> 14/14 pass. (Run with -p:BaseOutputPath in scratchpad.)

### Spec scenario -> test
| Scenario | Test | Status |
|---|---|---|
| Abbreviation does not split | SplitSentences_AbbreviationDoesNotSplit_AndKeepsOriginalText | COVERED |
| Displayed text is original | Align_DisplaysOriginalTextAndHonoursPronunciationDictionary | COVERED |
| Sentences inside one chunk | Align_SentencesInsideOneChunk_InterpolatesByNormalizedLength (0-250/250-1000) | COVERED |
| Sentence spanning two chunks | Align_SentenceSpanningChunks_StartsAndEndsAtItsChunkPortions | COVERED |
| Mismatch in one paragraph | Align_MismatchInOneParagraph_FallsBackOnlyForThatParagraph | COVERED |
| Roman numeral across paragraph break | Align_RomanNumeralAcrossParagraphBreak_... | COVERED |
| Empty and whitespace paragraphs | SplitParagraphs_DropsEmpty...; Align_BlankParagraphs_... | COVERED |
| Pronunciation dictionary | Align_DisplaysOriginalText...HonoursPronunciationDictionary | COVERED |
Plus 4-fixture parity theory vs real SentenceChunker; publisher test asserts paragraphs in sidecar.

### Invariants
- Chunk hashes: only SentenceSplitRegex visibility changed (private -> internal); ChunkHash tests green. No new packages, no Core constructor changes, Cli/App untouched.
- Monotonic/no gaps: entry start = previous end, first = 0, last = max(DurationMs, end); zero-length entries possible only at tail/empty-spoken paragraphs.
- Publisher: aligner call sits inside the existing try; any non-cancellation exception becomes the warning.

### Deviations
- (a) Wiring in ReaderDataPublisher: ACCEPTABLE (runner delegates there since F0). Task 1.5 text says ConversionRunner; doc drift only.
- (b) Lowercase-continuation merge: ACCEPTABLE and required by the "5 p. m. y se fue." scenario. Risk: a sentence legitimately starting lowercase (dialogue, poetry) merges; only affects sentence granularity, not verification.
- (c) Anchored matching + lookahead-8 resync: ACCEPTABLE; sound because SentenceChunker never crosses paragraph boundaries (chunks are per paragraph). Edge cases reasoned from code: empty chapter -> []; zero chunks -> all paragraph mode, no throw (no divide by zero: overlap>0 implies weight>0); single chunk ok; all mismatched -> one group to end; resync never found -> rest of chapter in paragraph mode.

### Issues
CRITICAL: none.
WARNING:
1. ReaderTextAligner.cs:178-194 - MatchesAt does two Array.IndexOf (O(C)) and FindAnchored scans all chunk starts calling it: O(C^2) per call, up to 8 calls per mismatch. A chapter with C=5000 chunks and several mismatches costs ~1e8-1e9 ops per mismatch; use a Dictionary/binary search over _starts/_ends. Analysis only, not benchmarked.
2. ReaderTextAligner.cs:96-130 - BuildParagraphs loops entries x chunks twice (O(E*C)); ~1.5e8 Overlap calls for 5k paragraphs. Acceptable now, restrict to overlapping chunk range (monotonic) to make it linear.
3. ReaderTextAligner.cs:65-78 - resync picks the nearest anchored match among 8 following paragraphs; a short repeated paragraph (e.g. "No.") can anchor at an earlier duplicate and compress the skipped group. Degrades timing only; no test for duplicated paragraphs.
4. Tests lack explicit cases for empty RawText / zero chunks, resync-never-found and "times within chunk bounds" (task 1.3 lists bounds); behavior is safe per code reading but unpinned.
SUGGESTION:
1. tasks.md 1.5 should say ReaderDataPublisher instead of ConversionRunner.
2. Segmenter merge heuristic could be documented as a known limit for lowercase-leading dialogue.

### F1 verdict: PASS WITH WARNINGS


---

## F2 verification (reader API, tasks 2.1-2.6)

Mode: Strict TDD. Tasks 2.1-2.6 all `[x]`. Scope: reader-api spec only.

### Runtime evidence
`dotnet test tests/ConversorLibros.Web.Tests --filter "ReaderEndpointsTests|LibraryAndBookEndpointsTests|LibraryScanner|ReaderSidecarWriterTests|ConversionRunnerReaderDataTests|ConversionEndpoints" -p:BaseOutputPath=<scratch>` -> exit 0, 68 passed, 0 failed, 0 skipped. Build warnings only NU1902 (pre-existing AngleSharp). Apply-progress reports 78 with the broader `Reader|Library|ConversionEndpoints` filter; the difference is the filter, not a regression.

### Spec compliance (reader-api: 5 requirements, 19 scenarios)
| Scenario | Test | Status |
|---|---|---|
| Book with reader data | Index_BookWithReaderData_ReturnsAvailableWithChapters | COVERED |
| Book without reader data | Index_BookWithoutReaderData_ReturnsUnavailableWithReason | COVERED |
| Missing chapter | Chapter_NotInIndex_ReturnsNotFound | COVERED |
| Corrupt JSON | Chapter_CorruptJson_IsControlledErrorAndOthersStayReadable; Index_CorruptIndex_ReportsUnavailable | COVERED |
| Unknown book / invalid id | UnknownOrEncodedBookId_ReturnsNotFound (6 paths), NegativeOrNonInteger..., DeleteBookmark_NonGuidId | COVERED (only `..%2F`; no `%5C`/drive-letter case, but id never reaches the filesystem, it is only compared by hash) |
| Full request | Audio_FullRequest_ReturnsOggWithAcceptRanges | COVERED |
| Range request | Audio_RangeRequest_Returns206WithExactBytes | COVERED |
| Unsatisfiable Range | Audio_RangeBeyondLength_Returns416 | COVERED |
| Path containment (symlink/junction) | none | UNTESTED |
| M4B or missing audio | Audio_M4bBook_ReturnsNotFound; Audio_MissingChapterFile_ReturnsNotFound | COVERED |
| First save and read | Progress_PutThenGet_RoundTrips (+ NoneStored 204) | COVERED |
| Invalid payload | Progress_InvalidPayload_Returns400AndKeepsStoredValue | COVERED |
| Concurrent writes | Progress_ConcurrentPuts_LeaveValidJsonWithOneOfTheValues | COVERED |
| Create and list | Bookmarks_CreateThenList_Returns201AndListsIt | COVERED |
| Delete unknown id | Bookmarks_UnknownId_DeleteAndUpdateReturn404 | COVERED |
| Note too long | Bookmarks_NoteOver500_Returns400 | COVERED |
Plus task 2.4: GetLibrary_HasReaderOnlyForOpusWithReaderIndex (passes).

### Deviations judged
- 200 `available:false` instead of design's 404: ACCEPTABLE, spec is explicit (spec wins over design).
- 422 for corrupt chapter: ACCEPTABLE (spec allows 500 problem details or 422).
- Excerpt cap 160, extra field: ACCEPTABLE, design sidecar shape lists `excerpt <=160`.
- Unknown schemaVersion reads empty / writes 409, never overwrites: ACCEPTABLE, matches design.md:54.
- 400 before 409: ACCEPTABLE. Shared semaphore for reads and writes: ACCEPTABLE.

### Invariants
LibraryIds.Compute is a verbatim move of the removed ComputeId; resolver hashes the same `EnumerateDirectories` paths that Core's `AudiobookLibrary.Scan` stores in `Path`. Core/Cli/App untouched (git status), no new packages, DI is AddSingleton like neighbours.

### Issues
CRITICAL: none.
WARNING:
1. ReaderEndpoints.cs:68 / LibraryBookResolver.cs:26 - `LibraryScanner.IsInside` is lexical (`Path.GetFullPath`); it does not resolve junctions/symlinks. A `.opus` symlink or a book folder junction pointing outside OutputRoot is served, contradicting the spec scenario "Path containment (after symlink/.. normalization)". No test covers it. Fix: resolve `FileInfo.ResolveLinkTarget(true)` (or reject entries with ReparsePoint attribute) and add a junction test.
2. ReaderStore.cs:22,205 - `_locks` grows by one SemaphoreSlim per book dir and is never pruned. Bounded by library size; negligible in practice.
SUGGESTION:
1. LibraryBookResolver.cs:19 - every audio Range request enumerates and hashes all book folders (O(books)); cache id->path with invalidation if libraries grow large.
2. ReaderStore.cs:86,114 - a bookmarks.json with a `null` list entry passes deserialization and NREs in `b.Id` -> 500 (not leaked detail, but uncontrolled). Filter nulls on load.
3. No test for excerpt truncation (160), blank note -> null, or `%5C`/absolute-path ids; unit-cover Truncate/NullIfBlank.
4. tasks.md 2.5 says LibraryIds is extracted "from LibraryScanner"; it came from LibraryEndpoints (doc drift).

Security notes: route constraint `:int:min(0)` rejects negative/non-int/overflow (404); leading zeros parse as ints; bookmark ids are `:guid`; ids are never used as paths. Range multi/suffix handled by ASP.NET RangeHelper. Error bodies are fixed Spanish messages, no exception text. Atomic write: `.partial` FileMode.Create + File.Move overwrite; leftover .partial is overwritten by the next write; readers use FileShare.ReadWrite|Delete.

### F2 verdict: PASS WITH WARNINGS

## F3a verification (static view + TOC, tasks 3a.1-3a.5) - branch feat/read-along-reader-f3a, uncommitted

Mode: Strict TDD. Tasks 3a.1-3a.5 all `[x]`. Scope: reader-ui F3a scenarios only (audio sync, appearance, resize-keeps-position, resume and bookmarks belong to F3b/F3c/F4).

### Runtime evidence (from src/ConversorLibros.Web/ClientApp)
- `npx vitest run src/features/reader src/features/library src/app` -> 6 files, 27 tests passed, 0 failed (React act() warnings on stderr only).
- `npx tsc --noEmit -p .` -> exit clean, no output.
- `npm run build` -> OK (1670 modules, built in 13.6s). `git status` afterwards: unchanged; wwwroot is git-ignored, no tracked file dirtied.

### Spec compliance (8 F3a scenarios)
| Scenario | Test | Status |
|---|---|---|
| Open chapter from TOC | ReaderPage "opens a chapter from the TOC at page 1..." (asserts chapter 3 text, page 1 de 3, translateX(0) after being on page 2). Audio part is F3b | COVERED |
| Chapter without data | same test: "Capítulo 2" button disabled | COVERED |
| Bounds | "clamps page navigation at the chapter bounds" (prev at page 1, next x3 stays at 3 de 3). Last page of the LAST chapter is not distinguished, but next-chapter is disabled there by `nextChapter === undefined` | COVERED (page level) |
| Missing cover | "omits the cover page when the book has no cover" (no img) | COVERED |
| Direct URL without data | it.each no-data + 404: hint text and link href=/biblioteca | COVERED |
| Spread threshold | pagination.test.ts pagesPerView 1100 -> 2, 1099 -> 1; columnMetrics 1101/2. No component-level test of two-page rendering (F3c owns spread styling) | COVERED (unit) |
| Reader available | BookCard.test "enables Leer and links to the reader" (href /biblioteca/b1/leer) | COVERED |
| Reader unavailable | BookCard.test it.each hasReader=false and M4B: disabled button + hint, no link | COVERED |
Plus: chapter 422 shows role=alert and the TOC stays usable; cover first-page test asserts alt text. 8/8 covered.

### Tasks vs implementation / design
3a.1-3a.5 match the code: pagination.ts, entries.ts, ReaderPage, PaginatedChapter, CoverPage, ReaderToc, PageIndicator, usePagination, reader.css, types/client, route `/biblioteca/:id/leer`, BookCard "Leer". No new dependencies (package.json untouched). Cover is the first column of chapter content with `break-after: column` (reader.css:62) and only for the first indexed chapter.

### API contract check
- Index DTO (available, reason, title, hasCover, chapters{index,title,durationMs,audioKind}) matches ApiDtos.cs ReaderIndexResponse/ReaderIndexChapter. `title` is null when unavailable but typed `string` (types.ts); harmless since the unavailable branch never reads it.
- Chapter DTO fields match Core ReaderChapter (chapterIndex, title, durationMs, chunks, paragraphs{mode,sentences{text,startMs,endMs}}); `chunks` omits `normalizedText` (subset, OK). schemaVersion is a frontend-only field carried from the sidecar JSON.
- Endpoints match: `/api/library/{id}/reader`, `/reader/chapters/{n}`, cover `/api/library/{id}/cover` (LibraryEndpoints.cs:26). `LibraryItemDto.hasReader` matches `HasReader` (ApiDtos.cs:51).

### Correctness review
- Stale fetch on chapter switch: both effects use a `cancelled` flag with cleanup, so late responses are dropped. Sound. Not covered by a test.
- ResizeObserver: created in useLayoutEffect with disconnect cleanup, guarded for jsdom (`typeof ResizeObserver`). Not exercised by tests (jsdom lacks it), so the width-change path is only verified by the pure functions.
- Pagination math: `pageCount = ceil((scrollWidth+gap)/stride - 1e-6)` consistent with the tests (3096 -> 3). `clampPage` and `spreadStart` compose correctly; `shown` is re-clamped on render so a shrinking count cannot leave an out-of-range page.
- Effects cleanup, Spanish strings, a11y: TOC is a `nav` with aria-label and aria-current; icon-less text buttons; cover img has alt. All user strings Spanish.

### Issues
CRITICAL: none.
WARNING: none blocking.
SUGGESTION:
1. ReaderPage.tsx:88-96 - while a chapter loads (`status: "loading"`) `<main>` is empty with no indicator, and the page/chapter footer buttons stay active with the previous layout count; add a "Cargando capítulo…" placeholder and disable page buttons until ready.
2. ReaderPage.tsx:102-108 - "Página anterior/siguiente" are never disabled at bounds (spec only requires clamping, so compliant); disabling them or adding `aria-live="polite"` to PageIndicator.tsx would help screen readers.
3. ReaderPage.tsx:45-56 / usePagination.ts - no test for the stale-response race or for ResizeObserver-driven recompute; add a fake ResizeObserver test in F3c when resize persistence lands.
4. types.ts ReaderIndexDto - `title` should be `string | null` to mirror ApiDtos.cs.
5. ReaderToc.tsx:14 - fills every index between min and max; a pathological sparse index (e.g. 1 and 5000) renders thousands of disabled buttons; cap or collapse gaps.

### F3a verdict: PASS

## F3b verification (audio sync + follow, tasks 3b.1-3b.5) - branch feat/read-along-reader-f3b, uncommitted

Mode: Strict TDD. Tasks 3b.1-3b.5 all `[x]`. Scope respected: no appearance, spread toggles, aria-live, bound-disabled page buttons (F3c) and no progress PUT or bookmarks (F4). No new dependencies.

### Runtime evidence (from src/ConversorLibros.Web/ClientApp)
- `npx vitest run src/features/reader src/features/library src/app` -> 9 files, 61 tests passed, 0 failed (act() warnings on stderr only).
- `npx tsc --noEmit -p .` -> clean, no output.
- `npm run build` -> OK (1676 modules, built in 1.7s). `git status --short` afterwards: identical to before the build; wwwroot is git-ignored.

### Spec compliance (11 F3b scenarios)
| Scenario | Test | Status |
|---|---|---|
| Highlight follows audio | useAudioSync.test.tsx:88-100 (entry 1 at 1.2 s, back to 0 at 0.5 s, exactly one `.reader-active`); sync.test.ts:14-27 | COVERED |
| Paragraph fallback | useAudioSync.test.tsx:102-110 (single whole-range entry); entries.test.ts covers paragraph-mode flattening | COVERED |
| Sentence spanning two pages | useAudioSync.test.tsx:112-129 (lines on pages 3,3,4,4: turns to 3, stays at 33 %, turns to 4 at 53 %); sync.test.ts:29-42. Highlight on all fragments holds because one span carries the class | COVERED |
| Chapter ends | useAudioSync.test.tsx:143-160 (next src autoplays; no next: no play). ReaderPage `onEnded` -> `selectChapter` wiring is not exercised | COVERED (hook level) |
| Click to seek | useAudioSync.test.tsx:162-168 (paused, currentTime 2.5, entry 2 active); useAutoFollow.test.tsx:115-124 (UI click, 1.5 s, highlighted) | COVERED |
| Suspend | useAutoFollow.test.tsx:86-97 (button appears, audio not paused); no auto-turn while suspended: useAudioSync.test.tsx:131-141 | COVERED |
| Resume follow | useAutoFollow.test.tsx:106-113 asserts only that the button disappears; the jump to the active entry's page (`syncNow`) is not asserted at page level (jsdom has no rects). Tick page-turn logic itself is covered at useAudioSync.test.tsx:112-129 | PARTIAL |
| Click-to-seek resumes follow | useAutoFollow.test.tsx:115-124 | COVERED |
| Skip bounds | sync.test.ts:44-48; useAudioSync.test.tsx:186-192 (5 s, -15 -> 0; +15 clamps at 4 s duration); useAutoFollow.test.tsx:126-132 | COVERED |
| Speed range | sync.test.ts:50-56; useAudioSync.test.tsx:194-197 (clamps to 2 and 0.75); useAutoFollow.test.tsx:134-135 (1.25) | COVERED |
| Audio load error | useAudioSync.test.tsx:170-184 (error event and rejected play(), text kept); useAutoFollow.test.tsx:137-139 (role=alert, text still rendered). A real 404 is simulated by the `error` event | COVERED |

Coverage: 10/11 fully, 11/11 including the partial.

### Backend contract
`api.readerAudioUrl` -> `/api/library/{id}/chapters/{n}/audio` matches ReaderEndpoints.cs:58, which serves with `enableRangeProcessing: true` (line 73). Seeking therefore works against the real endpoint.

### Declared deviations judged
- `pageTurnFraction` as `pageAtProgress(rectPages, progress)`: ACCEPTED. Same behaviour as the spec (page of first rect, turn when progress reaches the share of lines on the next page); tested.
- `findActiveEntry = entryAtMs`: ACCEPTED, binary search, already covered.
- Skip +15 s clamps at the end and the end auto-advances: ACCEPTED, spec allows clamp or next chapter.
- Speed buttons, step 0.25, 0.75-2: ACCEPTED.
- Book percent refreshed ~1 s (timeupdate floored to seconds): ACCEPTED.

### Correctness review
- rAF loop starts on `playing` and is cancelled by the effect cleanup (pause, unmount, dependency change). Listeners are removed in cleanup and the audio is paused on unmount. Highlight uses `classList.toggle` on `[data-entry]`; there is no per-entry React state. `followingRef` is updated synchronously, so click-to-seek followed by `tick` sees follow=true. Follow suspends only in `manualGo` while playing; `selectChapter` resumes it. Spec-compliant.
- A new chapter swaps `audio.src` and resets `activeRef`; the old DOM is replaced, so no stale highlight stays. Chapter fetch has its own `cancelled` guard.

### Issues
CRITICAL: none.

WARNING:
1. useAudioSync.ts:72-81 - changing `audio.src` runs the HTML media load algorithm, which (a) resets `playbackRate` to `defaultPlaybackRate` while the `speed` state stays (the effect at :119 does not re-run), so a chosen speed is lost on every chapter change including auto-advance; and (b) sets `paused = true` without a `pause` event, so after a manual chapter change while playing `playing` stays true (label "Pausa", rAF keeps running) with no sound. FakeAudio does not model either, so no test catches it. Fix: in the src effect re-apply `audio.playbackRate = speed`, and `setPlaying(false)` plus reset `positionMs` when not autoplaying.
2. useAutoFollow.test.tsx:106-113 - "Resume follow" does not assert the page jump to the active entry (spec THEN clause); add a test with mocked client rects at ReaderPage level or a `syncNow` assertion in the hook test.
3. useAudioSync.test.tsx:77 - `cancelAnimationFrame` is a no-op stub and nothing asserts that the loop stops after pause/unmount; add a test that no frame callbacks remain pending after `pause`.

SUGGESTION:
1. useAudioSync.ts:61 - `entryPages` runs `querySelector` + `getClientRects` (forced layout) every frame while following; cache per active index until layout/stride changes.
2. useAudioSync.ts:77 / ReaderPage.tsx:63-67 - `autoplayRef` stays true if the next chapter fails to load (src stays null), so a later manual chapter pick autoplays unexpectedly; clear it on error or on manual selection.
3. ReaderPage.tsx:54 - for one render after `selectChapter` the new `src` is paired with the old chapter's entries (`chapterState` is set to loading in an effect); derive `src` from the chapter whose `chapterIndex === current`.
4. useAudioSync.ts:102-105 - on unmount the element keeps its `src` (possible buffering); set `audio.removeAttribute("src")` / `load()`.
5. ReaderPage `onEnded` wiring (next chapter selection + autoplay) has no page-level test.

### F3b verdict: PASS WITH WARNINGS


## F3c - appearance (themes, font size) and re-anchoring

Scope: tasks 3c.1, 3c.2 (working tree vs HEAD, branch `feat/read-along-reader-f3c`). Deferred F3a follow-ups excluded. Size (~613 insertions, above the 500-line target) is a known, accepted decision.

### Results
- `npx vitest run src/features/reader src/features/library src/app`: 11 files, 84 tests, all pass.
- `npx tsc --noEmit -p .`: clean (no output).

### Spec coverage
- Resize keeps position: reanchor.test.tsx:146 (ResizeObserver callback, width 1200, spread, page by content). Covered.
- Font size change: reanchor.test.tsx:108 (first visible entry), :119 (active entry while following), :132 (suspended follow uses first visible). Covered, behavior-first.
- Change theme during playback: reanchor.test.tsx:173 (data-theme dark, no pause, one play, same page). Covered. Colours themselves are not assertable in jsdom; acceptable.
- fonts.ready re-anchor: reanchor.test.tsx:157. Persistence/validation: appearance.test.tsx (14 tests). Panel bounds: appearance.test.tsx:127.

### Correctness review
- No effect loop: `onPage` changes `page`, which re-runs the layout effect with an unchanged signature, so it only re-records the anchor. A stale entry never crosses chapters (`anchor.chapter === chapterKey` guard; a new chapter first records, then the next geometry change re-anchors to its own entry 0).
- fonts.ready: `cancelled` guard on unmount; ResizeObserver disconnect lives in usePagination.ts:32. Both clean.
- Theme CSS: all reader surfaces switched to variables; sepia and dark contrast plausible (fg/bg roughly 7:1+ and 12:1+); `color-scheme: dark` covers native controls.

### Findings
CRITICAL: none.

WARNING:
1. ReaderPage.tsx:136,139 - loading and error shells render `.reader-shell` without `data-theme`/font style, so they use the light defaults. A dark-theme user gets a light flash while loading and a light error screen. Pass `data-theme={theme}` there too.
2. useReanchor.ts:57 - anchor drift: after a re-anchor to page N (e.g. an odd page in a spread), the effect re-records `firstVisibleEntry` of the whole spread, which can be an earlier entry than the one just anchored. Each later relayout (font steps, resize across 1100 px) can move the position backward. Keep the anchor entry when the signature just changed and only refresh it on user-driven page changes.
3. useReanchor.ts:50-56 - when the stored entry is null (spread without an entry start, e.g. one long entry or the cover) or `entryPages` returns nothing, the function returns without moving and without updating the anchor, so position is lost silently on relayout. Fall back to the last non-null anchor.
4. reanchor.test.tsx:157-170 - `document.fonts` is deleted at the end of the test body, not in `afterEach`/finally, so a failing assertion leaks the stub into later tests.

SUGGESTION:
1. No test for chapter change followed by relayout (stale-anchor guard) or for unmounting before `fonts.ready` resolves; no test asserts `--reader-font-size` is applied to the shell (ReaderPage.tsx:163).
2. AppearancePanel.tsx:13,17 - no Escape/outside-click close, no `aria-controls`, and no visible/announced current font size; focus is lost when a font button becomes disabled at a bound (:27,:30). The panel is `position:absolute; bottom:100%` (reader.css) and can clip on narrow toolbars.
3. ReaderAppearanceContext.tsx:16 - writes defaults to localStorage on mount and does not sync across tabs (no `storage` listener); harmless.
4. reader.css - links/buttons in the toolbar and TOC inherit UA colours rather than the theme variables; fine with `color-scheme: dark` but not explicitly themed.

### F3c verdict: PASS WITH WARNINGS


## F3d - a11y polish and coverage (tasks 3d.1, 3d.2) - branch feat/read-along-reader-f3d, uncommitted

Runs (ClientApp): `vitest run src/features/reader src/features/library src/app` - 11 files, 97 tests passed. `tsc --noEmit -p .` - clean.

Per item:
1. Page buttons disabled at chapter bounds (ReaderPage.tsx:213,218-221): `start === 0` / `start + ppv >= count`, spread-aware (uses spreadStart). Test (ReaderPage.test.tsx "disables the page buttons at the chapter bounds") exercises real clicks and fails if the conditions break. Chapter crossing via chapter buttons is the accepted deviation. OK.
2. `aria-live="polite"` on indicator (PageIndicator.tsx:5). Test asserts the attribute on the rendered text. OK.
3. Stale chapter fetch (coverage only): test holds chapter 1 pending, selects 3, resolves 1 late, asserts 3 stays. Genuinely exercises the guard. OK.
4. ResizeObserver (coverage only): test invokes captured RO callbacks after changing client/scrollWidth, expects count 3 -> 4. Fails if the observer is not wired. Prototype overrides are cleaned in afterEach. OK.
5. useReanchor spanning logic (useReanchor.ts:20-35): correct. Entries are iterated in DOM order; last entry with start < spread first is kept as `spanning`; first entry starting at/after `first` returns itself if inside the spread, else the spanning one; none-started returns null (cover -> previous anchor fallback preserved); no start after the loop returns spanning. Double-page spreads use `first + ppv`, no off-by-one found. Test (reanchor.test.tsx "anchors to the sentence spanning...") uses a multi-page entry and verifies the result differs from the fallback (entry 0 would give page 1, expected page 7). Chapter-change case is not covered by a dedicated test, but anchor state is per-chapter in existing tests.
6. AppearancePanel a11y: Escape/focus return, aria-expanded/aria-controls (useId), focus hand-off at bounds, `<output aria-live>` - each has a behavior test using real user events (Escape is pressed with focus inside the panel). Hand-off logic (AppearancePanel.tsx:22-26) is correct for both bounds.

Findings
- CRITICAL: none.
- WARNING: none.
- SUGGESTION: ReaderPage.tsx:213,218 - disabling the focused page button at a bound drops keyboard focus (known/accepted note). Could mirror the font-button hand-off.
- SUGGESTION: AppearancePanel.tsx:19-21 - Escape is handled on the wrapper `onKeyDown`, so it only fires with focus inside the panel/toggle (no global listener, no cleanup issue, does not hijack Escape elsewhere). Consequence: with the panel open and focus elsewhere, Escape does not close it; no outside-click close either.
- SUGGESTION: AppearancePanel.tsx:23-25 - the hand-off condition `next <= 0 || next >= MAX_STEP` includes a dead branch per direction; harmless.
- SUGGESTION: reanchor test covers a single spanning scenario; no test for double-page (ppv=2) spanning or chapter change.

No regressions of F3a/F3b/F3c observed (full reader suite green).

### F3d verdict: PASS


## F4a - resume position (tasks 4.1, 4.2) - branch feat/read-along-reader-f4a, uncommitted

Runs (ClientApp): `vitest run src/features/reader src/features/library src/app` - 13 files, 115 tests passed. `tsc --noEmit -p .` - clean (exit 0).

Contract check: client DTOs match the server (GET 204 -> null, 200 -> {chapterIndex, ms, updatedAt}; PUT body {chapterIndex, ms}; server rejects ms < 0 or unknown chapter with 4xx, client never sends either: ms is Math.round(currentTime*1000) >= 0, chapter comes from the index). Keepalive body is ~30 bytes, far below the 64 KB limit.

Mechanics verified
- Effect order: useAudioSync (src effect, sets audio.src) is declared before useReaderProgress in ReaderPage.tsx:68-94, so in the commit where `ready` flips the src effect runs first and the resume seek (useReaderProgress.ts:62-67) runs after it; the seek is not wiped. Test "reopens at the saved chapter and ms" asserts currentTime 1.5 with the real hook order.
- Listener/interval cleanup: pause, visibilitychange, pagehide and the 10 s interval are all removed in effect cleanups; `saveNow` is stable (deps audio, id) so no duplicate listeners across re-renders; `currentRef` avoids stale chapter closures.
- Guard cannot get stuck on failed GET (falls back to first chapter, pending null) or missing saved chapter (first chapter, 0 ms). Late loadResume vs user pick is impossible: the TOC renders only after setIndexState, which happens after loadResume; `cancelled` is rechecked after the await.
- Chapter change: initial null -> start does not save; later changes save {chapter, 0} (useReaderProgress.ts:68-73).

Findings
- CRITICAL: none.
- WARNING: useReaderProgress.ts:62-67 / :40 - the pending resume ms is not tied to the resume chapter. If the resumed chapter fails to load (chapterState "error") or the user picks another chapter while it is still loading, `ready` becomes true for the other chapter and the saved ms is applied there (seek to the wrong chapter's offset, clamped by its duration). While the chapter is in error, pending stays non-null and all saves except the chapter-change one are suppressed. Fix: store {chapter, ms} and clear pending when `current` differs from it. No test covers this.
- WARNING: useReaderProgress.ts:46-52 - `saveNow` is gated on pending but not on `ready`. After a chapter change, `current` is the new chapter while audio still holds the previous chapter's position (useAudioSync.ts:~70 returns early when src is null and does not pause or reset). A pause/interval/visibility save in that window writes {newChapter, previousChapter's ms}. Window is the chapter fetch (short; also after `ended` auto-advance). Fix: also require `ready`.
- WARNING: spec "on leaving the reader" is not implemented. There is no unmount/route-leave save; the hook only saves on pause, 10 s, chapter change, hidden/pagehide. useAudioSync's cleanup pauses the audio on unmount but the async `pause` event arrives after the progress listener is removed, so up to 10 s of progress is lost when navigating back to the library with audio playing. SPA navigation fires neither visibilitychange nor pagehide.
- WARNING: spec "the page containing that entry is shown" has no test. Resume relies on seekTo -> tick() (useAudioSync.ts seekTo) paging only when the entry is active and following; tick uses contentRef/entries at effect time and `followingRef`. ReaderPageResume asserts text, currentTime and src but not the visible page or the active entry.
- SUGGESTION: Real-browser risk (MEDIUM-LOW, accepted): currentTime is assigned right after audio.src, before metadata. Per the HTML spec this sets the default playback start position and is applied after load in Chrome/Firefox/Safari; Safari historically needs `loadedmetadata`. Consider re-applying on `loadedmetadata` if the diff to the target exceeds a tolerance. jsdom cannot prove this.
- SUGGESTION: Not covered by tests: unmount cleanup of listeners/interval, failed PUT retry across triggers is covered only indirectly ("keeps working when saving fails"), error-state resume chapter, "start of next chapter" alternative (spec allows clamp, so fine).
- SUGGESTION: useReaderProgress.ts:75 / client.ts - a rejected keepalive fetch on hide is swallowed and retried at the next trigger; fine, but there is no sendBeacon fallback.

No regressions of F3a-F3d observed (reader/library/app suites green).

### F4a verdict: PASS WITH WARNINGS

## F4b (tasks 4.3, 4.4, 4.5: bookmarks UI and CLAUDE.md)

Runs: `npx vitest run src/features/reader src/features/library src/app` -> 15 files / 132 tests green on 4 of 5 runs; `npx tsc --noEmit -p .` clean. The very first (cold) run had 1 failure in a reader page test (`expect(getByText(/página 1 de 3/i))`, around line 74 of its test file) that did not reproduce in 4 reruns (flaky under load, pre-existing area, not bookmark code).

Verified OK
- Client matches backend: GET/POST `/reader/bookmarks` (201 + body), DELETE `/reader/bookmarks/{guid}` (204); request DTO fields chapterIndex/ms/excerpt/note match `CreateBookmarkRequest`. PUT (note edit) is not used by the UI (not required by 4.3-4.5). 400/404/409 all go through `parseJson` errors and surface as the generic Spanish error message.
- Pending-seek design: `pendingRef` is tied to `{chapter, ms}`, applied once when `ready && pending.chapter === current`, clamped to `durationMs`, dropped when `current` becomes another chapter (useBookmarks.ts:116-119). Applying it goes through `resumeSeek` so `resumeSyncRef` re-paging works; `selectChapter` resets that flag first, so no stale re-page flag. It cannot leak into `useReaderProgress`: its pending is dropped on chapter change (useReaderProgress.ts:68-70), and when both are pending in the same chapter the bookmark effect runs last and wins. Progress saves stay gated on `ready`, and the chapter change saves `{chapter, 0}`, so no wrong-offset save.
- Scenarios covered by behavior tests: add (excerpt <=160), jump same chapter, cross-chapter jump with ms clamp, reload list, removed chapter (disabled but deletable), corrupt list -> empty. Panel a11y: aria-expanded/controls, Escape returns focus, focus returns to toggle on jump.
- CLAUDE.md entry matches ReaderEndpoints routes, status codes (422 corrupt chapter, 204 no progress, 409 newer schema), 160 cap and hook/component names; style consistent with neighbors.

Findings
- CRITICAL: none.
- WARNING: useBookmarks.ts:57 / :39 - spec requires bookmarks "ordered by chapter then ms". The server returns file order (ReaderStore.cs:211-212, no sort) and the client appends new ones at the end, so the list is in creation order. No test asserts ordering. Fix: sort in the hook (chapterIndex, ms) on load and on add.
- WARNING: useBookmarks.ts:46-61 - no in-flight guard on add: a double click sends two POSTs and creates two identical bookmarks. Fix: busy flag/ref and disable the button meanwhile. Untested.
- WARNING: BookmarksPanel.tsx:72-74 - after deleting the item that holds focus, the button unmounts and focus falls to `body` (a11y requirement: focus after deleting). Move focus to the next item, the previous one or the add button. Untested.
- WARNING: useBookmarks.ts:55 - `text.slice(0, 160)` is UTF-16 based and can cut a surrogate pair, sending a lone surrogate. The server also truncates by UTF-16 (ReaderStore.cs:216), so a lone surrogate may reach `System.Text.Json` and fail the POST (generic "No se pudo guardar"). Fix: cut by code points (`Array.from(text).slice(0,160).join("")` would still exceed 160 UTF-16 units for astral chars, so truncate by units but back off one if the last unit is a high surrogate). Rare (emoji at position 160), no test.
- WARNING: tests missing for: delete failure message, add failure message at hook level, manual chapter change dropping a pending jump, stale response after book id change, add while chapter loading. The pending-drop logic (useBookmarks.ts:116-119) is the riskiest piece and has no direct test.
- SUGGESTION: useBookmarks.ts:52-57 and :67-68 - add/remove responses are not guarded by a cancelled/id check, so a response arriving after the book id changed could append to the new book's list (only reachable on route param change with the same mounted component).
- SUGGESTION: ReaderPage.tsx:219/:119 - `canAdd` uses `chapterState.status === "ready"` while the hook uses `chapter.chapterIndex === current`; for one render after a chapter change `canAdd` can be true while `audio.currentTime` still belongs to the previous chapter. Use the same `ready` flag for both.
- SUGGESTION: BookmarksPanel.tsx:69-70 - the button accessible name concatenates label and excerpt without a separator; consider a visually-hidden separator or `aria-describedby` for the excerpt.

### F4b verdict: PASS WITH WARNINGS
