using System.Globalization;
using System.Text;

namespace Calcy.Python.Runtime;

/// <summary>Python str. Indexing works on code points; strings without surrogates use the fast path.</summary>
internal class PyStr : PyObject
{
    internal readonly string V;
    private long _hash;
    private bool _hashed;
    private int[]? _cps;
    private sbyte _surrogates = -1;

    private static readonly PyStr[] CharCache = CreateCache();
    public static readonly PyStr Empty = new("");

    private static PyStr[] CreateCache()
    {
        var c = new PyStr[256];
        for (int i = 0; i < 256; i++) c[i] = new PyStr(((char)i).ToString());
        return c;
    }

    private protected PyStr(string v) { V = v; }

    internal override PyType Type => PyTypes.Str;

    public static PyStr New(string s)
    {
        if (s.Length == 0) return Empty;
        if (s.Length == 1 && s[0] < 256) return CharCache[s[0]];
        return new PyStr(s);
    }

    public static PyStr FromChar(char c) => c < 256 ? CharCache[c] : new PyStr(c.ToString());

    public static PyStr FromCodePoint(int cp)
    {
        if (cp < 256) return CharCache[cp];
        if (cp < 0x10000) return new PyStr(((char)cp).ToString());
        return new PyStr(char.ConvertFromUtf32(cp));
    }

    public bool HasSurrogates
    {
        get
        {
            if (_surrogates < 0)
            {
                bool has = false;
                foreach (char c in V)
                    if (char.IsSurrogate(c)) { has = true; break; }
                _surrogates = (sbyte)(has ? 1 : 0);
            }
            return _surrogates == 1;
        }
    }

    public int[] CodePoints => _cps ??= ToCodePoints(V);

    public static int[] ToCodePoints(string s)
    {
        var list = new List<int>(s.Length);
        for (int i = 0; i < s.Length; i++)
        {
            char c = s[i];
            if (char.IsHighSurrogate(c) && i + 1 < s.Length && char.IsLowSurrogate(s[i + 1]))
            {
                list.Add(char.ConvertToUtf32(c, s[i + 1]));
                i++;
            }
            else list.Add(c);
        }
        return list.ToArray();
    }

    public static string FromCodePoints(IEnumerable<int> cps)
    {
        var sb = new StringBuilder();
        foreach (int cp in cps)
        {
            if (cp < 0x10000) sb.Append((char)cp);
            else sb.Append(char.ConvertFromUtf32(cp));
        }
        return sb.ToString();
    }

    public int Length => HasSurrogates ? CodePoints.Length : V.Length;

    public PyStr CharAt(int index)
    {
        if (!HasSurrogates) return FromChar(V[index]);
        return FromCodePoint(CodePoints[index]);
    }

    public long Hash
    {
        get
        {
            if (!_hashed)
            {
                _hash = HashOf(V);
                _hashed = true;
            }
            return _hash;
        }
    }

    /// <summary>The deterministic hash used for str values.</summary>
    public static long HashOf(string s)
    {
        ulong h = 14695981039346656037UL;
        foreach (char c in s)
        {
            h ^= c;
            h *= 1099511628211UL;
        }
        long r = (long)h;
        return r == -1 ? -2 : r;
    }

    public static string Repr(string s)
    {
        bool hasSingle = s.Contains('\''), hasDouble = s.Contains('"');
        char q = hasSingle && !hasDouble ? '"' : '\'';
        var sb = new StringBuilder(s.Length + 2);
        sb.Append(q);
        for (int i = 0; i < s.Length; i++)
        {
            char c = s[i];
            int cp = c;
            if (char.IsHighSurrogate(c) && i + 1 < s.Length && char.IsLowSurrogate(s[i + 1]))
            {
                cp = char.ConvertToUtf32(c, s[i + 1]);
                if (IsPrintable(cp)) sb.Append(c).Append(s[i + 1]);
                else sb.Append("\\U").Append(cp.ToString("x8"));
                i++;
                continue;
            }
            if (c == q || c == '\\') sb.Append('\\').Append(c);
            else if (c == '\n') sb.Append("\\n");
            else if (c == '\r') sb.Append("\\r");
            else if (c == '\t') sb.Append("\\t");
            else if (c < 0x20 || c == 0x7f) sb.Append("\\x").Append(((int)c).ToString("x2"));
            else if (c < 0x7f) sb.Append(c);
            else if (IsPrintable(cp)) sb.Append(c);
            else if (cp <= 0xff) sb.Append("\\x").Append(cp.ToString("x2"));
            else sb.Append("\\u").Append(cp.ToString("x4"));
        }
        sb.Append(q);
        return sb.ToString();
    }

