# Design: Read-Along Reader

## Technical Approach

The pipeline gains one optional trailing parameter, `Action<ChapterSyncMap>? onChapterSynced = null`, on `BookConversionPipeline.ConvertAsync`. Constructors and the Cli/App roots stay untouched. After each chapter is assembled, the pipeline builds exact chunk timings from the chunk WAV headers. `CoreConversionRunner` collects the maps in memory. Only after `OpusFolderExporter` succeeds does it align the raw text (segment, normalize each segment, verify) and write `<BookDir>/reader/*.json` atomically. A new `ReaderEndpoints` group serves the data, Opus audio with Range, progress and bookmarks, resolving books through a lightweight id-to-folder lookup. The React reader lives in `features/reader/`, a full-screen route that uses CSS multi-column pagination and a rAF-driven sync hook. It adds no new dependencies.

## Architecture Decisions

| Topic | Choice | Rejected | Rationale |
|---|---|---|---|
| Hook point | Optional trailing callback on `ConvertAsync` | New ctor dependency; `IProgress` payload | Source-compatible. Only `ConversionRunner` opts in. |
| When sidecars are written | After a successful **Opus** export, from the maps held in memory | Writing inside the callback during the pipeline | A failed or missing ffmpeg run would otherwise leave new timings next to stale `.opus` files (for example, ones made at a different speed). The maps are only text and timings, so holding them doesn't buffer audio. |
| Chunk duration | `fileLength - 44` per chunk ÷ `fmt` byteRate, cumulative bytes → ms | `ReadDuration` per chunk, then summing rounded ms | Mirrors `WavChapterAssembler`, which strips exactly 44 bytes per chunk. Cumulative rounding means no drift. |
| Failure policy | `SyncMapBuilder` returns `null` on a byteRate mismatch, and the pipeline then skips the callback for that chapter. The runner catches every non-cancellation exception from align/write and returns `ConversionRunResult(outputPath, Warning: "Audiolibro creado, pero no se pudieron generar los datos de lectura.")`. `OperationCanceledException` always propagates. | Throwing inside `ConvertAsync` | Reader data is auxiliary and must never break an expensive conversion. |
| Map text source | `RawText`/`Title` come from the post-translation `chapter` variable, not `sourceChapter` | — | The reader shows what is spoken. |
| Alignment ground truth | Actual `chunk.NormalizedText`, compared with whitespace runs collapsed | A re-derived canonical string | `SentenceChunker` must not change. Chunks are the authority. |
| Sentence timing | Char offset in the paragraph's chunk-joined string, interpolated linearly inside each chunk | Equal split per sentence | Exact at chunk edges. Handles sentences that span several chunks (`SplitLongSentence`). |
| Fallback | A paragraph that fails verification becomes one entry holding the whole paragraph text (`mode:"paragraph"`) | Showing the normalized text | The UI renders both cases the same way, so whole-paragraph highlight needs no extra code. |
| Book lookup | `LibraryBookResolver`: enumerates root folders, `LibraryIds.Compute(folder)` | `FindById` (full `Scan`, reads Opus tails) | Audio Range requests and progress PUTs are frequent. Ids stay identical because both take the same `EnumerateDirectories` path string. |
| Pagination | CSS columns, integer px column width, explicit content width, re-anchor by entry | epub.js; JS fit; page-number anchoring | No dependency. Integer strides avoid subpixel drift. |
| Highlight | `classList` toggle on `[data-entry]` | React state per entry | Keeps thousands of spans from re-rendering. |
| State | `ReaderSessionContext` (useReducer) + `ReaderAppearanceContext` (localStorage `reader.appearance.v1`) | New state library | Matches the existing `ConversionContext` pattern. |
| Reader route | `/biblioteca/:id/leer` as a sibling of the `AppShell` route, inside the providers | Nested in `AppShell` | Immersive layout. The SPA fallback in `Program.cs` already serves deep links. |

## Data Flow

    ConvertAsync ─chunks+paths─→ SyncMapBuilder ─ChapterSyncMap─→ callback (runner list)
    runner: export Opus OK? ─→ ReaderTextAligner(map, new SpanishTextNormalizer(), null)
         ─→ ReaderSidecarWriter → <BookDir>/reader/{index,chapter_NNN}.json (.partial → move)
    UI ─→ ReaderEndpoints ─→ LibraryBookResolver ─→ files (IsInside check) / ReaderStore (lock per book)

## Interfaces / Contracts

