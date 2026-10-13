using System.Numerics;

namespace Calcy.Modes.Programmer.Tests;

public class CalculatorTests
{
    private static ProgrammerCalculator Press(params string[] keys)
    {
        var c = new ProgrammerCalculator();
        foreach (var k in keys) c.Press(k);
        return c;
    }

    [Fact]
    public void Defaults_are_16_bit_signed_hex_input()
    {
        var c = new ProgrammerCalculator();
        Assert.Equal(16, c.Format.Bits);
        Assert.True(c.Format.Signed);
        Assert.Equal(OverflowMode.Wrap, c.Format.Overflow);
        Assert.Equal(NumberBase.Hex, c.InputBase);
        Assert.Equal(BigInteger.Zero, c.Value);
    }

    [Fact]
    public void Keypad_builds_an_expression_and_equals_adds_a_card()
    {
        var c = Press("B", "5", "AND", "0", "F", "=");
        var card = Assert.Single(c.Cards);
        Assert.Equal("B5 AND 0F", card.Expression);
        Assert.Equal(new BigInteger(5), c.Value);
        Assert.Equal("0x5", card.Result);
        Assert.Equal("5", card.Secondary);
        Assert.Equal("16-bit signed", card.Context);
        Assert.Equal("", c.Expression);
    }

    [Fact]
    public void An_operator_on_an_empty_input_starts_with_ans()
    {
        var c = Press("7", "=", "+");
        Assert.Equal("ans + ", c.Expression);
        c.Press("1");
        c.Press("=");
        Assert.Equal(new BigInteger(8), c.Value);
    }

    [Fact]
    public void Not_rol_ror_and_sign_act_on_the_value_when_the_input_is_empty()
    {
        var c = new ProgrammerCalculator();
        c.Press("NOT");
        Assert.Equal(BigInteger.MinusOne, c.Value);
        Assert.Equal("0xFFFF", c.Cards[^1].Result);
        Assert.Equal("-1", c.Cards[^1].Secondary);
        c.SetValue(-32768);
        c.Press("ROL");
        Assert.Equal(BigInteger.One, c.Value);
        c.Press("ROR");
        Assert.Equal(new BigInteger(-32768), c.Value);
        c.SetValue(5);
        c.Press("±");
        Assert.Equal(new BigInteger(-5), c.Value);
        Assert.Equal("-ans", c.Cards[^1].Expression);
    }

    [Fact]
    public void Not_inside_an_expression_is_inserted()
    {
        var c = Press("1", "AND", "NOT", "2");
        Assert.Equal("1 AND NOT 2", c.Expression);
        c.Press("=");
        Assert.Equal(BigInteger.One, c.Value);
    }

    [Fact]
    public void Sign_key_toggles_the_last_number()
    {
        var c = Press("5", "+", "3", "±");
        Assert.Equal("5 + -3", c.Expression);
        c.Press("±");
        Assert.Equal("5 + 3", c.Expression);
    }

    [Fact]
    public void Switching_base_keeps_the_meaning_of_the_expression()
    {
        var c = Press("F", "F", "AND", "0", "F");
        c.SetInputBase(NumberBase.Dec);
        Assert.Equal("255 AND 15", c.Expression);
        Assert.Equal(new BigInteger(15), c.GetPreview().Value);
    }

    [Fact]
    public void Digits_outside_the_input_base_are_disabled()
    {
        var c = new ProgrammerCalculator();
        c.SetInputBase(NumberBase.Bin);
        Assert.False(c.IsKeyEnabled("2"));
        Assert.True(c.IsKeyEnabled("1"));
        Assert.False(c.Press("2"));
        Assert.Equal("", c.Expression);
        c.SetInputBase(NumberBase.Oct);
        Assert.True(c.IsKeyEnabled("7"));
        Assert.False(c.IsKeyEnabled("8"));
        Assert.False(c.IsKeyEnabled("."));
    }

