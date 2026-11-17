// MigraDoc - Creating Documents on the Fly
// See the LICENSE file in the solution root for more information.

using FluentAssertions;
using MigraDoc.DocumentObjectModel;
using MigraDoc.Rendering;
using PdfSharp.Diagnostics;
using PdfSharp.Fonts;
using PdfSharp.Pdf.Content;
using PdfSharp.Pdf.Content.Objects;
using PdfSharp.Quality;
using Xunit;

namespace MigraDoc.Tests;

[Collection("PDFsharp")]
public class HatchedBorderColorTests : IDisposable
{
    public HatchedBorderColorTests()
    {
        PdfSharpCore.ResetAll();
#if CORE
        GlobalFontSettings.FontResolver = new UnitTestFontResolver();
#endif
    }
    public void Dispose() => PdfSharpCore.ResetAll();

    [Theory]
    [InlineData(false, 0)]
    [InlineData(true, 0)]
    [InlineData(false, 1)]
    [InlineData(true, 1)]
    [InlineData(false, 2)]
    [InlineData(true, 2)]
    [InlineData(false, 3)]
    [InlineData(true, 3)]
    public void HatchStrokesUseTheConfiguredBorderColorAndOpacity(bool vertical, int colorCase)
    {
        var expected = Render(BorderStyle.Single);
        var actual = Render(BorderStyle.DashDotStroked);
        actual.Color.Should().Be(expected.Color);
        actual.Alphas.Should().Equal(expected.Alphas);

        (string Color, double[] Alphas) Render(BorderStyle style)
        {
            var color = colorCase switch
            {
                0 => Colors.Black,
                1 => Color.FromArgb(128, 37, 83, 149),
                2 => Color.FromCmyk(12, 34, 56, 7),
                _ => Color.FromCmyk(43, 12, 34, 56, 7)
            };
            var document = new Document { UseCmykColor = colorCase >= 2 };
            document.Styles.Normal.Font.Name = "Arial";
            var table = document.AddSection().AddTable();
            table.AddColumn(Unit.FromPoint(37));
            var cell = table.AddRow()[0];
            cell.Borders.Top.Visible = cell.Borders.Bottom.Visible =
                cell.Borders.Left.Visible = cell.Borders.Right.Visible = false;
            var border = vertical ? cell.Borders.Left : cell.Borders.Bottom;
            border.Visible = true;
            border.Style = style;
            border.Width = Unit.FromPoint(3);
            border.Color = color;
            cell.AddParagraph("Cell");
            var renderer = new PdfDocumentRenderer { Document = document };
            renderer.RenderDocument();
            using var pdf = renderer.PdfDocument;
            var operators = ContentReader.ReadContent(pdf.Pages[0]).OfType<COperator>().ToArray();
            var strokeColor = operators.LastOrDefault(op => op.OpCode.Name is "RG" or "K" or "G");
            string colorKey = strokeColor == null ? "0 G" :
                string.Join(" ", strokeColor.Operands.Select(v => v.ToString())) + " " + strokeColor.OpCode.Name;
            var resources = pdf.Pages[0].Resources.Elements.GetDictionary("/ExtGState");
            var alphas = resources == null ? Array.Empty<double>() : resources.Elements.Keys
                .Select(key => resources.Elements.GetDictionary(key)!)
                .Where(state => state.Elements.ContainsKey("/CA"))
                .Select(state => state.Elements.GetReal("/CA")).Distinct().OrderBy(a => a).ToArray();
            return (colorKey, alphas);
        }
    }
}
