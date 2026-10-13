using System.Numerics;
using Calcy.Core;
using Calcy.Core.Expressions;
using Calcy.Core.Numbers;
using Calcy.Numerics;
using Complex = Calcy.Numerics.Complex;
using NumMatrix = Calcy.Numerics.Matrix;

namespace Calcy.Modes.Matrix.Logic;

/// <summary>How eigenvalues were found.</summary>
public enum EigenMethod
{
    /// <summary>Exact values (fractions, square roots, i).</summary>
    Exact,

    /// <summary>Decimal approximations.</summary>
    Numeric,
}

/// <summary>One distinct eigenvalue with its multiplicity and a basis of its eigenspace.</summary>
public sealed record EigenValue
{
    /// <summary>The exact value, or null for a numeric result.</summary>
    public Expr? Exact { get; init; }

    /// <summary>Textbook text of the exact value (for example (1 + √5)/2), when the value is a quadratic number.</summary>
    public string? ExactText { get; init; }

    /// <summary>Textbook text of each exact eigenvector's entries, in the same order as <see cref="Vectors"/>.</summary>
    public IReadOnlyList<string[]>? VectorTexts { get; init; }

    /// <summary>The decimal value (NaN when it depends on symbols).</summary>
    public Complex Approx { get; init; }

    /// <summary>How often the value is a root of the characteristic polynomial.</summary>
    public int AlgebraicMultiplicity { get; init; } = 1;

    /// <summary>Exact eigenvectors (a basis of the eigenspace).</summary>
    public IReadOnlyList<Expr[]> Vectors { get; init; } = [];

    /// <summary>Numeric unit eigenvectors.</summary>
    public IReadOnlyList<Complex[]> NumericVectors { get; init; } = [];

    /// <summary>The dimension of the eigenspace.</summary>
    public int GeometricMultiplicity => Math.Max(Vectors.Count, NumericVectors.Count);
}

/// <summary>Eigenvalues, eigenvectors and the characteristic polynomial of a square matrix.</summary>
public sealed record EigenAnalysis
{
    /// <summary>Exact or numeric.</summary>
    public required EigenMethod Method { get; init; }

    /// <summary>Coefficients of det(λI − A), ascending powers.</summary>
    public required Expr[] CharacteristicPolynomial { get; init; }

    /// <summary>The factorisation of the characteristic polynomial over the rationals (display text), when known.</summary>
    public string? Factored { get; init; }

    /// <summary>The factorisation as Calcy input (uses * for products).</summary>
    public string? FactoredInput { get; init; }

    /// <summary>The distinct eigenvalues, largest real part first.</summary>
    public required IReadOnlyList<EigenValue> Values { get; init; }

    /// <summary>True when there are n independent eigenvectors.</summary>
    public bool IsDiagonalizable { get; init; } = true;

    /// <summary>Why the result is numeric, when it is.</summary>
    public string? Note { get; init; }
}

/// <summary>
/// Eigenvalues and eigenvectors. Matrices with rational entries get exact answers whenever the characteristic polynomial
/// splits into rational linear and quadratic factors (always the case for 2×2, and for 3×3 with a rational eigenvalue);
/// 2×2 matrices with any entries use the quadratic formula; everything else is solved numerically.
/// </summary>
public static class Eigen
{
    /// <summary>Analyses a square matrix.</summary>
    /// <exception cref="CalcException">The matrix is not square, or it holds symbols and is larger than 2×2.</exception>
    public static EigenAnalysis Analyse(MatrixExpr a, CalcSession session, CancellationToken ct = default)
    {
        if (a.Rows != a.Cols) throw new CalcException($"Eigenvalues need a square matrix, not {a.Rows}×{a.Cols}");
        if (a.Rows == 0) throw new CalcException("The matrix is empty");
        var poly = CharacteristicPolynomial.Coefficients(a, session, ct);
        if (NumericBridge.IsRational(a))
        {
            var exact = TryRational(a, poly, ct);
            if (exact is not null) return exact;
            return Numeric(a, poly, session, ct) with
            {
                Note = "The characteristic polynomial has no rational or quadratic factors, so the eigenvalues are decimals",
            };
        }
        if (a.Rows == 2) return TwoByTwo(a, poly, session, ct);
        if (!NumericBridge.IsConstant(a))
            throw new CalcException("Eigenvalues of a matrix with letters can only be found exactly for 2×2 matrices; give the letters values first");
        return Numeric(a, poly, session, ct) with { Note = "Entries with roots or π are solved numerically for matrices larger than 2×2" };
    }

