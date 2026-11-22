using System.Collections.Concurrent;
using System.Diagnostics;
using System.Threading.Channels;
using ConversorLibros.Core.Abstractions;
using ConversorLibros.Core.Models;
using ConversorLibros.Web.Models;

namespace ConversorLibros.Web.Services;

public sealed record ConversionSseEvent(string EventName, ConversionStateDto State);

/// <summary>
/// Coordinates a single active conversion job and broadcasts live progress to SSE subscribers.
/// </summary>
public sealed class ConversionJobManager
{
    private readonly IConversionRunner _runner;
    private readonly JsonSettingsStore _settingsStore;
    private readonly object _gate = new();
    private readonly ConcurrentDictionary<Guid, Channel<ConversionSseEvent>> _subscribers = new();
    private readonly List<string> _logs = [];
    private readonly HashSet<int> _seenChapters = [];
    private readonly Stopwatch _clock = new();

    private CancellationTokenSource? _cts;
    private int _selectedCount;
    private string _status = "idle";
    private string _stage = "Inactivo";
    private double _progressFraction;
    private string _currentChapter = string.Empty;
    private string _timing = string.Empty;
    private string? _bookTitle;
    private string? _outputPath;
    private string? _warning;
    private string? _error;

    public ConversionJobManager(IConversionRunner runner, JsonSettingsStore settingsStore)
    {
        _runner = runner;
        _settingsStore = settingsStore;
    }

    public ConversionStateDto GetSnapshot()
    {
        lock (_gate)
        {
            return BuildSnapshotUnsafe();
        }
    }

    public (Guid Id, ChannelReader<ConversionSseEvent> Reader) Subscribe()
    {
        var id = Guid.NewGuid();
        var channel = Channel.CreateBounded<ConversionSseEvent>(new BoundedChannelOptions(64)
        {
            FullMode = BoundedChannelFullMode.DropOldest,
            SingleReader = true,
            SingleWriter = false,
        });

        _subscribers[id] = channel;
        return (id, channel.Reader);
    }

    public void Unsubscribe(Guid id)
    {
        if (_subscribers.TryRemove(id, out var channel))
        {
            channel.Writer.TryComplete();
        }
    }

    public bool TryStart(UploadedBook uploaded, StartConversionRequest request, out ConversionStateDto snapshot)
    {
        var chapterIndexes = request.ChapterIndexes.ToHashSet();
        var format = string.Equals(request.Format, "m4b", StringComparison.OrdinalIgnoreCase)
            ? AudiobookFormat.M4b
            : AudiobookFormat.Opus;

        CancellationTokenSource cts;
        WebSettings settings;

        lock (_gate)
        {
            if (_status == "running")
            {
                snapshot = BuildSnapshotUnsafe();
                return false;
            }

            _cts?.Dispose();
            cts = new CancellationTokenSource();
            _cts = cts;

            _selectedCount = chapterIndexes.Count;
            _seenChapters.Clear();
            _logs.Clear();
            _status = "running";
            _stage = "Iniciando";
            _progressFraction = 0;
            _currentChapter = string.Empty;
            _timing = string.Empty;
            _bookTitle = $"{uploaded.Book.Title} — {uploaded.Book.Author}";
            _outputPath = null;
            _warning = null;
            _error = null;
            _clock.Restart();

            AddLogUnsafe("Conversión iniciada.");
            snapshot = BuildSnapshotUnsafe();
            settings = _settingsStore.Get();
        }

        Broadcast("progress", snapshot);

        var jobRequest = new ConversionJobRequest(
            EpubPath: uploaded.EpubPath,
            Book: uploaded.Book,
            ChapterIndexes: chapterIndexes,
            Speed: Math.Clamp(request.Speed, 0.5f, 1.5f),
            SourceLanguage: request.Translate ? (string.IsNullOrWhiteSpace(request.SourceLanguage) ? "en" : request.SourceLanguage.Trim()) : null,
            Format: format);

        _ = Task.Run(() => ExecuteJobAsync(jobRequest, settings, cts));
        return true;
    }

    public bool Cancel()
    {
        lock (_gate)
        {
            if (_status != "running" || _cts is null)
            {
                return false;
            }

            _cts.Cancel();
            return true;
        }
    }

