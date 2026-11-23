namespace ConversorLibros.Core.Models;

/// <summary>PCM facts read from a WAV header. <c>DataBytes</c> is clamped to the actual file length.</summary>
public sealed record WavPcmInfo(int SampleRate, int Channels, int BitsPerSample, int ByteRate, long DataOffset, long DataBytes);

/// <summary>Timing of one synthesized chunk inside the assembled chapter audio.</summary>
public sealed record ChunkTiming(int Order, string NormalizedText, long StartMs, long EndMs);

/// <summary>Chunk timings of a chapter plus the raw text that was spoken (post-translation).</summary>
public sealed record ChapterSyncMap(
    int ChapterIndex,
    string Title,
    string RawText,
    long DurationMs,
    IReadOnlyList<ChunkTiming> Chunks);

public sealed record ReaderSentence(string Text, long StartMs, long EndMs);

/// <summary><c>Mode</c> is <c>sentence</c> or <c>paragraph</c>.</summary>
public sealed record ReaderParagraph(string Mode, IReadOnlyList<ReaderSentence> Sentences);

public sealed record ReaderChapter(
    int ChapterIndex,
    string Title,
    long DurationMs,
    IReadOnlyList<ChunkTiming> Chunks,
    IReadOnlyList<ReaderParagraph> Paragraphs);
