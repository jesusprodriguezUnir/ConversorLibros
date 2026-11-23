# Delta Spec: reader-api

HTTP endpoints in the Web host under `/api/library/{id}`.

## ADDED Requirements

### Requirement: Reader index and chapter data
`GET /api/library/{id}/reader` MUST return the index (chapters, titles, durations, cover availability, reader availability). `GET /api/library/{id}/reader/chapters/{n}` MUST return that chapter's paragraphs and timed entries.

#### Scenario: Book with reader data
- GIVEN an Opus book with `reader/index.json`
- WHEN the client requests the index
- THEN it receives 200 with the chapter list and `available: true`

#### Scenario: Book without reader data
- GIVEN a book converted before this feature
- WHEN the client requests the index
- THEN it receives 200 with `available: false` and a reason (no data), enabling the UI hint to reconvert

#### Scenario: Missing chapter
- GIVEN chapter 9 is not in the index
- WHEN its data is requested
- THEN the response is 404

#### Scenario: Corrupt JSON
- GIVEN `chapter_002.json` is malformed
- WHEN it is requested
- THEN the response is a controlled error (500 with problem details or 422), the server does not crash, and other chapters remain readable; a corrupt `index.json` is reported as `available: false`

#### Scenario: Unknown book or invalid id
- GIVEN an id that does not exist or contains path separators or `..`
- WHEN any reader endpoint is called
- THEN the response is 404 (or 400) and no file outside `OutputRoot` is touched

### Requirement: Chapter audio with Range
`GET /api/library/{id}/chapters/{n}/audio` MUST stream the chapter's `NNN - *.opus` (resolved via `AudiobookFileNames.ChapterPrefix(index)`) as `audio/ogg` with Range processing enabled.

#### Scenario: Full request
- GIVEN chapter 1 has an Opus file
- WHEN requested without Range
- THEN 200 with `Content-Type: audio/ogg` and `Accept-Ranges: bytes`

#### Scenario: Range request
- GIVEN a valid file
- WHEN requested with `Range: bytes=1000-1999`
- THEN 206 with `Content-Range` and exactly 1000 bytes

#### Scenario: Unsatisfiable Range
- GIVEN a Range start beyond file length
- WHEN requested
- THEN 416

#### Scenario: Path containment
- GIVEN the resolved file path (after symlink/`..` normalization) is not under `OutputRoot`
- WHEN audio is requested
- THEN the response is 404 and the file is not served

#### Scenario: M4B or missing audio
- GIVEN the book is M4B or the chapter file is missing
- WHEN chapter audio is requested
- THEN 404

### Requirement: Progress endpoints
`GET /api/library/{id}/reader/progress` MUST return the single resume position `{chapterIndex, ms, updatedAt}` or 204/404 when none. `PUT` MUST replace it (last write wins) with validation: `chapterIndex` in the index and `ms >= 0`.

#### Scenario: First save and read
- GIVEN no progress
- WHEN the client PUTs `{chapterIndex: 2, ms: 5000}`
- THEN a later GET returns that value

#### Scenario: Invalid payload
- GIVEN `ms` negative or `chapterIndex` not in the index
- WHEN PUT is sent
- THEN 400 and the stored progress is unchanged

#### Scenario: Concurrent writes
- GIVEN two PUTs arrive in quick succession
- WHEN both complete
- THEN the stored value equals the last processed one and the file is never partially written (atomic write)

### Requirement: Bookmark endpoints
The API MUST provide list, create, update note and delete for bookmarks `{id, chapterIndex, ms, note?, createdAt}`, with server-generated ids and note length capped (500 characters).

#### Scenario: Create and list
- GIVEN no bookmarks
- WHEN the client POSTs `{chapterIndex:1, ms:1200, note:"idea"}`
- THEN 201 with the created bookmark and GET lists it

#### Scenario: Delete unknown id
- GIVEN a bookmark id that does not exist
- WHEN DELETE is called
- THEN 404

#### Scenario: Note too long
- GIVEN a note over 500 characters
- WHEN POST is sent
- THEN 400
