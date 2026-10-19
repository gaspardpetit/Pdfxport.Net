using FluentAssertions;
using MigraDoc.DocumentObjectModel;
using MigraDoc.DocumentObjectModel.Shapes;
using MigraDoc.DocumentObjectModel.Tables;
using MigraDoc.Rendering;
using PdfSharp.Diagnostics;
using PdfSharp.Fonts;
using PdfSharp.Quality;
using PdfSharp.TestHelper;
using Xunit;

namespace MigraDoc.Tests
{
    [Collection("PDFsharp")]
    public class TextFrameContinuationRenderInfoTests : IDisposable
    {
        public TextFrameContinuationRenderInfoTests()
        {
            PdfSharpCore.ResetAll();
#if CORE
            GlobalFontSettings.FontResolver = new UnitTestFontResolver();
#endif
        }

        public void Dispose() => PdfSharpCore.ResetAll();

        [Fact]
        public void ContinuationAccessorReturnsItsNestedTableFragment()
        {
            var document = new Document();
            document.Styles.Normal.Font.Name = "Arial";
            var section = document.AddSection();
            section.PageSetup.TopMargin = Unit.FromPoint(72);
            section.PageSetup.BottomMargin = Unit.FromPoint(72);
            var frame = section.AddTextFrame();
            frame.RelativeHorizontal = RelativeHorizontal.Margin;
            frame.RelativeVertical = RelativeVertical.Margin;
            frame.Left = Unit.FromPoint(144);
            frame.Top = Unit.FromPoint(36);
            frame.Width = Unit.FromPoint(144);
            frame.WrapFormat.Style = WrapStyle.TopBottom;
            frame.PaginateContent = true;
            var table = frame.AddTable();
            table.AddColumn(Unit.FromPoint(144));
            for (var row = 1; row <= 60; row++)
                table.AddRow().Cells[0].AddParagraph($"Row {row}");
            var renderer = new PdfDocumentRenderer { Document = document };

            renderer.RenderDocument();

            renderer.PdfDocument.PageCount.Should().BeGreaterThan(1);
            var first = renderer.DocumentRenderer.GetRenderInfoFromPage(1)!
                .Single(info => ReferenceEquals(info.DocumentObject, frame));
            renderer.DocumentRenderer.GetTextFrameContinuationRenderInfos(first)
                .Should().BeNull();
            var continuation = renderer.DocumentRenderer.GetRenderInfoFromPage(2)!
                .Single(info => ReferenceEquals(info.DocumentObject, frame));
            var nested = renderer.DocumentRenderer.GetTextFrameContinuationRenderInfos(continuation);
            nested.Should().NotBeNullOrEmpty();
            nested!.Any(info => ReferenceEquals(info.DocumentObject, table) &&
                info.FormatInfo is TableFormatInfo { StartRow: > 0 }).Should().BeTrue();
        }
    }
}
