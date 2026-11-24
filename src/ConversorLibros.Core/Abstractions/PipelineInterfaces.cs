using ConversorLibros.Core.Models;

namespace ConversorLibros.Core.Abstractions;

/// <summary>
/// Extracts structured chapters and metadata from a book file (e.g., DRM-free EPUB).
/// </summary>
public interface IBookSource
{
    Task<Book> LoadAsync(string filePath, CancellationToken cancellationToken = default);
}

/// <summary>
/// Normalizes raw chapter text into speakable text for a target language (e.g., es-ES).
/// </summary>
public interface ITextNormalizer
{
    string Normalize(string rawText, IReadOnlyDictionary<string, string>? pronunciationDictionary = null);
}

/// <summary>
/// Splits normalized chapter text into synthesis-ready chunks under engine token limits.
/// </summary>
public interface IChunker
{
    IReadOnlyList<TextChunk> Chunk(Chapter chapter, string normalizedText, string voiceId, float speed, string modelVersion);
}

/// <summary>
/// Synthesizes a single text chunk into WAV audio bytes.
/// </summary>
public interface ITtsEngine
{
    string ModelVersion { get; }
    Task<byte[]> SynthesizeWavAsync(string text, string voiceId, float speed, CancellationToken cancellationToken = default);
}

/// <summary>
/// Caches synthesized chunk WAV files on disk keyed by content hash.
/// </summary>
public interface IChunkCache
{
    Task<string?> TryGetAudioPathAsync(string contentHash, CancellationToken cancellationToken = default);
    Task<string> SaveAudioAsync(string contentHash, byte[] wavBytes, CancellationToken cancellationToken = default);
}

/// <summary>
/// Translates a text fragment between languages using a local or remote model.
/// </summary>
public interface ITranslator
{
    /// <summary>Identifies the model and prompt version; part of the translation cache key.</summary>
    string ModelVersion { get; }
    Task<string> TranslateAsync(string text, string sourceLanguage, string targetLanguage, CancellationToken cancellationToken = default);
}

/// <summary>
/// Caches translated text fragments keyed by a deterministic content key.
/// </summary>
public interface ITranslationCache
{
    Task<string?> TryGetAsync(string key, CancellationToken cancellationToken = default);
    Task SaveAsync(string key, string translatedText, CancellationToken cancellationToken = default);
}

/// <summary>
/// Assembles synthesized chunk WAV files into a single chapter audio file via streaming.
/// </summary>
public interface IChapterAssembler
{
    Task<string> AssembleChapterAsync(
        int chapterIndex,
        IReadOnlyList<string> chunkAudioPaths,
        string outputDirectory,
        CancellationToken cancellationToken = default);

    Task<string> AssembleChapterAsync(
        int chapterIndex,
        IReadOnlyList<string> chunkAudioPaths,
        string outputDirectory,
        IReadOnlyList<TimeSpan>? pausesAfter,
        CancellationToken cancellationToken = default) =>
        AssembleChapterAsync(chapterIndex, chunkAudioPaths, outputDirectory, cancellationToken);
}
