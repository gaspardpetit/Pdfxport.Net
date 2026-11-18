// MigraDoc - Creating Documents on the Fly
// See the LICENSE file in the solution root for more information.

using System.Globalization;
using FluentAssertions;
using MigraDoc.DocumentObjectModel;
using MigraDoc.Rendering;
using PdfSharp.Drawing;
using PdfSharp.Diagnostics;
using PdfSharp.Fonts;
using PdfSharp.Pdf.Content;
using PdfSharp.Pdf.Content.Objects;
using PdfSharp.Quality;
using Xunit;

namespace MigraDoc.Tests;

[Collection("PDFsharp")]
public class SpacedThreeDimensionalBorderTests : IDisposable
{
    public SpacedThreeDimensionalBorderTests()
    {
        PdfSharpCore.ResetAll();
#if CORE
        GlobalFontSettings.FontResolver = new UnitTestFontResolver();
#endif
    }
    public void Dispose() => PdfSharpCore.ResetAll();

    [Theory]
    [InlineData(BorderStyle.Emboss3D, .25, .5)]
    [InlineData(BorderStyle.Engrave3D, .25, .5)]
    [InlineData(BorderStyle.Emboss3D, 3, .5)]
    [InlineData(BorderStyle.Engrave3D, 3, .5)]
    [InlineData(BorderStyle.Emboss3D, .25, 3)]
    [InlineData(BorderStyle.Engrave3D, .25, 3)]
    [InlineData(BorderStyle.Emboss3D, 3, 3)]
    [InlineData(BorderStyle.Engrave3D, 3, 3)]
    public void ClosedSpacedCellsHaveFourMiteredFaces(BorderStyle style, double width, double spacing)
    {
        var document = Create(style, width, spacing, 2);
        var renderer = new PdfDocumentRenderer { Document = document };
        renderer.RenderDocument();
        using var pdf = renderer.PdfDocument;
        var faces = Faces(ContentReader.ReadContent(pdf.Pages[0]).OfType<COperator>());
        faces.Should().HaveCount(16, "each of the four independent cells has four clipped faces");
        for (int cell = 0; cell < 4; cell++)
        {
            var box = faces.Skip(cell * 4).Take(4).ToArray();
            var xs = box.SelectMany(f => f).Select(p => p.X).Distinct().OrderBy(v => v).ToArray();
            var ys = box.SelectMany(f => f).Select(p => p.Y).Distinct().OrderBy(v => v).ToArray();
            xs.Should().HaveCount(4);
            ys.Should().HaveCount(4);
            (xs[1] - xs[0]).Should().BeApproximately(width * BorderStyleDescriptor.For(style).BandMultiplier, .002);
            (xs[3] - xs[2]).Should().BeApproximately(xs[1] - xs[0], .002);
            (ys[1] - ys[0]).Should().BeApproximately(xs[1] - xs[0], .002);
            (ys[3] - ys[2]).Should().BeApproximately(xs[1] - xs[0], .002);
            double ringArea = (xs[3] - xs[0]) * (ys[3] - ys[0]) - (xs[2] - xs[1]) * (ys[2] - ys[1]);
            box.Sum(Area).Should().BeApproximately(ringArea, .02);
            for (int face = 0; face < 4; face++)
                box[face].Intersect(box[(face + 1) % 4]).Should().HaveCount(2,
                    "adjacent shaded faces meet on a shared miter edge");
        }
    }

    [Theory]
    [InlineData(BorderStyle.Emboss3D, false)]
    [InlineData(BorderStyle.Engrave3D, false)]
    [InlineData(BorderStyle.Emboss3D, true)]
    [InlineData(BorderStyle.Engrave3D, true)]
    public void PartialOrMixedBordersKeepTheirIndividualSides(BorderStyle style, bool mixed)
    {
        var document = Create(style, 3, 3, 1);
        var cell = document.LastSection.Elements[0].Should().BeOfType<MigraDoc.DocumentObjectModel.Tables.Table>().Subject[0, 0];
        if (mixed) cell.Borders.Top.Width = Unit.FromPoint(6);
        else cell.Borders.Bottom.Visible = false;
        var renderer = new PdfDocumentRenderer { Document = document };
        renderer.RenderDocument();
        using var pdf = renderer.PdfDocument;
        var operators = ContentReader.ReadContent(pdf.Pages[0]).OfType<COperator>().ToArray();
        Faces(operators).Should().BeEmpty();
        operators.Count(op => op.OpCode.Name == "S").Should().Be(mixed ? 12 : 9);
    }

    public static IEnumerable<object[]> DiagonalCases
    {
        get
        {
            foreach (var style in new[] { BorderStyle.Emboss3D, BorderStyle.Engrave3D })
            foreach (var mask in new[] { 1, 2, 3 })
            foreach (var diagonalStyle in new[] { BorderStyle.Single, BorderStyle.Double })
            foreach (var moved in new[] { false, true })
                yield return new object[] { style, mask, diagonalStyle, moved };
        }
    }

