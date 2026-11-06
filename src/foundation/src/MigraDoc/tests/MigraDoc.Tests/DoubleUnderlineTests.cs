// MigraDoc - Creating Documents on the Fly
// See the LICENSE file in the solution root for more information.

using System.Globalization;
using FluentAssertions;
using MigraDoc.DocumentObjectModel;
using MigraDoc.DocumentObjectModel.IO;
using MigraDoc.Rendering;
using MigraDoc.RtfRendering;
using PdfSharp.Diagnostics;
using PdfSharp.Drawing;
using PdfSharp.Fonts;
using PdfSharp.Pdf.Content;
using PdfSharp.Pdf.Content.Objects;
using PdfSharp.Quality;
using Xunit;

namespace MigraDoc.Tests;

[Collection("PDFsharp")]
public class DoubleUnderlineTests : IDisposable
{
    public DoubleUnderlineTests()
    {
        PdfSharpCore.ResetAll();
        GlobalFontSettings.FontResolver = new UnitTestFontResolver();
    }
    public void Dispose() => PdfSharpCore.ResetAll();

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public void DoubleStrokesShareExtentAndLeaveOneStrokeWidthOfClearSpace(bool metrics, bool custom)
    {
        var document = CreateDocument();
        document.AddSection().AddParagraph("Double").Format.Font.Underline = Underline.Double;
        var renderer = new PdfDocumentRenderer { Document = document };
        renderer.DocumentRenderer.UseFontUnderlineMetrics = metrics;
        if (custom)
            renderer.DocumentRenderer.UnderlineGeometry = _ => (0.1, 0.5);
        renderer.RenderDocument();
        var operators = Operators(renderer);
        var starts = operators.Where(op => op.OpCode.Name == "m").ToArray();
        var ends = operators.Where(op => op.OpCode.Name == "l").ToArray();
        starts.Should().HaveCount(2);
        ends.Should().HaveCount(2);
        var width = N(operators.Single(op => op.OpCode.Name == "w").Operands[0]);
        var font = new XFont("Arial", 12);
        var expectedWidth = custom ? 1.2 : metrics
            ? 12.0 * font.Metrics.UnderlineThickness / font.Metrics.UnitsPerEm : 12.0 / 16;
        width.Should().BeApproximately(expectedWidth, 0.001);
        N(starts[0].Operands[0]).Should().Be(N(starts[1].Operands[0]));
        N(ends[0].Operands[0]).Should().Be(N(ends[1].Operands[0]));
        (N(starts[0].Operands[1]) - N(starts[1].Operands[1])).Should().BeApproximately(2 * width, 0.002);
        N(starts[0].Operands[1]).Should().Be(N(ends[0].Operands[1]));
        N(starts[1].Operands[1]).Should().Be(N(ends[1].Operands[1]));
    }

    [Theory]
    [InlineData(Underline.None, 0)]
    [InlineData(Underline.Single, 1)]
    [InlineData(Underline.Double, 2)]
    public void InheritedStyleDrawsExpectedNumberOfStrokes(Underline underline, int count)
    {
        var document = CreateDocument();
        document.Styles.Normal.Font.Underline = underline;
        document.AddSection().AddParagraph().AddFormattedText("Inherited");
        var renderer = new PdfDocumentRenderer { Document = document };
        renderer.RenderDocument();
        Operators(renderer).Count(op => op.OpCode.Name == "m").Should().Be(count);
    }

    [Fact]
    public void TransitionsBetweenSingleAndDoubleSplitIdenticalPens()
    {
        var document = CreateDocument();
        var paragraph = document.AddSection().AddParagraph();
        paragraph.AddFormattedText("First").Font.Underline = Underline.Single;
        paragraph.AddFormattedText("Middle").Font.Underline = Underline.Double;
        paragraph.AddFormattedText("Last").Font.Underline = Underline.Single;
        var renderer = new PdfDocumentRenderer { Document = document };
        renderer.RenderDocument();
        var operators = Operators(renderer);
        var starts = operators.Where(op => op.OpCode.Name == "m").ToArray();
        var ends = operators.Where(op => op.OpCode.Name == "l").ToArray();
        starts.Should().HaveCount(4);
        N(starts[0].Operands[1]).Should().Be(N(starts[3].Operands[1]));
        N(ends[0].Operands[0]).Should().Be(N(starts[1].Operands[0]));
        N(starts[1].Operands[0]).Should().Be(N(starts[2].Operands[0]));
        N(ends[1].Operands[0]).Should().Be(N(starts[3].Operands[0]));
    }

    [Fact]
    public void CloneAndDdlPreserveDoubleAndSingleOverride()
    {
        var document = CreateDocument();
        var paragraph = document.AddSection().AddParagraph();
        paragraph.Format.Font.Underline = Underline.Double;
        paragraph.Format.Font.Clone().Underline.Should().Be(Underline.Double);
        paragraph.AddFormattedText("Single").Font.Underline = Underline.Single;
        var restored = DdlReader.DocumentFromString(DdlWriter.WriteToString(document))!;
        restored.LastSection.LastParagraph!.Format.Font.Underline.Should().Be(Underline.Double);
        ((FormattedText)restored.LastSection.LastParagraph.Elements[0]!).Font.Underline.Should().Be(Underline.Single);
    }

    [Fact]
    public void RtfUsesDoubleUnderlineControl()
    {
        var document = CreateDocument();
        document.AddSection().AddParagraph("Double").Format.Font.Underline = Underline.Double;
        new RtfDocumentRenderer().RenderToString(document, ".").Should().Contain(@"\uldb");
    }

    static Document CreateDocument()
    {
        var document = new Document();
        document.Styles.Normal.Font.Name = "Arial";
        document.Styles.Normal.Font.Size = 12;
        return document;
    }
    static COperator[] Operators(PdfDocumentRenderer renderer) =>
        ContentReader.ReadContent(renderer.PdfDocument.Pages[0]).OfType<COperator>().ToArray();
    static double N(CObject value) => double.Parse(value.ToString()!, CultureInfo.InvariantCulture);
}
