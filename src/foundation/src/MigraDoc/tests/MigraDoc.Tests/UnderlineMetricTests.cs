// MigraDoc - Creating Documents on the Fly
// See the LICENSE file in the solution root for more information.

using System.Globalization;
using FluentAssertions;
using MigraDoc.DocumentObjectModel;
using MigraDoc.Rendering;
using PdfSharp.Diagnostics;
using PdfSharp.Drawing;
using PdfSharp.Fonts;
using PdfSharp.Pdf.Content;
using PdfSharp.Pdf.Content.Objects;
using PdfSharp.Quality;
using Xunit;

namespace MigraDoc.Tests;

[Collection("PDFsharp")]
public class UnderlineMetricTests : IDisposable
{
    public UnderlineMetricTests()
    {
        PdfSharpCore.ResetAll();
        GlobalFontSettings.FontResolver = new UnitTestFontResolver();
    }
    public void Dispose() => PdfSharpCore.ResetAll();

    [Theory]
    [InlineData("Arial", 12, true, false, false)]
    [InlineData("Times New Roman", 24, true, false, false)]
    [InlineData("Arial", 12, false, false, false)]
    [InlineData("Arial", 12, true, true, false)]
    [InlineData("Arial", 12, false, true, false)]
    [InlineData("Arial", 12, true, false, true)]
    public void UnderlineGeometryUsesMetricsOnlyWhenEnabled(string family, double size, bool enabled, bool custom, bool mixedSizes)
    {
        var document = new Document();
        document.Styles.Normal.Font.Name = family;
        document.Styles.Normal.Font.Size = size;
        var paragraph = document.AddSection().AddParagraph("Underline");
        paragraph.Format.Font.Underline = Underline.Single;
        if (mixedSizes)
        {
            var large = paragraph.AddFormattedText("Large");
            large.Font.Size = 36;
            large.Font.Underline = Underline.None;
        }
        var renderer = new PdfDocumentRenderer { Document = document };
        renderer.DocumentRenderer.UseFontUnderlineMetrics.Should().BeFalse();
        renderer.DocumentRenderer.UseFontUnderlineMetrics = enabled;
        if (custom)
            renderer.DocumentRenderer.UnderlineGeometry = _ => (0.1, 0.5);
        renderer.RenderDocument();
        var operators = ContentReader.ReadContent(renderer.PdfDocument.Pages[0]).OfType<COperator>().ToArray();
        var font = new XFont(family, size);
        var metrics = font.Metrics;
        var descent = font.GetHeight() * font.CellDescent / font.CellSpace;
        var expectedWidth = custom ? 0.1 * size : enabled
            ? size * metrics.UnderlineThickness / metrics.UnitsPerEm : size / 16;
        var expectedOffset = custom ? 0.5 * descent : enabled
            ? size * (-metrics.UnderlinePosition + metrics.UnderlineThickness / 2.0) / metrics.UnitsPerEm
            : 0.33 * descent;
        N(operators.Single(op => op.OpCode.Name == "w").Operands[0]).Should().BeApproximately(expectedWidth, 0.001);
        var textY = N(operators.First(op => op.OpCode.Name == "Td").Operands[1]);
        var lineY = N(operators.Single(op => op.OpCode.Name == "m").Operands[1]);
        (textY - lineY).Should().BeApproximately(expectedOffset, 0.002);
    }

    static double N(CObject value) => double.Parse(value.ToString()!, CultureInfo.InvariantCulture);
}