    // ------------------------------------------------------------------ exact, rational entries

    /// <summary>A monic factor with integer coefficients (ascending) and its roots.</summary>
    internal sealed record Factor(BigInteger[] Monic, QuadraticNumber[] Roots);

    private static EigenAnalysis? TryRational(MatrixExpr a, Expr[] poly, CancellationToken ct)
    {
        int n = a.Rows;
        var q = a.Items.Select(e => ((NumberExpr)e).Value.ReQ).ToArray();
        BigInteger d = BigInteger.One;
        foreach (var x in q) d = d / BigInteger.GreatestCommonDivisor(d, x.Den) * x.Den;
        // μ = dλ is an eigenvalue of B = dA, an integer matrix with a monic integer characteristic polynomial,
        // so every rational root μ is a whole number (rational root theorem).
        var cA = poly.Select(e => ((NumberExpr)e).Value.ReQ).ToArray();
        var cB = new BigInteger[n + 1];
        for (int k = 0; k <= n; k++)
        {
            var v = cA[k] * new BigRational(BigInteger.Pow(d, n - k));
            if (!v.IsInteger) return null;
            cB[k] = v.Num;
        }
        var factors = FactorMonic(cB, ct);
        if (factors is null) return null;

        var dq = new BigRational(d);
        var all = new List<QuadraticNumber>();
        foreach (var f in factors)
            foreach (var r in f.Roots) all.Add(new QuadraticNumber(r.A / dq, r.B / dq, r.D));

        var groups = all.GroupBy(v => v).Select(g => (Value: g.Key, Count: g.Count()))
            .OrderByDescending(g => g.Value.ToComplex().Re).ThenByDescending(g => g.Value.ToComplex().Im).ToList();
        var values = new List<EigenValue>();
        int geometric = 0;
        foreach (var (value, count) in groups)
        {
            ct.ThrowIfCancellationRequested();
            var vectors = EigenvectorsExact(q, n, value);
            if (vectors.Count == 0 || !vectors.All(v => Verify(q, n, value, v))) return null;
            geometric += vectors.Count;
            var (re, im) = value.ToComplex();
            values.Add(new EigenValue
            {
                Exact = value.ToExpr(),
                ExactText = value.ToDisplay(),
                Approx = new Complex(re, im),
                AlgebraicMultiplicity = count,
                Vectors = [.. vectors.Select(v => v.Select(x => x.ToExpr()).ToArray())],
                VectorTexts = [.. vectors.Select(v => v.Select(x => x.ToDisplay()).ToArray())],
            });
        }
        var (text, input) = FormatFactors(factors, dq);
        return new EigenAnalysis
        {
            Method = EigenMethod.Exact,
            CharacteristicPolynomial = poly,
            Factored = text,
            FactoredInput = input,
            Values = values,
            IsDiagonalizable = geometric == n,
        };
    }

    /// <summary>
    /// Splits a monic integer polynomial (ascending coefficients) into linear and quadratic factors with integer coefficients,
    /// or returns null when some factor of degree 3 or more is left.
    /// </summary>
    internal static List<Factor>? FactorMonic(BigInteger[] ascending, CancellationToken ct)
    {
        var factors = new List<Factor>();
        var p = Trim(ascending);
        while (p.Length > 1 && p[0].IsZero)
        {
            factors.Add(Linear(BigInteger.Zero));
            p = p[1..];
        }
        // Whole-number roots: numeric roots suggest candidates, exact evaluation confirms them.
        var tried = new HashSet<BigInteger>();
        foreach (var guess in Candidates(p, ct))
        {
            if (p.Length <= 1) break;
            if (!tried.Add(guess)) continue;
            while (p.Length > 1 && Evaluate(p, guess).IsZero)
            {
                factors.Add(Linear(guess));
                p = DivideLinear(p, guess);
            }
        }
        // Small constant terms: try every divisor as a safety net for numerically awkward polynomials.
        if (p.Length > 3 && p[0].GetBitLength() <= 40)
        {
            foreach (var div in IntegerMath.Divisors(BigInteger.Abs(p[0])))
            {
                foreach (var g in new[] { div, -div })
                {
                    while (p.Length > 1 && Evaluate(p, g).IsZero)
                    {
                        factors.Add(Linear(g));
                        p = DivideLinear(p, g);
                    }
                }
            }
        }
        // Remaining quadratic factors: pair numeric roots, round the pair's sum and product, confirm by exact division.
        int guard = 0;
        while (p.Length > 3 && guard++ < 20)
        {
            ct.ThrowIfCancellationRequested();
            var found = FindQuadraticFactor(p);
            if (found is null) return null;
            factors.Add(Quadratic(found.Value.B, found.Value.C));
            p = found.Value.Quotient;
        }
        if (p.Length == 2) factors.Add(Linear(-p[0]));
        else if (p.Length == 3) factors.Add(Quadratic(p[1], p[0]));
        else if (p.Length > 3) return null;
        return factors;
    }