    [Fact]
    public void Word_size_and_sign_changes_keep_values_and_patterns()
    {
        var c = new ProgrammerCalculator();
        c.SetValue(-1);
        c.SetWordSize(8);
        Assert.Equal(BigInteger.MinusOne, c.Value);
        c.SetSigned(false);
        Assert.Equal(new BigInteger(255), c.Value);
        c.SetWordSize(16);
        Assert.Equal(new BigInteger(255), c.Value);
        c.SetValue(0x1234);
        c.SetWordSize(8);
        Assert.Equal(new BigInteger(0x34), c.Value);
    }

    [Fact]
    public void Bit_grid_flips_bits()
    {
        var c = new ProgrammerCalculator();
        c.FlipBit(15);
        Assert.Equal(new BigInteger(-32768), c.Value);
        c.FlipBit(0);
        Assert.Equal(new BigInteger(-32767), c.Value);
    }

    [Fact]
    public void Backspace_removes_a_word_operator_in_one_press()
    {
        var c = Press("5", "AND");
        Assert.Equal("5 AND ", c.Expression);
        c.Press("⌫");
        Assert.Equal("5", c.Expression);
        c.Press("⌫");
        Assert.Equal("", c.Expression);
        Assert.False(c.Press("⌫"));
    }

    [Fact]
    public void Errors_make_an_error_card_and_keep_the_input()
    {
        var c = new ProgrammerCalculator();
        c.SetExpression("1 +", 3);
        var card = c.Evaluate();
        Assert.NotNull(card);
        Assert.True(card!.IsError);
        Assert.Equal("1 +", c.Expression);
        Assert.Null(new ProgrammerCalculator().Evaluate());
    }

    [Fact]
    public void Preview_shows_the_live_value()
    {
        var c = new ProgrammerCalculator();
        c.SetExpression("2 * 3", 5);
        var p = c.GetPreview();
        Assert.True(p.IsLive);
        Assert.Equal(new BigInteger(6), p.Value);
        c.SetExpression("2 *", 3);
        p = c.GetPreview();
        Assert.False(p.IsLive);
        Assert.NotNull(p.Error);
    }

    [Fact]
    public void Variables_and_card_references()
    {
        var c = new ProgrammerCalculator();
        c.SetExpression("x = 5", 5);
        c.Evaluate();
        c.SetExpression("0xFF & (x << 3)", 15);
        c.Evaluate();
        Assert.Equal(new BigInteger(40), c.Value);
        c.SetExpression("#1 + #2", 7);
        c.Evaluate();
        Assert.Equal(new BigInteger(45), c.Value);
        Assert.Contains("x", c.VariableNames);
    }

    [Fact]
    public void Clear_resets_value_and_input()
    {
        var c = Press("1", "2", "=", "3");
        c.Press("CLR");
        Assert.Equal("", c.Expression);
        Assert.Equal(BigInteger.Zero, c.Value);
        Assert.Single(c.Cards);
        c.ClearCards();
        Assert.Empty(c.Cards);
    }

    [Fact]
    public void Escape_clears_input_first_then_value()
    {
        var c = Press("1", "=", "2");
        c.Escape();
        Assert.Equal("", c.Expression);
        Assert.Equal(BigInteger.One, c.Value);
        c.Escape();
        Assert.Equal(BigInteger.Zero, c.Value);
    }

    [Fact]
    public void Overflow_in_cards_is_noted()
    {
        var c = new ProgrammerCalculator();
        c.SetExpression("0x7FFF + 1", 10);
        var card = c.Evaluate()!;
        Assert.Equal("0x8000", card.Result);
        Assert.Equal("-32768", card.Secondary);
        Assert.Contains("overflow wrapped", card.Notes);
    }

    [Fact]
    public void Decimal_cards_show_hex_as_the_second_line()
    {
        var c = new ProgrammerCalculator();
        c.SetInputBase(NumberBase.Dec);
        c.SetWordSize(8);
        c.SetExpression("255", 3);
        var card = c.Evaluate()!;
        Assert.Equal("-1", card.Result);
        Assert.Equal("0xFF", card.Secondary);
        Assert.Equal("8-bit signed", card.Context);
    }

    [Fact]
    public void Keypad_has_the_designed_38_keys() =>
        Assert.Equal(38, ProgrammerCalculator.KeypadKeys.Count);
}
