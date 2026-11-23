# Delta Spec: reader-sync-map

Chunk timing extraction and `reader/` sidecar files. Location: `<BookDir>/reader/` with `index.json` and `chapter_NNN.json`.

## ADDED Requirements

### Requirement: WAV PCM info
The system MUST expose PCM info (sample rate, channels, bits, data length) read from a chunk WAV header, and chunk duration MUST be `(fileLength - 44) / byteRate` for the 44-byte-header mono PCM16 format.

#### Scenario: Duration from header
- GIVEN a mono PCM16 WAV of 44 + 48000 bytes at 24000 Hz
- WHEN PCM info is read
- THEN the duration is 1000 ms

#### Scenario: Unsupported or truncated WAV
- GIVEN a file shorter than 44 bytes or without a valid RIFF/PCM header
- WHEN PCM info is read
- THEN a controlled error is raised and the conversion is not aborted by the sync map (the chapter's sync file is skipped, audio export continues)

### Requirement: Chunk timing map
For each synthesized or cache-hit chunk of a chapter, the system MUST compute `startMs` and `endMs` by cumulative duration in chunk order. The sum of chunk durations MUST equal the chapter WAV duration (`WavFile.ReadDuration`).

#### Scenario: Durations add up
- GIVEN a chapter with N chunks assembled into one WAV
- WHEN the sync map is built
- THEN the last chunk's `endMs` equals the chapter WAV duration and each `startMs` equals the previous `endMs`

#### Scenario: Cache-hit chunks
- GIVEN all chunks of a chapter come from `IChunkCache`
- WHEN the chapter is converted again
- THEN the sync map is produced identically without calling the TTS engine

### Requirement: Pipeline hook without constructor change
`BookConversionPipeline.ConvertAsync` MUST accept an optional `onChapterSynced` callback invoked once per completed chapter. The Core service constructor, `TextChunk.Hash` and `SentenceChunker` MUST NOT change. Callers that omit the callback MUST behave exactly as before (Cli, App).

#### Scenario: Callback omitted
- GIVEN the CLI converts a book without passing the callback
- WHEN conversion finishes
- THEN no `reader/` files are written and outputs are unchanged

#### Scenario: Chunk hashes unchanged
- GIVEN fixtures including pathological text
- WHEN hashes are computed before and after the change
- THEN every `TextChunk.Hash` is identical (characterization test)

### Requirement: Sidecar files
The Web `ConversionRunner` MUST write `chapter_NNN.json` per chapter and an `index.json` listing available chapters (index, title, durationMs, audio kind), atomically (write to `<file>.partial`, then move). Files MUST survive `--delete-wav` and MUST be ignored by `LibraryScanner` and zip downloads.

#### Scenario: Chapter subset merges
- GIVEN a book previously converted with chapters 1-3 has reader files
- WHEN chapters 4-5 are converted into the same book folder
- THEN `chapter_004.json` and `chapter_005.json` are added, chapters 1-3 files are untouched, and `index.json` lists chapters 1-5

#### Scenario: Reconversion of an existing chapter
- GIVEN `chapter_002.json` exists
- WHEN chapter 2 is converted again
- THEN its file and its index entry are replaced, not duplicated

#### Scenario: Translated book
- GIVEN a book converted with translation to `es`
- WHEN the sync map is written
- THEN timings correspond to the translated (spoken) chapter text

#### Scenario: Interrupted write
- GIVEN the process stops while writing a sidecar
- WHEN the book is read later
- THEN no partially written `.json` is visible; only `.partial` remains and is ignored

#### Scenario: M4B export
- GIVEN a book exported as M4B
- WHEN the conversion finishes
- THEN the reader data MAY exist but the index marks audio as unavailable for the reader (reader unsupported for M4B)
