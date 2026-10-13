using System.Numerics;

namespace Calcy.Modes.Programmer.Tests;

public class ExpressionTests
{
    private static readonly IntegerFormat S16 = new(16, true);
    private static readonly IntegerFormat U8 = new(8, false);
    private static readonly IntegerFormat S8 = new(8, true);

    private static BigInteger Dec(string text, IntegerFormat? f = null, IVariableScope? scope = null) =>
        ExpressionEvaluator.EvaluateValue(text, f ?? new IntegerFormat(32, true), NumberBase.Dec, scope);

    [Theory]
    [InlineData("1 + 2 * 3", 7)]
    [InlineData("(1 + 2) * 3", 9)]
    [InlineData("1 << 2 + 1", 8)]
    [InlineData("6 & 3 == 3", 0)]
    [InlineData("1 | 2 ^ 3 & 4", 3)]
    [InlineData("~0 & 0xF", 15)]
    [InlineData("-2 * 3", -6)]
    [InlineData("10 - 4 - 3", 3)]
    [InlineData("100 / 10 / 5", 2)]
    [InlineData("2 * 3 % 4", 2)]
    [InlineData("1 ? 2 : 3", 2)]
    [InlineData("0 ? 2 : 0 ? 3 : 4", 4)]
    [InlineData("1 < 2 && 2 < 3", 1)]
    [InlineData("!0 + !5", 1)]
    [InlineData("0 || 5", 1)]
    [InlineData("3 > 2 == 1", 1)]
    [InlineData("- -5", 5)]
    [InlineData("~~7", 7)]
    [InlineData("1 + 2 == 3 ? 10 : 20", 10)]
    public void Precedence_follows_C(string text, long expected) => Assert.Equal(expected, Dec(text));

    [Theory]
    [InlineData("0b1010", 10)]
    [InlineData("0o17", 15)]
    [InlineData("0x1F", 31)]
    [InlineData("0XfF", 255)]
    [InlineData("1_000", 1000)]
    [InlineData("0xFFu", 255)]
    [InlineData("42UL", 42)]
    public void Literals(string text, long expected) => Assert.Equal(expected, Dec(text));

    [Fact]
    public void The_example_from_the_spec_uses_a_variable()
    {
        var scope = new VariableScope();
        scope.Set("x", 5);
        Assert.Equal(40, Dec("0xFF & (x << 3)", scope: scope));
        Assert.Equal(0xF8, Dec("0xFF & (x << 3 | 0xF0)", scope: scope));
    }

    [Fact]
    public void Arithmetic_overflow_wraps_and_says_so()
    {
        var r = ExpressionEvaluator.Evaluate("0x7FFF + 1", S16, NumberBase.Dec);
        Assert.Equal(-32768, r.Value);
        Assert.Contains("overflow wrapped", r.Notes);
    }

    [Fact]
    public void Overflow_flag_mode_raises_an_error() =>
        Assert.Throws<ProgrammerException>(() => ExpressionEvaluator.Evaluate("0x7FFF + 1", S16.WithOverflow(OverflowMode.Flag), NumberBase.Dec));

    [Fact]
    public void Saturate_mode_clamps_in_expressions() =>
        Assert.Equal(32767, ExpressionEvaluator.EvaluateValue("30000 + 30000", S16.WithOverflow(OverflowMode.Saturate)));

    [Theory]
    [InlineData("B5 AND 0F", 5)]
    [InlineData("FF", 255)]
    [InlineData("0b11", 3)]
    [InlineData("0B", 11)]
    [InlineData("0b12", 0xB12)]
    [InlineData("a + b", 21)]
    [InlineData("10", 16)]
    [InlineData("0d", 13)]
    public void Hex_input_base_reads_bare_digits_as_hex(string text, long expected) =>
        Assert.Equal(expected, ExpressionEvaluator.EvaluateValue(text, new IntegerFormat(32, false), NumberBase.Hex));

    [Theory]
    [InlineData("777", NumberBase.Oct, 511)]
    [InlineData("1010", NumberBase.Bin, 10)]
    [InlineData("1010 + 0x10", NumberBase.Bin, 26)]
    public void Other_input_bases(string text, NumberBase b, long expected) =>
        Assert.Equal(expected, ExpressionEvaluator.EvaluateValue(text, new IntegerFormat(32, false), b));

