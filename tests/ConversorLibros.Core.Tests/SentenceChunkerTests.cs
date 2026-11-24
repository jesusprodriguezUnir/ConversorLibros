using System.Globalization;
using ConversorLibros.Core.Models;
using ConversorLibros.Core.Services;
using Xunit;

namespace ConversorLibros.Core.Tests;

public class SentenceChunkerTests
{
    [Fact]
    public void Chunk_SplitsLongTextUnderMaxChars_AndProducesDeterministicHashes()
    {
        var chunker = new SentenceChunker(maxChunkChars: 60);
        var chapter = new Chapter(1, "Cap 1", "raw");
        var text = "Primera oración corta. Segunda oración que también es corta. Tercera oración final.";

        var firstRun = chunker.Chunk(chapter, text, "ef_dora", 1.0f, "v1");
        var secondRun = chunker.Chunk(chapter, text, "ef_dora", 1.0f, "v1");

        Assert.True(firstRun.Count >= 2);
        Assert.All(firstRun, c => Assert.True(c.NormalizedText.Length <= 60));
        Assert.Equal(firstRun.Select(c => c.ContentHash), secondRun.Select(c => c.ContentHash));
    }

    [Fact]
    public void Chunk_ProducesSameHashAcrossDifferentCultures()
    {
        var chunker = new SentenceChunker();
        var chapter = new Chapter(1, "Cap 1", "raw");
        var text = "Una oración sencilla para probar el hash.";
        var originalCulture = CultureInfo.CurrentCulture;

        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("es-ES");
            var esChunks = chunker.Chunk(chapter, text, "ef_dora", 1.25f, "v1");

            CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
            var invariantChunks = chunker.Chunk(chapter, text, "ef_dora", 1.25f, "v1");

            Assert.Single(esChunks);
            Assert.Single(invariantChunks);
            Assert.Equal(invariantChunks[0].ContentHash, esChunks[0].ContentHash);
        }
        finally
        {
            CultureInfo.CurrentCulture = originalCulture;
        }
    }
}
