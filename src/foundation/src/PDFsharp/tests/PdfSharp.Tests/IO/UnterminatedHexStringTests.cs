// PDFsharp - A .NET library for processing PDF
// See the LICENSE file in the solution root for more information.

using System.Text;
using FluentAssertions;
using PdfSharp.Pdf.IO;
using Xunit;

namespace PdfSharp.Tests.IO;

public class UnterminatedHexStringTests
{
    [Theory]
    [InlineData("<", "")]
    [InlineData("<9", "\u0090")]
    [InlineData("<90", "\u0090")]
    public async Task HexStringAtEofFinishesScanning(string input, string expected)
    {
        var scan = Task.Run(() =>
        {
            using var stream = new MemoryStream(Encoding.ASCII.GetBytes(input));
            var lexer = new Lexer(stream, null);
            lexer.ScanNextToken(false).Should().Be(Symbol.HexString);
            var value = lexer.Token;
            lexer.ScanNextToken(false).Should().Be(Symbol.Eof);
            return value;
        });

        (await scan.WaitAsync(TimeSpan.FromSeconds(3))).Should().Be(expected);
    }

    [Fact]
    public void ClosedOddDigitIsPaddedOnTheRight()
    {
        using var stream = new MemoryStream(Encoding.ASCII.GetBytes("<9>"));
        var lexer = new Lexer(stream, null);

        lexer.ScanNextToken(false).Should().Be(Symbol.HexString);
        lexer.Token.Should().Be("\u0090");
    }
}
