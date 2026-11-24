using ConversorLibros.Core.Abstractions;
using ConversorLibros.Core.Models;

namespace ConversorLibros.Core.Services;

/// <summary>
/// Orchestrates book extraction, text normalization, chunking, cached TTS synthesis, and chapter assembly.
/// </summary>
public sealed class BookConversionPipeline
{
    private readonly IBookSource _bookSource;
    private readonly ITextNormalizer _normalizer;
    private readonly IChunker _chunker;
    private readonly ITtsEngine _ttsEngine;
    private readonly IChunkCache _chunkCache;
    private readonly IChapterAssembler _chapterAssembler;
    private readonly CachedChapterTranslator? _chapterTranslator;

    /// <summary>
    /// Creates the pipeline. The optional <paramref name="chapterTranslator"/> is only used when
    /// <see cref="ConvertAsync"/> receives source and target languages.
    /// </summary>
    public BookConversionPipeline(
        IBookSource bookSource,
        ITextNormalizer normalizer,
        IChunker chunker,
        ITtsEngine ttsEngine,
        IChunkCache chunkCache,
        IChapterAssembler chapterAssembler,
        CachedChapterTranslator? chapterTranslator = null)
    {
        _bookSource = bookSource;
        _normalizer = normalizer;
        _chunker = chunker;
        _ttsEngine = ttsEngine;
        _chunkCache = chunkCache;
        _chapterAssembler = chapterAssembler;
        _chapterTranslator = chapterTranslator;
    }

    public async Task<IReadOnlyList<string>> ConvertAsync(
        string epubPath,
        string outputDirectory,
        string voiceId,
        float speed = 1.0f,
        int? startChapter = null,
        int? endChapter = null,
        IReadOnlyDictionary<string, string>? pronunciationDictionary = null,
        IProgress<ConversionProgress>? progress = null,
        CancellationToken cancellationToken = default,
        string? sourceLanguage = null,
        string? targetLanguage = null,
        IReadOnlySet<int>? chapterIndexes = null,
        Action<ChapterSyncMap>? onChapterSynced = null)
    {
        var translate = sourceLanguage is not null || targetLanguage is not null;
        if (translate && (string.IsNullOrWhiteSpace(sourceLanguage) || string.IsNullOrWhiteSpace(targetLanguage)))
        {
            throw new ArgumentException("Both source and target languages are required for translation.");
        }

        if (translate && _chapterTranslator is null)
        {
            throw new InvalidOperationException("Translation was requested but no chapter translator was configured.");
        }

        var book = await _bookSource.LoadAsync(epubPath, cancellationToken).ConfigureAwait(false);
        var selectedChapters = book.Chapters
            .Where(c => (!startChapter.HasValue || c.Index >= startChapter.Value)
                     && (!endChapter.HasValue || c.Index <= endChapter.Value)
                     && (chapterIndexes is null || chapterIndexes.Contains(c.Index)))
            .ToList();

        var assembledChapters = new List<string>(selectedChapters.Count);

        foreach (var sourceChapter in selectedChapters)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var chapter = sourceChapter;
            if (translate)
            {
                var chapterIndex = sourceChapter.Index;
                chapter = await _chapterTranslator!.TranslateChapterAsync(
                    sourceChapter,
                    sourceLanguage!,
                    targetLanguage!,
                    (done, total) => progress?.Report(new ConversionProgress(chapterIndex, selectedChapters.Count, done, total, "Translating")),
                    cancellationToken).ConfigureAwait(false);
            }

            var normalized = _normalizer.Normalize(chapter.RawText, pronunciationDictionary);
            var chunks = _chunker.Chunk(chapter, normalized, voiceId, speed, _ttsEngine.ModelVersion);
            if (chunks.Count == 0)
            {
                continue;
            }

            var chunkPaths = new List<string>(chunks.Count);
            for (var i = 0; i < chunks.Count; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var chunk = chunks[i];

                var cachedPath = await _chunkCache.TryGetAudioPathAsync(chunk.ContentHash, cancellationToken).ConfigureAwait(false);
                if (cachedPath is null)
                {
                    var wavBytes = await _ttsEngine.SynthesizeWavAsync(chunk.NormalizedText, voiceId, speed, cancellationToken).ConfigureAwait(false);
                    cachedPath = await _chunkCache.SaveAudioAsync(chunk.ContentHash, wavBytes, cancellationToken).ConfigureAwait(false);
                }

                chunkPaths.Add(cachedPath);
                progress?.Report(new ConversionProgress(chapter.Index, selectedChapters.Count, i + 1, chunks.Count, "Synthesizing"));
            }

            var pausesAfter = chunks.Select(c => c.PauseAfter).ToList();
            var chapterPath = await _chapterAssembler.AssembleChapterAsync(chapter.Index, chunkPaths, outputDirectory, pausesAfter, cancellationToken).ConfigureAwait(false);
            assembledChapters.Add(chapterPath);

            // Timings use the post-translation chapter, so the reader shows what is spoken.
            // A null map (unreadable or mismatched chunk WAVs) only skips the reader data.
            if (onChapterSynced is not null && SyncMapBuilder.Build(chapter, chunks, chunkPaths) is { } syncMap)
            {
                onChapterSynced(syncMap);
            }

            progress?.Report(new ConversionProgress(chapter.Index, selectedChapters.Count, chunks.Count, chunks.Count, "Assembled"));
        }

        return assembledChapters;
    }
}
