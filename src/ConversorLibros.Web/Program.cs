using System.Text.Json;
using ConversorLibros.Web.Endpoints;
using ConversorLibros.Web.Services;

var builder = WebApplication.CreateBuilder(args);

if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("ASPNETCORE_URLS")) &&
    builder.Configuration["urls"] is null)
{
    builder.WebHost.UseUrls("http://localhost:5180");
}

builder.Services.ConfigureHttpJsonOptions(options =>
{
    options.SerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
});

builder.Services.AddSingleton<JsonSettingsStore>();
builder.Services.AddSingleton<UploadedBookStore>();
builder.Services.AddSingleton<IConversionRunner, CoreConversionRunner>();
builder.Services.AddSingleton<ConversionJobManager>();

var app = builder.Build();

app.UseDefaultFiles();
app.UseStaticFiles();

app.MapSettingsEndpoints();
app.MapFileSystemEndpoints();
app.MapLibraryEndpoints();
app.MapBookEndpoints();
app.MapConversionEndpoints();

app.MapFallback(async context =>
{
    if (context.Request.Path.StartsWithSegments("/api"))
    {
        context.Response.StatusCode = StatusCodes.Status404NotFound;
        return;
    }

    var indexPath = Path.Combine(app.Environment.WebRootPath ?? string.Empty, "index.html");
    if (File.Exists(indexPath))
    {
        context.Response.ContentType = "text/html; charset=utf-8";
        await context.Response.SendFileAsync(indexPath);
        return;
    }

    context.Response.StatusCode = StatusCodes.Status404NotFound;
    await context.Response.WriteAsync("Frontend not built yet. Run 'npm run build' in ClientApp.");
});

app.Run();

public partial class Program;