    private static IEnumerable<BigInteger> Candidates(BigInteger[] p, CancellationToken ct)
    {
        var result = new List<BigInteger>();
        var roots = NumericRoots(p, ct);
        if (roots is null) return result;
        foreach (var z in roots)
        {
            if (Math.Abs(z.Im) > 1e-3 * Math.Max(1, Math.Abs(z.Re))) continue;
            if (Math.Abs(z.Re) > 1e15) continue;
            var k = new BigInteger(Math.Round(z.Re));
            result.Add(k);
            result.Add(k - 1);
            result.Add(k + 1);
        }
        return result;
    }

    private static Complex[]? NumericRoots(BigInteger[] p, CancellationToken ct)
    {
        var c = p.Select(x => (double)x).ToArray();
        if (c.Any(x => !double.IsFinite(x))) return null;
        try
        {
            return Polynomial.AllRoots(c, ct);
        }
        catch (Exception ex) when (ex is ArgumentException or ArithmeticException or InvalidOperationException)
        {
            return null;
        }
    }

    private static (BigInteger B, BigInteger C, BigInteger[] Quotient)? FindQuadraticFactor(BigInteger[] p)
    {
        var roots = NumericRoots(p, CancellationToken.None);
        if (roots is null) return null;
        for (int i = 0; i < roots.Length; i++)
        {
            for (int j = i + 1; j < roots.Length; j++)
            {
                var s = roots[i] + roots[j];
                var prod = roots[i] * roots[j];
                if (Math.Abs(s.Im) > 1e-6 * Math.Max(1, Math.Abs(s.Re)) || Math.Abs(prod.Im) > 1e-6 * Math.Max(1, Math.Abs(prod.Re))) continue;
                if (Math.Abs(s.Re) > 1e15 || Math.Abs(prod.Re) > 1e15) continue;
                var b = -new BigInteger(Math.Round(s.Re));
                var c = new BigInteger(Math.Round(prod.Re));
                var (quotient, exact) = DivideQuadratic(p, b, c);
                if (exact) return (b, c, quotient);
            }
        }
        return null;
    }

    private static Factor Linear(BigInteger root) => new([-root, BigInteger.One], [QuadraticNumber.Rational(new BigRational(root))]);

    /// <summary>The factor μ² + bμ + c and its two roots (−b ± √(b² − 4c)) / 2.</summary>
    private static Factor Quadratic(BigInteger b, BigInteger c)
    {
        var disc = b * b - 4 * c;
        var half = new BigRational(-b, 2);
        QuadraticNumber[] roots;
        if (disc.IsZero) roots = [half, half];
        else
        {
            var (outside, inside) = IntegerMath.ExtractPower(BigInteger.Abs(disc), 2);
            var radicand = disc.Sign < 0 ? -inside : inside;
            var coefficient = new BigRational(outside, 2);
            if (radicand.IsOne) roots = [half + coefficient, half - coefficient];
            else roots = [new QuadraticNumber(half, coefficient, radicand), new QuadraticNumber(half, -coefficient, radicand)];
        }
        return new Factor([c, b, BigInteger.One], roots);
    }

    private static BigInteger[] Trim(BigInteger[] p)
    {
        int len = p.Length;
        while (len > 1 && p[len - 1].IsZero) len--;
        return p[..len];
    }

    private static BigInteger Evaluate(BigInteger[] p, BigInteger x)
    {
        BigInteger r = BigInteger.Zero;
        for (int i = p.Length - 1; i >= 0; i--) r = r * x + p[i];
        return r;
    }

