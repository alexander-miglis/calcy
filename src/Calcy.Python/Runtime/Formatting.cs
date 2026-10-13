using System.Globalization;
using System.Numerics;
using System.Text;

namespace Calcy.Python.Runtime;

/// <summary>Float to string conversion matching CPython (repr, 'e', 'f', 'g' and '%').</summary>
internal static class FloatFormat
{
    public static string Repr(double d) => Format(d, 'r', 0, addDot0: true, alt: false, upper: false);

    /// <summary>Returns the digits and decimal point position for |v| (value = 0.digits * 10^decpt).</summary>
    private static void Dtoa(double v, char mode, int ndigits, out string digits, out int decpt)
    {
        v = Math.Abs(v);
        if (v == 0)
        {
            digits = "0";
            decpt = 1;
            return;
        }
        if (mode == 'r')
        {
            string r = v.ToString("R", CultureInfo.InvariantCulture);
            int ePos = r.IndexOfAny(new[] { 'E', 'e' });
            int exp = 0;
            string mant = r;
            if (ePos >= 0)
            {
                exp = int.Parse(r.Substring(ePos + 1), NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture);
                mant = r.Substring(0, ePos);
            }
            int dot = mant.IndexOf('.');
            string ip = dot >= 0 ? mant.Substring(0, dot) : mant;
            string fp = dot >= 0 ? mant.Substring(dot + 1) : "";
            string all = ip + fp;
            int dp = ip.Length + exp;
            int lead = 0;
            while (lead < all.Length - 1 && all[lead] == '0') lead++;
            all = all.Substring(lead);
            dp -= lead;
            all = all.TrimEnd('0');
            if (all.Length == 0)
            {
                all = "0";
                dp = 1;
            }
            digits = all;
            decpt = dp;
            return;
        }
        // exact value m * 2^e
        long bits = BitConverter.DoubleToInt64Bits(v);
        int bexp = (int)((bits >> 52) & 0x7FF);
        long mant2 = bits & 0xFFFFFFFFFFFFFL;
        if (bexp == 0) bexp = 1;
        else mant2 |= 1L << 52;
        int e2 = bexp - 1075;
        BigInteger M = mant2;
        if (mode == 'f')
        {
            int p = ndigits;
            BigInteger N = ScaleRound(M, e2, p);
            if (N.IsZero)
            {
                digits = "";
                decpt = -p;
                return;
            }
            string s = N.ToString(CultureInfo.InvariantCulture);
            decpt = s.Length - p;
            digits = s.TrimEnd('0');
            return;
        }
        // mode 'e': ndigits significant digits
        int n = Math.Max(1, ndigits);
        int k = (int)Math.Floor(Math.Log10(v));
        for (int attempt = 0; attempt < 4; attempt++)
        {
            BigInteger N = ScaleRound(M, e2, n - 1 - k);
            var lo = BigInteger.Pow(10, n - 1);
            var hi = lo * 10;
            if (N >= hi)
            {
                k++;
                continue;
            }
            if (N < lo && attempt < 3)
            {
                // estimate too high, but N may legitimately be rounded up to lo
                var N2 = ScaleRound(M, e2, n - k);
                if (N2 < hi)
                {
                    k--;
                    continue;
                }
            }
            string s = N.ToString(CultureInfo.InvariantCulture);
            decpt = k + 1;
            digits = s.TrimEnd('0');
            if (digits.Length == 0) digits = "0";
            return;
        }
        digits = "0";
        decpt = 1;
    }

    /// <summary>round_half_even(v * 10^p) computed exactly, keeping the sign of v.</summary>
    public static BigInteger ScaleRoundSigned(double v, int p)
    {
        bool neg = v < 0;
        v = Math.Abs(v);
        long bits = BitConverter.DoubleToInt64Bits(v);
        int bexp = (int)((bits >> 52) & 0x7FF);
        long mant2 = bits & 0xFFFFFFFFFFFFFL;
        if (bexp == 0) bexp = 1;
        else mant2 |= 1L << 52;
        var r = ScaleRound(mant2, bexp - 1075, p);
        return neg ? -r : r;
    }

