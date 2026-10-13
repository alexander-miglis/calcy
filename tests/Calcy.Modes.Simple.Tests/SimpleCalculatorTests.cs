using Calcy.Core.Numbers;

namespace Calcy.Modes.Simple.Tests;

public class SimpleCalculatorTests
{
    private static SimpleCalculator Run(string keys)
    {
        var c = new SimpleCalculator(new FixedTime());
        c.Type(keys);
        return c;
    }

    private static string Show(string keys) => Run(keys).Display;

    // Exact arithmetic and formatting

    [Theory]
    [InlineData("0.1+0.2=", "0.3")]
    [InlineData("1/3=", "0.3333333333")]
    [InlineData("2/3=", "0.6666666667")]
    [InlineData("19.99*3=", "59.97")]
    [InlineData("1200+34.5=", "1,234.5")]
    [InlineData("86*12=", "1,032")]
    [InlineData("1-0.9=", "0.1")]
    [InlineData("0.3-0.1=", "0.2")]
    [InlineData("1/8=", "0.125")]
    [InlineData("7-10=", "-3")]
    [InlineData("1.5*1.5=", "2.25")]
    public void Exact_decimal_arithmetic(string keys, string expected) => Assert.Equal(expected, Show(keys));

    [Fact]
    public void Thirds_add_back_up_exactly()
    {
        var c = Run("1/3=");
        c.Type("*3=");
        Assert.Equal("1", c.Display);
    }

    [Fact]
    public void Large_results_use_scientific_notation()
    {
        Assert.Equal("1E14", Show("99999999999999+1="));
        Assert.Equal("1.23456789E20", Show("123456789*1000000000000="));
        Assert.Equal("99,999,999,999,999", Show("99999999999999"));
    }

    [Fact]
    public void Tiny_results_use_scientific_notation()
    {
        Assert.Equal("1E-6", Show("1/1000000="));
        Assert.Equal("0.00001", Show("1/100000="));
        Assert.Equal("3.333333333E-7", Show("1/3000000="));
    }

    [Fact]
    public void Big_fractions_round_to_whole_numbers_when_they_fit()
    {
        Assert.Equal("33,333,333,333", Show("100000000000/3="));
    }

    [Fact]
    public void Results_beyond_the_range_are_an_error()
    {
        var c = Run("99999999999999*99999999999999=");
        for (int i = 0; i < 6; i++) c.Type("q");
        Assert.True(c.IsError);
        Assert.Equal("Error", c.Display);
    }

    // Immediate execution

    [Fact]
    public void Operators_run_left_to_right()
    {
        var c = Run("2+3*4=");
        Assert.Equal("20", c.Display);
        Assert.Equal("2 + 3 × 4 =", c.Expression);
    }

    [Fact]
    public void Running_total_shows_after_each_operator()
    {
        var c = Run("2+3*");
        Assert.Equal("5", c.Display);
        Assert.Equal("2 + 3 ×", c.Expression);
        Assert.Equal(CalcOp.Multiply, c.PendingOperator);
    }

    [Fact]
    public void A_second_operator_replaces_the_first()
    {
        var c = Run("8+*2=");
        Assert.Equal("16", c.Display);
        Assert.Equal("8 × 2 =", c.Expression);
    }

    [Fact]
    public void Equals_without_a_second_number_uses_the_first()
    {
        Assert.Equal("10", Show("5+="));
        Assert.Equal("25", Show("5*="));
    }

    [Fact]
    public void Repeated_equals_repeats_the_last_operation()
    {
        var c = Run("2+3=");
        c.Type("==");
        Assert.Equal("11", c.Display);
        Assert.Equal("8 + 3 =", c.Expression);
        Assert.Equal(3, c.Tape.Count);
        Assert.Equal("162", Show("2*3=*3==="));
    }

    [Fact]
    public void Repeated_equals_applies_to_a_new_number()
    {
        var c = Run("2*5=");
        c.Type("7=");
        Assert.Equal("35", c.Display);
    }

    [Fact]
    public void Typing_after_equals_starts_a_new_calculation()
    {
        var c = Run("2+3=");
        c.Type("4");
        Assert.Equal("4", c.Display);
        Assert.Equal("", c.Expression);
        c.Type("+1=");
        Assert.Equal("5", c.Display);
    }