    [Fact]
    public void A_negative_literal_does_not_overflow()
    {
        var r = ExpressionEvaluator.Evaluate("-128", S8, NumberBase.Dec);
        Assert.Equal(-128, r.Value);
        Assert.Empty(r.Notes);
        Assert.Equal(-128, ExpressionEvaluator.EvaluateValue("-0x80", S8));
    }

    [Fact]
    public void Signed_display_of_0xFF_in_8_bit_is_minus_one()
    {
        Assert.Equal(-1, ExpressionEvaluator.EvaluateValue("0xFF", S8));
        Assert.Equal("-1", BaseFormatter.Format(S8, ExpressionEvaluator.EvaluateValue("0xFF", S8), NumberBase.Dec));
        Assert.Equal(255, ExpressionEvaluator.EvaluateValue("0xFF", U8));
    }

    [Theory]
    [InlineData("5 NAND 3", 0xFE)]
    [InlineData("5 NOR 3", 0xF8)]
    [InlineData("5 xnor 3", 0xF9)]
    [InlineData("0x81 ROL 1", 0x03)]
    [InlineData("0x81 ror 1", 0xC0)]
    [InlineData("0xF0 SHR 4", 0x0F)]
    [InlineData("0xF0 >>> 4", 0x0F)]
    [InlineData("1 SHL 7", 0x80)]
    [InlineData("12 MOD 5", 2)]
    [InlineData("NOT 0", 0xFF)]
    [InlineData("0xF0 AND 0x3C OR 1", 0x31)]
    [InlineData("0xF0 XOR 0xFF", 0x0F)]
    public void Word_operators(string text, long expected) => Assert.Equal(expected, ExpressionEvaluator.EvaluateValue(text, U8));

    [Theory]
    [InlineData("-16 SAR 2", -4)]
    [InlineData("-16 >> 2", -4)]
    [InlineData("-16 >>> 2", 60)]
    [InlineData("-1 < 0", 1)]
    [InlineData("0x80 < 0", 1)]
    public void Signed_aware_operators(string text, long expected) => Assert.Equal(expected, ExpressionEvaluator.EvaluateValue(text, S8));

    [Fact]
    public void Unsigned_comparisons_treat_all_ones_as_large() =>
        Assert.Equal(0, ExpressionEvaluator.EvaluateValue("0xFFFF < 0", new IntegerFormat(16, false)));

    [Theory]
    [InlineData("popcount(0xB5)", 5)]
    [InlineData("clz(1)", 15)]
    [InlineData("ctz(8)", 3)]
    [InlineData("parity(7)", 1)]
    [InlineData("bswap(0x1234)", 0x3412)]
    [InlineData("bitrev(1)", -32768)]
    [InlineData("sext(0x80, 8)", -128)]
    [InlineData("zext(-1, 4)", 15)]
    [InlineData("bits(0xB5, 7, 4)", 11)]
    [InlineData("insert(0xB5, 7, 4, 3)", 0x35)]
    [InlineData("mask(4)", 15)]
    [InlineData("mask(7, 4)", 0xF0)]
    [InlineData("bit(3)", 8)]
    [InlineData("rol(0x8001, 4)", 0x0018)]
    [InlineData("ror(1)", -32768)]
    [InlineData("rcl(0x8000, 1, 0)", 0)]
    [InlineData("rcr(0, 1, 1)", -32768)]
    [InlineData("shl(1, 4)", 16)]
    [InlineData("shr(-1, 12)", 15)]
    [InlineData("sar(-256, 4)", -16)]
    [InlineData("abs(-5)", 5)]
    [InlineData("min(3, -2, 5)", -2)]
    [InlineData("max(3, -2, 5)", 5)]
    [InlineData("i8(255)", -1)]
    [InlineData("u8(-1)", 255)]
    [InlineData("nand(5, 3)", -2)]
    [InlineData("and(7, 6, 12)", 4)]
    [InlineData("INT_MAX", 32767)]
    [InlineData("INT_MIN", -32768)]
    [InlineData("UINT_MAX", -1)]
    [InlineData("bits + 1", 17)]
    public void Functions_and_constants(string text, long expected) => Assert.Equal(expected, ExpressionEvaluator.EvaluateValue(text, S16));

