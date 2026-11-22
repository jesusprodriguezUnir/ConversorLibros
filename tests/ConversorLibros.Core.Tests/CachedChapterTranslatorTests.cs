using ConversorLibros.Core.Abstractions;
using ConversorLibros.Core.Models;
using ConversorLibros.Core.Services;
using Xunit;

namespace ConversorLibros.Core.Tests;

public class CachedChapterTranslatorTests
{
    [Fact]
    public async Task TranslateChapterAsync_CacheHit_AvoidsTranslator()
    {
        var translator = new PrefixTranslator();
        var cache = new InMemoryTranslationCache();
        var sut = new CachedChapterTranslator(translator, cache);
        var chapter = new Chapter(1, "Intro", "Uno\n\nDos");

        await sut.TranslateChapterAsync(chapter, "en", "es");
        var callsAfterFirstRun = translator.Calls.Count;

        var second = await sut.TranslateChapterAsync(chapter, "en", "es");

        Assert.Equal(callsAfterFirstRun, translator.Calls.Count);
        Assert.Equal("ES(Uno)\n\nES(Dos)", second.RawText);
    }

    [Fact]
    public async Task TranslateChapterAsync_NonLetterParagraphs_AreCopiedUnchanged()
    {
        var translator = new PrefixTranslator();
        var sut = new CachedChapterTranslator(translator, new InMemoryTranslationCache());
        var chapter = new Chapter(2, "Capítulo", "1985\n\n***\n\nHola mundo");

        var result = await sut.TranslateChapterAsync(chapter, "en", "es");

        Assert.Equal("1985\n\n***\n\nES(Hola mundo)", result.RawText);
        Assert.DoesNotContain("1985", translator.Calls);
        Assert.DoesNotContain("***", translator.Calls);
    }

    [Fact]
    public async Task TranslateChapterAsync_PreservesParagraphOrderAndTranslatesTitle()
    {
        var sut = new CachedChapterTranslator(new PrefixTranslator(), new InMemoryTranslationCache());
        var chapter = new Chapter(3, "Chapter Three", "First\n\nSecond\n\nThird");

        var result = await sut.TranslateChapterAsync(chapter, "en", "es");

        Assert.Equal(3, result.Index);
        Assert.Equal("ES(Chapter Three)", result.Title);
        Assert.Equal("ES(First)\n\nES(Second)\n\nES(Third)", result.RawText);
    }

    [Fact]
    public async Task TranslateChapterAsync_ReportsProgressOverTranslatableParagraphs()
    {
        var sut = new CachedChapterTranslator(new PrefixTranslator(), new InMemoryTranslationCache());
        var chapter = new Chapter(4, "T", "A\n\n***\n\nB");
        var reports = new List<(int Done, int Total)>();

        await sut.TranslateChapterAsync(chapter, "en", "es", (done, total) => reports.Add((done, total)));

        Assert.Equal([(1, 2), (2, 2)], reports);
    }

    private sealed class PrefixTranslator : ITranslator
    {
        public List<string> Calls { get; } = [];
        public string ModelVersion => "fake-model|v1";

        public Task<string> TranslateAsync(string text, string sourceLanguage, string targetLanguage, CancellationToken cancellationToken = default)
        {
            Calls.Add(text);
            return Task.FromResult($"ES({text})");
        }
    }

    private sealed class InMemoryTranslationCache : ITranslationCache
    {
        private readonly Dictionary<string, string> _entries = [];

        public Task<string?> TryGetAsync(string key, CancellationToken cancellationToken = default) =>
            Task.FromResult(_entries.TryGetValue(key, out var value) ? value : null);

        public Task SaveAsync(string key, string translatedText, CancellationToken cancellationToken = default)
        {
            _entries[key] = translatedText;
            return Task.CompletedTask;
        }
    }
}