    [Fact]
    public void An_operator_after_equals_continues_from_the_result()
    {
        var c = Run("2+3=");
        c.Type("*10=");
        Assert.Equal("50", c.Display);
        Assert.Equal("5 × 10", c.Tape[1].Expression);
    }

    [Fact]
    public void Starting_with_an_operator_uses_zero()
    {
        Assert.Equal("-5", Show("-5="));
    }

    // Percent

    [Theory]
    [InlineData("200+10%", "220")]
    [InlineData("200-10%", "180")]
    [InlineData("200*10%", "20")]
    [InlineData("200/10%", "2,000")]
    [InlineData("250+15%", "287.5")]
    [InlineData("50%", "0.5")]
    public void Desk_percent(string keys, string expected) => Assert.Equal(expected, Show(keys));

    [Fact]
    public void Percent_completes_the_line()
    {
        var c = Run("250+15%");
        Assert.Equal("250 + 15% =", c.Expression);
        Assert.Single(c.Tape);
        Assert.Equal("250 + 15%", c.Tape[0].Expression);
        Assert.Null(c.PendingOperator);
    }

    [Fact]
    public void Percent_in_a_chain_uses_the_running_total()
    {
        Assert.Equal("33", Show("10+20+10%"));
    }

    // Functions

    [Fact]
    public void Square_root_shows_ten_significant_digits()
    {
        var c = Run("2@");
        Assert.Equal("1.414213562", c.Display);
        Assert.Equal("√2", c.Tape[0].Expression);
        Assert.Equal("√2 =", c.Expression);
    }

    [Fact]
    public void Square_root_of_a_square_is_exact()
    {
        Assert.Equal("1.5", Show("2.25@"));
        Assert.Equal("12", Show("144@"));
    }

    [Fact]
    public void Square_root_squared_shows_the_original()
    {
        var c = Run("2@");
        c.Type("q");
        Assert.Equal("2", c.Display);
    }

    [Fact]
    public void Square_and_reciprocal()
    {
        Assert.Equal("2.25", Show("1.5q"));
        Assert.Equal("0.25", Show("4r"));
        Assert.Equal("1/4", Run("4r").Tape[0].Expression);
    }

    [Fact]
    public void Functions_inside_a_calculation_change_the_operand()
    {
        var c = Run("2+9@");
        Assert.Equal("3", c.Display);
        Assert.Equal("2 + √9", c.Expression);
        c.Type("=");
        Assert.Equal("5", c.Display);
        Assert.Single(c.Tape);
        Assert.Equal("2 + √9", c.Tape[0].Expression);
    }

    [Fact]
    public void Function_right_after_an_operator_uses_the_running_total()
    {
        Assert.Equal("12", Show("9+@="));
    }

    [Fact]
    public void Nested_functions_are_bracketed()
    {
        var c = Run("16@@");
        Assert.Equal("2", c.Display);
        var d = Run("3+9@q=");
        Assert.Equal("12", d.Display);
        Assert.Equal("3 + (√9)²", d.Tape[0].Expression);
    }

    [Fact]
    public void Square_root_of_a_negative_number_is_an_error()
    {
        var c = new SimpleCalculator(new FixedTime());
        c.Type("4");
        c.Press(CalcKey.Negate);
        c.Press(CalcKey.SquareRoot);
        Assert.True(c.IsError);
        Assert.Equal("Error", c.Display);
        Assert.Equal("√(-4) =", c.Expression);
        Assert.Empty(c.Tape);
    }

    [Fact]
    public void Reciprocal_of_zero_is_an_error()
    {
        Assert.Equal("Error", Show("0r"));
    }

    // Division by zero and error recovery

    [Fact]
    public void Division_by_zero_is_an_error()
    {
        var c = Run("5/0=");
        Assert.True(c.IsError);
        Assert.Equal("Error", c.Display);
        Assert.Equal("5 ÷ 0 =", c.Expression);
        Assert.Empty(c.Tape);
    }

    [Fact]
    public void Division_by_zero_in_a_chain_is_an_error()
    {
        Assert.Equal("Error", Show("5/0+"));
    }

