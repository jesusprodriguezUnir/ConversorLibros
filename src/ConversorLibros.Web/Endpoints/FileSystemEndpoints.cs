using ConversorLibros.Web.Models;

namespace ConversorLibros.Web.Endpoints;

public static class FileSystemEndpoints
{
    public static IEndpointRouteBuilder MapFileSystemEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/fs/browse", (string? path) =>
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                var drives = DriveInfo.GetDrives()
                    .Where(d => d.IsReady)
                    .Select(d => new DirectoryEntryDto(d.Name.TrimEnd('\\'), d.RootDirectory.FullName))
                    .OrderBy(d => d.Name, StringComparer.OrdinalIgnoreCase)
                    .ToList();

                return Results.Ok(new FileSystemBrowseResponse(null, null, drives));
            }

            try
            {
                var fullPath = Path.GetFullPath(path.Trim());
                if (!Directory.Exists(fullPath))
                {
                    return Results.BadRequest(new ErrorResponse("La carpeta indicada no existe."));
                }

                var parent = Directory.GetParent(fullPath)?.FullName;
                var directories = new List<DirectoryEntryDto>();
                foreach (var dir in Directory.EnumerateDirectories(fullPath))
                {
                    try
                    {
                        var info = new DirectoryInfo(dir);
                        if ((info.Attributes & FileAttributes.System) != 0)
                        {
                            continue;
                        }

                        directories.Add(new DirectoryEntryDto(info.Name, info.FullName));
                    }
                    catch (UnauthorizedAccessException)
                    {
                        // Skip restricted folders cleanly.
                    }
                }

                directories.Sort((a, b) => StringComparer.OrdinalIgnoreCase.Compare(a.Name, b.Name));
                return Results.Ok(new FileSystemBrowseResponse(fullPath, parent, directories));
            }
            catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException or UnauthorizedAccessException or IOException)
            {
                return Results.BadRequest(new ErrorResponse($"No se pudo acceder a la carpeta: {ex.Message}"));
            }
        });

        return app;
    }
}