    public static bool IsPrintable(int cp)
    {
        if (cp == ' ') return true;
        if (cp >= 0xD800 && cp <= 0xDFFF) return false;
        UnicodeCategory cat;
        try { cat = CharUnicodeInfo.GetUnicodeCategory(cp); }
        catch (ArgumentOutOfRangeException) { return false; }
        switch (cat)
        {
            case UnicodeCategory.Control:
            case UnicodeCategory.Format:
            case UnicodeCategory.Surrogate:
            case UnicodeCategory.PrivateUse:
            case UnicodeCategory.OtherNotAssigned:
            case UnicodeCategory.LineSeparator:
            case UnicodeCategory.ParagraphSeparator:
            case UnicodeCategory.SpaceSeparator:
                return false;
            default:
                return true;
        }
    }
}

/// <summary>Methods of the str type.</summary>
internal static class StrType
{
    public static readonly char[] WhitespaceChars =
    {
        ' ', '\t', '\n', '\r', '\v', '\f', '\x1c', '\x1d', '\x1e', '\x1f', '\x85', '\xa0', (char)0x1680,
        (char)0x2000, (char)0x2001, (char)0x2002, (char)0x2003, (char)0x2004, (char)0x2005, (char)0x2006, (char)0x2007, (char)0x2008, (char)0x2009, (char)0x200A,
        (char)0x2028, (char)0x2029, (char)0x202F, (char)0x205F, (char)0x3000,
    };

    public static bool IsSpace(char c) => c == ' ' || (c >= '\t' && c <= '\r') || (c >= '\x1c' && c <= '\x1f') || c == '\x85' || (c > 127 && Array.IndexOf(WhitespaceChars, c) >= 0);

    private static string S(PyObject self) => ((PyStr)self).V;

    private static readonly Sig SplitSig = new("split", 0, 0, 2, "sep", "maxsplit");
    private static readonly Sig RSplitSig = new("rsplit", 0, 0, 2, "sep", "maxsplit");
    private static readonly Sig SplitLinesSig = new("splitlines", 0, 0, 1, "keepends");
    private static readonly Sig ExpandTabsSig = new("expandtabs", 0, 0, 1, "tabsize");

