using System.Globalization;
using System.IO.Compression;
using System.Text;
using ConversorLibros.Core.Models;
using ConversorLibros.Core.Services;
using VersOne.Epub.Schema;
using Xunit;

namespace ConversorLibros.Core.Tests;

public class BookMetadataTests
{
    [Fact]
    public void Dates_PreferPublicationEventAndIgnoreCalibreSentinel()
    {
        var metadata = Build(dates: new List<EpubMetadataDate>
        {
            new("0101-01-01", null, null),
            new("1997", null, "modification"),
            new("1998-03", null, "publication"),
        });

        Assert.Equal("1998-03", metadata.PublishedDate);
    }

    [Fact]
    public void Dates_OnlyCalibreSentinel_ReturnsNull()
    {
        var metadata = Build(dates: new List<EpubMetadataDate> { new("0101-01-01", null, null) });

        Assert.Null(metadata.PublishedDate);
    }

    [Fact]
    public void Isbn_ReadsIsbnSchemeAndDropsHyphens()
    {
        var metadata = Build(identifiers: new List<EpubMetadataIdentifier>
        {
            new("978-84-666-1234-7", null, "ISBN"),
        });

        Assert.Equal("9788466612347", metadata.Isbn);
    }

    [Fact]
    public void Isbn_AcceptsUrnIsbnPrefix()
    {
        var metadata = Build(identifiers: new List<EpubMetadataIdentifier>
        {
            new("urn:isbn:8466612345", null, null),
        });

        Assert.Equal("8466612345", metadata.Isbn);
    }

    [Fact]
    public void Isbn_UuidIdentifierIsNotReportedAsIsbn()
    {
        var metadata = Build(identifiers: new List<EpubMetadataIdentifier>
        {
            new("urn:uuid:12345", null, null),
        });

        Assert.Null(metadata.Isbn);
    }

    [Fact]
    public void Description_StripsHtmlAndKeepsParagraphs()
    {
        var metadata = Build(descriptions: new List<EpubMetadataDescription>
        {
            new("<p>Primera &amp; última.</p><p>Segunda <b>frase</b>.</p>", null, null, null),
        });

        Assert.Equal("Primera & última.\n\nSegunda frase.", metadata.Description);
    }

    [Fact]
    public void Subjects_AreTrimmedAndDeduplicatedIgnoringCase()
    {
        var metadata = Build(subjects: new List<EpubMetadataSubject>
        {
            new("  Ciencia ficción ", null, null, null),
            new("ciencia ficción", null, null, null),
            new("Robots", null, null, null),
        });

        Assert.Equal(new[] { "Ciencia ficción", "Robots" }, metadata.Subjects);
    }

    [Fact]
    public void Publisher_SkipsBlankEntries()
    {
        var metadata = Build(publishers: new List<EpubMetadataPublisher>
        {
            new("   ", null, null, null),
            new("Plaza & Janés", null, null, null),
        });

        Assert.Equal("Plaza & Janés", metadata.Publisher);
    }

    [Fact]
    public void CalibreSeriesIndex_ParsesWithInvariantCultureEvenUnderSpanishLocale()
    {
        var previous = CultureInfo.CurrentCulture;
        CultureInfo.CurrentCulture = new CultureInfo("es-ES");
        try
        {
            var metadata = Build(metaItems: new List<EpubMetadataMeta>
            {
                new("calibre:series", "Trilogía", null, null, null, null, null, null),
                new("calibre:series_index", "1.5", null, null, null, null, null, null),
            });

            Assert.Equal("Trilogía", metadata.Series);
            Assert.Equal(1.5, metadata.SeriesIndex);
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
        }
    }

    [Fact]
    public void Epub3Collection_GroupPositionRefinesCollectionIdWithoutHash()
    {
        var metadata = Build(metaItems: new List<EpubMetadataMeta>
        {
            new(null, "Fundación", "c01", null, "belongs-to-collection", null, null, null),
            new(null, "3", null, "#c01", "group-position", null, null, null),
        });

        Assert.Equal("Fundación", metadata.Series);
        Assert.Equal(3, metadata.SeriesIndex);
    }