    [Fact]
    public void Keys_are_ignored_in_error_until_cleared()
    {
        var c = Run("5/0=");
        c.Type("7+");
        Assert.Equal("Error", c.Display);
        c.Press(CalcKey.Clear);
        Assert.False(c.IsError);
        Assert.Equal("0", c.Display);
        c.Type("7+1=");
        Assert.Equal("8", c.Display);
    }

    [Fact]
    public void Backspace_and_clear_entry_also_clear_an_error()
    {
        var c = Run("5/0=");
        c.Press(CalcKey.Backspace);
        Assert.Equal("0", c.Display);
        var d = Run("5/0=");
        d.Press(CalcKey.ClearEntry);
        Assert.False(d.IsError);
    }

    // Entry

    [Fact]
    public void Display_groups_thousands_while_typing()
    {
        Assert.Equal("1,234,567.50", Show("1234567.50"));
    }

    [Fact]
    public void At_most_fourteen_digits_can_be_typed()
    {
        Assert.Equal("12,345,678,901,234", Show("1234567890123456"));
        Assert.Equal("0.12345678901234", Show("0.123456789012345"));
    }

    [Fact]
    public void Decimal_point_starts_with_zero_and_appears_once()
    {
        Assert.Equal("0.5", Show(".5"));
        Assert.Equal("1.25", Show("1.2.5"));
        Assert.Equal("1.25", Show("1,25"));
    }

    [Fact]
    public void Leading_zeros_are_replaced()
    {
        Assert.Equal("7", Show("0007"));
        Assert.Equal("0", Show("000"));
    }

    [Fact]
    public void Backspace_removes_the_last_digit()
    {
        var c = Run("1234");
        c.Press(CalcKey.Backspace);
        Assert.Equal("123", c.Display);
        c.Press(CalcKey.Backspace, CalcKey.Backspace, CalcKey.Backspace, CalcKey.Backspace);
        Assert.Equal("0", c.Display);
    }

    [Fact]
    public void Backspace_on_a_result_clears_the_display_but_keeps_the_calculation()
    {
        var c = Run("6+");
        c.Type("4");
        c.Press(CalcKey.Backspace);
        c.Type("5=");
        Assert.Equal("11", c.Display);
    }

    [Fact]
    public void Change_sign_while_typing_and_on_results()
    {
        var c = Run("12");
        c.Press(CalcKey.Negate);
        Assert.Equal("-12", c.Display);
        c.Type("3");
        Assert.Equal("-123", c.Display);
        c.Press(CalcKey.Negate);
        Assert.Equal("123", c.Display);
        var d = Run("2+3=");
        d.Press(CalcKey.Negate);
        Assert.Equal("-5", d.Display);
        d.Type("+1=");
        Assert.Equal("-4", d.Display);
    }

    [Fact]
    public void Change_sign_after_an_operator_negates_the_running_total()
    {
        var c = Run("5+");
        c.Press(CalcKey.Negate);
        c.Type("=");
        Assert.Equal("0", c.Display);
    }

    [Fact]
    public void Clear_resets_everything_except_memory_and_tape()
    {
        var c = Run("9");
        c.Press(CalcKey.MemoryAdd);
        c.Type("2+3=4+");
        c.Press(CalcKey.Clear);
        Assert.Equal("0", c.Display);
        Assert.Equal("", c.Expression);
        Assert.Null(c.PendingOperator);
        Assert.True(c.HasMemory);
        Assert.Single(c.Tape);
        c.Type("=");
        Assert.Single(c.Tape);
    }

    [Fact]
    public void Clear_entry_keeps_the_pending_calculation()
    {
        var c = Run("100+55");
        c.Press(CalcKey.ClearEntry);
        Assert.Equal("0", c.Display);
        c.Type("5=");
        Assert.Equal("105", c.Display);
    }

    // Memory

    [Fact]
    public void Memory_keys()
    {
        var c = Run("10");
        Assert.False(c.HasMemory);
        c.Press(CalcKey.MemoryAdd);
        Assert.True(c.HasMemory);
        c.Type("4");
        Assert.Equal("4", c.Display);
        c.Press(CalcKey.MemorySubtract);
        Assert.Equal(new BigRational(6), c.Memory);
        c.Press(CalcKey.Clear);
        c.Press(CalcKey.MemoryRecall);
        Assert.Equal("6", c.Display);
        c.Press(CalcKey.MemoryClear);
        Assert.False(c.HasMemory);
    }

