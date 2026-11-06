// PDFsharp - A .NET library for processing PDF
// See the LICENSE file in the solution root for more information.

using FluentAssertions;
using PdfSharp.Diagnostics;
using PdfSharp.Drawing;
using PdfSharp.Fonts;
using PdfSharp.Quality;
using Xunit;

namespace PdfSharp.Tests.Fonts;

[Collection("PDFsharp")]
public class EastAsianCodePageTests : IDisposable
{
    public EastAsianCodePageTests()
    {
        PdfSharpCore.ResetAll();
        GlobalFontSettings.FontResolver = new FixtureFontResolver();
    }

    public void Dispose() => PdfSharpCore.ResetAll();

    [Theory]
    [InlineData(0u, false)]
    [InlineData(1u << 0, false)] // Latin 1.
    [InlineData(1u << 16, false)] // Thai: immediately below the CJK flags.
    [InlineData(1u << 17, true)] // JIS/Japan (932).
    [InlineData(1u << 18, true)] // Simplified Chinese (936).
    [InlineData(1u << 19, true)] // Korean Wansung (949).
    [InlineData(1u << 20, true)] // Traditional Chinese (950).
    [InlineData(1u << 21, true)] // Korean Johab (1361).
    [InlineData(1u << 22, false)] // Reserved: immediately above the CJK flags.
    [InlineData(1u << 31, false)] // Symbol.
    [InlineData((1u << 0) | (1u << 17) | (1u << 31), true)]
    [InlineData((1u << 0) | (1u << 16) | (1u << 22) | (1u << 31), false)]
    public void ReportsDeclaredCodePagesWithoutInferringGlyphCoverage(uint flags, bool expected)
    {
        var font = new XFont("BlankGlyph", 12);
        var os2 = font.OpenTypeDescriptor.FontFace.os2;
        var originalFlags = os2.ulCodePageRange1;
        try
        {
            // Exercise interpretation of parsed metadata independently of a system font's declarations.
            // This fixture has only .notdef and space; flags alone do not establish CJK glyph coverage.
            os2.ulCodePageRange1 = flags;
            font.HasEastAsianCodePage.Should().Be(expected);
        }
        finally
        {
            os2.ulCodePageRange1 = originalFlags;
        }
    }

    [Fact]
    public void UnflaggedFontDoesNotAcquireCodePageMetadataFromItsSize()
    {
        var small = new XFont("BlankGlyph", 10);
        var large = new XFont("BlankGlyph", 30);
        small.OpenTypeDescriptor.FontFace.os2.ulCodePageRange1.Should().Be(0);
        small.HasEastAsianCodePage.Should().BeFalse();
        large.HasEastAsianCodePage.Should().BeFalse();
    }

    sealed class FixtureFontResolver : IFontResolver
    {
        readonly UnitTestFontResolver _fallback = new();

        public FontResolverInfo? ResolveTypeface(string familyName, bool bold, bool italic)
            => familyName == "BlankGlyph" ? new FontResolverInfo("BlankGlyph")
                : _fallback.ResolveTypeface(familyName, bold, italic);

        public byte[]? GetFont(string faceName)
            => faceName == "BlankGlyph" ? File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory,
                "Fonts", "Fixtures", "BlankGlyphFont.ttf")) : _fallback.GetFont(faceName);
    }
}
