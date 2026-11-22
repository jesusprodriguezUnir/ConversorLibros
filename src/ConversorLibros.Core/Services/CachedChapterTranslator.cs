using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using ConversorLibros.Core.Abstractions;
using ConversorLibros.Core.Models;

namespace ConversorLibros.Core.Services;

/// <summary>
/// Translates chapters paragraph by paragraph, reusing cached translations keyed by paragraph content,
/// model version and language pair. Paragraphs without letters are copied unchanged.
/// </summary>
public sealed partial class CachedChapterTranslator
{
    private const string ParagraphSeparator = "\n\n";

    private readonly ITranslator _translator;
    private readonly ITranslationCache _cache;

    public CachedChapterTranslator(ITranslator translator, ITranslationCache cache)
    {
        _translator = translator ?? throw new ArgumentNullException(nameof(translator));
        _cache = cache ?? throw new ArgumentNullException(nameof(cache));
    }

    /// <summary>
    /// Returns a copy of the chapter with translated title and paragraphs, preserving paragraph order.
    /// </summary>
    /// <param name="onProgress">Optional callback with (translatedParagraphs, totalTranslatableParagraphs).</param>
    public async Task<Chapter> TranslateChapterAsync(
        Chapter chapter,
        string sourceLanguage,
        string targetLanguage,
        Action<int, int>? onProgress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(chapter);

        var title = HasLetters(chapter.Title)
            ? await TranslateParagraphAsync(chapter.Title, sourceLanguage, targetLanguage, cancellationToken).ConfigureAwait(false)
            : chapter.Title;

        var paragraphs = chapter.RawText.Split([ParagraphSeparator], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var total = paragraphs.Count(HasLetters);
        var done = 0;
        var translated = new string[paragraphs.Length];

        for (var i = 0; i < paragraphs.Length; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (!HasLetters(paragraphs[i]))
            {
                translated[i] = paragraphs[i];
                continue;
            }

            translated[i] = await TranslateParagraphAsync(paragraphs[i], sourceLanguage, targetLanguage, cancellationToken).ConfigureAwait(false);
            done++;
            onProgress?.Invoke(done, total);
        }

        return chapter with { Title = title, RawText = string.Join(ParagraphSeparator, translated) };
    }

    private async Task<string> TranslateParagraphAsync(string text, string sourceLanguage, string targetLanguage, CancellationToken cancellationToken)
    {
        var key = BuildCacheKey(text, sourceLanguage, targetLanguage);

        var cached = await _cache.TryGetAsync(key, cancellationToken).ConfigureAwait(false);
        if (cached is not null)
        {
            return cached;
        }

        var translated = (await _translator.TranslateAsync(text, sourceLanguage, targetLanguage, cancellationToken).ConfigureAwait(false)).Trim();
        await _cache.SaveAsync(key, translated, cancellationToken).ConfigureAwait(false);
        return translated;
    }

    private string BuildCacheKey(string text, string sourceLanguage, string targetLanguage)
    {
        var key = $"{text}|{_translator.ModelVersion}|{sourceLanguage}|{targetLanguage}";
        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(key)));
    }

    private static bool HasLetters(string text) => LetterRegex().IsMatch(text);

    [GeneratedRegex(@"\p{L}")]
    private static partial Regex LetterRegex();
}
