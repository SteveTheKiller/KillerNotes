using System.Linq;
using KillerNotes.Services;
using Xunit;

namespace KillerNotes.Tests
{
    public sealed class CalcEngineTests
    {
        // Keypad glyphs, built from codepoints so this file stays 0 non-ASCII bytes.
        private static readonly string Times = ((char)0x00D7).ToString();
        private static readonly string Minus = ((char)0x2212).ToString();

        private static CalcEngine Feed(params string[] tokens)
        {
            var e = new CalcEngine();
            foreach (var t in tokens) e.Input(t);
            return e;
        }

        private static string[] Digits(string s) => s.Select(c => c.ToString()).ToArray();

        [Fact]
        public void StartsAtZero()
        {
            Assert.Equal("0", new CalcEngine().Display);
        }

        [Fact]
        public void NoPrecedenceMathLikeADeskCalculator()
        {
            // 2 + 3 x 4 = 20, not 14: each operator computes the pending result first.
            var e = Feed("2", "add", "3", "mul", "4", "eq");
            Assert.Equal("20", e.Display);
        }

        [Fact]
        public void EquationTapeShowsEachRunningResult()
        {
            var e = Feed("1", "2", "add", "5", "eq");
            Assert.Equal("17", e.Display);
            e.Input("mul"); e.Input("3"); e.Input("eq");
            Assert.Equal("51", e.Display);
            Assert.Equal("12 + 5 = 17 " + Times + " 3 = 51", e.EquationText());
        }

        [Fact]
        public void EquationTextMidEntryIncludesTheLiveOperand()
        {
            var e = Feed("1", "2", "add", "5");
            Assert.Equal("12 + 5 = 17", e.EquationText());
        }

        [Fact]
        public void EquationTextDegradesToTheBareNumber()
        {
            Assert.Equal("42", Feed("4", "2").EquationText());
        }

        [Fact]
        public void DigitAfterEqualsStartsANewEntryAndDropsTheTape()
        {
            var e = Feed("2", "add", "2", "eq");   // 4
            e.Input("7");
            Assert.Equal("7", e.Display);
            Assert.Equal("7", e.EquationText());   // finished tape dropped, not extended
        }

        [Fact]
        public void OperatorAfterEqualsContinuesFromTheResult()
        {
            var e = Feed("2", "add", "2", "eq", "mul", "5", "eq");
            Assert.Equal("20", e.Display);
        }

        [Fact]
        public void DivideByZeroShowsErrorAndClearIfErrorRecovers()
        {
            var e = Feed("5", "div", "0", "eq");
            Assert.Equal("Error", e.Display);
            e.ClearIfError();
            e.Input("3");
            Assert.Equal("3", e.Display);
        }

        [Fact]
        public void PercentDividesTheLiveOperandByOneHundred()
        {
            var e = Feed("5", "0", "pct");
            Assert.Equal("0.5", e.Display);
        }

        [Fact]
        public void NegTogglesTheSignButNeverOnZero()
        {
            var e = Feed("7", "neg");
            Assert.Equal("-7", e.Display);
            e.Input("neg");
            Assert.Equal("7", e.Display);
            Assert.Equal("0", Feed("neg").Display);
        }

        [Fact]
        public void BackspaceEditsTheLiveOperand()
        {
            var e = Feed(Digits("123").Concat(new[] { "back" }).ToArray());
            Assert.Equal("12", e.Display);
            e.Input("back"); e.Input("back");
            Assert.Equal("0", e.Display);
            e.Input("back");   // fresh zero: nothing to erase
            Assert.Equal("0", e.Display);
        }

        [Fact]
        public void SecondDotIsIgnored()
        {
            var e = Feed("1", "dot", "5", "dot", "5");
            Assert.Equal("1.55", e.Display);
        }

        [Fact]
        public void SwappingTheOperatorBeforeAnOperandUsesTheLastOne()
        {
            // add then mul with no operand between: the mul wins, on the tape too.
            var e = Feed("6", "add", "mul", "7", "eq");
            Assert.Equal("42", e.Display);
            Assert.Equal("6 " + Times + " 7 = 42", e.EquationText());
        }

        [Fact]
        public void SubtractionUsesTheMinusGlyphOnTheTape()
        {
            var e = Feed("9", "sub", "4", "eq");
            Assert.Equal("5", e.Display);
            Assert.Equal("9 " + Minus + " 4 = 5", e.EquationText());
        }

        [Fact]
        public void ClearResetsEverything()
        {
            var e = Feed("8", "add", "1", "clear");
            Assert.Equal("0", e.Display);
            Assert.Equal("0", e.EquationText());
        }

        [Fact]
        public void ShellTokensAreDeclined()
        {
            var e = new CalcEngine();
            Assert.False(e.Input("close"));
            Assert.False(e.Input("print"));
            Assert.False(e.Input("printeq"));
            Assert.True(e.Input("5"));
        }
    }
}
