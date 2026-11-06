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
public class ThickUnderlineTests : IDisposable
{
    public ThickUnderlineTests()
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
    public void ThickStrokeUsesTwiceTheOrdinaryWidth(bool metrics, bool custom)
    {
        var document = CreateDocument();
        document.AddSection().AddParagraph("Thick").Format.Font.Underline = Underline.Thick;
        var renderer = new PdfDocumentRenderer { Document = document };
        renderer.DocumentRenderer.UseFontUnderlineMetrics = metrics;
        if (custom)
            renderer.DocumentRenderer.UnderlineGeometry = _ => (0.1, 0.5);
        renderer.RenderDocument();
        var operators = Operators(renderer);
        operators.Count(op => op.OpCode.Name == "m").Should().Be(1);
        var font = new XFont("Arial", 12);
        var ordinaryWidth = custom ? 1.2 : metrics
            ? 12.0 * font.Metrics.UnderlineThickness / font.Metrics.UnitsPerEm : 12.0 / 16;
        N(operators.Single(op => op.OpCode.Name == "w").Operands[0])
            .Should().BeApproximately(2 * ordinaryWidth, 0.001);
    }

    [Theory]
    [InlineData(Underline.None, 0)]
    [InlineData(Underline.Single, 1)]
    [InlineData(Underline.Thick, 1)]
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
    public void AdjacentSingleAndThickRunsHaveDifferentStrokeWidths()
    {
        var document = CreateDocument();
        var paragraph = document.AddSection().AddParagraph();
        paragraph.AddFormattedText("First").Font.Underline = Underline.Single;
        paragraph.AddFormattedText("Middle").Font.Underline = Underline.Thick;
        paragraph.AddFormattedText("Last").Font.Underline = Underline.Single;
        var renderer = new PdfDocumentRenderer { Document = document };
        renderer.RenderDocument();
        var operators = Operators(renderer);
        operators.Count(op => op.OpCode.Name == "m").Should().Be(3);
        operators.Where(op => op.OpCode.Name == "w").Select(op => N(op.Operands[0]))
            .Should().Equal(0.75, 1.5, 0.75);
    }

    [Fact]
    public void CloneAndDdlPreserveThickAndSingleOverride()
    {
        var document = CreateDocument();
        var paragraph = document.AddSection().AddParagraph();
        paragraph.Format.Font.Underline = Underline.Thick;
        paragraph.Format.Font.Clone().Underline.Should().Be(Underline.Thick);
        paragraph.AddFormattedText("Single").Font.Underline = Underline.Single;
        var restored = DdlReader.DocumentFromString(DdlWriter.WriteToString(document))!;
        restored.LastSection.LastParagraph!.Format.Font.Underline.Should().Be(Underline.Thick);
        ((FormattedText)restored.LastSection.LastParagraph.Elements[0]!).Font.Underline.Should().Be(Underline.Single);
    }

    [Fact]
    public void RtfUsesThickUnderlineControl()
    {
        var document = CreateDocument();
        document.AddSection().AddParagraph("Thick").Format.Font.Underline = Underline.Thick;
        new RtfDocumentRenderer().RenderToString(document, ".").Should().Contain(@"\ulth");
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
