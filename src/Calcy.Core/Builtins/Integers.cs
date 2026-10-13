using System.Collections.Immutable;
using System.Numerics;
using Calcy.Core.Evaluation;
using Calcy.Core.Expressions;
using Calcy.Core.Numbers;

namespace Calcy.Core.Builtins;

public static partial class CoreBuiltins
{
    private static void RegisterIntegers(FunctionRegistry r)
    {
        r.Add(Def("factorial", Factorial, "Combinatorics", "n! or factorial(n)", "n! = 1·2·3·…·n. For non-integers, Γ(n + 1).", "10!", real1: x => GammaFunction(x + 1)));
        r.Add(Def("doublefactorial", c => Z(IntegerMath.DoubleFactorial(IntegerArg(c, 0))), "Combinatorics", "n!!", "Double factorial: n·(n-2)·(n-4)·…", "7!!"));
        r.Add(Def("nCr", c => BinomialCall(c), "Combinatorics", "nCr(n, r)", "Combinations: ways to choose r items from n, order not important.", "nCr(5, 2)",
            min: 2, max: 2, aliases: ["binomial", "comb", "C"]));
        r.Add(Def("nPr", c => Z(IntegerMath.Permutations(IntegerArg(c, 0), IntegerArg(c, 1))), "Combinatorics", "nPr(n, r)", "Permutations: ordered arrangements of r items from n.", "nPr(5, 2)",
            min: 2, max: 2, aliases: ["perm", "P"]));
        r.Add(Def("gcd", c => GcdLcm(c, gcd: true), "Number theory", "gcd(a, b, ...)", "Greatest common divisor (also of fractions and lists).", "gcd(84, 36)", min: 1, max: -1, listable: false));
        r.Add(Def("lcm", c => GcdLcm(c, gcd: false), "Number theory", "lcm(a, b, ...)", "Least common multiple.", "lcm(4, 6)", min: 1, max: -1, listable: false));
        r.Add(Def("mod", Mod, "Number theory", "mod(a, n) or a mod n", "Remainder after division, with the sign of n (floored): mod(-7, 3) = 2.", "mod(17, 5)", min: 2, max: 2,
            real2: (a, n) => a - n * Math.Floor(a / n)));
        r.Add(Def("rem", c =>
        {
            var a = NumberArg(c, 0); var n = NumberArg(c, 1);
            if (n.IsZero) throw c.Error("division by zero");
            if (a.IsExactRational && n.IsExactRational) return Q(a.ReQ - n.ReQ * new BigRational((a.ReQ / n.ReQ).Truncate()));
            return N(a.Re - n.Re * Math.Truncate(a.Re / n.Re));
        }, "Number theory", "rem(a, n)", "Remainder with the sign of a (truncated division): rem(-7, 3) = -1.", "rem(-7, 3)", min: 2, max: 2));
        r.Add(Def("isprime", c => BoolExpr.Of(IntegerMath.IsProbablePrime(IntegerArg(c, 0))), "Number theory", "isprime(n)", "True when n is a prime number.", "isprime(97)", aliases: ["isPrime"]));
        r.Add(Def("nextprime", c => Z(IntegerMath.NextPrime(IntegerArg(c, 0))), "Number theory", "nextprime(n)", "The smallest prime greater than n.", "nextprime(100)", aliases: ["nextPrime"]));
        r.Add(Def("prevprime", c => Z(IntegerMath.PreviousPrime(IntegerArg(c, 0))), "Number theory", "prevprime(n)", "The largest prime less than n.", "prevprime(100)", aliases: ["prevPrime"]));
        r.Add(Def("ifactor", IntegerFactor, "Number theory", "ifactor(n)", "Prime factorization of a whole number: ifactor(360) = 2³·3²·5.", "ifactor(360)", aliases: ["factorint", "primefactors"]));
        r.Add(Def("factor", c => c[0] is NumberExpr { Value.IsExactRational: true } ? IntegerFactor(c) : null, "Algebra", "factor(x)",
            "Factorizes whole numbers (and, with the algebra engine, polynomials).", "factor(360)"));
        r.Add(Def("totient", c => Z(IntegerMath.Totient(IntegerArg(c, 0))), "Number theory", "totient(n)", "Euler's φ: how many numbers from 1 to n share no factor with n.", "totient(36)", aliases: ["phi", "eulerphi"]));
        r.Add(Def("divisors", c => new ListExpr([.. IntegerMath.Divisors(IntegerArg(c, 0)).Select(d => (Expr)Z(d))]), "Number theory", "divisors(n)", "All positive divisors of n.", "divisors(28)"));
        r.Add(Def("modinv", c => Z(IntegerMath.ModInverse(IntegerArg(c, 0), IntegerArg(c, 1))), "Number theory", "modinv(a, m)", "The inverse of a modulo m (a·x ≡ 1 mod m).", "modinv(3, 11)", min: 2, max: 2));
        r.Add(Def("modpow", c =>
        {
            var m = IntegerArg(c, 2);
            if (m.IsZero) throw c.Error("the modulus cannot be 0");
            var e = IntegerArg(c, 1);
            var b = IntegerArg(c, 0);
            if (e.Sign < 0) { b = IntegerMath.ModInverse(b, m); e = -e; }
            return Z(IntegerMath.Mod(BigInteger.ModPow(IntegerMath.Mod(b, m), e, BigInteger.Abs(m)), m));
        }, "Number theory", "modpow(a, b, m)", "a^b mod m, computed efficiently for huge numbers.", "modpow(2, 100, 7)", min: 3, max: 3));
        r.Add(Def("numer", c => c[0] is NumberExpr { Value.IsExactRational: true } n ? Z(n.Value.ReQ.Num) : null, "Numbers", "numer(q)", "Numerator of a fraction.", "numer(6/8)", aliases: ["numerator"]));
        r.Add(Def("denom", c => c[0] is NumberExpr { Value.IsExactRational: true } n ? Z(n.Value.ReQ.Den) : null, "Numbers", "denom(q)", "Denominator of a fraction.", "denom(6/8)", aliases: ["denominator"]));
    }

