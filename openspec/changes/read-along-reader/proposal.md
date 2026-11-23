# Proposal: Read-Along Reader

## Intent

Converted audiobooks can only be listened to or downloaded; the text is lost. Add a paginated e-reader in the Web host that plays the audiobook and highlights the sentence being spoken, so the user can read along, jump by clicking text and resume where they left off.

## Scope

### In Scope
- Per-chapter sync map (chunk timings) written to `<BookDir>/reader/` during conversion.
- Sentence segmentation of the original text (translated when used), sentence times interpolated by character count inside each chunk; paragraph-level fallback on alignment mismatch.
- API: reader index/chapter JSON, per-chapter Opus audio with Range, progress GET/PUT, bookmarks CRUD.
- Paginated reader (CSS multi-column): cover first page, chapter TOC/nav, page indicator and progress, two-page spread on wide screens, themes (light/sepia/dark), typography (size, family, margins).
- Audio sync: sentence highlight, auto page-turn (sentences spanning pages), manual navigation suspends auto-follow with "Volver al audio", click-to-seek, speed 0.75x–2x, ±15 s skip.
- Resume (server-side `{chapterIndex, ms}`, never page number) and bookmarks with optional note.

### Out of Scope
- M4B books (reader button disabled), MAUI reader, dedicated backfill action (backfill = reconvert with full cache hits).
- Sleep timer, keyboard shortcuts / Media Session, word-level highlight, EPUB3 Media Overlays, original EPUB HTML/images.
- Any change to `SentenceChunker` or `TextChunk.Hash`.

## Capabilities

### New Capabilities
- `reader-sync-map`: chunk timing extraction and `reader/` sidecar files.
- `reader-text-alignment`: raw-text segmentation, per-sentence normalization, alignment and fallback.
- `reader-api`: reader data, chapter audio, progress and bookmarks endpoints.
- `reader-ui`: paginated view, navigation, appearance and audio-synchronized reading.
- `reader-progress-bookmarks`: resume position and bookmark persistence and UI.

### Modified Capabilities
- None (no existing specs).

## Approach

Per `explore.md`. Optional `onChapterSynced` parameter on `BookConversionPipeline.ConvertAsync`; **no Core constructor change**, so only `ConversionRunner` passes it. Segment raw text, normalize per segment, verify against chunk text. No new frontend dependency.

Slices: F0 sync map → F1 segmentation → F2 API → F3a static view + TOC → F3b audio sync → F3c themes/typography/spread (~150) → F4 resume/bookmarks UI.

## Affected Areas

| Area | Impact | Description |
|------|--------|-------------|
| `Core/Services/BookConversionPipeline.cs` | Modified | Optional sync callback |
| `Core/` reader types | New | `WavFile.ReadPcmInfo`, `SyncMapBuilder`, segmenter |
| `Web/Services/ConversionRunner.cs` | Modified | Writes `reader/*.json` |
| `Web/` endpoints | New | Reader, audio, progress, bookmarks |
| `ClientApp/src/features/reader/`, `BookCard`, `shared/api` | New/Modified | Route `/biblioteca/:id/leer` |

## Risks

| Risk | Likelihood | Mitigation |
|------|------------|------------|
| Chunk hash drift breaks resumable cache | Med | Characterization test before F1 |
| Sentence/abbreviation misalignment | Med | Paragraph fallback |
| Interpolated highlight feels off | Med | Chunk edges exact; accepted |
| Multi-column layout quirks | Med | Manual verification |
| Old books lack reader data | High | Disabled button with reconvert hint |

## Rollback Plan

Revert slice PRs in reverse order. `reader/` sidecars are additive and ignored by `LibraryScanner` and downloads; deleting them restores prior state.

## Dependencies

- None new. Backfill requires `work/cache` to still exist.

## Success Criteria

- [ ] Existing chunk hashes unchanged on fixtures.
- [ ] Sum of chunk durations equals chapter WAV duration.
- [ ] New Opus conversion opens in the reader with highlight following audio and automatic page turns.
- [ ] Resume and bookmarks survive reload; M4B books show a disabled reader button.
- [ ] Every slice stays within 400 changed lines.
