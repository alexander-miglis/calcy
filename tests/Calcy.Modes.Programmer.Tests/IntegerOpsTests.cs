using System.Numerics;

namespace Calcy.Modes.Programmer.Tests;

public class IntegerOpsTests
{
    public static TheoryData<int> Sizes => [8, 16, 32, 64, 128];

    public static TheoryData<int, bool> SizesAndSigns
    {
        get
        {
            var d = new TheoryData<int, bool>();
            foreach (var n in new[] { 8, 16, 32, 64, 128 }) { d.Add(n, true); d.Add(n, false); }
            return d;
        }
    }

    private static IntegerFormat S(int bits, OverflowMode m = OverflowMode.Wrap) => new(bits, true, m);
    private static IntegerFormat U(int bits, OverflowMode m = OverflowMode.Wrap) => new(bits, false, m);
    private static BigInteger Pow2(int k) => BigInteger.One << k;

    [Theory, MemberData(nameof(Sizes))]
    public void Ranges_match_twos_complement(int n)
    {
        Assert.Equal(-Pow2(n - 1), S(n).MinValue);
        Assert.Equal(Pow2(n - 1) - 1, S(n).MaxValue);
        Assert.Equal(BigInteger.Zero, U(n).MinValue);
        Assert.Equal(Pow2(n) - 1, U(n).MaxValue);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(12)]
    [InlineData(256)]
    public void Odd_widths_are_supported(int n)
    {
        Assert.Equal(-Pow2(n - 1), S(n).MinValue);
        Assert.Equal(Pow2(n) - 1, U(n).MaxValue);
        Assert.Equal(S(n).MinValue, IntegerOps.Add(S(n), S(n).MaxValue, 1));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(257)]
    public void Word_size_outside_1_to_256_is_rejected(int n) =>
        Assert.Throws<ProgrammerException>(() => new IntegerFormat(n, true));

    [Theory, MemberData(nameof(Sizes))]
    public void Add_wraps_at_the_top(int n)
    {
        var s = IntegerOps.Add(S(n), S(n).MaxValue, 1, out bool overflow);
        Assert.True(overflow);
        Assert.Equal(S(n).MinValue, s);
        Assert.Equal(BigInteger.Zero, IntegerOps.Add(U(n), U(n).MaxValue, 1));
        Assert.Equal(U(n).MaxValue, IntegerOps.Subtract(U(n), 0, 1));
    }

    [Theory, MemberData(nameof(Sizes))]
    public void Saturate_clamps(int n)
    {
        var f = S(n, OverflowMode.Saturate);
        Assert.Equal(f.MaxValue, IntegerOps.Add(f, f.MaxValue, 1));
        Assert.Equal(f.MinValue, IntegerOps.Subtract(f, f.MinValue, 1));
        Assert.Equal(f.MaxValue, IntegerOps.Multiply(f, f.MaxValue, 2));
        var u = U(n, OverflowMode.Saturate);
        Assert.Equal(BigInteger.Zero, IntegerOps.Subtract(u, 3, 5));
    }

    [Theory, MemberData(nameof(Sizes))]
    public void Flag_mode_reports_overflow(int n)
    {
        var f = S(n, OverflowMode.Flag);
        var ex = Assert.Throws<ProgrammerException>(() => IntegerOps.Add(f, f.MaxValue, 1));
        Assert.Contains("Overflow", ex.Message);
        Assert.Equal(f.MaxValue - 1, IntegerOps.Add(f, f.MaxValue, -1));
    }

    [Theory, MemberData(nameof(Sizes))]
    public void Int_min_divided_by_minus_one_overflows(int n)
    {
        var min = S(n).MinValue;
        Assert.Equal(min, IntegerOps.Divide(S(n), min, -1, out bool overflow));
        Assert.True(overflow);
        Assert.Equal(S(n).MaxValue, IntegerOps.Divide(S(n, OverflowMode.Saturate), min, -1));
        Assert.Throws<ProgrammerException>(() => IntegerOps.Divide(S(n, OverflowMode.Flag), min, -1));
        Assert.Equal(BigInteger.Zero, IntegerOps.Remainder(S(n), min, -1));
    }

