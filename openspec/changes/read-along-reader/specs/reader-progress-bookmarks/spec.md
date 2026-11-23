# Delta Spec: reader-progress-bookmarks

Resume position and bookmarks, stored server-side in `reader/` (`progress.json`, `bookmarks.json`), expressed as `{chapterIndex, ms}` and never as page numbers.

## ADDED Requirements

### Requirement: Single resume position
Exactly one resume position per book MUST exist (last write wins). The reader MUST save it periodically while playing (throttled), on pause, on chapter change and on leaving the reader.

#### Scenario: Resume after reload
- GIVEN the user stopped at chapter 2, 83 000 ms
- WHEN the reader is reopened
- THEN chapter 2 loads, audio is positioned at 83 000 ms (paused) and the page containing that entry is shown

#### Scenario: No saved position
- GIVEN no progress exists
- WHEN the reader opens
- THEN it starts at the cover/first page of the first available chapter

#### Scenario: Layout independence
- GIVEN progress saved on a wide screen
- WHEN opened on a narrow viewport or with another font size
- THEN the correct entry is found by `ms`, not by page number

#### Scenario: Position beyond chapter end
- GIVEN saved `ms` exceeds the chapter duration (e.g. after reconversion)
- WHEN the reader resumes
- THEN position is clamped to the chapter end (or start of the next chapter when available) and no error is shown

#### Scenario: Saved chapter no longer available
- GIVEN the saved chapter is missing from the index
- WHEN the reader resumes
- THEN it falls back to the first available chapter at 0 ms

#### Scenario: Save failure
- GIVEN the progress PUT fails
- WHEN the reader is in use
- THEN reading and playback continue and the save is retried at the next trigger

### Requirement: Bookmarks
The user MUST be able to add a bookmark at the current position with an optional note (max 500 characters), list bookmarks ordered by chapter then `ms`, edit the note, delete, and jump to a bookmark. Bookmarks are a separate list from the resume position.

#### Scenario: Add while playing
- GIVEN audio plays at chapter 1, 12 300 ms
- WHEN the user adds a bookmark with note "revisar"
- THEN it appears in the list showing chapter title, time and note, and playback is not interrupted

#### Scenario: Jump to bookmark
- GIVEN a bookmark at chapter 3, 40 000 ms
- WHEN the user selects it
- THEN chapter 3 loads, audio seeks to 40 000 ms, the page shows its entry, and auto-follow is active

#### Scenario: Bookmarks survive reload
- GIVEN two bookmarks exist
- WHEN the page reloads
- THEN both are listed

#### Scenario: Bookmark beyond chapter end
- GIVEN a bookmark's `ms` exceeds its chapter duration
- WHEN selected
- THEN position is clamped to the chapter end without error

#### Scenario: Bookmark of removed chapter
- GIVEN a bookmark references a chapter absent from the index
- WHEN the list renders
- THEN it is shown disabled (still deletable)

#### Scenario: Corrupt storage file
- GIVEN `bookmarks.json` or `progress.json` is malformed
- WHEN loaded
- THEN the API treats it as empty/no progress, the reader keeps working, and the next write replaces the file
