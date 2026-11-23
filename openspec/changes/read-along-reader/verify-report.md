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
