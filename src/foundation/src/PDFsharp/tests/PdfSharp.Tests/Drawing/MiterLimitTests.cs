using System.Text.RegularExpressions;
using FluentAssertions;
using PdfSharp.Drawing;
using PdfSharp.Pdf;
using PdfSharp.TestHelper;
using Xunit;

namespace PdfSharp.Tests.Drawing
{
    [Collection("PDFsharp")]
    public class MiterLimitTests
    {
        [Theory]
        [InlineData(XLineCap.Flat, XLineJoin.Miter, true)]
        [InlineData(XLineCap.Round, XLineJoin.Miter, true)]
        [InlineData(XLineCap.Square, XLineJoin.Miter, true)]
        [InlineData(XLineCap.Flat, XLineJoin.Round, false)]
        public void MiterLimitFollowsLineJoinRatherThanLineCap(XLineCap lineCap, XLineJoin lineJoin, bool expected)
        {
            var document = new PdfDocument();
            using (var gfx = XGraphics.FromPdfPage(document.AddPage()))
            {
                var pen = new XPen(XColors.Black, 2)
                {
                    LineCap = lineCap,
                    LineJoin = lineJoin,
                    MiterLimit = 7,
                };
                gfx.DrawLines(pen, new[] { new XPoint(10, 10), new XPoint(30, 30), new XPoint(50, 10) });
            }

            var content = PdfFileHelper.GetPageContentStream(document, 0);
            Regex.IsMatch(content, @"(?m)^7 M$").Should().Be(expected);
        }
    }
}
