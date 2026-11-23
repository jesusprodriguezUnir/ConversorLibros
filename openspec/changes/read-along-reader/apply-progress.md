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