    /// <summary>round_half_even(M * 2^e2 * 10^p) computed exactly.</summary>
    private static BigInteger ScaleRound(BigInteger M, int e2, int p)
    {
        BigInteger num = M, den = BigInteger.One;
        if (e2 >= 0) num <<= e2;
        else den <<= -e2;
        if (p >= 0) num *= BigInteger.Pow(10, p);
        else den *= BigInteger.Pow(10, -p);
        var q = BigInteger.DivRem(num, den, out var r);
        var twice = r * 2;
        int c = twice.CompareTo(den);
        if (c > 0 || (c == 0 && !q.IsEven)) q += 1;
        return q;
    }

    /// <summary>Port of CPython format_float_short.</summary>
    public static string Format(double d, char code, int precision, bool addDot0, bool alt, bool upper)
    {
        if (double.IsNaN(d)) return upper ? "NAN" : "nan";
        bool neg = double.IsNegative(d);
        if (double.IsInfinity(d)) return (neg ? "-" : "") + (upper ? "INF" : "inf");
        string digits;
        int decpt;
        switch (code)
        {
            case 'r':
                Dtoa(d, 'r', 0, out digits, out decpt);
                break;
            case 'e':
                Dtoa(d, 'e', precision + 1, out digits, out decpt);
                break;
            case 'f':
                Dtoa(d, 'f', precision, out digits, out decpt);
                break;
            default:
                Dtoa(d, 'e', precision == 0 ? 1 : precision, out digits, out decpt);
                break;
        }
        if (d == 0)
        {
            digits = "0";
            decpt = 1;
        }
        int digitsLen = digits.Length;
        int vdigitsEnd = digitsLen;
        bool useExp = false;
        switch (code)
        {
            case 'e':
                useExp = true;
                vdigitsEnd = precision + 1;
                break;
            case 'f':
                vdigitsEnd = decpt + precision;
                break;
            case 'g':
                if (precision == 0) precision = 1;
                if (decpt <= -4 || decpt > (addDot0 ? precision - 1 : precision)) useExp = true;
                if (alt) vdigitsEnd = precision;
                break;
            case 'r':
                if (decpt <= -4 || decpt > 16) useExp = true;
                break;
        }
        int exp = 0;
        if (useExp)
        {
            exp = decpt - 1;
            decpt = 1;
        }
        int vdigitsStart = decpt <= 0 ? decpt - 1 : 0;
        if (!useExp && addDot0) vdigitsEnd = vdigitsEnd > decpt ? vdigitsEnd : decpt + 1;
        else vdigitsEnd = vdigitsEnd > decpt ? vdigitsEnd : decpt;

        var sb = new StringBuilder();
        if (neg) sb.Append('-');
        if (decpt <= 0)
        {
            sb.Append('0', decpt - vdigitsStart);
            sb.Append('.');
            sb.Append('0', -decpt);
        }
        else
        {
            sb.Append('0', -vdigitsStart);
        }
        if (0 < decpt && decpt <= digitsLen)
        {
            sb.Append(digits, 0, decpt);
            sb.Append('.');
            sb.Append(digits, decpt, digitsLen - decpt);
        }
        else sb.Append(digits);
        if (digitsLen < decpt)
        {
            sb.Append('0', decpt - digitsLen);
            sb.Append('.');
            sb.Append('0', Math.Max(0, vdigitsEnd - decpt));
        }
        else
        {
            sb.Append('0', Math.Max(0, vdigitsEnd - digitsLen));
        }
        if (sb[sb.Length - 1] == '.' && !alt) sb.Length--;
        if (useExp)
        {
            sb.Append(upper ? 'E' : 'e');
            sb.Append(exp < 0 ? '-' : '+');
            int ae = Math.Abs(exp);
            sb.Append(ae < 10 ? "0" + ae.ToString(CultureInfo.InvariantCulture) : ae.ToString(CultureInfo.InvariantCulture));
        }
        return sb.ToString();
    }

    public static string ComplexRepr(double re, double im)
    {
        // repr without the ".0" suffix for integral parts
        string F(double x) => Format(x, 'r', 0, addDot0: false, alt: false, upper: false);
        if (re == 0 && !double.IsNegative(re))
            return F(im) + "j";
        string imStr = F(im);
        if (!imStr.StartsWith('-')) imStr = "+" + imStr;
        return "(" + F(re) + imStr + "j)";
    }
}