    public static void Register()
    {
        var t = PyTypes.Str;
        t.Ctor = (type, a, kw) =>
        {
            if (kw != null && kw.Count > 0)
            {
                foreach (var n in kw.Names)
                    if (n is not ("object" or "encoding" or "errors"))
                        throw PyErr.TypeError($"'{n}' is an invalid keyword argument for str()");
                var obj = kw.Get("object");
                if (obj != null && a.Length == 0 && kw.Count == 1) return PyStr.New(Ops.Str(obj));
                throw PyErr.TypeError("decoding str is not supported");
            }
            if (a.Length == 0) return PyStr.Empty;
            if (a.Length > 1) throw PyErr.TypeError("decoding str is not supported");
            return a[0].GetType() == typeof(PyStr) ? a[0] : PyStr.New(Ops.Str(a[0]));
        };

        t.Method("split", (self, a, kw) =>
        {
            var b = SplitSig.Bind(a, kw);
            long max = Arg.IsNone(b[1]) ? -1 : Arg.Index(b[1]!);
            return Arg.IsNone(b[0]) ? SplitWhitespace(S(self), max) : Split(S(self), Arg.Str(b[0]!, "sep"), max);
        });
        t.Method("rsplit", (self, a, kw) =>
        {
            var b = RSplitSig.Bind(a, kw);
            long max = Arg.IsNone(b[1]) ? -1 : Arg.Index(b[1]!);
            return Arg.IsNone(b[0]) ? RSplitWhitespace(S(self), max) : RSplit(S(self), Arg.Str(b[0]!, "sep"), max);
        });
        t.Method("splitlines", (self, a, kw) =>
        {
            var b = SplitLinesSig.Bind(a, kw);
            return SplitLines(S(self), b[0] != null && Ops.Truthy(b[0]!));
        });
        t.Method1("join", (self, it) => Join(S(self), it));
        t.Method("strip", (self, a, kw) => PyStr.New(Strip(S(self), a, kw, "strip", true, true)));
        t.Method("lstrip", (self, a, kw) => PyStr.New(Strip(S(self), a, kw, "lstrip", true, false)));
        t.Method("rstrip", (self, a, kw) => PyStr.New(Strip(S(self), a, kw, "rstrip", false, true)));
        t.Method("replace", (self, a, kw) =>
        {
            Arg.NoKw(kw, "replace");
            Arg.Count(a, 2, 3, "replace");
            long count = a.Length > 2 ? Arg.Index(a[2]) : -1;
            return PyStr.New(Replace(S(self), Arg.Str(a[0], "replace() argument 1"), Arg.Str(a[1], "replace() argument 2"), count));
        });
        t.Method("find", (self, a, kw) => PyInt.From(Find(S(self), a, kw, "find", false)));
        t.Method("rfind", (self, a, kw) => PyInt.From(Find(S(self), a, kw, "rfind", true)));
        t.Method("index", (self, a, kw) =>
        {
            int r = Find(S(self), a, kw, "index", false);
            if (r < 0) throw PyErr.ValueError("substring not found");
            return PyInt.From(r);
        });
        t.Method("rindex", (self, a, kw) =>
        {
            int r = Find(S(self), a, kw, "rindex", true);
            if (r < 0) throw PyErr.ValueError("substring not found");
            return PyInt.From(r);
        });
        t.Method("count", (self, a, kw) =>
        {
            Arg.NoKw(kw, "count");
            Arg.Count(a, 1, 3, "count");
            var s = S(self);
            var sub = Arg.Str(a[0], "must be str");
            StartEnd(a, 1, s.Length, out int start, out int end);
            return PyInt.From(Count(s, sub, start, end));
        });
        t.Method0("upper", self => PyStr.New(S(self).ToUpperInvariant()));
        t.Method0("lower", self => PyStr.New(S(self).ToLowerInvariant()));
        t.Method0("casefold", self => PyStr.New(S(self).ToLowerInvariant().Replace(((char)0xDF).ToString(), "ss")));
        t.Method0("swapcase", self =>
        {
            var s = S(self);
            var sb = new StringBuilder(s.Length);
            foreach (var c in s)
                sb.Append(char.IsUpper(c) ? char.ToLowerInvariant(c) : char.IsLower(c) ? char.ToUpperInvariant(c) : c);
            return PyStr.New(sb.ToString());
        });
        t.Method0("title", self => PyStr.New(Title(S(self))));
        t.Method0("capitalize", self =>
        {
            var s = S(self);
            if (s.Length == 0) return Subclassing.Plain(self);
            return PyStr.New(char.ToUpperInvariant(s[0]) + s.Substring(1).ToLowerInvariant());
        });
        t.Method("startswith", (self, a, kw) => Py.Bool(StartsEnds(S(self), a, kw, "startswith", true)));
        t.Method("endswith", (self, a, kw) => Py.Bool(StartsEnds(S(self), a, kw, "endswith", false)));
        t.Method0("isdigit", self => Py.Bool(All(S(self), c => char.IsDigit(c) || CharUnicodeInfo.GetDigitValue(c) >= 0)));
        t.Method0("isdecimal", self => Py.Bool(All(S(self), char.IsDigit)));
        t.Method0("isnumeric", self => Py.Bool(All(S(self), c => CharUnicodeInfo.GetNumericValue(c) != -1)));
        t.Method0("isalpha", self => Py.Bool(All(S(self), char.IsLetter)));
        t.Method0("isalnum", self => Py.Bool(All(S(self), c => char.IsLetter(c) || CharUnicodeInfo.GetNumericValue(c) != -1)));
        t.Method0("isspace", self => Py.Bool(All(S(self), IsSpace)));
        t.Method0("isascii", self => Py.Bool(S(self).All(c => c < 128)));
        t.Method0("isprintable", self => Py.Bool(PyStr.ToCodePoints(S(self)).All(PyStr.IsPrintable)));
        t.Method0("isidentifier", self => Py.Bool(IsIdentifier(S(self))));
        t.Method0("isupper", self =>
        {
            var s = S(self);
            bool cased = false;
            foreach (var c in s)
            {
                if (char.IsLower(c)) return Py.False;
                if (char.IsUpper(c)) cased = true;
            }
            return Py.Bool(cased);
        });
        t.Method0("islower", self =>
        {
            var s = S(self);
            bool cased = false;
            foreach (var c in s)
            {
                if (char.IsUpper(c)) return Py.False;
                if (char.IsLower(c)) cased = true;
            }
            return Py.Bool(cased);
        });
        t.Method0("istitle", self =>
        {
            var s = S(self);
            bool cased = false, prevCased = false;
            foreach (var c in s)
            {
                if (char.IsUpper(c) || CharUnicodeInfo.GetUnicodeCategory(c) == UnicodeCategory.TitlecaseLetter)
                {
                    if (prevCased) return Py.False;
                    prevCased = true;
                    cased = true;
                }
                else if (char.IsLower(c))
                {
                    if (!prevCased) return Py.False;
                    prevCased = true;
                    cased = true;
                }
                else prevCased = false;
            }
            return Py.Bool(cased);
        });
        t.Method1("zfill", (self, w) =>
        {
            var s = S(self);
            long width = Arg.Index(w);
            if (s.Length >= width) return Subclassing.Plain(self);
            Limits.CheckStringLength(width);
            int fill = (int)width - s.Length;
            if (s.Length > 0 && (s[0] == '+' || s[0] == '-'))
                return PyStr.New(s[0] + new string('0', fill) + s.Substring(1));
            return PyStr.New(new string('0', fill) + s);
        });
        t.Method("center", (self, a, kw) => PyStr.New(Pad(S(self), a, kw, "center", 0)));
        t.Method("ljust", (self, a, kw) => PyStr.New(Pad(S(self), a, kw, "ljust", -1)));
        t.Method("rjust", (self, a, kw) => PyStr.New(Pad(S(self), a, kw, "rjust", 1)));
        t.Method("format", (self, a, kw) => PyStr.New(StrFormatter.Format(S(self), a, kw, null)));
        t.Method1("format_map", (self, m) => PyStr.New(StrFormatter.Format(S(self), Array.Empty<PyObject>(), null, m)));
        t.Method1("partition", (self, sepObj) =>
        {
            var s = S(self);
            var sep = Arg.Str(sepObj, "partition() argument");
            if (sep.Length == 0) throw PyErr.ValueError("empty separator");
            int i = s.IndexOf(sep, StringComparison.Ordinal);
            if (i < 0) return new PyTuple(new PyObject[] { Subclassing.Plain(self), PyStr.Empty, PyStr.Empty });
            return new PyTuple(new PyObject[] { PyStr.New(s.Substring(0, i)), PyStr.New(sep), PyStr.New(s.Substring(i + sep.Length)) });
        });
        t.Method1("rpartition", (self, sepObj) =>
        {
            var s = S(self);
            var sep = Arg.Str(sepObj, "rpartition() argument");
            if (sep.Length == 0) throw PyErr.ValueError("empty separator");
            int i = s.LastIndexOf(sep, StringComparison.Ordinal);
            if (i < 0) return new PyTuple(new PyObject[] { PyStr.Empty, PyStr.Empty, Subclassing.Plain(self) });
            return new PyTuple(new PyObject[] { PyStr.New(s.Substring(0, i)), PyStr.New(sep), PyStr.New(s.Substring(i + sep.Length)) });
        });
        t.Method("expandtabs", (self, a, kw) =>
        {
            var b = ExpandTabsSig.Bind(a, kw);
            long ts = b[0] == null ? 8 : Arg.Index(b[0]!);
            return PyStr.New(ExpandTabs(S(self), ts));
        });
        t.Method1("removeprefix", (self, p) =>
        {
            var s = S(self);
            var pre = Arg.Str(p, "removeprefix() argument");
            return pre.Length > 0 && s.StartsWith(pre, StringComparison.Ordinal) ? PyStr.New(s.Substring(pre.Length)) : Subclassing.Plain(self);
        });
        t.Method1("removesuffix", (self, p) =>
        {
            var s = S(self);
            var suf = Arg.Str(p, "removesuffix() argument");
            return suf.Length > 0 && s.EndsWith(suf, StringComparison.Ordinal) ? PyStr.New(s.Substring(0, s.Length - suf.Length)) : Subclassing.Plain(self);
        });
        t.Method1("translate", (self, table) => PyStr.New(Translate(S(self), table)));
        t.StaticMethod("maketrans", (a, kw) => MakeTrans(a));
        t.Method0("__len__", self => PyInt.From(((PyStr)self).Length));
        t.Method1("__contains__", (self, x) => Py.Bool(S(self).Contains(Arg.Str(x, "'in <string>' requires string as left operand,"), StringComparison.Ordinal)));
        t.Method1("__getitem__", (self, k) => Ops.GetItemBase(self, k));
        t.Method1("__eq__", (self, o) => o is PyStr so ? Py.Bool(S(self) == so.V) : Py.NotImplemented);
        t.Method1("__ne__", (self, o) => o is PyStr so ? Py.Bool(S(self) != so.V) : Py.NotImplemented);
        t.Method1("__lt__", (self, o) => o is PyStr so ? Py.Bool(string.CompareOrdinal(S(self), so.V) < 0) : Py.NotImplemented);
        t.Method0("__hash__", self => PyInt.From(((PyStr)self).Hash));
        t.Method0("__str__", self => Subclassing.Plain(self));
        t.Method0("__repr__", self => PyStr.New(PyStr.Repr(S(self))));
        t.Method1("__format__", (self, spec) => PyStr.New(Formatter.FormatStr(S(self), Arg.Str(spec, "format spec"))));
        t.Method0("encode", self => throw PyErr.NotImplementedError("bytes are not supported in this interpreter"));
    }

