using FluentAssertions;
using PdfSharp.Internal.OpenType;
using PdfSharp.Quality;
using Xunit;

namespace PdfSharp.Tests.Fonts
{
    [Collection("PDFsharp")]
    public class NestedCompositeGlyphTests
    {
        [Fact]
        public void NestedCompositeIncludesGrandchild()
        {
            // Arial: Aringacute (470) -> Aring (99) -> A (36).
            var fontPath = IOUtility.GetAssetsPath("pdfsharp-6.x/fonts/Arial/arial.ttf")!;
            var source = OpenTypeFontSource.GetOrCreateFrom(File.ReadAllBytes(fontPath));
            var face = source.OTFontFace ?? OpenTypeFontFace.GetOrCreateFrom(source);
            var glyphs = new Dictionary<ushort, object?> { [470] = null };

            face.glyf.CompleteGlyphClosure(glyphs);

            glyphs.Keys.Should().Contain(new ushort[] { 0, 470, 99, 36 });
        }
    }
}
