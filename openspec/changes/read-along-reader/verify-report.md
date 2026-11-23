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