    private async Task ExecuteJobAsync(ConversionJobRequest jobRequest, WebSettings settings, CancellationTokenSource cts)
    {
        var progress = new DirectProgress(OnProgress);
        try
        {
            var result = await _runner.RunAsync(jobRequest, settings, progress, cts.Token);
            ConversionStateDto finalSnapshot;
            lock (_gate)
            {
                _clock.Stop();
                _timing = string.Empty;
                _warning = result.Warning;
                _outputPath = result.OutputPath;

                if (result.Warning is not null)
                {
                    AddLogUnsafe(result.Warning);
                }

                if (result.OutputPath is not null)
                {
                    _progressFraction = 1.0;
                    _stage = "Completado";
                    _status = "completed";
                    AddLogUnsafe($"Audiolibro generado: {result.OutputPath}");
                }
                else
                {
                    _stage = "Completado con aviso";
                    _status = "completed";
                }

                finalSnapshot = BuildSnapshotUnsafe();
            }

            Broadcast("completed", finalSnapshot);
        }
        catch (OperationCanceledException)
        {
            ConversionStateDto cancelledSnapshot;
            lock (_gate)
            {
                _clock.Stop();
                _timing = string.Empty;
                _status = "cancelled";
                _stage = "Cancelado";
                AddLogUnsafe("Conversión cancelada.");
                cancelledSnapshot = BuildSnapshotUnsafe();
            }

            Broadcast("cancelled", cancelledSnapshot);
        }
        catch (Exception ex)
        {
            ConversionStateDto errorSnapshot;
            lock (_gate)
            {
                _clock.Stop();
                _timing = string.Empty;
                _status = "error";
                _stage = "Error";
                _error = ex.Message;
                AddLogUnsafe($"Error: {ex.Message}");
                errorSnapshot = BuildSnapshotUnsafe();
            }

            Broadcast("error", errorSnapshot);
        }
        finally
        {
            lock (_gate)
            {
                if (ReferenceEquals(_cts, cts))
                {
                    _cts.Dispose();
                    _cts = null;
                }
            }
        }
    }

    private void OnProgress(ConversionProgress progress)
    {
        ConversionStateDto snapshot;
        lock (_gate)
        {
            if (_status != "running")
            {
                return;
            }

            if (progress.ChapterIndex > 0)
            {
                _seenChapters.Add(progress.ChapterIndex);
            }

            _stage = progress.Stage switch
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
            _progressFraction = fraction;

            _currentChapter = progress.ChapterIndex > 0
                ? $"Capítulo {progress.ChapterIndex} ({_seenChapters.Count}/{_selectedCount})"
                : string.Empty;

            var elapsed = _clock.Elapsed;
            var timing = $"Transcurrido {FormatTime(elapsed)}";
            if (fraction > 0.01)
            {
                var remaining = TimeSpan.FromTicks((long)(elapsed.Ticks * (1 - fraction) / fraction));
                timing += $" · Restante ~{FormatTime(remaining)}";
            }

            _timing = timing;
            snapshot = BuildSnapshotUnsafe();
        }

        Broadcast("progress", snapshot);
    }

    private void Broadcast(string eventName, ConversionStateDto snapshot)
    {
        var evt = new ConversionSseEvent(eventName, snapshot);
        foreach (var channel in _subscribers.Values)
        {
            channel.Writer.TryWrite(evt);
        }
    }

    private void AddLogUnsafe(string message) =>
        _logs.Add($"{DateTime.Now:HH:mm:ss}  {message}");

    private ConversionStateDto BuildSnapshotUnsafe() =>
        new(
            Status: _status,
            Stage: _stage,
            ProgressFraction: _progressFraction,
            CurrentChapter: _currentChapter,
            Timing: _timing,
            BookTitle: _bookTitle,
            OutputPath: _outputPath,
            Warning: _warning,
            Error: _error,
            Logs: _logs.ToArray());

    private static string FormatTime(TimeSpan time) =>
        time.TotalHours >= 1 ? time.ToString(@"h\:mm\:ss") : time.ToString(@"m\:ss");

    private sealed class DirectProgress(Action<ConversionProgress> handler) : IProgress<ConversionProgress>
    {
        public void Report(ConversionProgress value) => handler(value);
    }
}