/// <summary>Parsed format specification (the format spec mini language).</summary>
internal struct FormatSpec
{
    public char Fill;
    public char Align; // '\0' means default
    public char Sign;
    public bool Alt;
    public bool ZeroPad;
    public int Width;
    public char Grouping;
    public int Precision; // -1 = none
    public char Type;
    public bool NoNegZero;

    public static FormatSpec Parse(string spec)
    {
        var f = new FormatSpec { Fill = ' ', Precision = -1, Width = -1 };
        int i = 0, n = spec.Length;
        if (n >= 2 && IsAlign(spec[1]))
        {
            f.Fill = spec[0];
            f.Align = spec[1];
            i = 2;
        }
        else if (n >= 1 && IsAlign(spec[0]))
        {
            f.Align = spec[0];
            i = 1;
        }
        if (i < n && (spec[i] == '+' || spec[i] == '-' || spec[i] == ' '))
        {
            f.Sign = spec[i];
            i++;
        }
        if (i < n && spec[i] == 'z')
        {
            f.NoNegZero = true;
            i++;
        }
        if (i < n && spec[i] == '#')
        {
            f.Alt = true;
            i++;
        }
        if (i < n && spec[i] == '0')
        {
            f.ZeroPad = true;
            if (f.Align == '\0')
            {
                f.Fill = '0';
                f.Align = '=';
            }
            i++;
        }
        int ws = i;
        while (i < n && char.IsAsciiDigit(spec[i])) i++;
        if (i > ws)
        {
            if (!int.TryParse(spec.AsSpan(ws, i - ws), NumberStyles.None, CultureInfo.InvariantCulture, out f.Width))
                throw PyErr.ValueError("Too many decimal digits in format string");
        }
        if (i < n && (spec[i] == ',' || spec[i] == '_'))
        {
            f.Grouping = spec[i];
            i++;
            if (i < n && (spec[i] == ',' || spec[i] == '_')) throw PyErr.ValueError("Cannot specify both ',' and '_'.");
        }
        if (i < n && spec[i] == '.')
        {
            i++;
            int ps = i;
            while (i < n && char.IsAsciiDigit(spec[i])) i++;
            if (i == ps) throw PyErr.ValueError("Format specifier missing precision");
            if (!int.TryParse(spec.AsSpan(ps, i - ps), NumberStyles.None, CultureInfo.InvariantCulture, out f.Precision) || f.Precision > 100000)
                throw PyErr.ValueError("precision too big");
        }
        if (i < n)
        {
            f.Type = spec[i];
            i++;
        }
        if (i < n) throw PyErr.ValueError("Invalid format specifier '" + spec + "'");
        return f;
    }

    private static bool IsAlign(char c) => c == '<' || c == '>' || c == '^' || c == '=';
}

/// <summary>Implements format(value, spec) for builtin types.</summary>
internal static class Formatter
{
    public static string Format(PyObject value, string spec)
    {
        if (value.IsBuiltinSubclass && Ops.UserMethodOf(value.Type, "__format__") is { } uf)
        {
            var r = Ops.CallBound(uf, value, new PyObject[] { PyStr.New(spec) });
            if (r is not PyStr rs) throw PyErr.TypeError($"__format__ must return a str, not {r.Type.Name}");
            return rs.V;
        }
        return FormatBase(value, spec);
    }

