using AngleSharp;
using AngleSharp.Dom;
using ConversorLibros.Core.Services;
using Xunit;

namespace ConversorLibros.Core.Tests;

public class EpubBookSourceTests
{
    [Fact]
    public async Task ExtractText_SeparatesTableCells()
    {
        var doc = await OpenHtmlAsync(
            "<body><table><tr><th>You will be able to</th><th>Why it matters</th></tr>" +
            "<tr><td>Choose an assistant.</td><td>You avoid confusion.</td></tr></table></body>");

        var text = EpubBookSource.ExtractText(doc);

        Assert.Equal(
            "You will be able to\n\nWhy it matters\n\nChoose an assistant.\n\nYou avoid confusion.",
            text);
    }

    [Fact]
    public async Task ExtractText_SeparatesSiblingParagraphsWithBlankLine()
    {
        var doc = await OpenHtmlAsync("<body><p>First Edition.</p><p>Publisher: Gem Iroko</p></body>");

        var text = EpubBookSource.ExtractText(doc);

        Assert.Equal("First Edition.\n\nPublisher: Gem Iroko", text);
    }

    [Fact]
    public async Task ExtractText_BreakElementStartsNewParagraph()
    {
        var doc = await OpenHtmlAsync("<body><p>Line one<br/>Line two</p></body>");

        var text = EpubBookSource.ExtractText(doc);

        Assert.Equal("Line one\n\nLine two", text);
    }

    [Fact]
    public async Task ExtractText_DivTextIsSeparatedFromFollowingParagraph()
    {
        var doc = await OpenHtmlAsync("<body><div>Loose text</div><p>Next paragraph</p></body>");

        var text = EpubBookSource.ExtractText(doc);

        Assert.Equal("Loose text\n\nNext paragraph", text);
    }

    [Fact]
    public async Task ExtractText_IgnoresScriptAndStyleContent()
    {
        var doc = await OpenHtmlAsync("<head><style>p{}</style></head><body><script>var x=1;</script><p>Visible</p></body>");

        var text = EpubBookSource.ExtractText(doc);

        Assert.Equal("Visible", text);
    }

    [Fact]
    public async Task IsNonContentDocument_CopyrightPage_ReturnsTrue()
    {
        var doc = await OpenHtmlAsync("""<body epub:type="copyright-page"><p>© 2024</p></body>""");

        Assert.True(EpubBookSource.IsNonContentDocument(doc));
    }

    [Fact]
    public async Task IsNonContentDocument_Dedication_ReturnsTrue()
    {
        var doc = await OpenHtmlAsync("""<body><section epub:type="dedication"><p>For you</p></section></body>""");

        Assert.True(EpubBookSource.IsNonContentDocument(doc));
    }

    [Fact]
    public async Task IsNonContentDocument_DocTocRole_ReturnsTrue()
    {
        var doc = await OpenHtmlAsync("""<body><section role="doc-toc"><ol><li>Item</li></ol></section></body>""");

        Assert.True(EpubBookSource.IsNonContentDocument(doc));
    }

    [Fact]
    public async Task IsNonContentDocument_RegularChapter_ReturnsFalse()
    {
        var doc = await OpenHtmlAsync("""<body><section epub:type="chapter"><p>Once upon a time.</p></section></body>""");

        Assert.False(EpubBookSource.IsNonContentDocument(doc));
    }

    private static Task<IDocument> OpenHtmlAsync(string html) =>
        BrowsingContext.New(Configuration.Default).OpenAsync(r => r.Content(html));
}