```csharp
// Core/Models/ReaderModels.cs
record WavPcmInfo(int SampleRate, int Channels, int BitsPerSample, int ByteRate, long DataOffset, long DataBytes);
record ChunkTiming(int Order, string NormalizedText, long StartMs, long EndMs);
record ChapterSyncMap(int ChapterIndex, string Title, string RawText, long DurationMs, IReadOnlyList<ChunkTiming> Chunks);
record ReaderSentence(string Text, long StartMs, long EndMs);
record ReaderParagraph(string Mode /*sentence|paragraph*/, IReadOnlyList<ReaderSentence> Sentences);
record ReaderChapter(int ChapterIndex, string Title, long DurationMs, IReadOnlyList<ChunkTiming> Chunks, IReadOnlyList<ReaderParagraph> Paragraphs);
// Core/Services
WavFile.ReadPcmInfo(string path);                       // walks RIFF chunks like ReadDuration
ChapterSyncMap? SyncMapBuilder.Build(Chapter, IReadOnlyList<TextChunk>, IReadOnlyList<string> chunkPaths); // null if byteRate differs; data = fileLength - 44 (as the assembler)
ReaderTextSegmenter.SplitParagraphs(raw); .SplitSentences(rawParagraph, Func<string,string> normalize);
ReaderTextAligner.Align(ChapterSyncMap, ITextNormalizer, IReadOnlyDictionary<string,string>?);
```

**Segmentation:** split raw text on `\n\n` (trim, drop empty pieces). Split each paragraph with a copy of the chunker regex `(?<=[.!?…])\s+`. Merge forward while the normalized accumulation does not end in `[.!?…]`, which covers abbreviations such as `Sr.` → `Señor`. Verification rule: the collapsed join of the normalized sentences must equal the collapsed join of the chunks consumed for that paragraph. Chunks are consumed until the collapsed length reaches the normalized paragraph's length. Results:
- Mismatch: the paragraph uses `mode:"paragraph"`.
- Leftover chunks: they extend the last paragraph.
- Empty normalized paragraph: the entry is untimed (`startMs = endMs =` previous end).

**Sidecars** (`<BookDir>/reader/`, camelCase, `schemaVersion: 1`, unknown version is treated as absent and never overwritten. PUT returns 409):
```json
// index.json — merged by chapter index (handles chapter subsets)
{"schemaVersion":1,"title":"…","chapters":[{"index":3,"title":"…","durationMs":812345}]}
// chapter_003.json
{"schemaVersion":1,"chapterIndex":3,"title":"…","durationMs":812345,
 "chunks":[{"order":0,"startMs":0,"endMs":4210}],
 "paragraphs":[{"mode":"sentence","sentences":[{"text":"…","startMs":0,"endMs":1830}]}]}
// progress.json — one per book, last write wins
{"schemaVersion":1,"chapterIndex":3,"ms":120500,"updatedAt":"2026-11-23T10:00:00Z"}
// bookmarks.json
{"schemaVersion":1,"bookmarks":[{"id":"<guid>","chapterIndex":3,"ms":120500,"excerpt":"…(≤160)","note":null,"createdAt":"…"}]}
```
Reconversion rewrites only `index.json` and the affected `chapter_NNN.json` files. Progress and bookmarks are never touched.

**Endpoints** (`Web/Endpoints/ReaderEndpoints.cs`, group `/api/library/{id}`):

| Method | Route | Notes |
|---|---|---|
| GET | `/reader` | index. 404 if the book is not Opus or has no index |
| GET | `/reader/chapters/{n:int:min(0)}` | `Results.File(json)` |
| GET | `/chapters/{n:int:min(0)}/audio` | `ChapterPrefix(n)*.opus`, `audio/ogg`, `enableRangeProcessing:true` |
| GET/PUT | `/reader/progress` | PUT validates that the chapter exists in the index and that `ms >= 0` |
| GET/POST | `/reader/bookmarks` | POST returns 201 with the id |
| PUT/DELETE | `/reader/bookmarks/{bookmarkId:guid}` | note ≤ 500 chars. 404 if unknown |

**Resolved spec gaps** (orchestrator decision):
- `GET /reader/progress` with no stored progress → `204 No Content`; the UI starts at chapter 0, ms 0.
- Malformed `progress.json` / `bookmarks.json` → treated as empty (logged warning, GET returns 204 / empty list); the next write replaces it atomically. Never 500/422 to the client.
- Resume or bookmark `ms` beyond the chapter end → clamped to the chapter end on the client (durations come from the chapter sync file); no automatic jump to the next chapter. Resume always loads paused.

`LibraryItemDto` gains `HasReader` (Opus and `reader/index.json` exists). Every resolved file passes `LibraryScanner.IsInside(outputRoot, path)`. `ReaderStore` serializes writes per book with a `SemaphoreSlim` and writes `.partial` → `File.Move(overwrite)`.

**Frontend** (`ClientApp/src/features/reader/`):
- Components: `ReaderPage` (container), `PaginatedChapter`, `CoverPage`, `ReaderToc`, `ReaderToolbar`, `PageIndicator`, `AudioControls` (play, ±15 s, speed 0.75–2), `BackToAudioButton`, `AppearancePanel`, `BookmarksPanel`.
- Hooks: `usePagination`, `useAudioSync`, `useAutoFollow`, `useReaderProgress` (PUT on pause, on chapter change, every 10 s, and with `keepalive` on hide), `useBookmarks`.
- Pure modules (unit-tested):
  - `pagination.ts`: `pagesPerView(w)` (≥1100 → 2), `columnMetrics`, `pageCount`, `pageOfRect`, `clampPage`, `spreadStart`.
  - `sync.ts`: `findActiveEntry` (binary search), `pageTurnFraction(rects)`, `skipMs`, `clampSpeed`, `bookPercent`.
  - `entries.ts`: `flattenEntries`, `entryAtMs`.