    /// <summary>format() of the builtin base value, ignoring __format__ overrides of subclasses.</summary>
    public static string FormatBase(PyObject value, string spec)
    {
        // Like CPython, an empty spec formats a subclass instance with str(), which honours __str__ and __repr__.
        if (spec.Length == 0 && value.IsBuiltinSubclass) return Ops.Str(value);
        switch (value)
        {
            case PyStr s:
                return FormatStr(s.V, spec);
            case PyBool b when spec.Length == 0:
                return b.Value ? "True" : "False";
            case PyInt i:
                return FormatInt(i, spec);
            case PyFloat f:
                return FormatFloat(f.V, spec);
            case PyComplex c:
                return FormatComplex(c, spec);
        }
        var m = value.Type.Lookup("__format__");
        if (m != null && m is not PyBuiltinMethod)
        {
            var r = Ops.CallBound(m, value, new PyObject[] { PyStr.New(spec) });
            if (r is not PyStr rs) throw PyErr.TypeError($"__format__ must return a str, not {r.Type.Name}");
            return rs.V;
        }
        if (m is PyBuiltinMethod bm && value.Type != PyTypes.Object && bm.Owner != PyTypes.Object)
        {
            var r = bm.Fn(value, new PyObject[] { PyStr.New(spec) }, null);
            return ((PyStr)r).V;
        }
        if (spec.Length > 0)
            throw PyErr.TypeError($"unsupported format string passed to {value.Type.Name}.__format__");
        return Ops.Str(value);
    }

    public static string FormatStr(string s, string spec)
    {
        if (spec.Length == 0) return s;
        var f = FormatSpec.Parse(spec);
        if (f.Type != '\0' && f.Type != 's')
            throw PyErr.ValueError($"Unknown format code '{f.Type}' for object of type 'str'");
        if (f.Sign != '\0') throw PyErr.ValueError("Sign not allowed in string format specifier");
        if (f.Alt) throw PyErr.ValueError("Alternate form (#) not allowed in string format specifier");
        if (f.Align == '=') throw PyErr.ValueError("'=' alignment not allowed in string format specifier");
        if (f.Grouping != '\0') throw PyErr.ValueError($"Cannot specify '{f.Grouping}' with 's'.");
        if (f.Precision >= 0 && s.Length > f.Precision) s = s.Substring(0, f.Precision);
        return Pad(s, f, '<');
    }

    private static string Pad(string body, FormatSpec f, char defaultAlign, string signPrefix = "")
    {
        int width = f.Width;
        int len = body.Length + signPrefix.Length;
        char align = f.Align == '\0' ? defaultAlign : f.Align;
        if (width <= len) return signPrefix + body;
        Limits.CheckStringLength(width);
        int pad = width - len;
        switch (align)
        {
            case '<':
                return signPrefix + body + new string(f.Fill, pad);
            case '^':
                int left = pad / 2;
                return new string(f.Fill, left) + signPrefix + body + new string(f.Fill, pad - left);
            case '=':
                return signPrefix + new string(f.Fill, pad) + body;
            default:
                return new string(f.Fill, pad) + signPrefix + body;
        }
    }

    private static string Group(string intDigits, char sep, int every)
    {
        if (sep == '\0' || intDigits.Length <= every) return intDigits;
        var sb = new StringBuilder();
        int first = intDigits.Length % every;
        if (first == 0) first = every;
        sb.Append(intDigits, 0, first);
        for (int i = first; i < intDigits.Length; i += every)
        {
            sb.Append(sep);
            sb.Append(intDigits, i, every);
        }
        return sb.ToString();
    }

    private static string SignOf(bool negative, char sign) => negative ? "-" : sign == '+' ? "+" : sign == ' ' ? " " : "";

