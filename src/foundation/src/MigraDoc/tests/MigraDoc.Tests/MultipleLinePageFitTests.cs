using FluentAssertions;
using MigraDoc.DocumentObjectModel;
using MigraDoc.Rendering;
using PdfSharp.Diagnostics;
using PdfSharp.Drawing;
using PdfSharp.Fonts;
using PdfSharp.Quality;
using Xunit;

namespace MigraDoc.Tests
{
    [Collection("PDFsharp")]
    public class MultipleLinePageFitTests : IDisposable
    {
        public MultipleLinePageFitTests()
        {
            PdfSharpCore.ResetAll();
#if CORE
            GlobalFontSettings.FontResolver = new UnitTestFontResolver();
#endif
        }

        public void Dispose() => PdfSharpCore.ResetAll();

        [Theory]
        [InlineData(0.5, 1)]
        [InlineData(-0.5, 2)]
        public void LastLineFitsByNaturalHeightWithoutItsTrailingLeading(double clearance, int pages)
        {
            const double fontSize = 12;
            const double spacing = 1.2;
            var naturalHeight = new XFont("Arial", fontSize).GetHeight();
            var document = new Document();
            document.Styles.Normal.Font.Name = "Arial";
            document.Styles.Normal.Font.Size = fontSize;
            var section = document.AddSection();
            section.PageSetup.PageHeight = Unit.FromPoint(40 + naturalHeight * (spacing * 2 + 1) + clearance);
            section.PageSetup.PageWidth = Unit.FromPoint(200);
            section.PageSetup.TopMargin = Unit.FromPoint(20);
            section.PageSetup.BottomMargin = Unit.FromPoint(20);
            section.PageSetup.LeftMargin = Unit.FromPoint(20);
            section.PageSetup.RightMargin = Unit.FromPoint(20);
            var paragraph = section.AddParagraph();
            paragraph.Format.LineSpacingRule = LineSpacingRule.Multiple;
            paragraph.Format.LineSpacing = spacing;
            paragraph.Format.WidowControl = false;
            paragraph.AddText("First");
            paragraph.AddLineBreak();
            paragraph.AddText("Second");
            paragraph.AddLineBreak();
            paragraph.AddText("Third");

            var renderer = new PdfDocumentRenderer { Document = document };
            renderer.RenderDocument();

            renderer.PdfDocument.PageCount.Should().Be(pages);
        }
    }
}
