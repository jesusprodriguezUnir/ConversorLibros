# Delta Spec: reader-text-alignment

Raw-text segmentation, per-sentence normalization, alignment against chunk text and fallback. Stored shape: `{paragraphs:[{sentences:[{text,startMs,endMs}]}]}`.

## ADDED Requirements

### Requirement: Segment raw text first
The system MUST split the chapter raw text (translated text when translation is used) into paragraphs and then sentences, merging splits that follow abbreviations from the normalizer dictionary, and MUST normalize each sentence individually with `SpanishTextNormalizer`.

#### Scenario: Abbreviation does not split
- GIVEN the paragraph "El Sr. López llegó a las 5 p. m. y se fue."
- WHEN it is segmented
- THEN it yields one sentence and the displayed text keeps the original "Sr." and "5"

#### Scenario: Displayed text is original
- GIVEN the sentence "Subió un 25 % en 2020."
- WHEN aligned
- THEN the reader text is the original sentence, while the match uses its normalized form

### Requirement: Alignment verification
For each chapter, the joined normalized sentences MUST equal the chunk text sequence. When equal, sentence times MUST be interpolated by character count inside each chunk, with chunk edges exact (first sentence of a chunk starts at the chunk's `startMs`, last ends at its `endMs`).

#### Scenario: Sentences inside one chunk
- GIVEN a chunk of 1000 ms with sentences of normalized length 100 and 300 characters
- WHEN times are interpolated
- THEN sentence 1 spans 0-250 ms and sentence 2 spans 250-1000 ms relative to the chunk

#### Scenario: Sentence spanning two chunks
- GIVEN a sentence whose normalized text is split across two chunks
- WHEN aligned
- THEN its `startMs` is the first chunk's portion start and `endMs` the last chunk's portion end, with no gaps or overlaps between consecutive entries

### Requirement: Paragraph-level fallback
When verification fails for a paragraph, the system MUST emit a single entry for that paragraph with interpolated times, and the reader MUST highlight the whole paragraph during its time range. A failing paragraph MUST NOT affect neighbouring paragraphs.

#### Scenario: Mismatch in one paragraph
- GIVEN paragraph 3 normalizes to text that diverges from the chunk text
- WHEN aligned
- THEN paragraph 3 is a single entry covering its time range and paragraphs 2 and 4 keep sentence-level entries

#### Scenario: Roman numeral across paragraph break
- GIVEN "Capítulo" and "IV" in separate paragraphs, merged by `RomanChapterRegex` in chunk text
- WHEN aligned
- THEN the affected paragraphs use the fallback and no exception occurs

### Requirement: Monotonic coverage
Entries MUST be ordered, non-overlapping, and cover the chapter from 0 to its duration without negative or zero-length-inverted ranges. Empty paragraphs MUST be omitted.

#### Scenario: Empty and whitespace paragraphs
- GIVEN raw text with blank paragraphs
- WHEN aligned
- THEN they produce no entries and do not shift timings

#### Scenario: Pronunciation dictionary
- GIVEN a per-book pronunciation dictionary replacing a name
- WHEN a sentence is normalized and verified
- THEN the original name is displayed and alignment still succeeds
