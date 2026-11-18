// MigraDoc - Creating Documents on the Fly
// See the LICENSE file in the solution root for more information.

using System.Globalization;
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
public class BorderGeometryApiTests : IDisposable
{
    public BorderGeometryApiTests()
    {
        PdfSharpCore.ResetAll();
#if CORE
        GlobalFontSettings.FontResolver = new UnitTestFontResolver();
#endif
    }
    public void Dispose() => PdfSharpCore.ResetAll();

    public static IEnumerable<object[]> RailStyles => Enum.GetValues<BorderStyle>()
        .Where(style => BorderStyleDescriptor.For(style).Kind == BorderDrawingKind.Rails)
        .SelectMany(style => new[] { new object[] { style, .25 }, new object[] { style, 3.0 } });

    [Theory]
    [MemberData(nameof(RailStyles))]
    public void PublicRailGeometryMatchesPaintedBand(BorderStyle style, double nominalWidth)
    {
        var descriptor = BorderStyleDescriptor.For(style);
        var document = new Document();
        document.Styles.Normal.Font.Name = "Arial";
        var table = document.AddSection().AddTable();
        table.AddColumn("3cm");
        var cell = table.AddRow()[0];
        cell.Borders.Top.Visible = cell.Borders.Bottom.Visible = cell.Borders.Right.Visible = false;
        cell.Borders.Left.Visible = true;
        cell.Borders.Left.Style = style;
        cell.Borders.Left.Width = Unit.FromPoint(nominalWidth);
        cell.AddParagraph("Cell");
        var renderer = new PdfDocumentRenderer { Document = document };
        renderer.RenderDocument();
        using var pdf = renderer.PdfDocument;
        var rails = new List<(double X, double Width)>();
        double width = 1, x = 0, y = 0;
        foreach (var op in ContentReader.ReadContent(pdf.Pages[0]).OfType<COperator>())
        {
            if (op.OpCode.Name == "w") width = N(op.Operands[0]);
            if (op.OpCode.Name == "m") { x = N(op.Operands[0]); y = N(op.Operands[1]); }
            if (op.OpCode.Name == "l" && Math.Abs(N(op.Operands[0]) - x) < .001 &&
                Math.Abs(N(op.Operands[1]) - y) > 5)
                rails.Add((x, width));
        }
        var sorted = rails.OrderBy(r => r.X).ToArray();
        sorted.Should().HaveCount(descriptor.Rails.Count);
        double left = sorted[0].X - sorted[0].Width / 2;
        double right = sorted[^1].X + sorted[^1].Width / 2;
        (right - left).Should().BeApproximately(descriptor.BandMultiplier * nominalWidth, .002);
        for (int i = 0; i < sorted.Length; i++)
        {
            sorted[i].Width.Should().BeApproximately(descriptor.Rails.WidthAt(i) * nominalWidth, .002);
            if (i > 0)
                (sorted[i].X - sorted[i].Width / 2 - sorted[i - 1].X - sorted[i - 1].Width / 2)
                    .Should().BeApproximately(descriptor.Rails.Gap * nominalWidth, .002);
        }
        Action below = () => descriptor.Rails.WidthAt(-1);
        Action above = () => descriptor.Rails.WidthAt(descriptor.Rails.Count);
        below.Should().Throw<ArgumentOutOfRangeException>();
        above.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Theory]
    [InlineData(BorderStyle.Single)]
    [InlineData(BorderStyle.Wave)]
    [InlineData(BorderStyle.DoubleWave)]
    [InlineData(BorderStyle.DashDotStroked)]
    [InlineData(BorderStyle.Emboss3D)]
    public void NonRailStylesHaveAnEmptyProfile(BorderStyle style)
    {
        var rails = BorderStyleDescriptor.For(style).Rails;
        rails.Count.Should().Be(0);
        rails.BandMultiplier.Should().Be(0);
        Action read = () => rails.WidthAt(0);
        read.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void DefaultProfileIsEmptyAndRejectsRailAccess()
    {
        var profile = default(BorderRailProfile);
        profile.Count.Should().Be(0);
        profile.BandMultiplier.Should().Be(0);
        Action read = () => profile.WidthAt(0);
        read.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Theory]
    [InlineData(BorderStyle.None)]
    [InlineData((BorderStyle)int.MaxValue)]
    public void NonDrawingAndUnrecognizedStylesRetainOrdinaryStrokeMetadata(BorderStyle style)
    {
        BorderStyleDescriptor.For(style).Should().Be(BorderStyleDescriptor.For(BorderStyle.Single));
    }

    [Fact]
    public void DefaultDescriptorHasAnEmptyBand()
    {
        default(BorderStyleDescriptor).BandMultiplier.Should().Be(0);
    }

    static double N(CObject value) => double.Parse(value.ToString()!, CultureInfo.InvariantCulture);
}
