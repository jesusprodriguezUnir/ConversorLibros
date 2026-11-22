using ConversorLibros.Core.Services;
using Xunit;

namespace ConversorLibros.Core.Tests;

public class ChapterTextStatisticsTests
{
    [Fact]
    public void CountWords_IgnoresDialogueDashesAndPunctuationOnly()
    {
        Assert.Equal(3, ChapterTextStatistics.CountWords("— Hola —  mundo 42 ..."));
    }

    [Fact]
    public void CountWords_NullOrBlank_IsZero()
    {
        Assert.Equal(0, ChapterTextStatistics.CountWords(null));
        Assert.Equal(0, ChapterTextStatistics.CountWords("  \n\t "));
    }

    [Fact]
    public void EstimateDuration_UsesOneHundredFiftyWordsPerMinute()
    {
        Assert.Equal(TimeSpan.FromMinutes(1), ChapterTextStatistics.EstimateDuration(150));
        Assert.Equal(TimeSpan.FromMinutes(2.5), ChapterTextStatistics.EstimateDuration(375));
    }

    [Fact]
    public void Excerpt_ShortTextIsReturnedWithWhitespaceCollapsed()
    {
        Assert.Equal("Uno dos tres", ChapterTextStatistics.Excerpt("Uno\n\ndos   tres"));
    }

    [Fact]
    public void Excerpt_LongTextIsCutAtWordBoundaryWithEllipsis()
    {
        var text = string.Join(' ', Enumerable.Repeat("palabra", 100));

        var excerpt = ChapterTextStatistics.Excerpt(text, 50);

        Assert.True(excerpt.Length <= 50);
        Assert.Equal(string.Join(' ', Enumerable.Repeat("palabra", 6)) + "…", excerpt);
    }

    [Theory]
    [InlineData("Índice", 500, true)]
    [InlineData("Dedicatoria", 500, true)]
    [InlineData("Copyright", 500, true)]
    [InlineData("El índice de Marcus", 500, false)]
    [InlineData("Capítulo 1", 500, false)]
    [InlineData("Capítulo corto", 40, true)]
    public void IsLikelyFrontMatter_MatchesWholeTitleIgnoringAccentsOrFlagsShortChapters(string title, int words, bool expected)
    {
        Assert.Equal(expected, ChapterTextStatistics.IsLikelyFrontMatter(title, words));
    }
}
