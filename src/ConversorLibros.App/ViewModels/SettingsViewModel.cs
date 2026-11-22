using System.Text.Json;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ConversorLibros.App.Services;

namespace ConversorLibros.App.ViewModels;

public sealed partial class SettingsViewModel : ObservableObject
{
    private readonly AppSettings _settings;
    private readonly IFolderPicker _folderPicker;

    public SettingsViewModel(AppSettings settings, IFolderPicker folderPicker)
    {
        _settings = settings;
        _folderPicker = folderPicker;

        PiperModelDir = settings.PiperModelDir;
        OutputRoot = settings.OutputRoot;
        OllamaUrl = settings.OllamaUrl;
        OllamaModel = settings.OllamaModel;
        FfmpegOverride = settings.FfmpegOverride;
        RefreshFfmpegStatus();
    }

    [ObservableProperty]
    private string piperModelDir = string.Empty;

    [ObservableProperty]
    private string outputRoot = string.Empty;

    [ObservableProperty]
    private string ollamaUrl = string.Empty;

    [ObservableProperty]
    private string ollamaModel = string.Empty;

    [ObservableProperty]
    private string ffmpegOverride = string.Empty;

    [ObservableProperty]
    private string ffmpegStatus = string.Empty;

    [ObservableProperty]
    private string connectionStatus = string.Empty;

    [ObservableProperty, NotifyCanExecuteChangedFor(nameof(TestConnectionCommand))]
    private bool isTesting;

    [ObservableProperty]
    private string saveStatus = string.Empty;

    [RelayCommand]
    private async Task PickModelDirAsync()
    {
        var folder = await _folderPicker.PickFolderAsync();
        if (folder is not null)
        {
            PiperModelDir = NormalizeModelDir(folder);
        }
    }

    [RelayCommand]
    private async Task PickOutputRootAsync()
    {
        var folder = await _folderPicker.PickFolderAsync();
        if (folder is not null)
        {
            OutputRoot = folder;
        }
    }

    [RelayCommand]
    private void Save()
    {
        PiperModelDir = NormalizeModelDir(PiperModelDir.Trim());
        _settings.PiperModelDir = PiperModelDir;
        _settings.OutputRoot = OutputRoot.Trim();
        _settings.OllamaUrl = OllamaUrl.Trim();
        _settings.OllamaModel = OllamaModel.Trim();
        _settings.FfmpegOverride = FfmpegOverride.Trim();
        RefreshFfmpegStatus();

        var modelProblem = DescribeModelDirProblem(PiperModelDir);
        SaveStatus = modelProblem is null
            ? "Ajustes guardados."
            : $"Ajustes guardados, pero la carpeta del modelo no es válida: {modelProblem}";
    }

    /// <summary>
    /// Maps a subfolder of a Piper model (e.g. espeak-ng-data) back to the model folder that holds the .onnx.
    /// </summary>
    private static string NormalizeModelDir(string dir)
    {
        if (string.IsNullOrWhiteSpace(dir) || !Directory.Exists(dir) || HasOnnx(dir))
        {
            return dir;
        }

        var parent = Directory.GetParent(dir)?.FullName;
        return parent is not null && HasOnnx(parent) ? parent : dir;
    }

    private static bool HasOnnx(string dir) => Directory.EnumerateFiles(dir, "*.onnx").Any();

    private static string? DescribeModelDirProblem(string dir)
    {
        if (string.IsNullOrWhiteSpace(dir))
        {
            return "está vacía.";
        }

        if (!Directory.Exists(dir))
        {
            return "no existe.";
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

        return missing.Count == 0 ? null : $"falta {string.Join(", ", missing)}.";
    }

    [RelayCommand(CanExecute = nameof(CanTestConnection))]
    private async Task TestConnectionAsync()
    {
        IsTesting = true;
        ConnectionStatus = "Probando conexión…";
        try
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
            using var response = await http.GetAsync($"{OllamaUrl.TrimEnd('/')}/api/tags");
            response.EnsureSuccessStatusCode();

            await using var stream = await response.Content.ReadAsStreamAsync();
            using var document = await JsonDocument.ParseAsync(stream);
            var models = document.RootElement.GetProperty("models")
                .EnumerateArray()
                .Select(m => m.GetProperty("name").GetString())
                .ToList();

            ConnectionStatus = models.Contains(OllamaModel)
                ? $"Conectado. El modelo '{OllamaModel}' está disponible."
                : $"Conectado, pero el modelo '{OllamaModel}' no está instalado.";
        }
        catch (TaskCanceledException)
        {
            ConnectionStatus = "Tiempo de espera agotado (10 s).";
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException or KeyNotFoundException or InvalidOperationException)
        {
            ConnectionStatus = $"No se pudo conectar: {ex.Message}";
        }
        finally
        {
            IsTesting = false;
        }
    }

    private bool CanTestConnection() => !IsTesting;

    private void RefreshFfmpegStatus()
    {
        var resolved = ConversionService.ResolveFfmpeg(FfmpegOverride);
        if (resolved is null)
        {
            FfmpegStatus = "ffmpeg no encontrado. Instálalo con 'winget install Gyan.FFmpeg' o indica su ruta.";
            return;
        }

        var manualMissing = !string.IsNullOrWhiteSpace(FfmpegOverride) && !File.Exists(FfmpegOverride);
        FfmpegStatus = manualMissing
            ? $"La ruta manual no existe; usando: {resolved}"
            : $"Usando: {resolved}";
    }
}
