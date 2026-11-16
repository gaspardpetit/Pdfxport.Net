// MigraDoc - Creating Documents on the Fly
// See the LICENSE file in the solution root for more information.

using System.Globalization;
using PdfSharp.Drawing;
using PdfSharp.Pdf.Content;
using PdfSharp.Pdf.Content.Objects;
using FluentAssertions;
using MigraDoc.DocumentObjectModel;
using MigraDoc.DocumentObjectModel.IO;
using MigraDoc.Rendering;
using PdfSharp.Diagnostics;
using PdfSharp.Fonts;
using PdfSharp.Quality;
using PdfSharp.TestHelper;
using Xunit;

namespace MigraDoc.Tests;

[Collection("PDFsharp")]
public class BaselineShiftTests : IDisposable
{
    public BaselineShiftTests()
    {
        PdfSharpCore.ResetAll();
#if CORE
        GlobalFontSettings.FontResolver = new UnitTestFontResolver();
#endif
    }
    public void Dispose() => PdfSharpCore.ResetAll();

    [Fact]
    public void ExplicitZeroMatchesUnsetGeometry()
    {
        Render(0).Should().BeEquivalentTo(Render(null));
    }

    [Theory]
    [InlineData(-6, false, 0)]
    [InlineData(4, false, 0)]
    [InlineData(-6, true, 0)]
    [InlineData(4, true, 0)]
    [InlineData(-6, true, 1)]
    [InlineData(4, true, 1)]
    [InlineData(-6, true, 2)]
    [InlineData(4, true, 2)]
    public void SignedOffsetMovesPaintWithoutResizingText(double shift, bool excluded, int script)
    {
        var normal = Render(0, excluded: excluded, script: script, exact: !excluded);
        var actual = Render(shift, excluded: excluded, script: script, exact: !excluded);
        actual.Height.Should().BeApproximately(normal.Height, .001);
        actual.Runs.Select(r => r.X).Should().Equal(normal.Runs.Select(r => r.X));
        for (int i = 0; i < actual.Runs.Count; i++)
            actual.Runs[i].Y.Should().BeApproximately(normal.Runs[i].Y +
                (actual.Runs[i].Text == "shifted" ? shift : 0), .002);
    }

    [Theory]
    [InlineData(4, 0, 4)]
    [InlineData(-6, 0, 6)]
    [InlineData(4, -6, 10)]
    public void AutomaticLinesContainRaisedAndLoweredRuns(double first, double second, double extra)
    {
        var normal = Render(0);
        var actual = Render(first, second);
        actual.Height.Should().BeApproximately(normal.Height + 2 * extra, .002);
        actual.Runs.Select(r => r.X).Should().Equal(normal.Runs.Select(r => r.X));
        for (int line = 0; line < 2; line++)
        {
            var runs = actual.Runs.Skip(line * 3).Take(3).ToArray();
            (runs[0].Y - runs[2].Y).Should().BeApproximately(first, .002);
            (runs[1].Y - runs[2].Y).Should().BeApproximately(second, .002);
        }
    }

    [Theory]
    [InlineData(-6, 1)]
    [InlineData(6, 1)]
    [InlineData(-20, 1)]
    [InlineData(20, 1)]
    [InlineData(-6, 2)]
    [InlineData(6, 2)]
    [InlineData(-20, 2)]
    [InlineData(20, 2)]
    public void IncludedScriptsUseTheSameBaselineForPaintAndMetrics(double shift, int script)
    {
        foreach (var scriptFirst in new[] { false, true })
        foreach (var normalSize in new[] { 12, 24 })
        {
            var normal = Render(0, script: script, scriptFirst: scriptFirst, normalSize: normalSize);
            var actual = Render(shift, script: script, scriptFirst: scriptFirst, normalSize: normalSize);
            for (int line = 0; line < 2; line++)
            {
                var before = normal.Runs.Skip(line * 3).Take(3).ToArray();
                var after = actual.Runs.Skip(line * 3).Take(3).ToArray();
                var beforeOffset = before.Single(r => r.Text == "shifted").Y - before.Single(r => r.Text == "normal").Y;
                var afterOffset = after.Single(r => r.Text == "shifted").Y - after.Single(r => r.Text == "normal").Y;
                afterOffset.Should().BeApproximately(beforeOffset + shift, .002);
            }
            var font = new XFont("Arial", 12);
            double ascent = 12.0 * font.Metrics.Ascent / font.FontFamily.GetEmHeight(font.Style);
            double descent = 12.0 * font.Metrics.Descent / font.FontFamily.GetEmHeight(font.Style);
            double scale = .8 * ascent / font.GetHeight();
            var scripts = actual.Runs.Where(r => r.Text == "shifted").ToArray();
            foreach (var run in scripts)
            {
                (run.Y + scale * (font.GetHeight() - descent)).Should().BeLessThanOrEqualTo(actual.Top + .002);
                (run.Y - scale * descent).Should().BeGreaterThanOrEqualTo(actual.Top - actual.Height - .002);
            }
            // The lower edge of one script cannot overlap the upper edge of the next.
            (scripts[0].Y - scripts[1].Y).Should().BeGreaterThanOrEqualTo(scale * font.GetHeight() - .002);
        }
    }

