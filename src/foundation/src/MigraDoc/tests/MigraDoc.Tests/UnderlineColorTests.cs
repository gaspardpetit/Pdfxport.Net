// MigraDoc - Creating Documents on the Fly
// See the LICENSE file in the solution root for more information.

using System.Globalization;
using FluentAssertions;
using MigraDoc.DocumentObjectModel;
using MigraDoc.Rendering;
using MigraDoc.RtfRendering;
using PdfSharp.Diagnostics;
using PdfSharp.Fonts;
using PdfSharp.Pdf.Content;
using PdfSharp.Pdf.Content.Objects;
using PdfSharp.Quality;
using Xunit;

namespace MigraDoc.Tests;

[Collection("PDFsharp")]
public class UnderlineColorTests : IDisposable
{
    public UnderlineColorTests()
    {
        PdfSharpCore.ResetAll();
        GlobalFontSettings.FontResolver = new UnitTestFontResolver();
    }
    public void Dispose() => PdfSharpCore.ResetAll();

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void PdfUnderlineColorIsIndependentAndInherited(bool explicitColor, bool cmyk)
    {
        var document = new Document { UseCmykColor = cmyk };
        document.Styles.Normal.Font.Name = "Arial";
        document.Styles.Normal.Font.Color = Colors.Red;
        document.Styles.Normal.Font.Underline = Underline.Single;
        if (explicitColor)
            document.Styles.Normal.Font.UnderlineColor = Colors.Blue;
        document.AddSection().AddParagraph().AddFormattedText("Underline");
        var renderer = new PdfDocumentRenderer { Document = document };
        renderer.RenderDocument();
        var operators = ContentReader.ReadContent(renderer.PdfDocument.Pages[0]).OfType<COperator>().ToArray();
        var stroke = operators.Single(op => op.OpCode.Name == (cmyk ? "K" : "RG"));
        double N(int index) => double.Parse(stroke.Operands[index].ToString()!, CultureInfo.InvariantCulture);
        if (cmyk)
        {
            // Blue uses cyan and magenta, with no yellow or black.
            N(0).Should().Be(1);
            N(1).Should().Be(1);
            N(2).Should().Be(0);
            N(3).Should().Be(0);
        }
        else
        {
            N(0).Should().Be(explicitColor ? 0 : 1);
            N(1).Should().Be(0);
            N(2).Should().Be(explicitColor ? 1 : 0);
        }
        var fill = operators.Single(op => op.OpCode.Name == (cmyk ? "k" : "rg"));
        var fillValues = fill.Operands.Select(value => double.Parse(value.ToString()!, CultureInfo.InvariantCulture)).ToArray();
        fillValues.Should().Equal(cmyk ? new[] { 0.0, 1.0, 1.0, 0.0 } : new[] { 1.0, 0.0, 0.0 });
    }

    [Fact]
    public void AdjacentRunsKeepDistinctUnderlineColors()
    {
        var document = new Document();
        document.Styles.Normal.Font.Name = "Arial";
        var paragraph = document.AddSection().AddParagraph();
        paragraph.Format.Font.Underline = Underline.Single;
        paragraph.Format.Font.UnderlineColor = Colors.Blue;
        paragraph.AddFormattedText("Inherited");
        paragraph.AddFormattedText("Override").Font.UnderlineColor = Colors.Red;
        var renderer = new PdfDocumentRenderer { Document = document };
        renderer.RenderDocument();
        var colors = ContentReader.ReadContent(renderer.PdfDocument.Pages[0]).OfType<COperator>()
            .Where(op => op.OpCode.Name == "RG")
            .Select(op => string.Join(" ", op.Operands.Select(value => value.ToString()))).ToArray();
        colors.Should().Equal("0 0 1", "1 0 0");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void RtfEmitsUnderlineColorOnlyWhenSet(bool explicitColor)
    {
        var document = new Document();
        var paragraph = document.AddSection().AddParagraph("Underline");
        paragraph.Format.Font.Underline = Underline.Single;
        paragraph.Format.Font.Color = Colors.Red;
        if (explicitColor)
            paragraph.Format.Font.UnderlineColor = Colors.Blue;
        var rtf = new RtfDocumentRenderer().RenderToString(document, ".");
        if (explicitColor)
            rtf.Should().Contain(@"\ulc");
        else
            rtf.Should().NotContain(@"\ulc");
    }
}