    [Fact]
    public async Task LoadAsync_Epub2_MapsCatalogueMetadataFromPackage()
    {
        var path = WriteEpub(epub3: false);
        try
        {
            var book = await new EpubBookSource().LoadAsync(path);

            Assert.NotNull(book.Metadata);
            Assert.Equal("Plaza & Janés", book.Metadata.Publisher);
            Assert.Equal("1951", book.Metadata.PublishedDate);
            Assert.Equal("9788466612347", book.Metadata.Isbn);
            Assert.Equal(new[] { "Ciencia ficción" }, book.Metadata.Subjects);
            Assert.Equal("Trilogía", book.Metadata.Series);
            Assert.Equal(2, book.Metadata.SeriesIndex);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task LoadAsync_Epub3_ReadsBelongsToCollectionAndGroupPosition()
    {
        var path = WriteEpub(epub3: true);
        try
        {
            var book = await new EpubBookSource().LoadAsync(path);

            Assert.NotNull(book.Metadata);
            Assert.Equal("Trilogía", book.Metadata.Series);
            Assert.Equal(3, book.Metadata.SeriesIndex);
            Assert.Equal("1951-06-01", book.Metadata.PublishedDate);
            Assert.Null(book.Metadata.Isbn);
            Assert.Null(book.Metadata.Publisher);
        }
        finally
        {
            File.Delete(path);
        }
    }

    private static BookMetadata Build(
        List<EpubMetadataPublisher>? publishers = null,
        List<EpubMetadataDescription>? descriptions = null,
        List<EpubMetadataDate>? dates = null,
        List<EpubMetadataIdentifier>? identifiers = null,
        List<EpubMetadataSubject>? subjects = null,
        List<EpubMetadataMeta>? metaItems = null)
    {
        // Positional order follows EpubMetadata's constructor: titles, creators, subjects, descriptions, publishers,
        // contributors, dates, types, formats, identifiers, sources, languages, relations, coverages, rights, links, meta.
        var package = new EpubMetadata(
            [], [], subjects ?? new(), descriptions ?? new(), publishers ?? new(), [], dates ?? new(), [], [],
            identifiers ?? new(), [], [], [], [], [], [], metaItems ?? new());

        return EpubBookSource.BuildMetadata(package);
    }

    private static string WriteEpub(bool epub3)
    {
        var path = Path.Combine(Path.GetTempPath(), $"metadata-{Guid.NewGuid():N}.epub");
        using var zip = ZipFile.Open(path, ZipArchiveMode.Create);

        WriteEntry(zip, "mimetype", "application/epub+zip");
        WriteEntry(zip, "META-INF/container.xml", """
            <?xml version="1.0" encoding="UTF-8"?>
            <container version="1.0" xmlns="urn:oasis:names:tc:opendocument:xmlns:container">
              <rootfiles>
                <rootfile full-path="OEBPS/content.opf" media-type="application/oebps-package+xml"/>
              </rootfiles>
            </container>
            """);

        WriteEntry(zip, "OEBPS/content.opf", epub3 ? Epub3Package : Epub2Package);
        WriteEntry(zip, "OEBPS/ch1.xhtml", """
            <?xml version="1.0" encoding="UTF-8"?>
            <html xmlns="http://www.w3.org/1999/xhtml">
              <head><title>Capítulo 1</title></head>
              <body><h1>Capítulo 1</h1><p>Este es un párrafo de prueba suficientemente largo para el capítulo.</p></body>
            </html>
            """);

        if (epub3)
        {
            WriteEntry(zip, "OEBPS/nav.xhtml", """
                <?xml version="1.0" encoding="UTF-8"?>
                <html xmlns="http://www.w3.org/1999/xhtml" xmlns:epub="http://www.idpf.org/2007/ops">
                  <head><title>Índice</title></head>
                  <body><nav epub:type="toc"><ol><li><a href="ch1.xhtml">Capítulo 1</a></li></ol></nav></body>
                </html>
                """);
        }
        else
        {
            WriteEntry(zip, "OEBPS/toc.ncx", """
                <?xml version="1.0" encoding="UTF-8"?>
                <ncx xmlns="http://www.daisy.org/z3986/2005/ncx/" version="2005-1">
                  <head><meta name="dtb:uid" content="urn:uuid:12345"/></head>
                  <docTitle><text>Fundación</text></docTitle>
                  <navMap>
                    <navPoint id="np1" playOrder="1">
                      <navLabel><text>Capítulo 1</text></navLabel>
                      <content src="ch1.xhtml"/>
                    </navPoint>
                  </navMap>
                </ncx>
                """);
        }

        return path;
    }

    private const string Epub2Package = """
        <?xml version="1.0" encoding="UTF-8"?>
        <package xmlns="http://www.idpf.org/2007/opf" version="2.0" unique-identifier="bookid">
          <metadata xmlns:dc="http://purl.org/dc/elements/1.1/" xmlns:opf="http://www.idpf.org/2007/opf">
            <dc:title>Fundación</dc:title>
            <dc:creator>Isaac Asimov</dc:creator>
            <dc:language>es</dc:language>
            <dc:identifier id="bookid">urn:uuid:12345</dc:identifier>
            <dc:identifier opf:scheme="ISBN">978-84-666-1234-7</dc:identifier>
            <dc:publisher>Plaza &amp; Janés</dc:publisher>
            <dc:date opf:event="publication">1951</dc:date>
            <dc:date opf:event="modification">0101-01-01</dc:date>
            <dc:subject>Ciencia ficción</dc:subject>
            <meta name="calibre:series" content="Trilogía"/>
            <meta name="calibre:series_index" content="2"/>
          </metadata>
          <manifest>
            <item id="ncx" href="toc.ncx" media-type="application/x-dtbncx+xml"/>
            <item id="ch1" href="ch1.xhtml" media-type="application/xhtml+xml"/>
          </manifest>
          <spine toc="ncx">
            <itemref idref="ch1"/>
          </spine>
        </package>
        """;

    private const string Epub3Package = """
        <?xml version="1.0" encoding="UTF-8"?>
        <package xmlns="http://www.idpf.org/2007/opf" version="3.0" unique-identifier="bookid">
          <metadata xmlns:dc="http://purl.org/dc/elements/1.1/">
            <dc:title>Fundación</dc:title>
            <dc:creator>Isaac Asimov</dc:creator>
            <dc:language>es</dc:language>
            <dc:identifier id="bookid">urn:uuid:12345</dc:identifier>
            <dc:date>1951-06-01</dc:date>
            <meta property="belongs-to-collection" id="c01">Trilogía</meta>
            <meta refines="#c01" property="group-position">3</meta>
          </metadata>
          <manifest>
            <item id="nav" href="nav.xhtml" media-type="application/xhtml+xml" properties="nav"/>
            <item id="ch1" href="ch1.xhtml" media-type="application/xhtml+xml"/>
          </manifest>
          <spine>
            <itemref idref="ch1"/>
          </spine>
        </package>
        """;

    private static void WriteEntry(ZipArchive zip, string entryPath, string content)
    {
        var entry = zip.CreateEntry(entryPath);
        using var writer = new StreamWriter(entry.Open(), new UTF8Encoding(false));
        writer.Write(content.Trim());
    }
}
