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
public class WindowsCellMetricsTests : IDisposable
{
    public WindowsCellMetricsTests()
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
    public void WindowsMetricsScaleToPointSize(string family, XFontStyleEx style)
    {
        var small = new XFont(family, 10, style);
        var large = new XFont(family, 30, style);
        var descriptor = small.OpenTypeDescriptor;
        var os2 = descriptor.FontFace.os2;
        small.WindowsCellHeight.Should().BeApproximately(
            (os2.usWinAscent + os2.usWinDescent) * 10.0 / descriptor.UnitsPerEm, 1e-10);
        small.WindowsCellDescent.Should().BeApproximately(
            os2.usWinDescent * 10.0 / descriptor.UnitsPerEm, 1e-10);
        large.WindowsCellHeight.Should().BeApproximately(small.WindowsCellHeight * 3, 1e-10);
        large.WindowsCellDescent.Should().BeApproximately(small.WindowsCellDescent * 3, 1e-10);
    }

    [Theory]
    [InlineData(10)]
    [InlineData(30)]
    public void WindowsMetricsRemainIndependentOfTypographicLineSpacing(double size)
    {
        var font = new XFont("WindowsCellMetrics", size);
        font.Metrics.UnitsPerEm.Should().Be(1000);
        font.Metrics.Ascent.Should().Be(700);
        font.Metrics.Descent.Should().Be(200);
        font.GetHeight().Should().BeApproximately(0.9 * size, 1e-10);
        font.WindowsCellHeight.Should().BeApproximately(1.2 * size, 1e-10);
        font.WindowsCellDescent.Should().BeApproximately(0.3 * size, 1e-10);
    }

    sealed class FixtureFontResolver : IFontResolver
    {
        readonly UnitTestFontResolver _fallback = new();

        public FontResolverInfo? ResolveTypeface(string familyName, bool bold, bool italic)
            => familyName == "WindowsCellMetrics" ? new FontResolverInfo("WindowsCellMetrics")
                : _fallback.ResolveTypeface(familyName, bold, italic);

        public byte[]? GetFont(string faceName)
            => faceName == "WindowsCellMetrics" ? File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory,
                "Fonts", "Fixtures", "WindowsCellMetricsFont.ttf")) : _fallback.GetFont(faceName);
    }
}