    /// <summary>Divides a monic polynomial by (μ − root) with synthetic division (the remainder must be zero).</summary>
    private static BigInteger[] DivideLinear(BigInteger[] p, BigInteger root)
    {
        int n = p.Length - 1;
        var q = new BigInteger[n];
        BigInteger carry = BigInteger.Zero;
        for (int i = n; i >= 1; i--)
        {
            carry = p[i] + carry * root;
            q[i - 1] = carry;
        }
        return q;
    }

    /// <summary>Divides a monic polynomial by μ² + bμ + c; returns the quotient and whether the remainder is zero.</summary>
    private static (BigInteger[] Quotient, bool Exact) DivideQuadratic(BigInteger[] p, BigInteger b, BigInteger c)
    {
        var r = (BigInteger[])p.Clone();
        int n = p.Length - 1;
        var q = new BigInteger[n - 1];
        for (int i = n; i >= 2; i--)
        {
            var lead = r[i];
            q[i - 2] = lead;
            r[i] = BigInteger.Zero;
            r[i - 1] -= lead * b;
            r[i - 2] -= lead * c;
        }
        return (q, r[0].IsZero && r[1].IsZero);
    }

    private static (string Text, string Input) FormatFactors(List<Factor> factors, BigRational d)
    {
        // Factors of p_A(λ) = p_B(dλ) / dⁿ: (μ − k) becomes (λ − k/d), μ² + bμ + c becomes λ² + (b/d)λ + c/d².
        var parts = new List<(string Text, string Input)>();
        var lam = new SymbolExpr(CharacteristicPolynomial.Variable);
        // Linear factors first, largest root first (matching the order of the eigenvalues), then quadratics.
        var ordered = factors.GroupBy(f => string.Join(",", f.Monic))
            .OrderBy(g => g.First().Monic.Length)
            .ThenByDescending(g => g.First().Roots[0].ToComplex().Re)
            .ToList();
        foreach (var group in ordered)
        {
            var f = group.First();
            Expr e = f.Monic.Length == 2
                ? Algebra.Sub(lam, Algebra.Q(new BigRational(-f.Monic[0]) / d))
                : Algebra.Add(Algebra.Pow(lam, Expr.Two), Algebra.Mul(Algebra.Q(new BigRational(f.Monic[1]) / d), lam), Algebra.Q(new BigRational(f.Monic[0]) / (d * d)));
            int count = group.Count();
            string text = Calcy.Core.Printing.ExprPrinter.ToText(e);
            string input = Calcy.Core.Printing.ExprPrinter.ToInput(e);
            bool bare = e is SymbolExpr;
            // No-break spaces keep each factor on one line when the text wraps.
            text = bare ? text : "(" + text.Replace(' ', ' ') + ")";
            input = bare ? input : "(" + input + ")";
            if (count > 1)
            {
                text += Superscript(count);
                input += "^" + count;
            }
            parts.Add((text, input));
        }
        return (string.Concat(parts.Select(p => p.Text)), string.Join("*", parts.Select(p => p.Input)));
    }

    private static string Superscript(int k) =>
        new([.. k.ToString(System.Globalization.CultureInfo.InvariantCulture).Select(c => "⁰¹²³⁴⁵⁶⁷⁸⁹"[c - '0'])]);

    /// <summary>A basis of the null space of A − λI, computed exactly in ℚ(√d) and scaled to whole numbers.</summary>
    private static List<QuadraticNumber[]> EigenvectorsExact(BigRational[] a, int n, QuadraticNumber lambda)
    {
        var m = new QuadraticNumber[n, n];
        for (int r = 0; r < n; r++)
            for (int c = 0; c < n; c++)
                m[r, c] = r == c ? QuadraticNumber.Rational(a[r * n + c]) - lambda : QuadraticNumber.Rational(a[r * n + c]);
        var pivots = new List<int>();
        int row = 0;
        for (int col = 0; col < n && row < n; col++)
        {
            int p = -1;
            for (int r = row; r < n; r++) if (!m[r, col].IsZero) { p = r; break; }
            if (p < 0) continue;
            if (p != row) for (int c = 0; c < n; c++) (m[p, c], m[row, c]) = (m[row, c], m[p, c]);
            var inv = QuadraticNumber.One / m[row, col];
            for (int c = 0; c < n; c++) m[row, c] *= inv;
            for (int r = 0; r < n; r++)
            {
                if (r == row || m[r, col].IsZero) continue;
                var f = m[r, col];
                for (int c = 0; c < n; c++) m[r, c] -= f * m[row, c];
            }
            pivots.Add(col);
            row++;
        }
        var basis = new List<QuadraticNumber[]>();
        for (int free = 0; free < n; free++)
        {
            if (pivots.Contains(free)) continue;
            var v = new QuadraticNumber[n];
            for (int i = 0; i < n; i++) v[i] = QuadraticNumber.Zero;
            v[free] = QuadraticNumber.One;
            for (int i = 0; i < pivots.Count; i++) v[pivots[i]] = -m[i, free];
            basis.Add(ScaleToIntegers(v));
        }
        return basis;
    }