    [Fact]
    public void Memory_recall_feeds_a_pending_calculation()
    {
        var c = Run("7");
        c.Press(CalcKey.MemoryAdd);
        c.Type("3*");
        c.Press(CalcKey.MemoryRecall);
        c.Type("=");
        Assert.Equal("21", c.Display);
    }

    [Fact]
    public void Memory_adds_results_exactly()
    {
        var c = Run("1/3=");
        c.Press(CalcKey.MemoryAdd);
        c.Type("2/3=");
        c.Press(CalcKey.MemoryAdd);
        Assert.Equal(BigRational.One, c.Memory);
    }

    [Fact]
    public void Memory_indicator_goes_off_when_memory_returns_to_zero()
    {
        var c = Run("5");
        c.Press(CalcKey.MemoryAdd);
        c.Press(CalcKey.MemorySubtract);
        Assert.False(c.HasMemory);
    }

    // Tape

    [Fact]
    public void Every_calculation_lands_on_the_tape()
    {
        var c = Run("1200+34.5=86*12=");
        Assert.Equal(2, c.Tape.Count);
        Assert.Equal(1, c.Tape[0].Number);
        Assert.Equal("1,200 + 34.5", c.Tape[0].Expression);
        Assert.Equal("1,234.5", c.Tape[0].ResultText);
        Assert.Equal("86 × 12", c.Tape[1].Expression);
        Assert.Equal(new DateTimeOffset(2026, 10, 13, 14, 5, 0, TimeSpan.Zero), c.Tape[0].Time);
    }

    [Fact]
    public void Use_puts_a_result_on_the_display()
    {
        var c = Run("6*7=1+1=");
        c.Use(c.Tape[0]);
        Assert.Equal("42", c.Display);
        c.Type("+8=");
        Assert.Equal("50", c.Display);
        Assert.Same(c.Tape[0], c.Tape[2].First.Source);
    }

    [Fact]
    public void Use_feeds_a_pending_calculation()
    {
        var c = Run("6*7=100-");
        c.Use(c.Tape[0]);
        c.Type("=");
        Assert.Equal("58", c.Display);
    }

    [Fact]
    public void Editing_a_line_recalculates_the_lines_that_used_it()
    {
        var c = Run("100+50=*2=-25=");
        Assert.Equal(["150", "300", "275"], c.Tape.Select(l => l.ResultText));
        Assert.Equal("#1 × 2", c.Tape[1].EditText);

        Assert.True(c.TryEdit(c.Tape[0], "200 + 50", out var error), error);

        Assert.Equal(["250", "500", "475"], c.Tape.Select(l => l.ResultText));
        Assert.True(c.Tape[0].IsEdited);
        Assert.Equal("475", c.Display);
        Assert.Equal("500 − 25 =", c.Expression);
    }

    [Fact]
    public void Editing_does_not_touch_independent_lines()
    {
        var c = Run("1+1=5*5=");
        Assert.True(c.TryEdit(c.Tape[0], "1 + 2", out _));
        Assert.Equal("3", c.Tape[0].ResultText);
        Assert.Equal("25", c.Tape[1].ResultText);
    }

    [Fact]
    public void Editing_follows_used_results_and_functions()
    {
        var c = Run("8+8=@");
        Assert.Equal("4", c.Display);
        Assert.Equal("√#1", c.Tape[1].EditText);
        Assert.True(c.TryEdit(c.Tape[0], "40 + 9", out _));
        Assert.Equal("7", c.Tape[1].ResultText);
        Assert.Equal("7", c.Display);
    }

    [Fact]
    public void Editing_keeps_exact_values_that_were_not_changed()
    {
        var c = Run("1/3=");
        c.Press(CalcKey.MemoryAdd);
        c.Press(CalcKey.Clear);
        c.Press(CalcKey.MemoryRecall);
        c.Type("*3+1=");
        Assert.Equal("0.3333333333 × 3 + 1", c.Tape[1].EditText);
        Assert.True(c.TryEdit(c.Tape[1], "0.3333333333 × 3 + 2", out _));
        Assert.Equal("3", c.Tape[1].ResultText);
    }

