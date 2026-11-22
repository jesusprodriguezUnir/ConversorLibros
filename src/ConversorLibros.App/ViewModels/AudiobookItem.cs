using CommunityToolkit.Mvvm.Input;
using Microsoft.Maui.Controls;

namespace ConversorLibros.App.ViewModels;

public sealed record AudiobookItem(
    string Title,
    string Author,
    string Details,
    ImageSource? Cover,
    IRelayCommand OpenCommand);