    public static string FormatInt(PyInt v, string spec)
    {
        if (spec.Length == 0) return v.ToString();
        var f = FormatSpec.Parse(spec);
        char type = f.Type == '\0' ? 'd' : f.Type;
        switch (type)
        {
            case 'e':
            case 'E':
            case 'f':
            case 'F':
            case 'g':
            case 'G':
            case '%':
                return FormatFloatSpec(IntMath.ToDouble(v), f);
            case 'n':
                type = 'd';
                break;
        }
        if (f.Precision >= 0) throw PyErr.ValueError("Precision not allowed in integer format specifier");
        BigInteger big = v.ToBig();
        bool neg = big.Sign < 0;
        var abs = BigInteger.Abs(big);
        string body;
        string prefix = "";
        switch (type)
        {
            case 'd':
                body = abs.ToString(CultureInfo.InvariantCulture);
                body = Group(body, f.Grouping, 3);
                break;
            case 'b':
                body = Group(IntMath.ToRadix(abs, 2), f.Grouping, 4);
                if (f.Alt) prefix = "0b";
                break;
            case 'o':
                body = Group(IntMath.ToRadix(abs, 8), f.Grouping, 4);
                if (f.Alt) prefix = "0o";
                break;
            case 'x':
                body = Group(IntMath.ToRadix(abs, 16), f.Grouping, 4);
                if (f.Alt) prefix = "0x";
                break;
            case 'X':
                body = Group(IntMath.ToRadix(abs, 16).ToUpperInvariant(), f.Grouping, 4);
                if (f.Alt) prefix = "0X";
                break;
            case 'c':
                if (f.Sign != '\0') throw PyErr.ValueError("Sign not allowed with integer format specifier 'c'");
                if (big < 0 || big > 0x10FFFF) throw PyErr.OverflowError("%c arg not in range(0x110000)");
                return Pad(PyStr.FromCodePoint((int)big).V, f, '<');
            default:
                throw PyErr.ValueError($"Unknown format code '{type}' for object of type 'int'");
        }
        if (f.Align == '=' && f.Fill == '0' && f.Grouping != '\0' && f.Width > 0)
        {
            // zero padding with grouping keeps separators
            string sp = SignOf(neg, f.Sign) + prefix;
            int every = type == 'd' ? 3 : 4;
            string raw = body.Replace(f.Grouping.ToString(), "");
            while (true)
            {
                string g = Group(raw, f.Grouping, every);
                if (g.Length + sp.Length >= f.Width) return sp + g;
                raw = "0" + raw;
            }
        }
        return Pad(body, f, '>', SignOf(neg, f.Sign) + prefix);
    }

    public static string FormatFloat(double d, string spec)
    {
        if (spec.Length == 0) return FloatFormat.Repr(d);
        var f = FormatSpec.Parse(spec);
        return FormatFloatSpec(d, f);
    }

    private static string FormatFloatSpec(double d, FormatSpec f)
    {
        char type = f.Type;
        bool addDot0 = false;
        int precision = f.Precision;
        bool upper = false;
        if (type == '\0')
        {
            addDot0 = true;
            type = precision < 0 ? 'r' : 'g';
        }
        else if (type == 'n') type = 'g';
        switch (type)
        {
            case 'E':
                type = 'e';
                upper = true;
                break;
            case 'F':
                type = 'f';
                upper = true;
                break;
            case 'G':
                type = 'g';
                upper = true;
                break;
        }
        if (type is not ('e' or 'f' or 'g' or 'r' or '%'))
            throw PyErr.ValueError($"Unknown format code '{f.Type}' for object of type 'float'");
        if (precision < 0) precision = 6;
        bool pct = type == '%';
        if (pct)
        {
            d *= 100;
            type = 'f';
        }
        string s = FloatFormat.Format(d, type, precision, addDot0, f.Alt, upper);
        if (pct) s += "%";
        bool neg = s.StartsWith("-");
        if (neg) s = s.Substring(1);
        if (f.NoNegZero && neg && IsZeroString(s)) neg = false;
        if (f.Grouping != '\0')
        {
            int end = 0;
            while (end < s.Length && char.IsAsciiDigit(s[end])) end++;
            s = Group(s.Substring(0, end), f.Grouping, 3) + s.Substring(end);
        }
        return Pad(s, f, '>', SignOf(neg, f.Sign));
    }

    private static bool IsZeroString(string s)
    {
        foreach (var c in s)
            if (c >= '1' && c <= '9') return false;
        return true;
    }

    private static string FormatComplex(PyComplex c, string spec)
    {
        if (spec.Length == 0) return FloatFormat.ComplexRepr(c.Real, c.Imag);
        var f = FormatSpec.Parse(spec);
        var inner = f;
        inner.Width = -1;
        inner.Align = '\0';
        inner.Fill = ' ';
        bool noType = f.Type == '\0';
        if (noType) inner.Type = 'g';
        string re = FormatFloatSpec(c.Real, inner);
        var imSpec = inner;
        imSpec.Sign = '+';
        string im = FormatFloatSpec(c.Imag, imSpec);
        string body = (noType && c.Real == 0 && !double.IsNegative(c.Real)) ? FormatFloatSpec(c.Imag, inner) + "j" : re + im + "j";
        if (noType && !(c.Real == 0 && !double.IsNegative(c.Real))) body = "(" + body + ")";
        return Pad(body, f, '>');
    }
}