    [Fact]
    public void Editing_can_link_or_unlink_earlier_results()
    {
        var c = Run("10+5=2*3=4+4=");
        Assert.True(c.TryEdit(c.Tape[2], "#1 + #2", out var error), error);
        Assert.Equal("21", c.Tape[2].ResultText);
        Assert.True(c.TryEdit(c.Tape[0], "10 + 6", out _));
        Assert.Equal("22", c.Tape[2].ResultText);
        Assert.True(c.TryEdit(c.Tape[2], "1 + #2", out _));
        Assert.Null(c.Tape[2].First.Source);
        Assert.Equal("7", c.Tape[2].ResultText);
    }

    [Fact]
    public void Editing_accepts_negative_numbers_and_percent()
    {
        var c = Run("200+10%");
        Assert.Equal("200 + 10%", c.Tape[0].EditText);
        Assert.True(c.TryEdit(c.Tape[0], "200 + -10%", out _));
        Assert.Equal("180", c.Tape[0].ResultText);
        Assert.True(c.TryEdit(c.Tape[0], "1,5 + 100%", out _));
        Assert.Equal("3", c.Tape[0].ResultText);
    }

    [Fact]
    public void Editing_rejects_changed_operators_and_bad_numbers()
    {
        var c = Run("2+3=*4=");
        Assert.False(c.TryEdit(c.Tape[0], "2 × 3", out var e1));
        Assert.Contains("Only the numbers", e1);
        Assert.False(c.TryEdit(c.Tape[0], "2 + ", out var e2));
        Assert.NotNull(e2);
        Assert.False(c.TryEdit(c.Tape[0], "2 + abc", out _));
        Assert.False(c.TryEdit(c.Tape[0], "#2 + 3", out var e3));
        Assert.Contains("not an earlier line", e3);
        Assert.Equal(["5", "20"], c.Tape.Select(l => l.ResultText));
    }

    [Fact]
    public void An_edit_that_divides_by_zero_turns_dependent_lines_into_errors()
    {
        var c = Run("8/2=+1=");
        Assert.True(c.TryEdit(c.Tape[0], "8 ÷ 0", out _));
        Assert.True(c.Tape[0].IsError);
        Assert.True(c.Tape[1].IsError);
        Assert.Equal("Error", c.Tape[1].ResultText);
        Assert.True(c.TryEdit(c.Tape[0], "8 ÷ 4", out _));
        Assert.Equal("3", c.Tape[1].ResultText);
    }

    [Fact]
    public void Editing_updates_a_pending_running_total()
    {
        var c = Run("5+5=*");
        Assert.Equal("10", c.Display);
        Assert.True(c.TryEdit(c.Tape[0], "5 + 6", out _));
        Assert.Equal("11", c.Display);
        c.Type("2=");
        Assert.Equal("22", c.Display);
    }

    [Fact]
    public void Tape_events_report_changes()
    {
        var c = new SimpleCalculator(new FixedTime());
        var seen = new List<TapeChange>();
        c.TapeChanged += (_, e) => seen.Add(e.Change);
        c.Type("1+1=");
        c.TryEdit(c.Tape[0], "1 + 2", out _);
        c.ClearTape();
        Assert.Equal([TapeChange.Added, TapeChange.Recalculated, TapeChange.Cleared], seen);
    }

    [Fact]
    public void Clearing_the_tape_keeps_the_display_value_and_restarts_numbering()
    {
        var c = Run("6*7=");
        c.ClearTape();
        Assert.Empty(c.Tape);
        Assert.Equal("42", c.Display);
        c.Type("+1=");
        Assert.Equal(1, c.Tape[0].Number);
        Assert.Equal("42 + 1", c.Tape[0].EditText);
    }

    [Fact]
    public void Tape_keeps_a_bounded_number_of_lines()
    {
        var c = new SimpleCalculator(new FixedTime());
        c.Type("1+1=");
        for (int i = 0; i < SimpleCalculator.MaxTapeLines + 5; i++) c.Type("=");
        Assert.Equal(SimpleCalculator.MaxTapeLines, c.Tape.Count);
        Assert.Null(c.Tape[0].First.Source);
        Assert.Equal(c.Tape[0].Result!.Value - 1, c.Tape[0].First.Literal);
    }

