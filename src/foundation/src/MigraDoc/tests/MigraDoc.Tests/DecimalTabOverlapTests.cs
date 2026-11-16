// MigraDoc - Creating Documents on the Fly
// See the LICENSE file in the solution root for more information.

using System.Globalization;
using FluentAssertions;
using MigraDoc.DocumentObjectModel;
using MigraDoc.Rendering;
using PdfSharp.Diagnostics;
using PdfSharp.Drawing;
using PdfSharp.Fonts;
using PdfSharp.Quality;
using PdfSharp.TestHelper;
using Xunit;

namespace MigraDoc.Tests;

[Collection("PDFsharp")]
public class DecimalTabOverlapTests : IDisposable
{
    public DecimalTabOverlapTests()
    {
        PdfSharpCore.ResetAll();
#if CORE
        GlobalFontSettings.FontResolver = new UnitTestFontResolver();
#endif
    }
    public void Dispose() => PdfSharpCore.ResetAll();

    [Theory]
    [InlineData("LongFollowingText", 80, "Prefix")]
    [InlineData("MuchLongerFollowingText", 80, "Prefix")]
    [InlineData("Short", 180, "Prefix")]
    [InlineData("LongFollowingText", 80, "")]
    [InlineData("Short", 180, "")]
    public void NonnumericTextUsesRightTabFallback(string following, double stop, string prefix)
    {
        var reference = Render(TabAlignment.Right, following, stop, prefix);
        var actual = Render(TabAlignment.Decimal, following, stop, prefix);
        actual.Should().Equal(reference);
    }

    [Theory]
    [InlineData("en-US", "12.34", "12")]
    [InlineData("de-DE", "12,34", "12")]
    [InlineData("en-US", ".34", "")]
    [InlineData("en-US", "1234", "1234")]
    public void NumericAnchorsKeepTheirAlignment(string culture, string text, string beforeSeparator)
    {
        const double stop = 180;
        var result = Render(TabAlignment.Decimal, text, stop, "Prefix", culture);
        using var gfx = XGraphics.CreateMeasureContext(new XSize(1000, 1000),
            XGraphicsUnit.Point, XPageDirection.Downwards);
        var width = gfx.MeasureString(beforeSeparator, new XFont("Arial", 12)).Width;
        result.Last().X.Should().BeApproximately(50 + stop - width, .002);
        result.Last().Y.Should().BeApproximately(result.First().Y, .002);
    }

    static List<(string Text, double X, double Y)> Render(TabAlignment alignment, string following,
        double stop, string prefix, string culture = "en-US")
    {
        var document = new Document();
        document.Styles.Normal.Font.Name = "Arial";
        document.Styles.Normal.Font.Size = 12;
        document.Culture = CultureInfo.GetCultureInfo(culture);
        var section = document.AddSection();
        section.PageSetup.LeftMargin = Unit.FromPoint(50);
        var p = section.AddParagraph(prefix);
        p.Format.TabStops.AddTabStop(Unit.FromPoint(stop), alignment);
        p.AddTab(); p.AddText(following);
        var renderer = new PdfDocumentRenderer { Document = document };
        renderer.RenderDocument();
        using var pdf = renderer.PdfDocument;
        var reader = PdfFileHelper.GetPageContentStreamEnumerator(pdf, 0).Text;
        var result = new List<(string, double, double)>();
        while (reader.MoveAndGetNext(true, out var item))
            result.Add((item!.Text, item.X, item.Y));
        result.Should().HaveCount(prefix.Length == 0 ? 1 : 2);
        return result;
    }
}
