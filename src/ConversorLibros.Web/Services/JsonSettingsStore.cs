using System.Text.Json;
using ConversorLibros.Core.Services;
using ConversorLibros.Web.Models;

namespace ConversorLibros.Web.Services;

/// <summary>
/// Persists user settings to a local JSON file and validates external tool paths.
/// </summary>
public sealed class JsonSettingsStore
{
    public const string DefaultOllamaUrl = "http://localhost:11434";
    public const string DefaultOllamaModel = "qwen3:8b";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
    };

    private readonly string _settingsFilePath;
    private readonly PiperModelLocator? _locator;
    private readonly object _gate = new();

    /// <param name="locator">Null disables Piper auto-detection (used by tests to stay hermetic).</param>
    public JsonSettingsStore(string? settingsFilePath = null, PiperModelLocator? locator = null)
    {
        _settingsFilePath = settingsFilePath ?? GetDefaultSettingsFilePath();
        _locator = locator;
    }

    public static string DefaultOutputRoot =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "ConversorLibros");

    public static string GetDefaultSettingsFilePath() =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "ConversorLibros",
            "settings.json");

    public WebSettings Get()
    {
        lock (_gate)
        {
            if (!File.Exists(_settingsFilePath))
            {
                return CreateDefaults();
            }

            try
            {
                var json = File.ReadAllText(_settingsFilePath);
                var parsed = JsonSerializer.Deserialize<WebSettings>(json, JsonOptions);
                if (parsed is null)
                {
                    return CreateDefaults();
                }

                return new WebSettings(
                    PiperModelDir: parsed.PiperModelDir ?? string.Empty,
                    OutputRoot: string.IsNullOrWhiteSpace(parsed.OutputRoot) ? DefaultOutputRoot : parsed.OutputRoot,
                    OllamaUrl: string.IsNullOrWhiteSpace(parsed.OllamaUrl) ? DefaultOllamaUrl : parsed.OllamaUrl,
                    OllamaModel: string.IsNullOrWhiteSpace(parsed.OllamaModel) ? DefaultOllamaModel : parsed.OllamaModel,
                    FfmpegOverride: parsed.FfmpegOverride ?? string.Empty,
                    DefaultSpeed: NormalizeSpeed(parsed.DefaultSpeed),
                    DefaultFormat: NormalizeFormat(parsed.DefaultFormat));
            }
            catch (Exception ex) when (ex is JsonException or IOException)
            {
                return CreateDefaults();
            }
        }
    }

    public WebSettings Save(WebSettings input)
    {
        var normalized = new WebSettings(
            PiperModelDir: NormalizeModelDir((input.PiperModelDir ?? string.Empty).Trim()),
            OutputRoot: string.IsNullOrWhiteSpace(input.OutputRoot) ? DefaultOutputRoot : input.OutputRoot.Trim(),
            OllamaUrl: string.IsNullOrWhiteSpace(input.OllamaUrl) ? DefaultOllamaUrl : input.OllamaUrl.Trim(),
            OllamaModel: string.IsNullOrWhiteSpace(input.OllamaModel) ? DefaultOllamaModel : input.OllamaModel.Trim(),
            FfmpegOverride: (input.FfmpegOverride ?? string.Empty).Trim(),
            DefaultSpeed: NormalizeSpeed(input.DefaultSpeed),
            DefaultFormat: NormalizeFormat(input.DefaultFormat));

        lock (_gate)
        {
            var dir = Path.GetDirectoryName(_settingsFilePath);
            if (!string.IsNullOrWhiteSpace(dir))
            {
                Directory.CreateDirectory(dir);
            }

            var tempPath = _settingsFilePath + ".tmp";
            File.WriteAllText(tempPath, JsonSerializer.Serialize(normalized, JsonOptions));
            File.Move(tempPath, _settingsFilePath, overwrite: true);
        }

        return normalized;
    }

    public SettingsResponse GetResponse()
    {
        var settings = Get();
        return BuildResponse(settings);
    }

    public SettingsResponse BuildResponse(WebSettings settings)
    {
        var piper = _locator?.Resolve(settings.PiperModelDir)
            ?? new PiperModelResolution(
                settings.PiperModelDir,
                IsAutoDetected: false,
                ValidatePiper(settings.PiperModelDir));

        return new SettingsResponse(
            settings,
            piper.Status,
            ResolveFfmpeg(settings.FfmpegOverride),
            piper.EffectiveDir,
            piper.IsAutoDetected);
    }

    /// <summary>Keeps the default speed in the range the conversion job accepts; missing or invalid values become 1.0.</summary>
    public static float NormalizeSpeed(float speed) =>
        float.IsNaN(speed) || speed <= 0f ? 1.0f : Math.Clamp(speed, 0.5f, 1.5f);

    public static string NormalizeFormat(string? format) =>
        string.Equals(format?.Trim(), "m4b", StringComparison.OrdinalIgnoreCase) ? "m4b" : "opus";

    /// <summary>
    /// Maps a subfolder of a Piper model (e.g. espeak-ng-data) back to the parent folder that holds the .onnx.
    /// </summary>
    public static string NormalizeModelDir(string dir)
    {
        if (string.IsNullOrWhiteSpace(dir) || !Directory.Exists(dir) || HasOnnx(dir))
        {
            return dir;
        }

        var parent = Directory.GetParent(dir)?.FullName;
        return parent is not null && HasOnnx(parent) ? parent : dir;
    }

    public static PiperValidationStatus ValidatePiper(string dir)
    {
        if (string.IsNullOrWhiteSpace(dir))
        {
            return new PiperValidationStatus(false, "La carpeta del modelo Piper está vacía.");
        }

        if (!Directory.Exists(dir))
        {
            return new PiperValidationStatus(false, "La carpeta indicada no existe.");
        }

        var missing = new List<string>();
        if (!HasOnnx(dir))
        {
            missing.Add("un fichero .onnx");
        }

        if (!File.Exists(Path.Combine(dir, "tokens.txt")))
        {
            missing.Add("tokens.txt");
        }

        if (!Directory.Exists(Path.Combine(dir, "espeak-ng-data")))
        {
            missing.Add("la carpeta espeak-ng-data");
        }

        return missing.Count == 0
            ? new PiperValidationStatus(true, "Modelo Piper listo para sintetizar.")
            : new PiperValidationStatus(false, $"Falta {string.Join(", ", missing)}.");
    }

    public static FfmpegStatus ResolveFfmpeg(string? overridePath)
    {
        var hasManual = !string.IsNullOrWhiteSpace(overridePath);
        if (hasManual && File.Exists(overridePath))
        {
            return new FfmpegStatus(true, overridePath, $"Usando: {overridePath}");
        }

        var located = FfmpegLocator.Locate();
        if (located is null)
        {
            return new FfmpegStatus(
                false,
                null,
                "ffmpeg no encontrado. Instálalo con 'winget install Gyan.FFmpeg' o indica su ruta.");
        }

        var message = hasManual
            ? $"La ruta manual no existe; usando: {located}"
            : $"Usando: {located}";

        return new FfmpegStatus(true, located, message);
    }

    private static bool HasOnnx(string dir) =>
        Directory.EnumerateFiles(dir, "*.onnx").Any();

    private static WebSettings CreateDefaults() =>
        new(
            PiperModelDir: string.Empty,
            OutputRoot: DefaultOutputRoot,
            OllamaUrl: DefaultOllamaUrl,
            OllamaModel: DefaultOllamaModel,
            FfmpegOverride: string.Empty,
            DefaultSpeed: 1.0f,
            DefaultFormat: "opus");
}