    [Fact]
    public void Export_lists_every_line_oldest_first()
    {
        var c = Run("250+15%2@");
        var lines = c.ExportTape().Split(Environment.NewLine);
        Assert.Equal(["#1  14:05  250 + 15% = 287.5", "#2  14:05  √2 = 1.414213562"], lines);
    }

    // Input from other modes and the keyboard

    [Theory]
    [InlineData("1234.5", "1,234.5")]
    [InlineData("-0.25", "-0.25")]
    [InlineData("1,234.5", "1,234.5")]
    [InlineData("2,5", "2.5")]
    [InlineData("1.5E3", "1,500")]
    [InlineData(" −7 ", "-7")]
    public void Numbers_can_be_entered_as_text(string text, string expected)
    {
        var c = new SimpleCalculator();
        Assert.True(c.TryEnter(text));
        Assert.Equal(expected, c.Display);
    }

    [Theory]
    [InlineData("")]
    [InlineData("abc")]
    [InlineData("1+2")]
    [InlineData("1e999")]
    public void Text_that_is_not_a_number_is_rejected(string text) => Assert.False(new SimpleCalculator().TryEnter(text));

    [Fact]
    public void Entered_numbers_feed_a_pending_calculation()
    {
        var c = Run("3*");
        c.Enter(new BigRational(1, 3));
        c.Type("=");
        Assert.Equal("1", c.Display);
    }

    [Theory]
    [InlineData('7', CalcKey.Digit7)]
    [InlineData(',', CalcKey.Point)]
    [InlineData('.', CalcKey.Point)]
    [InlineData('*', CalcKey.Multiply)]
    [InlineData('/', CalcKey.Divide)]
    [InlineData('-', CalcKey.Subtract)]
    [InlineData('+', CalcKey.Add)]
    [InlineData('=', CalcKey.Equals)]
    [InlineData('%', CalcKey.Percent)]
    [InlineData('@', CalcKey.SquareRoot)]
    public void Keyboard_characters_map_to_keys(char c, CalcKey key) => Assert.Equal(key, CalcKeys.FromChar(c));

    [Fact]
    public void Every_key_has_a_label_and_an_accessible_name()
    {
        foreach (var key in Enum.GetValues<CalcKey>())
        {
            Assert.False(string.IsNullOrWhiteSpace(CalcKeys.Label(key)));
            Assert.False(string.IsNullOrWhiteSpace(CalcKeys.AutomationName(key)));
        }
        Assert.Equal("memory plus", CalcKeys.AutomationName(CalcKey.MemoryAdd));
        Assert.Equal("square root", CalcKeys.AutomationName(CalcKey.SquareRoot));
        Assert.Equal(27, CalcKeys.Layout.Count);
    }

    // Formatting

    [Theory]
    [InlineData("0", "0")]
    [InlineData("1234567.891", "1,234,567.891")]
    [InlineData("-1234", "-1,234")]
    [InlineData("12345678901.25", "12,345,678,901.25")]
    [InlineData("123456789012.345", "123,456,789,012")]
    [InlineData("0.000012345", "0.000012345")]
    [InlineData("100000000000000", "1E14")]
    public void Display_format(string value, string expected) =>
        Assert.Equal(expected, DisplayFormat.Format(BigRational.ParseDecimal(value)));

    [Fact]
    public void Square_root_is_accurate_to_many_digits()
    {
        var r = DeskMath.SquareRoot(new BigRational(2))!.Value;
        Assert.Equal("1.41421356237309504880168872421", DisplayFormatTests.Digits(r, 30));
        Assert.Null(DeskMath.SquareRoot(new BigRational(-1)));
    }

    private sealed class FixedTime : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(2026, 10, 13, 14, 5, 0, TimeSpan.Zero);

        public override TimeZoneInfo LocalTimeZone => TimeZoneInfo.Utc;
    }
}

internal static class DisplayFormatTests
{
    public static string Digits(BigRational v, int significant)
    {
        var (_, ds, e) = Calcy.Core.Printing.NumberFormatter.Significant(v, significant);
        return ds[..(e + 1)] + "." + ds[(e + 1)..];
    }
}