    [Theory]
    [InlineData(-7, 2, -3, -1)]
    [InlineData(7, -2, -3, 1)]
    [InlineData(-7, -2, 3, -1)]
    [InlineData(7, 2, 3, 1)]
    public void Division_truncates_toward_zero_like_C(int a, int b, int q, int r)
    {
        Assert.Equal(q, IntegerOps.Divide(S(32), a, b));
        Assert.Equal(r, IntegerOps.Remainder(S(32), a, b));
    }

    [Fact]
    public void Division_by_zero_is_an_error()
    {
        Assert.Throws<ProgrammerException>(() => IntegerOps.Divide(S(16), 1, 0));
        Assert.Throws<ProgrammerException>(() => IntegerOps.Remainder(S(16), 1, 0));
    }

    [Theory, MemberData(nameof(SizesAndSigns))]
    public void Not_zero_is_all_ones(int n, bool signed)
    {
        var f = new IntegerFormat(n, signed);
        Assert.Equal(signed ? BigInteger.MinusOne : f.Mask, IntegerOps.Not(f, 0));
        Assert.Equal(BigInteger.Zero, IntegerOps.Not(f, IntegerOps.Not(f, 0)));
    }

    [Theory, MemberData(nameof(SizesAndSigns))]
    public void Bitwise_operators(int n, bool signed)
    {
        var f = new IntegerFormat(n, signed);
        Assert.Equal(f.FromPattern(0x30), IntegerOps.And(f, 0xF0, 0x3C));
        Assert.Equal(f.FromPattern(0xFC), IntegerOps.Or(f, 0xF0, 0x3C));
        Assert.Equal(f.FromPattern(0xCC), IntegerOps.Xor(f, 0xF0, 0x3C));
        Assert.Equal(f.FromPattern(f.Mask ^ 0x30), IntegerOps.Nand(f, 0xF0, 0x3C));
        Assert.Equal(f.FromPattern(f.Mask ^ 0xFC), IntegerOps.Nor(f, 0xF0, 0x3C));
        Assert.Equal(f.FromPattern(f.Mask ^ 0xCC), IntegerOps.Xnor(f, 0xF0, 0x3C));
    }

    [Theory, MemberData(nameof(Sizes))]
    public void Shift_left_drops_bits_and_shifting_by_width_gives_zero(int n)
    {
        Assert.Equal(S(n).MinValue, IntegerOps.ShiftLeft(S(n), 1, n - 1));
        Assert.Equal(BigInteger.Zero, IntegerOps.ShiftLeft(S(n), 1, n));
        Assert.Equal(BigInteger.Zero, IntegerOps.ShiftLeft(U(n), -1, n + 5));
        Assert.Equal(U(n).Mask - 1, IntegerOps.ShiftLeft(U(n), U(n).Mask, 1));
    }

    [Theory, MemberData(nameof(Sizes))]
    public void Shift_right_arithmetic_and_logical(int n)
    {
        var min = S(n).MinValue;
        Assert.Equal(BigInteger.MinusOne, IntegerOps.ShiftRightArithmetic(S(n), min, n - 1));
        Assert.Equal(BigInteger.MinusOne, IntegerOps.ShiftRightArithmetic(S(n), min, n + 3));
        Assert.Equal(BigInteger.One, IntegerOps.ShiftRightLogical(S(n), min, n - 1));
        Assert.Equal(BigInteger.Zero, IntegerOps.ShiftRightLogical(S(n), min, n));
        Assert.Equal(BigInteger.Zero, IntegerOps.ShiftRightArithmetic(S(n), 5, n));
        Assert.Equal(-4, IntegerOps.ShiftRight(S(n), -16, 2));
        Assert.Equal(Pow2(n - 2), IntegerOps.ShiftRight(U(n), Pow2(n - 1), 1));
    }

