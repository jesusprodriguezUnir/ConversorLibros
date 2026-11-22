using Microsoft.Maui.Storage;

namespace ConversorLibros.App.Services;

/// <summary>User settings persisted with MAUI Preferences (per Windows user).</summary>
public sealed class AppSettings
{
    public const string DefaultOllamaUrl = "http://localhost:11434";
    public const string DefaultOllamaModel = "qwen3:8b";

    private const string PiperModelDirKey = "piper_model_dir";
    private const string OutputRootKey = "output_root";
    private const string OllamaUrlKey = "ollama_url";
    private const string OllamaModelKey = "ollama_model";
    private const string FfmpegOverrideKey = "ffmpeg_override";

    public static string DefaultOutputRoot =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "ConversorLibros");

    public string PiperModelDir
    {
        get => Preferences.Default.Get(PiperModelDirKey, string.Empty);
        set => Preferences.Default.Set(PiperModelDirKey, value);
    }

    public string OutputRoot
    {
        get => Preferences.Default.Get(OutputRootKey, DefaultOutputRoot);
        set => Preferences.Default.Set(OutputRootKey, value);
    }

    public string OllamaUrl
    {
        get => Preferences.Default.Get(OllamaUrlKey, DefaultOllamaUrl);
        set => Preferences.Default.Set(OllamaUrlKey, value);
    }

    public string OllamaModel
    {
        get => Preferences.Default.Get(OllamaModelKey, DefaultOllamaModel);
        set => Preferences.Default.Set(OllamaModelKey, value);
    }

    /// <summary>Manual ffmpeg path. Empty means auto-detect (PATH, then WinGet).</summary>
    public string FfmpegOverride
    {
        get => Preferences.Default.Get(FfmpegOverrideKey, string.Empty);
        set => Preferences.Default.Set(FfmpegOverrideKey, value);
    }
}