    [Fact]
    public void Shifts_by_the_width_or_more_give_zero()
    {
        Assert.Equal(0, ExpressionEvaluator.EvaluateValue("1 << 16", S16));
        Assert.Equal(0, ExpressionEvaluator.EvaluateValue("1 << 100", S16));
        Assert.Equal(-1, ExpressionEvaluator.EvaluateValue("-5 >> 99", S16));
    }

    [Fact]
    public void Assignments_report_the_name()
    {
        var r = ExpressionEvaluator.Evaluate("flags = 0xF0", S16, NumberBase.Dec);
        Assert.Equal("flags", r.AssignedName);
        Assert.Equal(0xF0, r.Value);
    }

    [Fact]
    public void Card_references_and_ans()
    {
        var scope = new VariableScope();
        scope.SetCard(1, 21);
        scope.Set("ans", 4);
        Assert.Equal(42, Dec("#1 * 2", scope: scope));
        Assert.Equal(5, Dec("ans + 1", scope: scope));
        Assert.Equal(0, Dec("ans"));
    }

    [Theory]
    [InlineData("1 +")]
    [InlineData("(1 + 2")]
    [InlineData("1 + 2)")]
    [InlineData("foo + 1")]
    [InlineData("1 / 0")]
    [InlineData("5 % 0")]
    [InlineData("5 5")]
    [InlineData("popcount(1, 2)")]
    [InlineData("frobnicate(1)")]
    [InlineData("mask = 5")]
    [InlineData("1 = 2")]
    [InlineData("#9")]
    [InlineData("1 ? 2")]
    [InlineData("()")]
    [InlineData("0x")]
    [InlineData("0b102")]
    [InlineData("1__0")]
    [InlineData("3 $ 4")]
    [InlineData("")]
    [InlineData("1 << -1")]
    [InlineData("popcount")]
    public void Errors_are_reported(string text) =>
        Assert.Throws<ProgrammerException>(() => ExpressionEvaluator.Evaluate(text, S16, NumberBase.Dec, new VariableScope()));

    [Fact]
    public void Errors_point_at_the_problem()
    {
        var ex = Assert.Throws<ProgrammerException>(() => ExpressionEvaluator.Evaluate("1 + foo", S16, NumberBase.Dec));
        Assert.Equal(4, ex.Position);
        Assert.Equal(3, ex.Length);
        var digit = Assert.Throws<ProgrammerException>(() => ExpressionEvaluator.Evaluate("19", S16, NumberBase.Oct));
        Assert.Contains("octal", digit.Message);
    }

    [Fact]
    public void Base_switch_rewrites_bare_literals()
    {
        Assert.Equal("255 AND 15", ExpressionText.ChangeBase("FF AND 0F", NumberBase.Hex, NumberBase.Dec));
        Assert.Equal("FF + 0b1", ExpressionText.ChangeBase("255 + 0b1", NumberBase.Dec, NumberBase.Hex));
        Assert.Equal("ans << 11", ExpressionText.ChangeBase("ans << 3", NumberBase.Dec, NumberBase.Bin));
        Assert.Equal("1 +", ExpressionText.ChangeBase("1 +", NumberBase.Dec, NumberBase.Dec));
    }

    [Fact]
    public void Sign_toggle_edits_the_number_at_the_caret()
    {
        Assert.Equal(("5 + -3", 6), ExpressionText.ToggleSign("5 + 3", 5, NumberBase.Dec));
        Assert.Equal(("5 + 3", 5), ExpressionText.ToggleSign("5 + -3", 6, NumberBase.Dec));
        Assert.Equal(("-7", 2), ExpressionText.ToggleSign("7", 1, NumberBase.Dec));
        Assert.Equal(("5 - -3", 6), ExpressionText.ToggleSign("5 - 3", 5, NumberBase.Dec));
        Assert.Equal(("(3)", 2), ExpressionText.ToggleSign("(-3)", 3, NumberBase.Dec));
    }
}