    private static QuadraticNumber[] ScaleToIntegers(QuadraticNumber[] v)
    {
        BigInteger lcm = BigInteger.One;
        foreach (var x in v)
        {
            lcm = lcm / BigInteger.GreatestCommonDivisor(lcm, x.A.Den) * x.A.Den;
            lcm = lcm / BigInteger.GreatestCommonDivisor(lcm, x.B.Den) * x.B.Den;
        }
        BigInteger gcd = BigInteger.Zero;
        foreach (var x in v)
        {
            gcd = BigInteger.GreatestCommonDivisor(gcd, x.A.Num * (lcm / x.A.Den));
            gcd = BigInteger.GreatestCommonDivisor(gcd, x.B.Num * (lcm / x.B.Den));
        }
        if (gcd.IsZero) return v;
        var factor = new BigRational(lcm, gcd);
        return [.. v.Select(x => x * QuadraticNumber.Rational(factor))];
    }

    private static bool Verify(BigRational[] a, int n, QuadraticNumber lambda, QuadraticNumber[] v)
    {
        if (v.All(x => x.IsZero)) return false;
        for (int r = 0; r < n; r++)
        {
            var s = QuadraticNumber.Zero;
            for (int c = 0; c < n; c++) s += QuadraticNumber.Rational(a[r * n + c]) * v[c];
            if (s != lambda * v[r]) return false;
        }
        return true;
    }

    // ------------------------------------------------------------------ exact, 2×2 with any entries

    private static EigenAnalysis TwoByTwo(MatrixExpr m, Expr[] poly, CalcSession session, CancellationToken ct)
    {
        Expr a = m[0, 0], b = m[0, 1], c = m[1, 0], d = m[1, 1];
        var values = new List<EigenValue>();
        if (RowReduction.IsZero(b) || RowReduction.IsZero(c))
        {
            // Triangular: the eigenvalues are the diagonal entries.
            if (a.Equals(d))
            {
                values.Add(new EigenValue { Exact = a, Approx = Approx(a, session, ct), AlgebraicMultiplicity = 2, Vectors = Vectors2(a, b, c, d, a, repeated: true) });
            }
            else
            {
                values.Add(new EigenValue { Exact = a, Approx = Approx(a, session, ct), Vectors = Vectors2(a, b, c, d, a, repeated: false) });
                values.Add(new EigenValue { Exact = d, Approx = Approx(d, session, ct), Vectors = Vectors2(a, b, c, d, d, repeated: false) });
                if (values.All(v => !double.IsNaN(v.Approx.Re)))
                    values = [.. values.OrderByDescending(v => v.Approx.Re).ThenByDescending(v => v.Approx.Im)];
            }
            return new EigenAnalysis
            {
                Method = EigenMethod.Exact,
                CharacteristicPolynomial = poly,
                Values = values,
                IsDiagonalizable = values.Sum(v => v.Vectors.Count) == 2,
            };
        }
        // det(λI − A) = λ² + c₁λ + c₀, so λ = (−c₁ ± √(c₁² − 4c₀)) / 2.
        var trace = Algebra.Neg(poly[1]);
        var disc = CharacteristicPolynomial.Tidy(Algebra.Sub(Algebra.Mul(poly[1], poly[1]), Algebra.Mul(Expr.Int(4), poly[0])), session, ct);
        if (RowReduction.IsZero(disc))
        {
            var lambda = Algebra.Div(trace, Expr.Two);
            var vectors = Vectors2(a, b, c, d, lambda, repeated: true);
            values.Add(new EigenValue { Exact = lambda, Approx = Approx(lambda, session, ct), AlgebraicMultiplicity = 2, Vectors = vectors });
        }
        else
        {
            var root = Algebra.Sqrt(disc);
            foreach (var sign in new[] { 1, -1 })
            {
                var lambda = CharacteristicPolynomial.Tidy(Algebra.Div(Algebra.Add(trace, sign > 0 ? root : Algebra.Neg(root)), Expr.Two), session, ct);
                values.Add(new EigenValue { Exact = lambda, Approx = Approx(lambda, session, ct), Vectors = Vectors2(a, b, c, d, lambda, repeated: false) });
            }
            if (values.All(v => !double.IsNaN(v.Approx.Re)))
                values = [.. values.OrderByDescending(v => v.Approx.Re).ThenByDescending(v => v.Approx.Im)];
        }
        return new EigenAnalysis
        {
            Method = EigenMethod.Exact,
            CharacteristicPolynomial = poly,
            Values = values,
            IsDiagonalizable = values.Sum(v => v.Vectors.Count) == 2,
        };
    }