    private static Expr? Factorial(CallInfo c)
    {
        var x = c[0];
        if (x is not NumberExpr n) return null;
        if (n.Value.IsExactInteger)
        {
            var k = n.Value.ToBigInteger();
            if (k.Sign < 0) throw c.Error("factorials of negative whole numbers are undefined");
            if (k > 5000 && !c.Approximate) return null;
            return Z(IntegerMath.Factorial(k));
        }
        if (!n.Value.IsReal) return null;
        double v = n.Value.Re;
        if (v < 0 && Math.Floor(v) == v) throw c.Error("factorials of negative whole numbers are undefined");
        if (n.Value.IsExact && !c.Approximate) return null;
        return N(GammaFunction(v + 1));
    }

    /// <summary>Lanczos approximation of the gamma function for real arguments.</summary>
    internal static double GammaFunction(double z)
    {
        if (z < 0.5) return Math.PI / (Math.Sin(Math.PI * z) * GammaFunction(1 - z));
        double[] g = [0.99999999999980993, 676.5203681218851, -1259.1392167224028, 771.32342877765313, -176.61502916214059,
            12.507343278686905, -0.13857109526572012, 9.9843695780195716e-6, 1.5056327351493116e-7];
        z -= 1;
        double x = g[0];
        for (int i = 1; i < 9; i++) x += g[i] / (z + i);
        double t = z + 7.5;
        return Math.Sqrt(2 * Math.PI) * Math.Pow(t, z + 0.5) * Math.Exp(-t) * x;
    }

    private static Expr? BinomialCall(CallInfo c)
    {
        if (c[0] is NumberExpr { Value.IsExactInteger: true } && c[1] is NumberExpr { Value.IsExactInteger: true })
            return Z(IntegerMath.Binomial(IntegerArg(c, 0), IntegerArg(c, 1)));
        if (c[0] is NumberExpr && c[1] is NumberExpr { Value.IsExactInteger: true } kk && kk.Value.ToBigInteger() is var k && k >= 0 && k < 1000)
        {
            // Generalized binomial coefficient: n(n-1)...(n-k+1)/k!
            Expr acc = Expr.One;
            for (int i = 0; i < (int)k; i++) acc = Algebra.Mul(acc, Algebra.Sub(c[0], Expr.Int(i)));
            return Algebra.Div(acc, Z(IntegerMath.Factorial(k)));
        }
        return null;
    }

    private static Expr? GcdLcm(CallInfo c, bool gcd)
    {
        var items = c.Args.Count == 1 && c[0] is ListExpr l ? l.Items.ToList() : c.Args.ToList();
        if (items.Count == 0) throw c.Error("needs at least one number");
        if (!items.All(i => i is NumberExpr { Value.IsExactRational: true })) return null;
        var qs = items.Select(i => ((NumberExpr)i).Value.ReQ).ToList();
        // For fractions: gcd(a/b, c/d) = gcd(a, c) / lcm(b, d).
        BigInteger Gcd(BigInteger a, BigInteger b) => BigInteger.GreatestCommonDivisor(a, b);
        BigInteger Lcm(BigInteger a, BigInteger b) => a.IsZero || b.IsZero ? BigInteger.Zero : BigInteger.Abs(a / Gcd(a, b) * b);
        BigInteger num = BigInteger.Abs(qs[0].Num), den = qs[0].Den;
        foreach (var q in qs.Skip(1))
        {
            if (gcd)
            {
                num = Gcd(num, q.Num);
                den = Lcm(den, q.Den);
            }
            else
            {
                num = Lcm(num, q.Num);
                den = Gcd(den, q.Den);
            }
        }
        return Q(new BigRational(num, den.IsZero ? 1 : den));
    }

    private static Expr? Mod(CallInfo c)
    {
        if (c[0] is NumberExpr a && c[1] is NumberExpr n)
        {
            if (n.Value.IsZero) throw c.Error("division by zero");
            if (a.Value.IsExactRational && n.Value.IsExactRational)
            {
                var q = a.Value.ReQ / n.Value.ReQ;
                return Q(a.Value.ReQ - n.Value.ReQ * new BigRational(q.Floor()));
            }
            if (!a.Value.IsReal || !n.Value.IsReal) throw c.Error("needs real numbers");
            double av = a.Value.Re, nv = n.Value.Re;
            return N(av - nv * Math.Floor(av / nv));
        }
        return null;
    }

    private static Expr? IntegerFactor(CallInfo c)
    {
        if (c[0] is not NumberExpr { Value.IsExactRational: true } n) return null;
        var q = n.Value.ReQ;
        if (q.IsZero) return Expr.Zero;
        var parts = new List<Expr>();
        if (q.IsNegative) parts.Add(Expr.MinusOne);
        void AddFactors(BigInteger v, bool inverse)
        {
            if (v.IsOne) return;
            foreach (var (p, e) in IntegerMath.Factorize(v, ct: c.Context.Cancellation))
            {
                var exp = inverse ? -e : e;
                parts.Add(exp == 1 ? Z(p) : new PowExpr(Z(p), Expr.Int(exp)));
            }
        }
        AddFactors(BigInteger.Abs(q.Num), false);
        AddFactors(q.Den, true);
        if (parts.Count == 0) return Expr.One;
        if (parts.Count == 1) return parts[0];
        // Built without simplification so the factored form is shown (2³·3²·5).
        return new MulExpr([.. parts]);
    }
}