    [Fact]
    public void Negative_shift_count_is_an_error() =>
        Assert.Throws<ProgrammerException>(() => IntegerOps.ShiftLeft(S(16), 1, -1));

    [Theory, MemberData(nameof(Sizes))]
    public void Rotates_wrap_bits_around(int n)
    {
        var top = Pow2(n - 1);
        Assert.Equal(BigInteger.One, IntegerOps.RotateLeft(U(n), top, 1));
        Assert.Equal(top, IntegerOps.RotateRight(U(n), 1, 1));
        Assert.Equal(new BigInteger(2), IntegerOps.RotateLeft(U(n), 1, 1));
        Assert.Equal(new BigInteger(0xB5), IntegerOps.RotateLeft(U(n), 0xB5, n));
        Assert.Equal(IntegerOps.RotateLeft(U(n), 0xB5, 1), IntegerOps.RotateLeft(U(n), 0xB5, n + 1));
        Assert.Equal(IntegerOps.RotateRight(U(n), 0xB5, 3), IntegerOps.RotateLeft(U(n), 0xB5, -3));
        Assert.Equal(BigInteger.MinusOne, IntegerOps.RotateLeft(S(n), -1, 7));
    }

    [Fact]
    public void Rotate_through_carry_uses_a_ring_of_n_plus_one_bits()
    {
        var f = U(8);
        bool c = false;
        Assert.Equal(BigInteger.Zero, IntegerOps.RotateLeftThroughCarry(f, 0x80, 1, ref c));
        Assert.True(c);
        Assert.Equal(BigInteger.One, IntegerOps.RotateLeftThroughCarry(f, 0x00, 1, ref c));
        Assert.False(c);
        c = false;
        Assert.Equal(BigInteger.Zero, IntegerOps.RotateRightThroughCarry(f, 0x01, 1, ref c));
        Assert.True(c);
        Assert.Equal(new BigInteger(0x80), IntegerOps.RotateRightThroughCarry(f, 0x00, 1, ref c));
        Assert.False(c);
        c = true;
        Assert.Equal(new BigInteger(0xB5), IntegerOps.RotateLeftThroughCarry(f, 0xB5, 9, ref c));
        Assert.True(c);
    }

    [Theory, MemberData(nameof(Sizes))]
    public void Counting_bits(int n)
    {
        Assert.Equal(n, IntegerOps.PopCount(S(n), -1));
        Assert.Equal(5, IntegerOps.PopCount(S(n), 0xB5));
        Assert.Equal(n - 1, IntegerOps.LeadingZeros(U(n), 1));
        Assert.Equal(0, IntegerOps.LeadingZeros(S(n), -1));
        Assert.Equal(n, IntegerOps.LeadingZeros(U(n), 0));
        Assert.Equal(7, IntegerOps.TrailingZeros(U(n), 0x80));
        Assert.Equal(n, IntegerOps.TrailingZeros(U(n), 0));
        Assert.Equal(1, IntegerOps.Parity(U(n), 0xB5));
    }

    [Theory, MemberData(nameof(Sizes))]
    public void Bit_reverse(int n)
    {
        Assert.Equal(Pow2(n - 1), IntegerOps.BitReverse(U(n), 1));
        Assert.Equal(BigInteger.One, IntegerOps.BitReverse(U(n), Pow2(n - 1)));
        Assert.Equal(new BigInteger(0xB5), IntegerOps.BitReverse(U(n), IntegerOps.BitReverse(U(n), 0xB5)));
    }

    [Fact]
    public void Byte_swap_reverses_endianness()
    {
        Assert.Equal(0x3412, IntegerOps.ByteSwap(U(16), 0x1234));
        Assert.Equal(0x78563412, IntegerOps.ByteSwap(U(32), 0x12345678));
        Assert.Equal(BigInteger.Parse("0807060504030201", System.Globalization.NumberStyles.HexNumber), IntegerOps.ByteSwap(U(64), BigInteger.Parse("0102030405060708", System.Globalization.NumberStyles.HexNumber)));
        Assert.Equal(new BigInteger(0x12), IntegerOps.ByteSwap(U(8), 0x12));
        Assert.Equal(-0x7EFF, IntegerOps.ByteSwap(S(16), 0x0181));
        Assert.Throws<ProgrammerException>(() => IntegerOps.ByteSwap(U(12), 0x123));
    }

