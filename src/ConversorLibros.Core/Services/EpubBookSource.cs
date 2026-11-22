using System.Text;
using System.Text.RegularExpressions;
using AngleSharp;
using AngleSharp.Dom;
using ConversorLibros.Core.Abstractions;
using ConversorLibros.Core.Models;
using VersOne.Epub;
using VersOne.Epub.Options;

namespace ConversorLibros.Core.Services;

/// <summary>
/// Extracts structured chapters and metadata from a DRM-free EPUB file.
/// </summary>
public sealed partial class EpubBookSource : IBookSource
{
    private static readonly HashSet<string> BoilerplateTitles = new(StringComparer.OrdinalIgnoreCase)
    {
        "portada", "cover", "créditos", "creditos", "copyright", "índice", "indice",
        "table of contents", "contents", "notas", "footnotes", "colofón", "colofon",
        "dedication", "acknowledgments"
    };

    private static readonly HashSet<string> NonContentDocumentTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "copyright-page", "dedication", "acknowledgments", "toc", "index", "landmarks"
    };

    private static readonly HashSet<string> BlockTags = new(StringComparer.OrdinalIgnoreCase)
    {
        "p", "h1", "h2", "h3", "h4", "h5", "h6", "li", "blockquote", "div",
        "section", "article", "figure", "figcaption", "dt", "dd", "header", "footer",
        "table", "caption", "tr", "td", "th"
    };

    private static readonly HashSet<string> IgnoredTags = new(StringComparer.OrdinalIgnoreCase)
    {
        "script", "style", "nav", "aside"
    };

    public async Task<Book> LoadAsync(string filePath, CancellationToken cancellationToken = default)
    {
        if (!File.Exists(filePath))
        {
            throw new FileNotFoundException("EPUB file not found.", filePath);
        }

        using var epub = await EpubReader.OpenBookAsync(filePath, new EpubReaderOptions(EpubReaderOptionsPreset.RELAXED)).ConfigureAwait(false);
        var title = string.IsNullOrWhiteSpace(epub.Title) ? Path.GetFileNameWithoutExtension(filePath) : epub.Title;
        var author = string.IsNullOrWhiteSpace(epub.Author) ? "Desconocido" : epub.Author;
        var language = epub.Schema.Package.Metadata.Languages.FirstOrDefault()?.Language ?? "es-ES";

        var tocTitles = BuildTocTitleMap(await ReadNavigationAsync(epub).ConfigureAwait(false));
        var readingOrder = await epub.GetReadingOrderAsync().ConfigureAwait(false);
        var browsingContext = BrowsingContext.New(Configuration.Default);

        var chapters = new List<Chapter>();
        var chapterIndex = 1;

        foreach (var htmlFile in readingOrder)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var html = await htmlFile.ReadContentAsync().ConfigureAwait(false);
            var doc = await browsingContext.OpenAsync(req => req.Content(html), cancellationToken).ConfigureAwait(false);
            if (IsNonContentDocument(doc))
            {
                continue;
            }

            var chapterTitle = tocTitles.TryGetValue(NormalizePath(htmlFile.Key), out var tocTitle)
                ? tocTitle
                : ExtractHeading(doc) ?? $"Capítulo {chapterIndex}";

            if (BoilerplateTitles.Contains(chapterTitle.Trim()))
            {
                continue;
            }

            var cleanText = ExtractText(doc);
            if (string.IsNullOrWhiteSpace(cleanText) || cleanText.Length < 40)
            {
                continue;
            }

            chapters.Add(new Chapter(chapterIndex++, chapterTitle.Trim(), cleanText));
        }

        var cover = await epub.ReadCoverAsync().ConfigureAwait(false);
        return new Book(title, author, language, chapters, cover);
    }

    /// <summary>
    /// Extracts readable text from a chapter document as paragraphs separated by blank lines.
    /// Block-level elements and line breaks start a new paragraph; whitespace inside a paragraph is collapsed.
    /// </summary>
    internal static string ExtractText(IDocument doc)
    {
        foreach (var node in doc.QuerySelectorAll("[epub\\:type='footnote'], .footnote"))
        {
            node.Remove();
        }

        if (doc.Body is null)
        {
            return string.Empty;
        }

        var paragraphs = new List<string>();
        var current = new StringBuilder();

        void Flush()
        {
            var text = CollapseWhitespaceRegex().Replace(current.ToString(), " ").Trim();
            if (text.Length > 0)
            {
                paragraphs.Add(text);
            }
            current.Clear();
        }

        void Walk(INode parent)
        {
            foreach (var child in parent.ChildNodes)
            {
                if (child.NodeType == NodeType.Text)
                {
                    current.Append(child.TextContent);
                    continue;
                }

                if (child is not IElement element || IgnoredTags.Contains(element.LocalName))
                {
                    continue;
                }

                if (element.LocalName.Equals("br", StringComparison.OrdinalIgnoreCase))
                {
                    Flush();
                    continue;
                }

                var isBlock = BlockTags.Contains(element.LocalName);
                if (isBlock)
                {
                    Flush();
                }

                Walk(element);

                if (isBlock)
                {
                    Flush();
                }
            }
        }

        Walk(doc.Body);
        Flush();

        return paragraphs.Count > 0
            ? string.Join("\n\n", paragraphs)
            : CollapseWhitespaceRegex().Replace(doc.Body.TextContent, " ").Trim();
    }

    /// <summary>
    /// Returns true when the document declares a non-content role (copyright page, dedication, TOC, index...)
    /// through <c>epub:type</c> or <c>role</c> on its body or top-level sections.
    /// </summary>
    internal static bool IsNonContentDocument(IDocument doc)
    {
        foreach (var element in doc.QuerySelectorAll("body, section"))
        {
            var declared = $"{element.GetAttribute("epub:type")} {element.GetAttribute("role")}";
            foreach (var token in declared.Split(' ', StringSplitOptions.RemoveEmptyEntries))
            {
                var value = token.StartsWith("doc-", StringComparison.OrdinalIgnoreCase) ? token[4..] : token;
                if (NonContentDocumentTypes.Contains(value))
                {
                    return true;
                }
            }
        }

        return false;
    }

    private static string NormalizePath(string path) => path.Replace('\\', '/');

    private static async Task<List<EpubNavigationItemRef>> ReadNavigationAsync(EpubBookRef epub)
    {
        try
        {
            return await epub.GetNavigationAsync().ConfigureAwait(false) ?? [];
        }
        catch (Epub3NavException)
        {
            // Some EPUBs (e.g. produced by external translators) use backslashes in nav hrefs,
            // which VersOne rejects. Fall back to headings instead of failing the whole book.
            return [];
        }
    }

    private static Dictionary<string, string> BuildTocTitleMap(IEnumerable<EpubNavigationItemRef>? navigation)
    {
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (navigation is null)
        {
            return map;
        }

        void Walk(IEnumerable<EpubNavigationItemRef> items)
        {
            foreach (var item in items)
            {
                var key = item.HtmlContentFileRef?.Key;
                if (!string.IsNullOrWhiteSpace(key) && !string.IsNullOrWhiteSpace(item.Title))
                {
                    map.TryAdd(NormalizePath(key), item.Title);
                }

                if (item.NestedItems.Count > 0)
                {
                    Walk(item.NestedItems);
                }
            }
        }

        Walk(navigation);
        return map;
    }

    private static string? ExtractHeading(IDocument doc)
    {
        var heading = doc.QuerySelector("h1, h2, h3");
        var text = heading?.TextContent.Trim();
        return string.IsNullOrWhiteSpace(text) ? null : text;
    }

    [GeneratedRegex(@"\s+")]
    private static partial Regex CollapseWhitespaceRegex();
}
