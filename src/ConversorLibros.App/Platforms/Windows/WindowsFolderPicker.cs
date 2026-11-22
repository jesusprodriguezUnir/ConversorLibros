using Windows.Storage.Pickers;
using WinRT.Interop;

namespace ConversorLibros.App.Services;

/// <summary>WinRT folder picker. Pickers must be bound to the owning window handle.</summary>
internal sealed class WindowsFolderPicker : IFolderPicker
{
    public async Task<string?> PickFolderAsync()
    {
        var window = Microsoft.Maui.Controls.Application.Current?.Windows.FirstOrDefault()
            ?? throw new InvalidOperationException("La ventana principal todavía no está disponible.");
        var nativeWindow = (Microsoft.UI.Xaml.Window)window.Handler!.PlatformView!;

        var picker = new FolderPicker { SuggestedStartLocation = PickerLocationId.DocumentsLibrary };
        picker.FileTypeFilter.Add("*");
        InitializeWithWindow.Initialize(picker, WindowNative.GetWindowHandle(nativeWindow));

        var folder = await picker.PickSingleFolderAsync();
        return folder?.Path;
    }
}
