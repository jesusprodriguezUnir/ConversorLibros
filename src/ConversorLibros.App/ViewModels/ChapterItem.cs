using CommunityToolkit.Mvvm.ComponentModel;

namespace ConversorLibros.App.ViewModels;

public sealed partial class ChapterItem : ObservableObject
{
    public ChapterItem(int index, string title)
    {
        Index = index;
        Title = title;
    }

    public int Index { get; }

    public string Title { get; }

    public string DisplayTitle => $"{Index}. {Title}";

    [ObservableProperty]
    private bool isSelected;
}