    [Fact]
    public void Sign_and_zero_extension()
    {
        Assert.Equal(0xFF80, IntegerOps.SignExtend(U(16), 0x80, 8));
        Assert.Equal(-128, IntegerOps.SignExtend(S(16), 0x80, 8));
        Assert.Equal(0x7F, IntegerOps.SignExtend(S(16), 0x7F, 8));
        Assert.Equal(-1, IntegerOps.SignExtend(S(32), 0xF, 4));
        Assert.Equal(0x80, IntegerOps.ZeroExtend(S(16), -128, 8));
        Assert.Throws<ProgrammerException>(() => IntegerOps.SignExtend(S(16), 1, 17));
    }

    [Fact]
    public void Bit_fields_and_masks()
    {
        Assert.Equal(0xB, IntegerOps.ExtractField(U(16), 0xB5, 7, 4));
        Assert.Equal(0xB, IntegerOps.ExtractField(U(16), 0xB5, 4, 7));
        Assert.Equal(0x35, IntegerOps.InsertField(U(16), 0xB5, 7, 4, 0x3));
        Assert.Equal(0xF5, IntegerOps.InsertField(U(16), 0xB5, 7, 4, 0x1F));
        Assert.Equal(0xFF, IntegerOps.Mask(U(16), 8));
        Assert.Equal(-1, IntegerOps.Mask(S(16), 16));
        Assert.Equal(0xF0, IntegerOps.RangeMask(U(16), 7, 4));
        Assert.Throws<ProgrammerException>(() => IntegerOps.ExtractField(U(16), 1, 16, 0));
    }

    [Fact]
    public void Single_bits()
    {
        Assert.Equal(-32768, IntegerOps.FlipBit(S(16), 0, 15));
        Assert.True(IntegerOps.GetBit(S(16), -1, 15));
        Assert.Equal(0xB4, IntegerOps.SetBit(U(16), 0xB5, 0, false));
        Assert.Throws<ProgrammerException>(() => IntegerOps.FlipBit(S(8), 0, 8));
    }

    [Fact]
    public void Converting_between_formats_keeps_values_that_fit()
    {
        Assert.Equal(-1, IntegerOps.Convert(S(16), -1));
        Assert.Equal(0xFFFF, IntegerOps.Convert(U(16), -1));
        Assert.Equal(0x34, IntegerOps.Convert(U(8), 0x1234));
        Assert.Equal(-1, IntegerOps.Convert(S(8), 255));
    }

    [Fact]
    public void Hex_literals_are_bit_patterns_decimal_literals_are_values()
    {
        Assert.Equal(-1, IntegerOps.Literal(S(8), 0xFF, isPattern: true, out bool o1));
        Assert.False(o1);
        Assert.Equal(-1, IntegerOps.Literal(S(8), 255, isPattern: false, out bool o2));
        Assert.True(o2);
        Assert.Equal(0xFF, IntegerOps.Literal(U(8), 0x1FF, isPattern: true, out bool o3));
        Assert.True(o3);
    }

    [Fact]
    public void Negation_and_abs()
    {
        Assert.Equal(S(8).MinValue, IntegerOps.Negate(S(8), -128, out bool o));
        Assert.True(o);
        Assert.Equal(255, IntegerOps.Negate(U(8), 1));
        Assert.Equal(127, IntegerOps.Abs(S(8, OverflowMode.Saturate), -128, out _));
    }

    [Fact]
    public void Bytes_little_endian()
    {
        Assert.Equal(new byte[] { 0x34, 0x12 }, IntegerOps.ToBytesLittleEndian(U(16), 0x1234));
        Assert.Equal(new byte[] { 0xFF, 0xFF, 0xFF, 0xFF }, IntegerOps.ToBytesLittleEndian(S(32), -1));
    }
}
