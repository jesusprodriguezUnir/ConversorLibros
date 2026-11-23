using ConversorLibros.Core.Models;
using ConversorLibros.Core.Services;
using Xunit;

namespace ConversorLibros.Core.Tests;

public sealed class ReaderTextSegmenterTests
{
    private static readonly SpanishTextNormalizer Normalizer = new();

    private static string Normalize(string text) => Normalizer.Normalize(text);

    [Fact]
    public void SplitSentences_AbbreviationDoesNotSplit_AndKeepsOriginalText()
    {
        const string paragraph = "El Sr. López llegó a las 5 p. m. y se fue.";

        var sentences = ReaderTextSegmenter.SplitSentences(paragraph, Normalize);

        var single = Assert.Single(sentences);
        Assert.Equal(paragraph, single.Raw);
        Assert.Equal("El señor López llegó a las cinco p. m. y se fue.", single.Normalized);
    }

    [Fact]
    public void SplitSentences_SplitsOnSentenceEnd()
    {
        var sentences = ReaderTextSegmenter.SplitSentences("Hola, Dr. Pérez.   ¿Qué tal? ¡Bien!", Normalize);

        Assert.Equal(["Hola, Dr. Pérez.", "¿Qué tal?", "¡Bien!"], sentences.Select(s => s.Raw));
    }

    [Fact]
    public void SplitParagraphs_DropsEmptyAndWhitespaceParagraphs()
    {
        var paragraphs = ReaderTextSegmenter.SplitParagraphs("Uno.\n\n   \n\n\nDos.\r\n\r\n\t\r\n\r\nTres.");

        Assert.Equal(["Uno.", "Dos.", "Tres."], paragraphs);
        Assert.Empty(ReaderTextSegmenter.SplitParagraphs(" \n\n \t "));
    }

    [Theory]
    [InlineData("El Sr. García y la Dra. Ruiz llegaron. Eran las 5 y cuarto; ¿tarde? Sí… muy tarde.")]
    [InlineData("Subió un 25 % en 2020. Costó 3,14 euros. Capítulo IV del tomo.")]
    [InlineData("—Hola —dijo ella—. ¿Vienes? —No, gracias.")]
    [InlineData("Esta es una frase muy larga que el troceador tiene que partir por palabras porque supera con creces el límite. Y otra corta.")]
    public void SplitSentences_JoinedNormalizedText_MatchesChunkerOutput(string paragraph)
    {
        var normalized = Normalize(paragraph);
        var chunks = new SentenceChunker(maxChunkChars: 50).Chunk(new Chapter(0, "c", paragraph), normalized, "0", 1f, "m");

        var sentences = ReaderTextSegmenter.SplitSentences(paragraph, Normalize);

        Assert.Equal(string.Join(' ', chunks.Select(c => c.NormalizedText)), string.Join(' ', sentences.Select(s => s.Normalized)));
    }
}