    private static bool All(string s, Func<char, bool> pred)
    {
        if (s.Length == 0) return false;
        foreach (var c in s)
            if (!pred(c)) return false;
        return true;
    }

    public static bool IsIdentifier(string s)
    {
        if (s.Length == 0) return false;
        for (int i = 0; i < s.Length; i++)
        {
            char c = s[i];
            bool ok = c == '_' || char.IsLetter(c) || char.IsSurrogate(c);
            if (!ok && i > 0)
            {
                var cat = CharUnicodeInfo.GetUnicodeCategory(c);
                ok = char.IsDigit(c) || cat == UnicodeCategory.NonSpacingMark || cat == UnicodeCategory.SpacingCombiningMark ||
                     cat == UnicodeCategory.ConnectorPunctuation || cat == UnicodeCategory.LetterNumber;
            }
            if (!ok) return false;
        }
        return true;
    }

    public static PyList SplitWhitespace(string s, long maxsplit)
    {
        var result = new PyList();
        int i = 0, n = s.Length;
        if (maxsplit < 0) maxsplit = long.MaxValue;
        while (maxsplit > 0)
        {
            while (i < n && IsSpace(s[i])) i++;
            if (i >= n) break;
            int j = i;
            while (j < n && !IsSpace(s[j])) j++;
            result.Append(PyStr.New(s.Substring(i, j - i)));
            i = j;
            maxsplit--;
        }
        if (i < n)
        {
            while (i < n && IsSpace(s[i])) i++;
            if (i < n) result.Append(PyStr.New(s.Substring(i)));
        }
        return result;
    }

