using FluentAssertions;
using PdfSharp.Drawing;
using PdfSharp.Pdf;
using PdfSharp.TestHelper;
using System.Text.RegularExpressions;
using Xunit;

namespace PdfSharp.Tests.Drawing
{
    [Collection("PDFsharp")]
    public class StartFigureTests
    {
        [Fact]
        public void StartFigureEmitsTwoMoveOperators()
        {
            var path = new XGraphicsPath();
            path.AddLine(0, 0, 10, 0);
            path.StartFigure();
            path.AddLine(20, 0, 30, 0);
            var document = new PdfDocument();
            using (var graphics = XGraphics.FromPdfPage(document.AddPage()))
                graphics.DrawPath(new XPen(XColors.Black), path);

            var content = PdfFileHelper.GetPageContentStream(document, 0);
            Regex.Matches(content, @"(?m)^\s*[-\d.]+\s+[-\d.]+\s+m\s*$")
                .Count.Should().Be(2, content);
        }
    }
}
