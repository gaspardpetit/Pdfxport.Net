// PDFsharp - A .NET library for processing PDF
// See the LICENSE file in the solution root for more information.

using FluentAssertions;
using PdfSharp.Diagnostics;
using PdfSharp.Internal.OpenType;
using PdfSharp.Quality;
using Xunit;

namespace PdfSharp.Tests.Fonts;

[Collection("PDFsharp")]
public class FontFaceCacheTests : IDisposable
{
    public FontFaceCacheTests() => PdfSharpCore.ResetAll();

    public void Dispose() => PdfSharpCore.ResetAll();

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void IdenticalUncachedSourceIsAssociatedWithCachedFace(bool cachedSourceFirst)
    {
        var bytes = ReadFixture();
        var first = cachedSourceFirst
            ? OpenTypeFontSource.GetOrCreateFrom(bytes)
            : OpenTypeFontSource.CreateCompiledFont(bytes);
        var face = OpenTypeFontFace.GetOrCreateFrom(first);
        var second = OpenTypeFontSource.CreateCompiledFont((byte[])bytes.Clone());
        second.Should().NotBeSameAs(first);
        second.OTFontFace.Should().BeNull();

        var reused = OpenTypeFontFace.GetOrCreateFrom(second);

        reused.Should().BeSameAs(face);
        second.OTFontFace.Should().BeSameAs(reused);
        second.FontFaceKey.Should().Be(first.FontFaceKey);
        OpenTypeFontFace.GetOrCreateFrom(second).Should().BeSameAs(reused);
    }

    [Fact]
    public void RepeatedRequestRetainsSourceAssociation()
    {
        var source = OpenTypeFontSource.GetOrCreateFrom(ReadFixture());
        var face = OpenTypeFontFace.GetOrCreateFrom(source);

        OpenTypeFontFace.GetOrCreateFrom(source).Should().BeSameAs(face);
        source.OTFontFace.Should().BeSameAs(face);
        source.FontFaceKey.Should().Be(face.OTDescriptor.Key);
    }

    [Fact]
    public void DifferentFontsKeepSeparateFacesAndSources()
    {
        var first = OpenTypeFontSource.CreateCompiledFont(ReadFixture());
        var path = IOUtility.GetAssetsPath("pdfsharp-6.x/fonts/Arial/arial.ttf")!;
        var second = OpenTypeFontSource.CreateCompiledFont(File.ReadAllBytes(path));

        var firstFace = OpenTypeFontFace.GetOrCreateFrom(first);
        var secondFace = OpenTypeFontFace.GetOrCreateFrom(second);

        first.ChecksumKey.Should().NotBe(second.ChecksumKey);
        secondFace.Should().NotBeSameAs(firstFace);
        first.OTFontFace.Should().BeSameAs(firstFace);
        second.OTFontFace.Should().BeSameAs(secondFace);
        firstFace.OTFontSource.Should().BeSameAs(first);
        secondFace.OTFontSource.Should().BeSameAs(second);
        secondFace.GlyphCount.Should().NotBe(firstFace.GlyphCount);
    }

    static byte[] ReadFixture() => File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory,
        "Fonts", "Fixtures", "BlankGlyphFont.ttf"));
}