**CSS** (`reader.css`):
- Layout: the viewport is `overflow:hidden`. `colW = floor((W - G·(ppv-1))/ppv)`, and the content `width = ppv·colW + (ppv-1)·G`, with `column-width:colW px; column-gap:G px; column-fill:auto; height:100%`.
- Movement: `translateX(-spreadStart·stride)` in integer px, where `stride = colW+G`. An entry's page is `floor((rect.left - content.left)/stride)`, measured relative to the transformed content.
- Sentences spanning pages: `getClientRects()` gives one rect per line. Auto-turn happens when the sentence's progress is ≥ the fraction of its lines on the visible spread.
- Cover: a `break-after:column` page that comes before the first chapter.
- Relayout triggers: `ResizeObserver`, an appearance change and `document.fonts.ready`. Each one re-anchors to the active entry (when following) or to the first visible entry.
- Themes are CSS variables on `[data-theme=light|sepia|dark]`.

## File Changes

| File | Action |
|---|---|
| `Core/Models/ReaderModels.cs`, `Core/Services/{SyncMapBuilder,ReaderTextSegmenter,ReaderTextAligner}.cs` | Create |
| `Core/Services/WavFile.cs` (`ReadPcmInfo`), `Core/Services/BookConversionPipeline.cs` (callback) | Modify |
| `Web/Services/{ReaderSidecarWriter,ReaderStore,LibraryBookResolver,LibraryIds}.cs`, `Web/Endpoints/ReaderEndpoints.cs` | Create |
| `Web/Services/ConversionRunner.cs`, `Web/Endpoints/LibraryEndpoints.cs`, `Web/Models/ApiDtos.cs`, `Web/Program.cs` | Modify |
| `ClientApp/src/features/reader/**` | Create |
| `ClientApp/src/app/App.tsx`, `features/library/BookCard.tsx`, `shared/api/{types,client}.ts` | Modify |

## Testing Strategy (strict TDD, RED first)

| Slice | Est. lines | RED tests |
|---|---|---|
| F0 sync map + writer + runner wiring | ~290 | **First:** golden `SentenceChunker` hashes on fixtures (abbreviations, numbers, long sentence, dialogue). Then: `ReadPcmInfo` on generated WAVs, Σ chunk ms = `ReadDuration(chapter wav)`, callback invoked once per chapter and not when null, writer atomicity and index merge, no sidecars when export fails or for M4B, mismatched byteRate → conversion succeeds, that chapter has no sidecar, warning set |
| F1 segmentation/alignment | ~320 | Copied regex vs chunker on fixtures; abbreviation merge; long sentence spanning chunks; Roman heading across paragraphs → fallback; empty paragraph; leftover chunks; times monotonic and within chunk bounds |
| F2 API | ~340 | WebApplicationFactory: each route, 206 Range, malformed Range → 416/200, progress roundtrip, bookmarks CRUD, validation 400, `hasReader`, threat rows below |
| F3a static view + TOC | ~360 | `pagination.ts`, `flattenEntries`, ReaderPage load with stubbed `scrollWidth`, TOC navigation, "Leer" disabled with hint |
| F3b audio sync | ~360 | `findActiveEntry`, `pageTurnFraction`, `skipMs`, `useAudioSync` with fake rAF/audio, click-to-seek, follow suspended + "Volver al audio" |
| F3c appearance/spread | ~150 | `pagesPerView`, appearance reducer + storage, re-anchor by entry |
| F4 resume/bookmarks | ~280 | `useReaderProgress` with fake timers, resume seek, bookmarks panel CRUD |

Real layout is verified manually (no e2e).

## Threat Matrix

| Boundary | Applicability | Design response | Planned RED tests (F2) |
|---|---|---|---|
| HTTP file-serving routes (`id`, `{n}`, `bookmarkId`) | Applicable | Segments are typed by route constraints. `id` is resolved only through the resolver's enumeration, never path-joined. Every file passes `IsInside`. | `..%2F`/encoded id → 404; negative/non-int `n` → 404; non-guid bookmark → 404. Symlink/junction resolution is out of scope: ids resolve only by enumerating the root's direct children, and this is a personal local library |
| Range requests | Applicable | Framework `enableRangeProcessing` | valid → 206; unsatisfiable → 416 |
| Documentation-like paths | N/A: no file classification | — | — |
| Git repo selection / commit / push / PR | N/A: no VCS automation. The existing `explorer.exe` call is untouched | — | — |

## Migration / Rollout

Additive. Old books have `hasReader=false`. The fix is to reconvert with the same settings, which hits the chunk cache fully. Rollback: revert the slices in reverse order. Deleting `reader/` restores the prior state.

## Open Questions

- [ ] None blocking. Assumption: the runner aligns with `new SpanishTextNormalizer()` and a null dictionary, the same as its pipeline call. If a pronunciation dictionary is ever added to the Web host, it must also be passed to `Align`.