    public static PyList RSplitWhitespace(string s, long maxsplit)
    {
        var parts = new List<PyObject>();
        int i = s.Length - 1;
        if (maxsplit < 0) maxsplit = long.MaxValue;
        while (maxsplit > 0)
        {
            while (i >= 0 && IsSpace(s[i])) i--;
            if (i < 0) break;
            int j = i;
            while (j >= 0 && !IsSpace(s[j])) j--;
            parts.Add(PyStr.New(s.Substring(j + 1, i - j)));
            i = j;
            maxsplit--;
        }
        if (i >= 0)
        {
            while (i >= 0 && IsSpace(s[i])) i--;
            if (i >= 0) parts.Add(PyStr.New(s.Substring(0, i + 1)));
        }
        parts.Reverse();
        return new PyList(parts);
    }

    public static PyList Split(string s, string sep, long maxsplit)
    {
        if (sep.Length == 0) throw PyErr.ValueError("empty separator");
        var result = new PyList();
        if (maxsplit < 0) maxsplit = long.MaxValue;
        int start = 0;
        while (maxsplit > 0)
        {
            int i = s.IndexOf(sep, start, StringComparison.Ordinal);
            if (i < 0) break;
            result.Append(PyStr.New(s.Substring(start, i - start)));
            start = i + sep.Length;
            maxsplit--;
        }
        result.Append(PyStr.New(s.Substring(start)));
        return result;
    }

