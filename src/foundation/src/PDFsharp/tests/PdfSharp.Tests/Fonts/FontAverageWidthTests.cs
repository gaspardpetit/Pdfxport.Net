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
public class FontAverageWidthTests : IDisposable
{
    public FontAverageWidthTests()
    {
        PdfSharpCore.ResetAll();
        GlobalFontSettings.FontResolver = new FixtureFontResolver();
    }

    public void Dispose() => PdfSharpCore.ResetAll();

    [Theory]
    [InlineData(UnitTestFontResolver.ArialFont, XFontStyleEx.Regular)]
    [InlineData(UnitTestFontResolver.ArialFont, XFontStyleEx.Bold)]
    [InlineData(UnitTestFontResolver.ArialFont, XFontStyleEx.Italic)]
    [InlineData(UnitTestFontResolver.CourierFont, XFontStyleEx.Regular)]
    [InlineData(UnitTestFontResolver.SymbolFont, XFontStyleEx.Regular)]
    [InlineData("BlankGlyph", XFontStyleEx.Regular)]
    public void AverageWidthReportsFontDesignUnitsRegardlessOfPointSize(string family, XFontStyleEx style)
    {
        var small = new XFont(family, 10, style);
        var large = new XFont(family, 30, style);
        var expected = small.OpenTypeDescriptor.FontFace.os2.xAvgCharWidth;
        expected.Should().BeGreaterThan(0, "these fixtures declare a nonzero average width");
        small.Metrics.AverageWidth.Should().Be(expected);
        large.Metrics.AverageWidth.Should().Be(expected);
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
