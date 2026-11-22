using System.Collections.ObjectModel;
using System.Diagnostics;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ConversorLibros.App.Services;
using ConversorLibros.Core.Abstractions;
using ConversorLibros.Core.Services;
using Microsoft.Maui.Controls;

namespace ConversorLibros.App.ViewModels;

public sealed partial class LibraryViewModel : ObservableObject
{
    private readonly AppSettings _settings;

    public LibraryViewModel(AppSettings settings)
    {
        _settings = settings;
    }

    public ObservableCollection<AudiobookItem> Books { get; } = new();

    [ObservableProperty]
    private bool isEmpty = true;

    [RelayCommand]
    private void Refresh()
    {
        Books.Clear();
        if (Directory.Exists(_settings.OutputRoot))
        {
            foreach (var summary in AudiobookLibrary.Scan(_settings.OutputRoot))
            {
                Books.Add(ToItem(summary));
            }
        }

        IsEmpty = Books.Count == 0;
    }

    private static AudiobookItem ToItem(AudiobookSummary summary)
    {
        var details = summary.Format == AudiobookFormat.Opus
            ? $"{summary.ChapterCount} capítulo(s)"
            : "Fichero M4B";
        if (summary.Duration is { } duration)
        {
            details += " · " + FormatDuration(duration);
        }

        var cover = summary.CoverPath is null ? null : ImageSource.FromFile(summary.CoverPath);
        var path = summary.Path;
        return new AudiobookItem(
            summary.Title,
            summary.Author ?? "Autor desconocido",
            details,
            cover,
            new RelayCommand(() => OpenInExplorer(path)));
    }

    private static void OpenInExplorer(string path)
    {
        var startInfo = Directory.Exists(path)
            ? new ProcessStartInfo("explorer.exe") { ArgumentList = { path } }
            : new ProcessStartInfo("explorer.exe") { ArgumentList = { "/select," + path } };
        Process.Start(startInfo);
    }

    private static string FormatDuration(TimeSpan duration) =>
        duration.TotalHours >= 1 ? duration.ToString(@"h\:mm\:ss") : duration.ToString(@"m\:ss");
}
