using ConversorLibros.Core.Services;
using Xunit;

namespace ConversorLibros.Core.Tests;

public class SpanishTextNormalizerTests
{
    private readonly SpanishTextNormalizer _sut = new();

    [Fact]
    public void Normalize_KeepsParagraphBreakBeforeDialogueDash()
    {
        var result = _sut.Normalize("fin.\n\n—Hola");
        Assert.Contains("fin.\n\n", result);
        Assert.DoesNotContain("fin.\n,", result);
    }

    [Fact]
    public void Normalize_JoinsHyphenatedLineBreaks()
    {
        var result = _sut.Normalize("es-\ntructura");
        Assert.Equal("estructura", result);
    }

    [Fact]
    public void Normalize_ExpandsAbbreviations()
    {
        var result = _sut.Normalize("El Sr. García y la Dra. López, pág. 12.");
        Assert.Equal("El señor García y la doctora López, página doce.", result);
    }

    [Fact]
    public void Normalize_ExpandsRomanNumeralsInChapterHeadings()
    {
        var result = _sut.Normalize("Capítulo IV");
        Assert.Equal("Capítulo cuatro", result);
    }

    [Fact]
    public void Normalize_ExpandsNumbersPercentagesAndDecimals()
    {
        var result = _sut.Normalize("Subió un 25% hasta 3,14 en 1998.");
        Assert.Equal("Subió un veinticinco por ciento hasta tres coma catorce en mil novecientos noventa y ocho.", result);
    }

    [Fact]
    public void Normalize_AppliesPronunciationDictionary()
    {
        var dict = new Dictionary<string, string> { ["Kvothe"] = "Kuoz" };
        var result = _sut.Normalize("Kvothe entró en la posada.", dict);
        Assert.Equal("Kuoz entró en la posada.", result);
    }
}
