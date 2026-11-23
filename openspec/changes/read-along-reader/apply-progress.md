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
