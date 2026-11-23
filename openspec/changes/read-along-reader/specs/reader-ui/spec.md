# Delta Spec: reader-ui

Paginated reader at route `/biblioteca/:id/leer`. UI strings in Spanish; pure logic (`findActiveEntry`, `pageForOffset`, `clampPage`) unit-tested; layout verified manually.

## ADDED Requirements

### Requirement: Entry point
`BookCard` MUST show a "Leer" button for books with reader data. For books without reader data (including M4B) the button MUST be disabled with a hint to reconvert.

#### Scenario: Reader available
- GIVEN the index reports `available: true`
- WHEN the library renders
- THEN "Leer" is enabled and navigates to `/biblioteca/:id/leer`

#### Scenario: Reader unavailable
- GIVEN `available: false` or an M4B book
- WHEN the library renders
- THEN "Leer" is disabled with a visible hint "Reconvierte el libro para habilitar el lector" (or equivalent Spanish text)

#### Scenario: Direct URL without data
- GIVEN the user opens `/leer` for a book without reader data
- WHEN the page loads
- THEN an error state with the same hint and a link back to the library is shown, without crashing

### Requirement: Pagination
The reader MUST render one chapter at a time with CSS multi-column pages, cover as the first page of the book, a page indicator ("página X de Y") and overall progress. Wide viewports (reader width >= 1100 px) MUST show a two-page spread; narrower widths a single page. Page count MUST be recomputed on resize and on typography change.

#### Scenario: Spread threshold
- GIVEN the reader viewport is 1100 px wide
- WHEN rendered
- THEN a two-page spread is shown; at 1099 px a single page

#### Scenario: Resize keeps position
- GIVEN the user is on a sentence on page 5
- WHEN the window is resized and pages are recomputed
- THEN the page containing that sentence is shown (position preserved by content, not page number)

#### Scenario: Missing cover
- GIVEN the book has no cover
- WHEN the reader opens at the start
- THEN the first page is the first chapter page without a broken image

### Requirement: Navigation
The reader MUST provide a chapter TOC, previous/next chapter, previous/next page controls and page-clamping at chapter bounds.

#### Scenario: Open chapter from TOC
- GIVEN the TOC lists chapters from the index
- WHEN the user selects chapter 3
- THEN chapter 3 loads at page 1 and audio switches to chapter 3 at 0 ms

#### Scenario: Chapter without data
- GIVEN a chapter is absent from the index (subset conversion)
- WHEN the TOC renders
- THEN that entry is shown disabled

#### Scenario: Bounds
- GIVEN the last page of the last chapter
- WHEN "next" is activated
- THEN nothing changes (clamped)

### Requirement: Appearance
The reader MUST offer themes light, sepia and dark; font size, font family and margin settings; choices MUST persist across sessions (browser storage) and apply without reloading audio.

#### Scenario: Change theme during playback
- GIVEN audio is playing
- WHEN the user switches to dark
- THEN colours change and playback continues uninterrupted

#### Scenario: Font size change
- GIVEN the current sentence is on page 4
- WHEN font size increases
- THEN pages are recomputed and the page with the current sentence is shown

### Requirement: Audio synchronization
The reader MUST play the chapter audio, highlight the active entry (sentence, or whole paragraph for fallback paragraphs) found by binary search on `audio.currentTime` via `requestAnimationFrame`, and turn pages automatically so the active entry is visible. Click on an entry MUST seek audio to its `startMs`.

#### Scenario: Highlight follows audio
- GIVEN playback within the time range of sentence 7
- WHEN a frame is processed
- THEN sentence 7 is highlighted and others are not

#### Scenario: Paragraph fallback
- GIVEN a paragraph with only paragraph-level alignment
- WHEN playback is inside its range
- THEN the whole paragraph is highlighted

#### Scenario: Sentence spanning two pages
- GIVEN the active sentence starts on page 3 and ends on page 4
- WHEN it becomes active
- THEN the page of its first rect (3) is shown and the page turns to 4 when the playback reaches the part on page 4; highlight is applied to all fragments

#### Scenario: Chapter ends
- GIVEN playback reaches the end of a chapter and the next chapter has data
- WHEN the audio ends
- THEN the next chapter loads and plays; if none, playback stops

#### Scenario: Click to seek
- GIVEN audio is paused
- WHEN the user clicks a sentence
- THEN audio seeks to that sentence's `startMs` and the sentence is highlighted

### Requirement: Manual navigation suspends auto-follow
When the user changes page manually while audio plays, the reader MUST stop auto-turning pages and show a "Volver al audio" control. Activating it MUST jump to the page of the active entry and resume auto-follow. Audio MUST keep playing during suspension.

#### Scenario: Suspend
- GIVEN audio plays and auto-follow is on
- WHEN the user presses "next page"
- THEN auto-turn stops, audio continues, and "Volver al audio" appears

#### Scenario: Resume follow
- GIVEN auto-follow is suspended
- WHEN the user presses "Volver al audio"
- THEN the view shows the active entry's page, the control disappears and auto-turn resumes

#### Scenario: Click-to-seek resumes follow
- GIVEN auto-follow is suspended
- WHEN the user clicks a sentence
- THEN audio seeks there and auto-follow resumes

### Requirement: Playback controls
The reader MUST provide play/pause, speed 0.75x to 2x, and skip of 15 s backward/forward. Skips MUST NOT go below 0 nor past the chapter end (past end moves to next chapter start or clamps at the end).

#### Scenario: Skip bounds
- GIVEN playback at 5 s
- WHEN "-15 s" is pressed
- THEN position is 0 s

#### Scenario: Speed range
- GIVEN speed 2x
- WHEN the user raises speed
- THEN it stays at 2x; highlight timing still follows `currentTime`

#### Scenario: Audio load error
- GIVEN the audio endpoint returns 404
- WHEN playback starts
- THEN an error message is shown, the text remains readable and the app does not crash
