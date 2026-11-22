using System.Collections.ObjectModel;
using System.Diagnostics;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ConversorLibros.App.Services;
using ConversorLibros.Core.Abstractions;
using ConversorLibros.Core.Models;
using ConversorLibros.Core.Services;
using Microsoft.Maui.Devices;
using Microsoft.Maui.Storage;

namespace ConversorLibros.App.ViewModels;

public sealed record FormatOption(string Label, AudiobookFormat Format);

public sealed partial class NewConversionViewModel : ObservableObject
{
    private readonly AppSettings _settings;
    private readonly ConversionService _conversion;
    private readonly Stopwatch _clock = new();
    private readonly HashSet<int> _seenChapters = new();
    private Book? _book;
    private CancellationTokenSource? _cts;
    private int _selectedCount;

    public NewConversionViewModel(AppSettings settings, ConversionService conversion)
    {
        _settings = settings;
        _conversion = conversion;
        SelectedFormat = FormatOptions[0];
    }

    public ObservableCollection<ChapterItem> Chapters { get; } = new();

    public ObservableCollection<string> Log { get; } = new();

    public IReadOnlyList<FormatOption> FormatOptions { get; } =
    [
        new("Carpeta Opus (un .opus por capítulo)", AudiobookFormat.Opus),
        new("M4B (un único fichero)", AudiobookFormat.M4b),
    ];

    /// <summary>Fixed target language: the app produces Spanish audiobooks.</summary>
    public string TargetLanguage => "es";

    [ObservableProperty, NotifyCanExecuteChangedFor(nameof(StartCommand))]
    private string? epubPath;

    [ObservableProperty]
    private string bookTitle = "Ningún libro seleccionado";

    [ObservableProperty]
    private bool translate;

    [ObservableProperty]
    private string sourceLanguage = "en";

    [ObservableProperty]
    private FormatOption? selectedFormat;

    [ObservableProperty]
    private double speed = 1.0;

    [ObservableProperty, NotifyCanExecuteChangedFor(nameof(StartCommand))]
    private bool isRunning;

    [ObservableProperty]
    private string stage = "Inactivo";

    [ObservableProperty]
    private double progressFraction;

    [ObservableProperty]
    private string currentChapter = string.Empty;

    [ObservableProperty]
    private string timing = string.Empty;

    [ObservableProperty]
    private bool showLog;

    [RelayCommand]
    private async Task PickEpubAsync()
    {
        var file = await FilePicker.Default.PickAsync(new PickOptions
        {
            PickerTitle = "Selecciona un EPUB sin DRM",
            FileTypes = new FilePickerFileType(new Dictionary<DevicePlatform, IEnumerable<string>>
            {
                [DevicePlatform.WinUI] = new[] { ".epub" },
            }),
        });

        if (file is not null)
        {
            await LoadBookAsync(file.FullPath);
        }
    }

    private async Task LoadBookAsync(string path)
    {
        Book book;
        try
        {
            book = await new EpubBookSource().LoadAsync(path);
        }
        catch (Exception ex)
        {
            AddLog($"No se pudo abrir el EPUB: {ex.Message}");
            return;
        }

        _book = book;
        EpubPath = path;
        BookTitle = $"{book.Title} — {book.Author}";
        if (!string.IsNullOrWhiteSpace(book.Language))
        {
            SourceLanguage = book.Language;
        }

        Chapters.Clear();
        foreach (var chapter in book.Chapters)
        {
            var item = new ChapterItem(chapter.Index, chapter.Title) { IsSelected = true };
            item.PropertyChanged += (_, _) => StartCommand.NotifyCanExecuteChanged();
            Chapters.Add(item);
        }

        StartCommand.NotifyCanExecuteChanged();
        AddLog($"Libro cargado: {book.Chapters.Count} capítulo(s).");
    }

    [RelayCommand(CanExecute = nameof(CanStart))]
    private async Task StartAsync()
    {
        if (_book is null || EpubPath is null)
        {
            return;
        }

        var indexes = Chapters.Where(c => c.IsSelected).Select(c => c.Index).ToHashSet();
        _selectedCount = indexes.Count;
        _seenChapters.Clear();

        var request = new ConversionRequest(
            EpubPath,
            _book,
            indexes,
            (float)Speed,
            Translate ? SourceLanguage : null,
            SelectedFormat?.Format ?? AudiobookFormat.Opus);

        _cts = new CancellationTokenSource();
        var progress = new Progress<ConversionProgress>(OnProgress);
        IsRunning = true;
        ProgressFraction = 0;
        Stage = "Iniciando";
        _clock.Restart();
        AddLog("Conversión iniciada.");

        try
        {
            var result = await _conversion.RunAsync(request, _settings, progress, _cts.Token);
            if (result.Warning is not null)
            {
                AddLog(result.Warning);
            }

            if (result.OutputPath is not null)
            {
                ProgressFraction = 1;
                Stage = "Completado";
                AddLog($"Audiolibro generado: {result.OutputPath}");
            }
        }
        catch (OperationCanceledException)
        {
            Stage = "Cancelado";
            AddLog("Conversión cancelada.");
        }
        catch (Exception ex)
        {
            Stage = "Error";
            AddLog($"Error: {ex.Message}");
        }
        finally
        {
            _clock.Stop();
            Timing = string.Empty;
            IsRunning = false;
            _cts.Dispose();
            _cts = null;
        }
    }

    private bool CanStart() =>
        !IsRunning && EpubPath is not null && _book is not null && Chapters.Any(c => c.IsSelected);

    [RelayCommand]
    private void Cancel() => _cts?.Cancel();

    [RelayCommand]
    private void ToggleLog() => ShowLog = !ShowLog;

    private void OnProgress(ConversionProgress progress)
    {
        if (progress.ChapterIndex > 0)
        {
            _seenChapters.Add(progress.ChapterIndex);
        }

        Stage = progress.Stage switch
        {
            "Translating" => "Traduciendo",
            "Synthesizing" => "Sintetizando",
            "Assembled" => "Ensamblando",
            "Exporting" => "Exportando",
            _ => progress.Stage,
        };

        var chapterFraction = progress.TotalChunks > 0
            ? (double)progress.CompletedChunks / progress.TotalChunks
            : 0;
        var finishedChapters = Math.Max(0, _seenChapters.Count - 1) + chapterFraction;
        if (progress.Stage == "Exporting")
        {
            finishedChapters = _selectedCount;
        }

        var fraction = Math.Clamp(finishedChapters / Math.Max(1, _selectedCount), 0, 1);
        ProgressFraction = fraction;

        CurrentChapter = progress.ChapterIndex > 0
            ? $"Capítulo {progress.ChapterIndex} ({_seenChapters.Count}/{_selectedCount})"
            : string.Empty;

        var elapsed = _clock.Elapsed;
        var timing = $"Transcurrido {FormatTime(elapsed)}";
        if (fraction > 0.01)
        {
            var remaining = TimeSpan.FromTicks((long)(elapsed.Ticks * (1 - fraction) / fraction));
            timing += $" · Restante ~{FormatTime(remaining)}";
        }

        Timing = timing;
    }

    private void AddLog(string message) => Log.Add($"{DateTime.Now:HH:mm:ss}  {message}");

    private static string FormatTime(TimeSpan time) =>
        time.TotalHours >= 1 ? time.ToString(@"h\:mm\:ss") : time.ToString(@"m\:ss");
}