    [Theory]
    [InlineData(-6)]
    [InlineData(6)]
    [InlineData(-20)]
    [InlineData(20)]
    public void AtLeastBottomGapRemainsAdditionalToShiftedEnvelope(double shift)
    {
        foreach (var scriptFirst in new[] { false, true })
        {
            var withoutGap = Render(shift, atLeastBottom: 0, scriptFirst: scriptFirst);
            var withGap = Render(shift, atLeastBottom: 3, scriptFirst: scriptFirst);
            withGap.Height.Should().BeApproximately(withoutGap.Height + 6, .002);
            var first = withGap.Runs.Take(3).Single(r => r.Text == "normal");
            var second = withGap.Runs.Skip(3).Single(r => r.Text == "normal");
            var firstControl = withoutGap.Runs.Take(3).Single(r => r.Text == "normal");
            var secondControl = withoutGap.Runs.Skip(3).Single(r => r.Text == "normal");
            (first.Y - second.Y).Should().BeApproximately(firstControl.Y - secondControl.Y + 3, .002);
        }
    }

    [Theory]
    [InlineData(null)]
    [InlineData(0.0)]
    [InlineData(4.0)]
    [InlineData(-6.0)]
    public void CloneApplyFontAndDdlPreserveShift(double? shift)
    {
        var document = Create();
        var span = document.Sections[0].AddParagraph().AddFormattedText("shifted");
        if (shift.HasValue) span.Font.BaselineShift = Unit.FromPoint(shift.Value);
        span.Font.Clone().Values.BaselineShift.Should().Be(span.Font.Values.BaselineShift);
        var applied = new Font(); applied.ApplyFont(span.Font);
        applied.Values.BaselineShift.Should().Be(span.Font.Values.BaselineShift);
        if (shift.HasValue)
        {
            var restored = DdlReader.DocumentFromString(DdlWriter.WriteToString(document))!;
            var p = restored.Sections[0].Elements[0].Should().BeOfType<Paragraph>().Subject;
            var run = p.Elements[0].Should().BeOfType<FormattedText>().Subject;
            run.Font.BaselineShift.Point.Should().Be(shift.Value);
            run.Font.Values.BaselineShift.Should().NotBeNull();
        }
    }

    [Fact]
    public void NestedFontInheritsShiftAndExplicitZeroOverridesIt()
    {
        var document = Create();
        document.Styles.Normal.Font.BaselineShift = Unit.FromPoint(4);
        var paragraph = document.Sections[0].AddParagraph();
        paragraph.Format.LineSpacingRule = LineSpacingRule.Exactly;
        paragraph.Format.LineSpacing = Unit.FromPoint(20);
        paragraph.AddFormattedText("inherited").Bold = true;
        var reset = paragraph.AddFormattedText("reset");
        reset.Font.BaselineShift = Unit.FromPoint(0);
        var renderer = new PdfDocumentRenderer { Document = document };
        renderer.RenderDocument();
        using var pdf = renderer.PdfDocument;
        var reader = PdfFileHelper.GetPageContentStreamEnumerator(pdf, 0).Text;
        var ys = new List<double>();
        while (reader.MoveAndGetNext(true, out var item)) ys.Add(item!.Y);
        ys.Should().HaveCount(2);
        (ys[0] - ys[1]).Should().BeApproximately(4, .002);
    }

    [Fact]
    public void UnderlineSegmentsFollowTheirOwnBaseline()
    {
        var document = Create();
        var paragraph = document.Sections[0].AddParagraph();
        paragraph.Format.LineSpacingRule = LineSpacingRule.Exactly;
        paragraph.Format.LineSpacing = Unit.FromPoint(20);
        foreach (var shift in new[] { 0, 4, -6, 0 })
        {
            var run = paragraph.AddFormattedText("word");
            run.Font.BaselineShift = Unit.FromPoint(shift);
            run.Underline = Underline.Single;
        }
        var renderer = new PdfDocumentRenderer { Document = document };
        renderer.RenderDocument();
        using var pdf = renderer.PdfDocument;
        var operators = ContentReader.ReadContent(pdf.Pages[0]).OfType<COperator>().ToArray();
        var starts = operators.Where(op => op.OpCode.Name == "m").ToArray();
        var ends = operators.Where(op => op.OpCode.Name == "l").ToArray();
        starts.Should().HaveCount(4);
        ends.Should().HaveCount(4);
        static double N(CObject value) => double.Parse(value.ToString()!, CultureInfo.InvariantCulture);
        var baseline = N(starts[0].Operands[1]);
        var shifts = new[] { 0, 4, -6, 0 };
        for (int i = 0; i < starts.Length; i++)
        {
            N(starts[i].Operands[1]).Should().BeApproximately(baseline + shifts[i], .002);
            N(ends[i].Operands[1]).Should().Be(N(starts[i].Operands[1]));
            if (i > 0) N(starts[i].Operands[0]).Should().Be(N(ends[i - 1].Operands[0]));
        }
    }