/// <summary>Implements str.format and str.format_map.</summary>
internal static class StrFormatter
{
    public static string Format(string fmt, PyObject[] args, KwArgs? kw, PyObject? mapping)
    {
        var sb = new StringBuilder();
        int auto = 0;
        bool manual = false, automatic = false;
        Format(fmt, args, kw, mapping, sb, ref auto, ref manual, ref automatic, 0);
        return sb.ToString();
    }

    private static void Format(string fmt, PyObject[] args, KwArgs? kw, PyObject? mapping, StringBuilder sb,
        ref int auto, ref bool manual, ref bool automatic, int depth)
    {
        if (depth > 2) throw PyErr.ValueError("Max string recursion exceeded");
        int i = 0, n = fmt.Length;
        while (i < n)
        {
            char c = fmt[i];
            if (c == '{')
            {
                if (i + 1 < n && fmt[i + 1] == '{')
                {
                    sb.Append('{');
                    i += 2;
                    continue;
                }
                // find matching close brace
                int j = i + 1, level = 1;
                while (j < n)
                {
                    if (fmt[j] == '{') level++;
                    else if (fmt[j] == '}')
                    {
                        level--;
                        if (level == 0) break;
                    }
                    j++;
                }
                if (j >= n) throw PyErr.ValueError("expected '}' before end of string");
                string field = fmt.Substring(i + 1, j - i - 1);
                sb.Append(RenderField(field, args, kw, mapping, ref auto, ref manual, ref automatic, depth));
                if (sb.Length > Limits.MaxCollection) Limits.CheckStringLength(sb.Length);
                i = j + 1;
            }
            else if (c == '}')
            {
                if (i + 1 < n && fmt[i + 1] == '}')
                {
                    sb.Append('}');
                    i += 2;
                    continue;
                }
                throw PyErr.ValueError("Single '}' encountered in format string");
            }
            else
            {
                sb.Append(c);
                i++;
            }
        }
    }

    private static string RenderField(string field, PyObject[] args, KwArgs? kw, PyObject? mapping,
        ref int auto, ref bool manual, ref bool automatic, int depth)
    {
        // split name, conversion and spec (brackets may contain ':' or '!')
        int k = 0;
        int bracket = 0;
        while (k < field.Length)
        {
            char ch = field[k];
            if (ch == '[') bracket++;
            else if (ch == ']') bracket--;
            else if (bracket == 0 && (ch == ':' || ch == '!')) break;
            k++;
        }
        string name = field.Substring(0, k);
        char conv = '\0';
        string spec = "";
        if (k < field.Length && field[k] == '!')
        {
            if (k + 1 >= field.Length) throw PyErr.ValueError("end of string while looking for conversion specifier");
            conv = field[k + 1];
            k += 2;
            if (k < field.Length && field[k] != ':') throw PyErr.ValueError("expected ':' after conversion specifier");
        }
        if (k < field.Length && field[k] == ':') spec = field.Substring(k + 1);
        if (spec.Contains('{'))
        {
            var inner = new StringBuilder();
            Format(spec, args, kw, mapping, inner, ref auto, ref manual, ref automatic, depth + 1);
            spec = inner.ToString();
        }
        // resolve the argument
        int p = 0;
        while (p < name.Length && name[p] != '.' && name[p] != '[') p++;
        string first = name.Substring(0, p);
        PyObject obj;
        if (first.Length == 0)
        {
            if (manual) throw PyErr.ValueError("cannot switch from manual field specification to automatic field numbering");
            automatic = true;
            int idx = auto++;
            if (idx >= args.Length) throw PyErr.IndexError($"Replacement index {idx} out of range for positional args tuple");
            obj = args[idx];
        }
        else if (int.TryParse(first, NumberStyles.None, CultureInfo.InvariantCulture, out int index))
        {
            if (automatic) throw PyErr.ValueError("cannot switch from automatic field numbering to manual field specification");
            manual = true;
            if (index >= args.Length) throw PyErr.IndexError($"Replacement index {index} out of range for positional args tuple");
            obj = args[index];
        }
        else
        {
            PyObject? v = null;
            if (mapping != null) v = Ops.GetItem(mapping, PyStr.New(first));
            else if (kw != null) v = kw.Get(first);
            obj = v ?? throw PyErr.KeyError(PyStr.New(first));
        }
        while (p < name.Length)
        {
            if (name[p] == '.')
            {
                int q = p + 1;
                while (q < name.Length && name[q] != '.' && name[q] != '[') q++;
                obj = Ops.GetAttr(obj, name.Substring(p + 1, q - p - 1));
                p = q;
            }
            else
            {
                int q = name.IndexOf(']', p);
                if (q < 0) throw PyErr.ValueError("Missing ']' in format string");
                string key = name.Substring(p + 1, q - p - 1);
                PyObject keyObj = int.TryParse(key, NumberStyles.None, CultureInfo.InvariantCulture, out int ki) ? PyInt.From(ki) : PyStr.New(key);
                obj = Ops.GetItem(obj, keyObj);
                p = q + 1;
            }
        }
        obj = conv switch
        {
            '\0' => obj,
            'r' => PyStr.New(Ops.Repr(obj)),
            's' => PyStr.New(Ops.Str(obj)),
            'a' => PyStr.New(Ops.Ascii(obj)),
            _ => throw PyErr.ValueError($"Unknown conversion specifier {conv}"),
        };
        return Formatter.Format(obj, spec);
    }
}

