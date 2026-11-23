using System.Globalization;
using ConversorLibros.Core.Models;
using ConversorLibros.Core.Services;
using Xunit;

namespace ConversorLibros.Core.Tests;

/// <summary>
/// Characterization tests: pin the current <see cref="SentenceChunker"/> output (text, order and
/// <see cref="TextChunk.ContentHash"/>) so the read-along work cannot change the chunk cache keys.
/// </summary>
public class ChunkHashCharacterizationTests
{
    private static readonly Chapter Ch = new(1, "T", "unused");

    private static string[] Run(string text, int max = 350)
    {
        // The chunker formats speed with the current culture ("1.00" vs "1,00"); pin invariant so the test is machine-independent.
        var previous = CultureInfo.CurrentCulture;
        CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
        try
        {
            return new SentenceChunker(max).Chunk(Ch, text, "0", 1.0f, "model.onnx")
                .Select(c => $"{c.Order}|{c.NormalizedText}|{c.ContentHash}")
                .ToArray();
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
        }
    }

    [Fact]
    public void Abbreviations_HashesAreStable() =>
        Assert.Equal(
            [
                "0|Hola Señor Pérez. Todo al cien por ciento.|93e4f0405521bd31476dc243e3c2498d79053af1b9a8e0f621f415ba28c22a0c",
                "1|Doctora López dijo que sí.|c991043fe4b28b4dae8f4a213dfc0f2041754b18a04e09ac810ae26ce38b5c20"
            ],
            Run("Hola Señor Pérez. Todo al cien por ciento.\n\nDoctora López dijo que sí."));

    [Fact]
    public void Numbers_HashesAreStable() =>
        Assert.Equal(
            [
                "0|Tengo veintitrés años y 3,5 euros. Capítulo cuarto.|2ec0c31ed1afb9417bf62a3f8895af247ee2047d8479ad83e2f120425d9d2b26",
                "1|El número uno.|8f0d28afd6d7a678f6965753101231c0bb6cf794bd7b44fa7d02332f29bf9c30"
            ],
            Run("Tengo veintitrés años y 3,5 euros. Capítulo cuarto.\n\nEl número uno."));

    [Fact]
    public void LongSentence_HashesAreStable() =>
        Assert.Equal(
            [
                "0|uno dos tres cuatro cinco seis siete ocho nueve diez once|1aa56f21ad95a3a25bc9678077f77ae4257d27b6874dadfc8be3e13a2fd9ebc4",
                "1|doce trece catorce quince dieciséis diecisiete|bd233cf467ad736131fe0c7f672d79dfcb4e999f39f1423e0a2dc89dc0644515"
            ],
            Run("uno dos tres cuatro cinco seis siete ocho nueve diez once doce trece catorce quince dieciséis diecisiete", 60));

    [Fact]
    public void Dialogue_HashesAreStable() =>
        Assert.Equal(
            [
                "0|— Hola, ¿cómo estás? — preguntó. — Bien, gracias… respondió él.|273c98ab89ff641511a504773938e55699d525bae0436d3e109c95ab99f241c0",
                "1|— Me alegro.|7958a0c87fc16a9d82ac0f0a960bd66a8bb7d4b7e5548d6bc167f4a470772896"
            ],
            Run("— Hola, ¿cómo estás? — preguntó. — Bien, gracias… respondió él.\n\n— Me alegro."));

    [Fact]
    public void SameInputTwice_ProducesIdenticalHashes() =>
        Assert.Equal(Run("Una frase. Otra frase."), Run("Una frase. Otra frase."));
}