    public static PyList RSplit(string s, string sep, long maxsplit)
    {
        if (sep.Length == 0) throw PyErr.ValueError("empty separator");
        var parts = new List<PyObject>();
        if (maxsplit < 0) maxsplit = long.MaxValue;
        int end = s.Length;
        while (maxsplit > 0 && end > 0)
        {
            int i = end - sep.Length < 0 ? -1 : s.LastIndexOf(sep, end - 1, end, StringComparison.Ordinal);
            if (i < 0) break;
            parts.Add(PyStr.New(s.Substring(i + sep.Length, end - i - sep.Length)));
            end = i;
            maxsplit--;
        }
        parts.Add(PyStr.New(s.Substring(0, end)));
        parts.Reverse();
        return new PyList(parts);
    }

    public static PyList SplitLines(string s, bool keepends)
    {
        var result = new PyList();
        int i = 0, n = s.Length;
        while (i < n)
        {
            int j = i;
            while (j < n && !IsLineBreak(s[j])) j++;
            int eol = j;
            if (j < n)
            {
                if (s[j] == '\r' && j + 1 < n && s[j + 1] == '\n') j += 2;
                else j++;
                if (keepends) eol = j;
            }
            result.Append(PyStr.New(s.Substring(i, eol - i)));
            i = j;
        }
        return result;
    }

    private static bool IsLineBreak(char c) =>
        c == '\n' || c == '\r' || c == '\v' || c == '\f' || c == '\x1c' || c == '\x1d' || c == '\x1e' || c == '\x85' || c == (char)0x2028 || c == (char)0x2029;

    public static PyStr Join(string sep, PyObject iterable)
    {
        var items = new List<string>();
        long total = 0;
        int idx = 0;
        foreach (var item in Ops.Iterate(iterable))
        {
            if (item is not PyStr ps)
                throw PyErr.TypeError($"sequence item {idx}: expected str instance, {item.Type.Name} found");
            items.Add(ps.V);
            total += ps.V.Length + sep.Length;
            if (total > Limits.MaxCollection) Limits.CheckStringLength(total);
            idx++;
        }
        if (items.Count == 1) return PyStr.New(items[0]);
        return PyStr.New(string.Join(sep, items));
    }

    private static string Strip(string s, PyObject[] a, KwArgs? kw, string name, bool left, bool right)
    {
        Arg.NoKw(kw, name);
        Arg.Count(a, 0, 1, name);
        string? chars = a.Length == 0 || a[0] == Py.None ? null : Arg.Str(a[0], $"{name} arg");
        int i = 0, j = s.Length - 1;
        if (chars == null)
        {
            if (left) while (i <= j && IsSpace(s[i])) i++;
            if (right) while (j >= i && IsSpace(s[j])) j--;
        }
        else
        {
            if (left) while (i <= j && chars.IndexOf(s[i]) >= 0) i++;
            if (right) while (j >= i && chars.IndexOf(s[j]) >= 0) j--;
        }
        return s.Substring(i, j - i + 1);
    }

    public static string Replace(string s, string old, string @new, long count)
    {
        if (count < 0) count = long.MaxValue;
        if (count == 0) return s;
        if (old.Length == 0)
        {
            var sb0 = new StringBuilder();
            long done = 0;
            long est = s.Length + (long)@new.Length * Math.Min(count, s.Length + 1);
            Limits.CheckStringLength(est);
            for (int i = 0; i <= s.Length; i++)
            {
                if (done < count)
                {
                    sb0.Append(@new);
                    done++;
                }
                if (i < s.Length) sb0.Append(s[i]);
            }
            return sb0.ToString();
        }
        var sb = new StringBuilder();
        int start = 0;
        long n = 0;
        while (n < count)
        {
            int i = s.IndexOf(old, start, StringComparison.Ordinal);
            if (i < 0) break;
            sb.Append(s, start, i - start).Append(@new);
            if (sb.Length > Limits.MaxCollection) Limits.CheckStringLength(sb.Length);
            start = i + old.Length;
            n++;
        }
        if (n == 0) return s;
        sb.Append(s, start, s.Length - start);
        return sb.ToString();
    }