/// <summary>printf style formatting: the str % operator.</summary>
internal static class PercentFormat
{
    public static string Format(string fmt, PyObject values)
    {
        PyObject[] args;
        PyObject? mapping = null;
        if (values is PyTuple t) args = t.Items;
        else
        {
            args = new[] { values };
            if (values is PyDict || (values is not PyStr && values.Type.Lookup("__getitem__") != null && values is not PyList))
                mapping = values;
        }
        int argi = 0;
        bool usedMapping = false;
        var sb = new StringBuilder();
        int i = 0, n = fmt.Length;
        PyObject Next()
        {
            if (argi >= args.Length) throw PyErr.TypeError("not enough arguments for format string");
            return args[argi++];
        }
        while (i < n)
        {
            char c = fmt[i];
            if (c != '%')
            {
                sb.Append(c);
                i++;
                continue;
            }
            i++;
            if (i >= n) throw PyErr.ValueError("incomplete format");
            PyObject? arg = null;
            if (fmt[i] == '(')
            {
                if (mapping == null) throw PyErr.TypeError("format requires a mapping");
                int depth = 1, j = i + 1;
                while (j < n && depth > 0)
                {
                    if (fmt[j] == '(') depth++;
                    else if (fmt[j] == ')') depth--;
                    j++;
                }
                if (depth > 0) throw PyErr.ValueError("incomplete format key");
                string key = fmt.Substring(i + 1, j - i - 2);
                arg = Ops.GetItem(mapping, PyStr.New(key));
                usedMapping = true;
                i = j;
            }
            bool left = false, plus = false, space = false, alt = false, zero = false;
            while (i < n)
            {
                char fl = fmt[i];
                if (fl == '-') left = true;
                else if (fl == '+') plus = true;
                else if (fl == ' ') space = true;
                else if (fl == '#') alt = true;
                else if (fl == '0') zero = true;
                else break;
                i++;
            }
            int width = -1;
            if (i < n && fmt[i] == '*')
            {
                width = Arg.Int(Next());
                if (width < 0)
                {
                    left = true;
                    width = -width;
                }
                i++;
            }
            else
            {
                int ws = i;
                while (i < n && char.IsAsciiDigit(fmt[i])) i++;
                if (i > ws) width = int.Parse(fmt.AsSpan(ws, i - ws), CultureInfo.InvariantCulture);
            }
            int prec = -1;
            if (i < n && fmt[i] == '.')
            {
                i++;
                if (i < n && fmt[i] == '*')
                {
                    prec = Arg.Int(Next());
                    i++;
                }
                else
                {
                    int ps = i;
                    while (i < n && char.IsAsciiDigit(fmt[i])) i++;
                    prec = i > ps ? int.Parse(fmt.AsSpan(ps, i - ps), CultureInfo.InvariantCulture) : 0;
                }
            }
            while (i < n && (fmt[i] == 'h' || fmt[i] == 'l' || fmt[i] == 'L')) i++;
            if (i >= n) throw PyErr.ValueError("incomplete format");
            char type = fmt[i++];
            if (type == '%')
            {
                sb.Append('%');
                continue;
            }
            arg ??= Next();
            if (width > Limits.MaxCollection) Limits.CheckStringLength(width);
            string body;
            string sign = "";
            bool numeric = false;
            switch (type)
            {
                case 's':
                    body = Ops.Str(arg);
                    if (prec >= 0 && body.Length > prec) body = body.Substring(0, prec);
                    break;
                case 'r':
                    body = Ops.Repr(arg);
                    if (prec >= 0 && body.Length > prec) body = body.Substring(0, prec);
                    break;
                case 'a':
                    body = Ops.Ascii(arg);
                    if (prec >= 0 && body.Length > prec) body = body.Substring(0, prec);
                    break;
                case 'c':
                    if (arg is PyStr cs && cs.Length == 1) body = cs.V;
                    else if (arg is PyInt ci) body = PyStr.FromCodePoint((int)ci.ToLong()).V;
                    else throw PyErr.TypeError("%c requires int or char");
                    break;
                case 'd':
                case 'i':
                case 'u':
                case 'o':
                case 'x':
                case 'X':
                {
                    numeric = true;
                    PyInt iv;
                    if (arg is PyInt ai) iv = ai;
                    else if (arg is PyFloat af) iv = FloatMath.ToInt(af.V);
                    else if (arg.Type.Lookup("__index__") != null || arg.Type.Lookup("__int__") != null) iv = Ops.ToInt(arg);
                    else throw PyErr.TypeError($"%{type} format: a real number is required, not {arg.Type.Name}");
                    var big = iv.ToBig();
                    bool neg = big.Sign < 0;
                    var abs = BigInteger.Abs(big);
                    body = type switch
                    {
                        'o' => IntMath.ToRadix(abs, 8),
                        'x' => IntMath.ToRadix(abs, 16),
                        'X' => IntMath.ToRadix(abs, 16).ToUpperInvariant(),
                        _ => abs.ToString(CultureInfo.InvariantCulture),
                    };
                    if (prec >= 0 && body.Length < prec) body = new string('0', prec - body.Length) + body;
                    if (alt && type == 'o') body = "0o" + body;
                    if (alt && type == 'x') body = "0x" + body;
                    if (alt && type == 'X') body = "0X" + body;
                    sign = neg ? "-" : plus ? "+" : space ? " " : "";
                    break;
                }
                case 'e':
                case 'E':
                case 'f':
                case 'F':
                case 'g':
                case 'G':
                {
                    numeric = true;
                    if (arg is not (PyInt or PyFloat) && arg.Type.Lookup("__float__") == null && arg.Type.Lookup("__index__") == null)
                        throw PyErr.TypeError($"must be real number, not {arg.Type.Name}");
                    double dv = Ops.ToDouble(arg);
                    char lt = char.ToLowerInvariant(type);
                    string s = FloatFormat.Format(dv, lt, prec < 0 ? 6 : prec, false, alt, char.IsUpper(type));
                    bool neg = s.StartsWith("-");
                    body = neg ? s.Substring(1) : s;
                    sign = neg ? "-" : plus ? "+" : space ? " " : "";
                    break;
                }
                default:
                    throw PyErr.ValueError($"unsupported format character '{type}' (0x{(int)type:x}) at index {i - 1}");
            }
            int total = body.Length + sign.Length;
            if (width > total)
            {
                if (left) sb.Append(sign).Append(body).Append(' ', width - total);
                else if (zero && numeric) sb.Append(sign).Append('0', width - total).Append(body);
                else sb.Append(' ', width - total).Append(sign).Append(body);
            }
            else sb.Append(sign).Append(body);
            if (sb.Length > Limits.MaxCollection) Limits.CheckStringLength(sb.Length);
        }
        if (argi < args.Length && !usedMapping && mapping == null)
            throw PyErr.TypeError("not all arguments converted during string formatting");
        return sb.ToString();
    }
}
