using System.Text.RegularExpressions;
using ConversorLibros.Core.Abstractions;
using ConversorLibros.Core.Models;

namespace ConversorLibros.Core.Services;

/// <summary>
/// Maps chapter WAV files (<c>chapter_NNN.wav</c>) back to book chapters and resolves the listener-facing title,
/// translated through the translation cache when translation is enabled.
/// </summary>
public static partial class AudiobookChapterResolver
{
    public static async Task<IReadOnlyList<AudiobookChapter>> ResolveAsync(
        Book book,
        IReadOnlyList<string> chapterAudioPaths,
        CachedChapterTranslator? translator = null,
        string? sourceLanguage = null,
        string targetLanguage = "es",
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(book);
        ArgumentNullException.ThrowIfNull(chapterAudioPaths);

        var result = new List<AudiobookChapter>(chapterAudioPaths.Count);
        foreach (var audioPath in chapterAudioPaths)
        {
            var match = ChapterFileNameRegex().Match(Path.GetFileNameWithoutExtension(audioPath));
            var chapter = match.Success
                ? book.Chapters.FirstOrDefault(c => c.Index == int.Parse(match.Groups[1].ValueSpan))
                : null;

            if (chapter is null)
            {
                result.Add(new AudiobookChapter(0, Path.GetFileNameWithoutExtension(audioPath), audioPath));
                continue;
            }

            var title = chapter.Title;
            if (translator is not null && !string.IsNullOrWhiteSpace(sourceLanguage))
            {
                var translated = await translator
                    .TranslateChapterAsync(chapter, sourceLanguage, targetLanguage, null, cancellationToken)
                    .ConfigureAwait(false);
                title = translated.Title;
            }

            result.Add(new AudiobookChapter(chapter.Index, title, audioPath));
        }

        return result;
    }

    [GeneratedRegex(@"^chapter_(\d+)$")]
    private static partial Regex ChapterFileNameRegex();
}