    public static void StartEnd(PyObject[] a, int idx, int len, out int start, out int end)
    {
        long st = idx < a.Length && a[idx] != Py.None ? Arg.Index(a[idx]) : 0;
        long en = idx + 1 < a.Length && a[idx + 1] != Py.None ? Arg.Index(a[idx + 1]) : len;
        if (en > len) en = len;
        else if (en < 0)
        {
            en += len;
            if (en < 0) en = 0;
        }
        if (st < 0)
        {
            st += len;
            if (st < 0) st = 0;
        }
        start = (int)Math.Min(st, int.MaxValue);
        end = (int)en;
    }

    private static int Find(string s, PyObject[] a, KwArgs? kw, string name, bool reverse)
    {
        Arg.NoKw(kw, name);
        Arg.Count(a, 1, 3, name);
        var sub = Arg.Str(a[0], "must be str");
        StartEnd(a, 1, s.Length, out int start, out int end);
        return reverse ? RFind(s, sub, start, end) : Find(s, sub, start, end);
    }

    public static int Find(string s, string sub, int start, int end)
    {
        if (start > s.Length || end - start < sub.Length) return -1;
        if (sub.Length == 0) return start;
        int r = s.AsSpan(start, end - start).IndexOf(sub.AsSpan());
        return r < 0 ? -1 : r + start;
    }

    public static int RFind(string s, string sub, int start, int end)
    {
        if (start > s.Length || end - start < sub.Length) return -1;
        if (sub.Length == 0) return end;
        int r = s.AsSpan(start, end - start).LastIndexOf(sub.AsSpan());
        return r < 0 ? -1 : r + start;
    }

    public static long Count(string s, string sub, int start, int end)
    {
        if (start > s.Length || end - start < sub.Length) return 0;
        if (sub.Length == 0) return end - start + 1;
        long n = 0;
        int i = start;
        while (true)
        {
            int r = s.AsSpan(i, end - i).IndexOf(sub.AsSpan());
            if (r < 0) break;
            n++;
            i += r + sub.Length;
            if (end - i < sub.Length) break;
        }
        return n;
    }

    private static bool StartsEnds(string s, PyObject[] a, KwArgs? kw, string name, bool starts)
    {
        Arg.NoKw(kw, name);
        Arg.Count(a, 1, 3, name);
        StartEnd(a, 1, s.Length, out int start, out int end);
        if (a[0] is PyTuple t)
        {
            foreach (var item in t.Items)
                if (Matches(s, Arg.Str(item, $"tuple for {name} must only contain str, not {item.Type.Name}"), start, end, starts))
                    return true;
            return false;
        }
        if (a[0] is not PyStr p)
            throw PyErr.TypeError($"{name} first arg must be str or a tuple of str, not {a[0].Type.Name}");
        return Matches(s, p.V, start, end, starts);
    }

    private static bool Matches(string s, string p, int start, int end, bool starts)
    {
        if (start > s.Length) return false;
        if (end - start < p.Length) return false;
        if (starts) return string.CompareOrdinal(s, start, p, 0, p.Length) == 0;
        return string.CompareOrdinal(s, end - p.Length, p, 0, p.Length) == 0;
    }

    public static string Title(string s)
    {
        var sb = new StringBuilder(s.Length);
        bool prevCased = false;
        foreach (var c in s)
        {
            bool upper = char.IsUpper(c), lower = char.IsLower(c);
            if (upper || lower)
            {
                sb.Append(prevCased ? char.ToLowerInvariant(c) : char.ToUpperInvariant(c));
                prevCased = true;
            }
            else
            {
                sb.Append(c);
                prevCased = false;
            }
        }
        return sb.ToString();
    }

    private static string Pad(string s, PyObject[] a, KwArgs? kw, string name, int mode)
    {
        Arg.NoKw(kw, name);
        Arg.Count(a, 1, 2, name);
        long width = Arg.Index(a[0]);
        char fill = ' ';
        if (a.Length > 1)
        {
            var f = Arg.Str(a[1], $"{name}() argument 2");
            if (f.Length != 1) throw PyErr.TypeError("The fill character must be exactly one character long");
            fill = f[0];
        }
        int len = s.Length;
        if (width <= len) return s;
        Limits.CheckStringLength(width);
        int marg = (int)width - len;
        int left;
        if (mode < 0) left = 0;
        else if (mode > 0) left = marg;
        else left = marg / 2 + (marg & (int)width & 1);
        return new string(fill, left) + s + new string(fill, marg - left);
    }