    [Theory]
    [MemberData(nameof(DiagonalCases))]
    public void ClosedSpacedCellsPreserveDiagonals(BorderStyle style, int mask, BorderStyle diagonalStyle, bool moved)
    {
        var document = Create(style, 3, 3, 1);
        var table = (MigraDoc.DocumentObjectModel.Tables.Table)document.LastSection.Elements[0]!;
        var cell = table[0, 0];
        if ((mask & 1) != 0) Configure(cell.Borders.DiagonalDown);
        if ((mask & 2) != 0) Configure(cell.Borders.DiagonalUp);
        var renderer = new PdfDocumentRenderer { Document = document };
        XRect? paintRect = null;
        renderer.DocumentRenderer.TableCellPlacement = (_, geometry) =>
        {
            var original = geometry.DefaultPaintRect;
            paintRect = new XRect(original.X + (moved ? 5 : 0), original.Y + (moved ? 7 : 0),
                original.Width * (moved ? .8 : 1), original.Height + (moved ? 3 : 0));
            return new TableCellPlacement(PaintRect: paintRect, ContentRect: geometry.GridRect);
        };
        renderer.RenderDocument();
        using var pdf = renderer.PdfDocument;
        paintRect.Should().NotBeNull();
        var rect = paintRect!.Value;
        var operators = ContentReader.ReadContent(pdf.Pages[0]).OfType<COperator>().ToArray();
        var lines = new List<(Point Start, Point End)>();
        Point? start = null, end = null;
        foreach (var op in operators)
        {
            if (op.OpCode.Name == "m") { start = new Point(N(op.Operands[0]), N(op.Operands[1])); end = null; }
            if (op.OpCode.Name == "l") end = new Point(N(op.Operands[0]), N(op.Operands[1]));
            if (op.OpCode.Name == "S" && start != null && end != null) lines.Add((start, end));
        }
        int diagonalCount = mask == 3 ? 2 : 1;
        int strokes = diagonalStyle == BorderStyle.Double ? 2 : 1;
        lines.Should().HaveCount(diagonalCount * strokes);
        double top = pdf.Pages[0].Height.Point - rect.Y, bottom = top - rect.Height;
        foreach (var direction in new[] { 1, 2 })
        {
            var group = lines.Where(line => direction == 1 ? line.End.Y < line.Start.Y : line.End.Y > line.Start.Y).ToArray();
            group.Should().HaveCount((mask & direction) == 0 ? 0 : strokes);
            if (group.Length == 0) continue;
            // Double diagonals straddle the same corner-to-corner centerline.
            group.Average(line => line.Start.X).Should().BeApproximately(rect.X, .002);
            group.Average(line => line.End.X).Should().BeApproximately(rect.Right, .002);
            group.Average(line => line.Start.Y).Should().BeApproximately(direction == 1 ? top : bottom, .002);
            group.Average(line => line.End.Y).Should().BeApproximately(direction == 1 ? bottom : top, .002);
        }
        var clips = Faces(operators);
        clips.Should().HaveCount(4 + diagonalCount);
        foreach (var clip in clips.Skip(4))
        {
            clip.Min(p => p.X).Should().BeApproximately(rect.X, .002);
            clip.Max(p => p.X).Should().BeApproximately(rect.Right, .002);
            clip.Min(p => p.Y).Should().BeApproximately(bottom, .002);
            clip.Max(p => p.Y).Should().BeApproximately(top, .002);
        }

        void Configure(Border border)
        {
            border.Visible = true;
            border.Style = diagonalStyle;
            border.Width = Unit.FromPoint(1);
            border.Color = Colors.Red;
        }
    }

    [Theory]
    [InlineData(BorderStyle.Emboss3D)]
    [InlineData(BorderStyle.Engrave3D)]
    public void HiddenDiagonalsRemainHidden(BorderStyle style)
    {
        var document = Create(style, 3, 3, 1);
        var table = (MigraDoc.DocumentObjectModel.Tables.Table)document.LastSection.Elements[0]!;
        foreach (var border in new[] { table[0, 0].Borders.DiagonalDown, table[0, 0].Borders.DiagonalUp })
        {
            border.Style = BorderStyle.Single;
            border.Width = Unit.FromPoint(1);
            border.Visible = false;
        }
        var renderer = new PdfDocumentRenderer { Document = document };
        renderer.RenderDocument();
        using var pdf = renderer.PdfDocument;
        var operators = ContentReader.ReadContent(pdf.Pages[0]).OfType<COperator>().ToArray();
        operators.Should().NotContain(op => op.OpCode.Name == "S");
        Faces(operators).Should().HaveCount(4);
    }

    static Document Create(BorderStyle style, double width, double spacing, int size)
    {
        var document = new Document();
        document.Styles.Normal.Font.Name = "Arial";
        var table = document.AddSection().AddTable();
        table.CellSpacing = Unit.FromPoint(spacing);
        for (int col = 0; col < size; col++) table.AddColumn("3cm");
        for (int row = 0; row < size; row++)
        {
            var cells = table.AddRow();
            for (int col = 0; col < size; col++)
            {
                cells[col].Borders.Style = style;
                cells[col].Borders.Width = Unit.FromPoint(width);
                cells[col].AddParagraph("Cell");
            }
        }
        return document;
    }

    static List<Point[]> Faces(IEnumerable<COperator> operators)
    {
        var result = new List<Point[]>();
        var points = new List<Point>();
        foreach (var op in operators)
        {
            if (op.OpCode.Name == "m") points.Clear();
            if (op.OpCode.Name is "m" or "l") points.Add(new Point(N(op.Operands[0]), N(op.Operands[1])));
            if (op.OpCode.Name is "W" or "W*")
            {
                if (points.Count == 4) result.Add(points.ToArray());
                points.Clear();
            }
        }
        return result;
    }
    static double Area(Point[] polygon)
    {
        var origin = polygon[0];
        double sum = 0;
        for (int i = 0; i < polygon.Length; i++)
        {
            var a = polygon[i]; var b = polygon[(i + 1) % polygon.Length];
            sum += (a.X - origin.X) * (b.Y - origin.Y) - (b.X - origin.X) * (a.Y - origin.Y);
        }
        return Math.Abs(sum) / 2;
    }
    static double N(CObject value) => double.Parse(value.ToString()!, CultureInfo.InvariantCulture);
    record Point(double X, double Y);
}
