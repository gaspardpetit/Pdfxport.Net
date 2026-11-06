// MigraDoc - Creating Documents on the Fly
// See the LICENSE file in the solution root for more information.

using System.Reflection;
using FluentAssertions;
using MigraDoc.DocumentObjectModel;
using MigraDoc.Rendering;
using PdfSharp.Diagnostics;
using PdfSharp.Drawing;
using PdfSharp.Fonts;
using PdfSharp.Quality;
using Xunit;

namespace MigraDoc.Tests;

[Collection("PDFsharp")]
public class FontCacheTests : IDisposable
{
    public FontCacheTests()
    {
        PdfSharpCore.ResetAll();
        GlobalFontSettings.FontResolver = new UnitTestFontResolver();
    }

    public void Dispose() => PdfSharpCore.ResetAll();

    [Theory]
    [InlineData("size")]
    [InlineData("small-size-change")]
    [InlineData("family")]
    [InlineData("bold")]
    [InlineData("italic")]
    [InlineData("clear-style")]
    public void MutatingTheSameFontInvalidatesItsCachedDrawingFont(string change)
    {
        var font = new Font("Arial", 12);
        if (change == "clear-style")
        {
            font.Bold = true;
            font.Italic = true;
        }
        var original = Convert(font);
        Convert(font).Should().BeSameAs(original);
        switch (change)
        {
            case "size": font.Size = 24; break;
            case "small-size-change": font.Size = Unit.FromPoint(12.00001); break;
            case "family": font.Name = "Courier New"; break;
            case "bold": font.Bold = true; break;
            case "italic": font.Italic = true; break;
            case "clear-style": font.Bold = false; font.Italic = false; break;
        }
        var updated = Convert(font);
        updated.Should().NotBeSameAs(original);
        updated.Size.Should().Be(font.Size.Point);
        updated.Name.Should().Be(font.Name);
        updated.Bold.Should().Be(font.Bold);
        updated.Italic.Should().Be(font.Italic);
        Convert(font).Should().BeSameAs(updated);
    }

    [Fact]
    public void PaintOnlyChangesPreserveTheCachedDrawingFont()
    {
        var font = new Font("Arial", 12);
        var original = Convert(font);
        font.Color = Colors.Red;
        font.Underline = Underline.Single;
        Convert(font).Should().BeSameAs(original);
    }

    // Keep cache identity checks focused without widening the production API.
    static XFont Convert(Font font)
        => (XFont)typeof(DocumentRenderer).Assembly.GetType("MigraDoc.Rendering.FontHandler")!
            .GetMethod("FontToXFont", BindingFlags.Static | BindingFlags.NonPublic)!
            .Invoke(null, new object[] { font })!;
}