    private static IReadOnlyList<Expr[]> Vectors2(Expr a, Expr b, Expr c, Expr d, Expr lambda, bool repeated)
    {
        // (A − λI)v = 0: the first row gives v = (b, λ − a), the second v = (λ − d, c).
        if (!RowReduction.IsZero(b)) return [[b, Algebra.Sub(lambda, a)]];
        if (!RowReduction.IsZero(c)) return [[Algebra.Sub(lambda, d), c]];
        // Diagonal matrix: the standard basis vectors.
        if (repeated) return [[Expr.One, Expr.Zero], [Expr.Zero, Expr.One]];
        bool isA = RowReduction.IsZero(Algebra.Sub(lambda, a));
        return isA ? [[Expr.One, Expr.Zero]] : [[Expr.Zero, Expr.One]];
    }

    private static Complex Approx(Expr e, CalcSession session, CancellationToken ct)
    {
        if (!e.IsConstantValue()) return new Complex(double.NaN, double.NaN);
        try
        {
            if (session.Evaluate(e, approximate: true, ct) is NumberExpr n) return new Complex(n.Value.Re, n.Value.Im);
        }
        catch (CalcException)
        {
        }
        return new Complex(double.NaN, double.NaN);
    }

    // ------------------------------------------------------------------ numeric

    private static EigenAnalysis Numeric(MatrixExpr a, Expr[] poly, CalcSession session, CancellationToken ct)
    {
        NumMatrix m = NumericBridge.ToNumeric(a, session, ct);
        EigenResult result;
        try
        {
            result = LinearAlgebra.Eigen(m, ct);
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or ArithmeticException)
        {
            throw new CalcException("The eigenvalues could not be computed: " + ex.Message);
        }
        double scale = Math.Max(1, m.MaxAbs());
        var values = new List<EigenValue>();
        for (int k = 0; k < result.Values.Length; k++)
        {
            var z = result.Values[k];
            var value = new Complex(NumericBridge.Clean(z.Re, scale), NumericBridge.Clean(z.Im, scale));
            var vector = result.Vectors.GetColumn(k).Select(x => new Complex(NumericBridge.Clean(x.Re, 1), NumericBridge.Clean(x.Im, 1))).ToArray();
            int same = values.FindIndex(v => Complex.Abs(v.Approx - value) <= 1e-9 * scale);
            if (same >= 0)
            {
                var old = values[same];
                bool independent = !old.NumericVectors.Any(u => Parallel(u, vector));
                values[same] = old with
                {
                    AlgebraicMultiplicity = old.AlgebraicMultiplicity + 1,
                    NumericVectors = independent ? [.. old.NumericVectors, vector] : old.NumericVectors,
                };
                continue;
            }
            values.Add(new EigenValue { Approx = value, NumericVectors = [vector] });
        }
        return new EigenAnalysis
        {
            Method = EigenMethod.Numeric,
            CharacteristicPolynomial = poly,
            Values = values,
            IsDiagonalizable = result.IsDiagonalizable,
        };
    }

    private static bool Parallel(Complex[] u, Complex[] v)
    {
        // |u* v| = |u||v| for parallel vectors (both are unit vectors here).
        Complex dot = Complex.Zero;
        for (int i = 0; i < u.Length; i++) dot += Complex.Conjugate(u[i]) * v[i];
        return Math.Abs(Complex.Abs(dot) - 1) < 1e-8;
    }
}