    [Fact]
    public void DdlPreservesExplicitZeroAgainstShiftedParagraphFont()
    {
        var document = Create();
        var paragraph = document.Sections[0].AddParagraph();
        paragraph.Format.Font.BaselineShift = Unit.FromPoint(4);
        paragraph.AddFormattedText("reset").Font.BaselineShift = Unit.FromPoint(0);
        var restored = DdlReader.DocumentFromString(DdlWriter.WriteToString(document))!;
        var p = restored.LastSection.LastParagraph!;
        p.Format.Font.BaselineShift.Point.Should().Be(4);
        var run = (FormattedText)p.Elements[0]!;
        run.Font.Values.BaselineShift.Should().NotBeNull();
        run.Font.BaselineShift.Point.Should().Be(0);
    }

    [Theory]
    [InlineData(double.NaN, 0)]
    [InlineData(double.PositiveInfinity, 0)]
    [InlineData(double.NegativeInfinity, 0)]
    [InlineData(double.NaN, 1)]
    [InlineData(double.PositiveInfinity, 1)]
    [InlineData(double.NegativeInfinity, 1)]
    [InlineData(double.NaN, 2)]
    [InlineData(double.PositiveInfinity, 2)]
    [InlineData(double.NegativeInfinity, 2)]
    public void InvalidShiftCannotEnterThroughStoredValues(double value, int path)
    {
        var font = new Font { BaselineShift = Unit.FromPoint(3) };
        var error = Record.Exception(() =>
        {
            if (path == 0) font.BaselineShift = Unit.FromPoint(value);
            else if (path == 1) font.Values.BaselineShift = Unit.FromPoint(value);
            else font.SetValue("BaselineShift", Unit.FromPoint(value));
        });
        error.Should().NotBeNull();
        error!.GetBaseException().Should().BeOfType<ArgumentOutOfRangeException>();
        font.BaselineShift.Point.Should().Be(3);
    }

    record Result(double Top, double Height, List<(string Text, double X, double Y)> Runs);
    static Result Render(double? shift, double second = 0, bool excluded = false,
        int script = 0, bool exact = false, bool scriptFirst = true, int normalSize = 12, double? atLeastBottom = null)
    {
        var document = Create();
        var p = document.Sections[0].AddParagraph();
        p.Format.Font.Size = normalSize;
        if (atLeastBottom.HasValue)
        {
            p.Format.LineSpacingRule = LineSpacingRule.AtLeast;
            p.Format.LineSpacing = Unit.FromPoint(1);
            p.Format.AtLeastBottomLineSpace = Unit.FromPoint(atLeastBottom.Value);
        }
        if (exact)
        {
            p.Format.LineSpacingRule = LineSpacingRule.Exactly;
            p.Format.LineSpacing = Unit.FromPoint(20);
        }
        for (int i = 0; i < 2; i++)
        {
            if (i > 0) p.AddLineBreak();
            if (!scriptFirst)
            {
                p.AddText("normal");
                p.AddFormattedText("second").Font.BaselineShift = Unit.FromPoint(second);
            }
            var run = p.AddFormattedText("shifted");
            run.Font.Size = 12;
            run.ExcludeFromLineMetrics = excluded;
            if (shift.HasValue) run.Font.BaselineShift = Unit.FromPoint(shift.Value);
            if (script == 1) run.Subscript = true;
            if (script == 2) run.Superscript = true;
            if (scriptFirst)
            {
                p.AddFormattedText("second").Font.BaselineShift = Unit.FromPoint(second);
                p.AddText("normal");
            }
        }
        var renderer = new PdfDocumentRenderer { Document = document };
        renderer.RenderDocument();
        using var pdf = renderer.PdfDocument;
        var area = renderer.DocumentRenderer.GetRenderInfoFromPage(1)!.Single().LayoutInfo.ContentArea;
        var reader = PdfFileHelper.GetPageContentStreamEnumerator(pdf, 0).Text;
        var runs = new List<(string, double, double)>();
        while (reader.MoveAndGetNext(true, out var item)) runs.Add((item!.Text, item.X, item.Y));
        runs.Should().HaveCount(6);
        return new Result(pdf.Pages[0].Height.Point - area.Y.Point, area.Height.Point, runs);
    }
    static Document Create()
    {
        var document = new Document();
        document.Styles.Normal.Font.Name = "Arial";
        document.Styles.Normal.Font.Size = 12;
        document.AddSection();
        return document;
    }
}
