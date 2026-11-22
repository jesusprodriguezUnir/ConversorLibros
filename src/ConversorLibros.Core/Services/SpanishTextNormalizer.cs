using System.Text;
using System.Text.RegularExpressions;
using ConversorLibros.Core.Abstractions;

namespace ConversorLibros.Core.Services;

/// <summary>
/// Normalizes Spanish (es-ES) text for natural TTS synthesis:
/// hyphenation, dialogue dashes, abbreviations, Roman numerals, numbers, and custom pronunciation.
/// </summary>
public sealed partial class SpanishTextNormalizer : ITextNormalizer
{
    private static readonly Dictionary<string, string> Abbreviations = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Sr."] = "señor",
        ["Sra."] = "señora",
        ["Srta."] = "señorita",
        ["Dr."] = "doctor",
        ["Dra."] = "doctora",
        ["Ud."] = "usted",
        ["Uds."] = "ustedes",
        ["Vd."] = "usted",
        ["Vds."] = "ustedes",
        ["pág."] = "página",
        ["págs."] = "páginas",
        ["cap."] = "capítulo",
        ["núm."] = "número",
        ["etc."] = "etcétera",
        ["aprox."] = "aproximadamente",
        ["ej."] = "ejemplo"
    };

    private static readonly string[] Units =
    [
        "cero", "uno", "dos", "tres", "cuatro", "cinco", "seis", "siete", "ocho", "nueve",
        "diez", "once", "doce", "trece", "catorce", "quince", "dieciséis", "diecisiete", "dieciocho", "diecinueve",
        "veinte", "veintiuno", "veintidós", "veintitrés", "veinticuatro", "veinticinco", "veintiséis", "veintisiete", "veintiocho", "veintinueve"
    ];

    private static readonly string[] Tens =
    [
        "", "", "veinte", "treinta", "cuarenta", "cincuenta", "sesenta", "setenta", "ochenta", "noventa"
    ];

    private static readonly string[] Hundreds =
    [
        "", "ciento", "doscientos", "trescientos", "cuatrocientos", "quinientos",
        "seiscientos", "setecientos", "ochocientos", "novecientos"
    ];

    public string Normalize(string rawText, IReadOnlyDictionary<string, string>? pronunciationDictionary = null)
    {
        if (string.IsNullOrWhiteSpace(rawText))
        {
            return string.Empty;
        }

        var text = rawText.Replace("\r\n", "\n").Replace('\r', '\n');

        // Join words split by end-of-line hyphenation (e.g. "pala-\nbra" -> "palabra")
        text = HyphenationRegex().Replace(text, "$1$2");

        // Normalize Latin quotes and dialogue dashes into TTS-friendly punctuation
        text = text.Replace('«', '"').Replace('»', '"');
        text = DialogueDashRegex().Replace(text, ", ");
        text = text.Replace("—", ", ");

        // Expand common Spanish abbreviations
        text = AbbreviationRegex().Replace(text, match =>
            Abbreviations.TryGetValue(match.Value, out var expanded) ? expanded : match.Value);

        // Expand Roman numerals in chapter/part headings (e.g. "Capítulo IV" -> "Capítulo cuatro")
        text = RomanChapterRegex().Replace(text, match =>
        {
            var prefix = match.Groups[1].Value;
            var roman = match.Groups[2].Value;
            var value = ParseRoman(roman);
            return value > 0 ? $"{prefix} {NumberToSpanishWords(value)}" : match.Value;
        });

        // Expand percentages (e.g. "25%" -> "veinticinco por ciento")
        text = PercentageRegex().Replace(text, match =>
        {
            var raw = match.Groups[1].Value.Replace(',', '.');
            return decimal.TryParse(raw, System.Globalization.NumberStyles.Number, System.Globalization.CultureInfo.InvariantCulture, out var val)
                ? $"{DecimalToSpanishWords(val)} por ciento"
                : match.Value;
        });

        // Expand decimal numbers (e.g. "3,14" -> "tres coma catorce")
        text = DecimalRegex().Replace(text, match =>
        {
            var intPart = long.Parse(match.Groups[1].Value);
            var fracPart = match.Groups[2].Value;
            return $"{NumberToSpanishWords(intPart)} coma {NumberToSpanishWords(long.Parse(fracPart))}";
        });

        // Expand standalone integers
        text = IntegerRegex().Replace(text, match =>
            long.TryParse(match.Value, out var n) && n <= 999_999_999
                ? NumberToSpanishWords(n)
                : match.Value);

        // Apply custom pronunciation dictionary
        if (pronunciationDictionary is { Count: > 0 })
        {
            foreach (var (term, replacement) in pronunciationDictionary)
            {
                if (!string.IsNullOrWhiteSpace(term))
                {
                    text = Regex.Replace(
                        text,
                        $@"\b{Regex.Escape(term)}\b",
                        replacement,
                        RegexOptions.CultureInvariant);
                }
            }
        }

        // Collapse excessive whitespace while preserving paragraph breaks
        text = MultiSpaceRegex().Replace(text, " ");
        text = MultiNewlineRegex().Replace(text, "\n\n");

        return text.Trim();
    }

    public static string NumberToSpanishWords(long number)
    {
        if (number < 0)
        {
            return $"menos {NumberToSpanishWords(-number)}";
        }

        if (number < 30)
        {
            return Units[number];
        }

        if (number < 100)
        {
            var t = number / 10;
            var u = number % 10;
            return u == 0 ? Tens[t] : $"{Tens[t]} y {Units[u]}";
        }

        if (number == 100)
        {
            return "cien";
        }

        if (number < 1000)
        {
            var h = number / 100;
            var rem = number % 100;
            return rem == 0 ? Hundreds[h] : $"{Hundreds[h]} {NumberToSpanishWords(rem)}";
        }

        if (number < 1_000_000)
        {
            var thousands = number / 1000;
            var rem = number % 1000;
            var prefix = thousands == 1 ? "mil" : $"{NumberToSpanishWords(thousands)} mil";
            return rem == 0 ? prefix : $"{prefix} {NumberToSpanishWords(rem)}";
        }

        var millions = number / 1_000_000;
        var mRem = number % 1_000_000;
        var mPrefix = millions == 1 ? "un millón" : $"{NumberToSpanishWords(millions)} millones";
        return mRem == 0 ? mPrefix : $"{mPrefix} {NumberToSpanishWords(mRem)}";
    }

    private static string DecimalToSpanishWords(decimal value)
    {
        var intPart = (long)Math.Truncate(value);
        var frac = value - intPart;
        if (frac == 0)
        {
            return NumberToSpanishWords(intPart);
        }

        var fracStr = value.ToString(System.Globalization.CultureInfo.InvariantCulture).Split('.')[1];
        return $"{NumberToSpanishWords(intPart)} coma {NumberToSpanishWords(long.Parse(fracStr))}";
    }

    private static int ParseRoman(string roman)
    {
        var map = new Dictionary<char, int>
        {
            ['I'] = 1, ['V'] = 5, ['X'] = 10, ['L'] = 50, ['C'] = 100, ['D'] = 500, ['M'] = 1000
        };

        var total = 0;
        for (var i = 0; i < roman.Length; i++)
        {
            if (!map.TryGetValue(char.ToUpperInvariant(roman[i]), out var current))
            {
                return 0;
            }

            var next = i + 1 < roman.Length && map.TryGetValue(char.ToUpperInvariant(roman[i + 1]), out var n) ? n : 0;
            total += current < next ? -current : current;
        }

        return total;
    }

    [GeneratedRegex(@"([a-zA-ZáéíóúüñÁÉÍÓÚÜÑ])-\n([a-zA-ZáéíóúüñÁÉÍÓÚÜÑ])")]
    private static partial Regex HyphenationRegex();

    [GeneratedRegex(@"(?<=^|\n)[ \t]*—[ \t]*")]
    private static partial Regex DialogueDashRegex();

    [GeneratedRegex(@"\b(?:Sr|Sra|Srta|Dr|Dra|Ud|Uds|Vd|Vds|pág|págs|cap|núm|etc|aprox|ej)\.", RegexOptions.IgnoreCase)]
    private static partial Regex AbbreviationRegex();

    [GeneratedRegex(@"\b(Capítulo|Cap\.|Parte|Libro|Tomo)\s+([IVXLCDM]+)\b", RegexOptions.IgnoreCase)]
    private static partial Regex RomanChapterRegex();

    [GeneratedRegex(@"(\d+(?:[.,]\d+)?)\s*%")]
    private static partial Regex PercentageRegex();

    [GeneratedRegex(@"\b(\d+),(\d+)\b")]
    private static partial Regex DecimalRegex();

    [GeneratedRegex(@"\b\d+\b")]
    private static partial Regex IntegerRegex();

    [GeneratedRegex(@"[ \t]{2,}")]
    private static partial Regex MultiSpaceRegex();

    [GeneratedRegex(@"\n{3,}")]
    private static partial Regex MultiNewlineRegex();
}
