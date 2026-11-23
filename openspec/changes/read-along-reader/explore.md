# Exploration: read-along-reader

## Goal

E-reader-style paginated reader in the Web host that plays the audiobook, highlights the text being spoken, turns pages automatically, seeks on click, resumes from the last position and supports bookmarks. Web only; MAUI may later reuse the same data files.

## Key finding

Chunks are not sentences. `SentenceChunker` packs sentences into chunks of up to 350 chars (`SentenceChunker.cs:47-76`). Chunk-level timing is exact; sentence-level timing inside a chunk must be interpolated (by character count) unless chunking changes.

## Q1 — Sync map

- Chunk WAVs come from `EncodePcm16Wav` (`PiperSherpaTtsEngine.cs:61`): 44-byte header, mono PCM16, one sample rate per model.
- `WavChapterAssembler` concatenates PCM with no silence (`PauseAfter` unused). Chunk duration = `(fileLength - 44) / byteRate`; exact.
- Add `WavFile.ReadPcmInfo` and a pure `SyncMapBuilder`.
- Wire through an optional `Action<ChapterSyncMap>? onChapterSynced` parameter on `BookConversionPipeline.ConvertAsync`; constructor and the three composition roots stay untouched, only `ConversionRunner` passes it.
- Test: sum of chunk durations equals `WavFile.ReadDuration` (already used for M4B chapter markers).
- Location: `<BookDir>/reader/` (`index.json`, `chapter_NNN.json`). Survives `--delete-wav`; `LibraryScanner` and `SelectZipFiles` ignore subfolders; per-chapter files merge safely for chapter subsets.
- If silence is ever inserted between chunks, the map must include it.

## Q2 — Original text mapping

| Option | Verdict |
|---|---|
| (a) Segment raw text first, normalize per segment | Recommended |
| (b) Whole-chapter normalization + alignment back to raw | Rejected: abbreviation expansion removes periods; needs an offset-tracking normalizer rewrite |
| (c) Display normalized text | Fallback only (shows "veinticinco por ciento") |

- `SpanishTextNormalizer` rules (abbreviations, numbers, percentages, decimals, pronunciation dictionary, dialogue dash) are local: per-paragraph output equals whole-chapter output.
- `ExtractText` collapses whitespace; hyphenation across lines is effectively dead for EPUB sources.
- Only divergence: `RomanChapterRegex` uses `\s+` and can span a paragraph break ("Capítulo" / "IV"). Effect: rare cache miss, only that text is re-synthesized.
- Design: split raw into paragraphs → sentences (same regex, merge after abbreviations from the normalizer dictionary) → normalize each → verify joined normalized text equals chunk text → on mismatch fall back to a paragraph-level entry with interpolated times.
- Store `{paragraphs:[{sentences:[{text,startMs,endMs}]}]}`; no offsets needed.
- Characterization test first (strict TDD): old chunk hashes equal new hashes on fixtures, including pathological cases.
- Translation: the translated chapter replaces `chapter` before normalization (`BookConversionPipeline.cs:79-91`); the reader shows the translated Spanish text (what is spoken).

## Q3 — Audio source

- Opus first: per-chapter files give natural chapter offsets; browsers handle Ogg Opus pre-skip. Needs `GET /api/library/{id}/chapters/{n}/audio` with `enableRangeProcessing` and `audio/ogg`, resolved via `AudiobookFileNames.ChapterPrefix(index)*.opus`.
- M4B: Range already supported (`LibraryEndpoints.cs:54`). Seek = cumulative chapter offset (same durations as `FfmetadataBuilder`) + chunk ms. AAC priming ~23-46 ms, acceptable. Re-exporting a different chapter subset invalidates the index.

## Q4 — Pagination

- CSS multi-column, no new dependency: `overflow:hidden`, `column-width` = page width, `translateX(-page*W)`; page count = `scrollWidth / W`, recomputed via `ResizeObserver` and on font change; sentence page = `floor(span.getClientRects()[0].left / W)`; one chapter rendered at a time.
- Rejected: epub.js/react-reader (dependency, EPUB-DOM based), JS measure-fit (slow), virtual scroll (not requested).
- Audio sync: `requestAnimationFrame` polling of `audio.currentTime` (`timeupdate` ~4 Hz), binary search for the active entry.
- Testing: jsdom cannot lay out text. Extract pure functions (`findActiveEntry`, `pageForOffset`, `clampPage`, position serialization) and test with Vitest; stub `scrollWidth`/`offsetLeft` for the hook; real layout verified manually (no Playwright).
- Structure: route `/biblioteca/:id/leer`, "Leer" button in `BookCard`, new `features/reader/`, extend `shared/api/types.ts` and `client.ts`.

## Q5 — Backfill

- Existing books lack sync data; EPUB path, speed and model are not persisted; `UploadedBookStore` purges uploads.
- Cheapest path: reconvert with the same settings. `work/cache` gives full chunk hits, so only ffmpeg reruns and sync files are produced. No new code.
- Dedicated "prepare reader" action is a later nice-to-have.

## Q6 — Size estimate (lines incl. tests, budget 400)

| Slice | Scope | Est. |
|---|---|---|
| F0 | Sync map, sidecar writer, runner wiring | ~270 |
| F1 | Raw segmentation, alignment/fallback, characterization tests (riskiest) | ~300 |
| F2 | API: chapter list, reader JSON, audio Range, progress GET/PUT, bookmarks CRUD, path containment | ~300 |
| F3a | Static paginated view, TOC, chapter nav | ~350 |
| F3b | Audio hook, highlight, auto page-turn, click-to-seek | ~350 |
| F4 | Resume and bookmarks UI | ~250 |

Progress and bookmarks stored server-side in `reader/*.json` as `{chapterIndex, ms}`.

## Recommendation

Sequence F0 → F1 → F2 → F3a → F3b → F4. F0 schema carries chunk entries so F1 only enriches it.

## Risks

- Sentence times inside a chunk are interpolated (approximate); chunk edges are exact.
- `RomanChapterRegex` divergence and abbreviation/sentence-split mismatches, mitigated by paragraph fallback and characterization tests.
- Backfill assumes `work/cache` still exists.
- M4B subset re-export invalidates the index.
- Safari/Opus support and multi-column quirks (sentence spanning two columns) verifiable only manually.
- Keep the pipeline change to an optional parameter.
