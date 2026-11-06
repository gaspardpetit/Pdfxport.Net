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
public class WavyUnderlineTests : IDisposable
{
    public WavyUnderlineTests()
    {
        PdfSharpCore.ResetAll();
        GlobalFontSettings.FontResolver = new UnitTestFontResolver();
    }
    public void Dispose() => PdfSharpCore.ResetAll();

    [Theory]
    [InlineData(false, false, "Wavy", false)]
    [InlineData(true, false, "Wavy", false)]
    [InlineData(false, true, "Wavy", false)]
    [InlineData(true, true, "Wavy", false)]
    [InlineData(false, false, "i", false)]
    [InlineData(false, true, "i", true)]
    public void WaveUsesCurvesInsideRunClip(bool metrics, bool custom, string text, bool hairline)
    {
        var document = CreateDocument();
        document.AddSection().AddParagraph(text).Format.Font.Underline = Underline.Wavy;
        var renderer = new PdfDocumentRenderer { Document = document };
        renderer.DocumentRenderer.UseFontUnderlineMetrics = metrics;
        if (custom)
            renderer.DocumentRenderer.UnderlineGeometry = _ => (hairline ? 0 : 0.1, 0.5);
        renderer.RenderDocument();
        var operators = Operators(renderer);
        var curves = operators.Where(op => op.OpCode.Name == "c").ToArray();
        curves.Should().NotBeEmpty();
        var clipIndex = Array.FindIndex(operators, op => op.OpCode.Name == "W" || op.OpCode.Name == "W*");
        clipIndex.Should().BeGreaterThan(0);
        var clipXs = operators.Take(clipIndex).Where(op => op.OpCode.Name is "m" or "l")
            .Select(op => N(op.Operands[0])).ToArray();
        var left = clipXs.Min();
        var right = clipXs.Max();
        right.Should().BeGreaterThan(left);
        foreach (var curve in curves)
            for (var index = 0; index < 6; index += 2)
                N(curve.Operands[index]).Should().BeInRange(left - 0.002, right + 0.002);
        operators.Should().Contain(op => op.OpCode.Name == "W" || op.OpCode.Name == "W*");
        var width = N(operators.Single(op => op.OpCode.Name == "w").Operands[0]);
        var font = new XFont("Arial", 12);
        using var measure = XGraphics.CreateMeasureContext(new XSize(200, 200), XGraphicsUnit.Point, XPageDirection.Downwards);
        (right - left).Should().BeApproximately(measure.MeasureString(text, font).Width, 0.002);
        var expectedWidth = custom ? (hairline ? 0 : 1.2) : metrics
            ? 12.0 * font.Metrics.UnderlineThickness / font.Metrics.UnitsPerEm : 0.75;
        width.Should().BeApproximately(expectedWidth, 0.001);
    }

    [Fact]
    public void SingleWavySingleTransitionsRestoreStraightStrokes()
    {
        var document = CreateDocument();
        var paragraph = document.AddSection().AddParagraph();
        paragraph.AddFormattedText("First").Font.Underline = Underline.Single;
        paragraph.AddFormattedText("Wave").Font.Underline = Underline.Wavy;
        paragraph.AddFormattedText("Last").Font.Underline = Underline.Single;
        var renderer = new PdfDocumentRenderer { Document = document };
        renderer.RenderDocument();
        var operators = Operators(renderer);
        var clipIndex = Array.FindIndex(operators, op => op.OpCode.Name is "W" or "W*");
        var saveIndex = Array.FindLastIndex(operators, clipIndex, op => op.OpCode.Name == "q");
        var restoreIndex = Array.FindIndex(operators, clipIndex, op => op.OpCode.Name == "Q");
        var straight = operators.Take(saveIndex).Concat(operators.Skip(restoreIndex + 1)).ToArray();
        straight.Count(op => op.OpCode.Name == "m").Should().Be(2);
        straight.Count(op => op.OpCode.Name == "l").Should().Be(2);
        restoreIndex.Should().BeLessThan(Array.FindLastIndex(operators, op => op.OpCode.Name == "l"));
    }

    [Fact]
    public void StyleInheritedByNestedTextRendersWave()
    {
        var document = CreateDocument();
        document.Styles.Normal.Font.Underline = Underline.Wavy;
        document.AddSection().AddParagraph().AddFormattedText("Inherited");
        var renderer = new PdfDocumentRenderer { Document = document };
        renderer.RenderDocument();
        Operators(renderer).Should().Contain(op => op.OpCode.Name == "c");
    }

    [Fact]
    public void CloneAndDdlPreserveWavyAndSingleOverride()
    {
        var document = CreateDocument();
        var paragraph = document.AddSection().AddParagraph();
        paragraph.Format.Font.Underline = Underline.Wavy;
        paragraph.Format.Font.Clone().Underline.Should().Be(Underline.Wavy);
        paragraph.AddFormattedText("Single").Font.Underline = Underline.Single;
        var restored = DdlReader.DocumentFromString(DdlWriter.WriteToString(document))!;
        restored.LastSection.LastParagraph!.Format.Font.Underline.Should().Be(Underline.Wavy);
        ((FormattedText)restored.LastSection.LastParagraph.Elements[0]!).Font.Underline.Should().Be(Underline.Single);
    }

    [Fact]
    public void RtfUsesWavyUnderlineControl()
    {
        var document = CreateDocument();
        document.AddSection().AddParagraph("Wavy").Format.Font.Underline = Underline.Wavy;
        new RtfDocumentRenderer().RenderToString(document, ".").Should().Contain(@"\ulwave");
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
