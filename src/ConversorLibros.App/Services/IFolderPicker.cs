namespace ConversorLibros.App.Services;

/// <summary>Platform folder selection. Returns null when the user cancels.</summary>
public interface IFolderPicker
{
    Task<string?> PickFolderAsync();
}