    private static string ExpandTabs(string s, long tabsize)
    {
        var sb = new StringBuilder();
        long col = 0;
        foreach (var c in s)
        {
            if (c == '\t')
            {
                if (tabsize > 0)
                {
                    long n = tabsize - col % tabsize;
                    Limits.CheckStringLength(sb.Length + n);
                    sb.Append(' ', (int)n);
                    col += n;
                }
            }
            else
            {
                sb.Append(c);
                col = c == '\n' || c == '\r' ? 0 : col + 1;
            }
        }
        return sb.ToString();
    }

    private static string Translate(string s, PyObject table)
    {
        var sb = new StringBuilder(s.Length);
        foreach (int cp in PyStr.ToCodePoints(s))
        {
            PyObject? r;
            try
            {
                r = Ops.GetItem(table, PyInt.From(cp));
            }
            catch (PythonException e) when (e.Value.Type.IsSubtypeOf(Exc.LookupError))
            {
                r = null;
            }
            if (r == null) AppendCp(sb, cp);
            else if (r == Py.None) { }
            else if (r is PyStr rs) sb.Append(rs.V);
            else if (r is PyInt ri) AppendCp(sb, (int)ri.ToLong());
            else throw PyErr.TypeError("character mapping must return integer, None or str");
        }
        return sb.ToString();
    }

    private static void AppendCp(StringBuilder sb, int cp)
    {
        if (cp < 0x10000) sb.Append((char)cp);
        else sb.Append(char.ConvertFromUtf32(cp));
    }

    private static PyObject MakeTrans(PyObject[] a)
    {
        var d = new PyDict();
        if (a.Length == 1)
        {
            if (a[0] is not PyDict src) throw PyErr.TypeError("if you give only one argument to maketrans it must be a dict");
            foreach (var (k, v) in src.Pairs())
            {
                if (k is PyStr ks)
                {
                    if (ks.Length != 1) throw PyErr.ValueError("string keys in translate table must be of length 1");
                    d.SetItem(PyInt.From(ks.CodePoints[0]), v);
                }
                else d.SetItem(k, v);
            }
            return d;
        }
        Arg.Count(a, 2, 3, "maketrans");
        var x = PyStr.ToCodePoints(Arg.Str(a[0], "maketrans() argument 1"));
        var y = PyStr.ToCodePoints(Arg.Str(a[1], "maketrans() argument 2"));
        if (x.Length != y.Length) throw PyErr.ValueError("the first two maketrans arguments must have equal length");
        for (int i = 0; i < x.Length; i++) d.SetItem(PyInt.From(x[i]), PyInt.From(y[i]));
        if (a.Length > 2)
            foreach (int cp in PyStr.ToCodePoints(Arg.Str(a[2], "maketrans() argument 3")))
                d.SetItem(PyInt.From(cp), Py.None);
        return d;
    }

    /// <summary>Repeats a string, enforcing the size limit.</summary>
    public static PyStr Repeat(PyStr s, long n)
    {
        if (n <= 0 || s.V.Length == 0) return PyStr.Empty;
        if (n == 1) return (PyStr)Subclassing.Plain(s);
        long total = s.V.Length * n;
        Limits.CheckStringLength(total);
        var sb = new StringBuilder((int)total);
        for (long i = 0; i < n; i++) sb.Append(s.V);
        return PyStr.New(sb.ToString());
    }

    /// <summary>Slices a string with Python semantics.</summary>
    public static PyStr Slice(PyStr s, PySlice slice)
    {
        int len = s.Length;
        slice.Indices(len, out long start, out long stop, out long step, out long count);
        if (count <= 0) return PyStr.Empty;
        if (!s.HasSurrogates)
        {
            if (step == 1) return PyStr.New(s.V.Substring((int)start, (int)count));
            var chars = new char[count];
            long idx = start;
            for (long i = 0; i < count; i++, idx += step) chars[i] = s.V[(int)idx];
            return PyStr.New(new string(chars));
        }
        var cps = s.CodePoints;
        var list = new List<int>((int)count);
        long j = start;
        for (long i = 0; i < count; i++, j += step) list.Add(cps[j]);
        return PyStr.New(PyStr.FromCodePoints(list));
    }
}
