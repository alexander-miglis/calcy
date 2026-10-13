using Calcy.Core;
using Calcy.Core.Compilation;
using Calcy.Core.Evaluation;
using Calcy.Core.Expressions;
using Calcy.Core.Numbers;
using Calcy.Numerics;
using NComplex = Calcy.Numerics.Complex;
using NMatrix = Calcy.Numerics.Matrix;

namespace Calcy.Engine;

/// <summary>
/// Numeric methods from Calcy.Numerics as calculator functions: nsolve, nint, nderiv, fmin, fmax, nsum, odesolve,
/// polyroots, numeric eigenvalues and decompositions, fft. Functions another module already provides (for example an
/// exact eigenvals from the algebra system) are left alone.
/// </summary>
public static class NumericsModule
{
    private const string Cat = "Numeric methods";
    private const string MatCat = "Matrices";

    /// <summary>Registers the numeric functions. Names in <paramref name="replace"/> are registered even when another module has them.</summary>
    public static void Register(FunctionRegistry r, params string[] replace)
    {
        void Add(FunctionDefinition f)
        {
            if (r.Contains(f.Name) && !replace.Contains(f.Name)) return;
            r.Add(new FunctionDefinition
            {
                Name = f.Name,
                Aliases = [.. f.Aliases.Where(a => !r.Contains(a) || (r.TryGet(a, out var owner) && owner.Name == f.Name))],
                MinArgs = f.MinArgs,
                MaxArgs = f.MaxArgs,
                Listable = f.Listable,
                HoldArgs = f.HoldArgs,
                Evaluate = f.Evaluate,
                Category = f.Category,
                Signature = f.Signature,
                Description = f.Description,
                Example = f.Example,
            });
        }

        Add(Def("nsolve", NSolve, Cat, "nsolve(equation, x) or nsolve(equation, x, guess) or nsolve(equation, x, a, b)",
            "Numeric solutions: near a guess, every root between a and b, or every root found in a wide search. Systems: nsolve({eq1, eq2}, {x, y}, {guesses}).",
            "nsolve(cos(x) = x, x)", 2, 4, hold: true, aliases: ["nSolve", "fsolve"]));
        Add(Def("nint", NInt, Cat, "nint(f, x, a, b)", "Numeric definite integral (adaptive, handles infinite bounds and endpoint singularities).",
            "nint(e^(-x^2), x, -inf, inf)", 4, 4, hold: true, aliases: ["fnInt", "nIntegrate", "quad"]));
        Add(Def("nderiv", NDeriv, Cat, "nderiv(f, x, a) or nderiv(f, x, a, n)", "Numeric derivative of order n (1 to 4) at x = a.",
            "nderiv(x^x, x, 2)", 3, 4, hold: true, aliases: ["nDeriv"]));
        Add(Def("fmin", c => Extremum(c, max: false), Cat, "fmin(f, x, a, b)", "The x between a and b where f is smallest.",
            "fmin(x^2 - 4x, x, -10, 10)", 4, 4, hold: true, aliases: ["fMin"]));
        Add(Def("fmax", c => Extremum(c, max: true), Cat, "fmax(f, x, a, b)", "The x between a and b where f is largest.",
            "fmax(sin(x) + x/5, x, 0, 10)", 4, 4, hold: true, aliases: ["fMax"]));
        Add(Def("nsum", NSum, Cat, "nsum(term, k, a, b)", "Numeric sum; b may be inf (convergence acceleration).",
            "nsum(1/k^2, k, 1, inf)", 4, 4, hold: true));
        Add(Def("odesolve", OdeSolve, Cat, "odesolve(rhs, t, y, t0, y0, t1)",
            "Value at t1 of the solution of y' = rhs(t, y) with y(t0) = y0. Systems use lists: odesolve({y2, -y1}, t, {y1, y2}, 0, {0, 1}, pi).",
            "odesolve(y, t, y, 0, 1, 1)", 6, 6, hold: true, aliases: ["rk", "dsolvenum"]));
        Add(Def("polyroots", PolyRoots, Cat, "polyroots({a_n, ..., a_0}) or polyroots(p, x)",
            "All complex roots of a polynomial (coefficients from the highest power down).", "polyroots({1, 0, -2})", 1, 2, hold: true,
            aliases: ["proots", "cPolyRoots"]));

        Add(Def("eigenvals", c => Eigenvalues(c), MatCat, "eigenvals(A)", "Eigenvalues of a square matrix (numeric).", "eigenvals([[2, 1], [1, 2]])",
            listable: false, aliases: ["eigenvalues", "eigVl"]));
        Add(Def("eigenvects", c => Eigenvectors(c), MatCat, "eigenvects(A)", "Eigenvectors of a square matrix as columns (numeric, unit length).",
            "eigenvects([[2, 1], [1, 2]])", listable: false, aliases: ["eigenvectors", "eigVc"]));
        Add(Def("charpoly", CharPoly, MatCat, "charpoly(A, x)", "Characteristic polynomial det(xI - A).", "charpoly([[2, 1], [1, 2]], x)",
            1, 2, listable: false));
        Add(Def("svd", c => SingularValues(c), MatCat, "svd(A)", "Singular values of a matrix, largest first.", "svd([[3, 0], [4, 5]])", listable: false));
        Add(Def("cond", c => N(LinearAlgebra.ConditionNumber(ToMatrix(c, 0))), MatCat, "cond(A)", "Condition number (2-norm).",
            "cond([[1, 2], [3, 4]])", listable: false));
        Add(Def("pinv", c => FromMatrix(LinearAlgebra.PseudoInverse(ToMatrix(c, 0))), MatCat, "pinv(A)", "Moore-Penrose pseudo-inverse.",
            "pinv([[1, 2], [2, 4]])", listable: false));
        Add(Def("expm", c => FromMatrix(LinearAlgebra.MatrixExponential(Square(c, 0))), MatCat, "expm(A)", "Matrix exponential e^A.",
            "expm([[0, 1], [-1, 0]])", listable: false));
        Add(Def("lu", Lu, MatCat, "lu(A)", "LU decomposition with partial pivoting: {L, U, P} with P·A = L·U.", "lu([[2, 1], [4, 3]])", listable: false));
        Add(Def("qr", Qr, MatCat, "qr(A)", "QR decomposition: {Q, R} with A = Q·R.", "qr([[1, 2], [3, 4]])", listable: false));
        Add(Def("cholesky", c => FromMatrix(new CholeskyDecomposition(Square(c, 0)).L), MatCat, "cholesky(A)",
            "Cholesky factor L of a symmetric positive definite matrix: A = L·Lᵀ.", "cholesky([[4, 2], [2, 3]])", listable: false));
        Add(Def("fft", c => Fft(c, inverse: false), "Lists", "fft(list)", "Discrete Fourier transform (any length).", "fft({1, 0, 0, 0})", listable: false));
        Add(Def("ifft", c => Fft(c, inverse: true), "Lists", "ifft(list)", "Inverse discrete Fourier transform.", "ifft({1, 1, 1, 1})", listable: false));
    }

    // ------------------------------------------------------------------ functions

    private static Expr NSolve(CallInfo c)
    {
        var ct = c.Context.Cancellation;
        if (c[1] is ListExpr vars)
        {
            var names = vars.Items.Select((v, i) => v as SymbolExpr ?? throw c.Error($"item {i + 1} of the variables must be a name")).Select(s => s.Name).ToList();
            var eqs = c[0] is ListExpr l ? l.Items.ToList() : [c[0]];
            if (eqs.Count != names.Count) throw c.Error($"give as many equations as unknowns ({eqs.Count} equations, {names.Count} unknowns)");
            var fs = eqs.Select(e => ExprCompiler.Compile(Residual(e), names, c.Context)).ToArray();
            double[] F(double[] x) => [.. fs.Select(f => f(x))];
            SystemSolveResult result;
            if (c.Count >= 3)
            {
                var guess = Evaluator.Eval(c[2], c.Context.WithApproximate());
                var x0 = guess is ListExpr gl ? gl.Items.Select(g => Real(c, g)).ToArray() : [Real(c, guess)];
                if (x0.Length != names.Count) throw c.Error("give one starting value per unknown");
                result = RootFinding.NewtonSystem(F, x0, restarts: 8, cancellationToken: ct);
                if (!result.Converged) throw new CalcException("No solution was found near the starting values; try other guesses");
            }
            else
            {
                var found = RootFinding.FindSystemSolutions(F, [.. names.Select(_ => -10.0)], [.. names.Select(_ => 10.0)], cancellationToken: ct);
                if (found.Count == 0) throw new CalcException("No solution was found between -10 and 10; give starting values");
                if (found.Count > 1)
                    return Any([.. found.Select(s => All(names, s.X))]);
                result = found[0];
            }
            return All(names, result.X);
        }

        var x = Symbol(c, 1);
        var f = ExprCompiler.Compile1(Residual(c[0]), x, c.Context);
        if (c.Count == 3)
        {
            double x0 = Real(c, Evaluator.Eval(c[2], c.Context.WithApproximate()));
            var r = RootFinding.FindRoot(f, x0, cancellationToken: ct);
            if (!r.Converged) throw new CalcException("No solution was found near " + Format(x0) + "; try another guess");
            return Is(x, r.Root);
        }
        IReadOnlyList<double> roots;
        if (c.Count == 4)
        {
            double a = Real(c, Evaluator.Eval(c[2], c.Context.WithApproximate()));
            double b = Real(c, Evaluator.Eval(c[3], c.Context.WithApproximate()));
            if (!(a < b)) (a, b) = (b, a);
            roots = [.. RootFinding.FindAllRoots(f, a, b, 4000, ct).Select(r => r.X)];
        }
        else roots = WideSearch(f, ct);
        if (roots.Count == 0) throw new CalcException(c.Count == 4 ? "No solution in that interval" : "No real solution was found; give an interval or a guess");
        if (roots.Count == 1) return Is(x, roots[0]);
        return Any([.. roots.Select(r => Is(x, r))]);
    }

    // Results read like solve: x = 2, x = -1 or x = 1, x = 2 and y = 1.
    private static Expr Is(string name, double value) => new RelationExpr(RelOp.Equal, new SymbolExpr(name), N(Clean(value)));

    private static Expr All(IReadOnlyList<string> names, double[] values) =>
        names.Count == 1 ? Is(names[0], values[0]) : new LogicExpr(LogicOp.And, [.. names.Select((n, i) => Is(n, values[i]))]);

    private static Expr Any(IReadOnlyList<Expr> options) => options.Count == 1 ? options[0] : new LogicExpr(LogicOp.Or, [.. options]);

    /// <summary>Roots in [-10, 10] at fine resolution, plus [-1000, 1000] coarsely, without duplicates.</summary>
    private static List<double> WideSearch(Func<double, double> f, CancellationToken ct)
    {
        var all = new List<double>();
        foreach (var (a, b, n) in new[] { (-10.0, 10.0, 8000), (-1000.0, 1000.0, 8000) })
            foreach (var r in RootFinding.FindAllRoots(f, a, b, n, ct))
                if (!all.Any(x => Math.Abs(x - r.X) <= 1e-9 * Math.Max(1, Math.Abs(x)))) all.Add(r.X);
        all.Sort();
        return all;
    }

    private static Expr NInt(CallInfo c)
    {
        var x = Symbol(c, 1);
        var f = ExprCompiler.Compile1(c[0], x, c.Context);
        double a = Bound(c, 2), b = Bound(c, 3);
        var r = Integration.Integrate(f, a, b, cancellationToken: c.Context.Cancellation);
        if (!r.Success && !(Math.Abs(r.ErrorEstimate) <= 1e-6 * Math.Max(1, Math.Abs(r.Value))))
            throw new CalcException("The integral did not converge" + (r.Message is null ? "" : ": " + r.Message));
        return N(Clean(r.Value));
    }

    private static Expr NDeriv(CallInfo c)
    {
        var x = Symbol(c, 1);
        var f = ExprCompiler.Compile1(c[0], x, c.Context);
        double a = Real(c, Evaluator.Eval(c[2], c.Context.WithApproximate()));
        int order = c.Count == 4 ? (int)Real(c, Evaluator.Eval(c[3], c.Context.WithApproximate())) : 1;
        if (order is < 1 or > 4) throw c.Error("the order must be 1, 2, 3 or 4");
        var d = Differentiation.Derivative(f, a, order);
        if (!double.IsFinite(d.Value)) throw new CalcException("The derivative does not exist there");
        return N(Clean(d.Value));
    }

    private static Expr Extremum(CallInfo c, bool max)
    {
        var x = Symbol(c, 1);
        var f = ExprCompiler.Compile1(c[0], x, c.Context);
        double a = Real(c, Evaluator.Eval(c[2], c.Context.WithApproximate()));
        double b = Real(c, Evaluator.Eval(c[3], c.Context.WithApproximate()));
        if (!(a < b)) (a, b) = (b, a);
        var r = max ? Optimization.FindGlobalMaximum(f, a, b, cancellationToken: c.Context.Cancellation)
                    : Optimization.FindGlobalMinimum(f, a, b, cancellationToken: c.Context.Cancellation);
        if (r.IsUnbounded) throw new CalcException(max ? "The function has no largest value there (it grows without bound)" : "The function has no smallest value there (it falls without bound)");
        return N(Clean(r.X));
    }

    private static Expr NSum(CallInfo c)
    {
        var k = Symbol(c, 1);
        var term = ExprCompiler.Compile1(c[0], k, c.Context);
        double a = Bound(c, 2), b = Bound(c, 3);
        if (!double.IsFinite(a) || a != Math.Floor(a)) throw c.Error("the start must be a whole number");
        if (double.IsPositiveInfinity(b))
        {
            var r = Series.SumInfinite(term, (long)a, cancellationToken: c.Context.Cancellation);
            if (!r.Converged) throw new CalcException("The series does not seem to converge" + (r.Message is null ? "" : ": " + r.Message));
            return N(Clean(r.Value));
        }
        if (b != Math.Floor(b)) throw c.Error("the end must be a whole number or inf");
        if (b - a > 1e8) throw c.Error("that is too many terms; use inf for an infinite sum");
        return N(Series.Sum(term, (long)a, (long)b, c.Context.Cancellation));
    }

    private static Expr OdeSolve(CallInfo c)
    {
        var t = Symbol(c, 1);
        bool system = c[2] is ListExpr;
        var names = system
            ? ((ListExpr)c[2]).Items.Select((v, i) => v is SymbolExpr s ? s.Name : throw c.Error($"item {i + 1} of the unknowns must be a name")).ToList()
            : [Symbol(c, 2)];
        var rhsExprs = c[0] is ListExpr rl ? rl.Items.ToList() : [c[0]];
        if (rhsExprs.Count != names.Count) throw c.Error("give one right-hand side per unknown");
        var vars = new List<string> { t };
        vars.AddRange(names);
        var rhs = rhsExprs.Select(e => ExprCompiler.Compile(e, vars, c.Context)).ToArray();
        double t0 = Real(c, Evaluator.Eval(c[3], c.Context.WithApproximate()));
        var y0Expr = Evaluator.Eval(c[4], c.Context.WithApproximate());
        double[] y0 = y0Expr is ListExpr yl ? [.. yl.Items.Select(v => Real(c, v))] : [Real(c, y0Expr)];
        if (y0.Length != names.Count) throw c.Error("give one starting value per unknown");
        double t1 = Real(c, Evaluator.Eval(c[5], c.Context.WithApproximate()));
        var buffer = new double[vars.Count];
        double[] F(double tt, double[] y)
        {
            buffer[0] = tt;
            Array.Copy(y, 0, buffer, 1, y.Length);
            var dy = new double[y.Length];
            for (int i = 0; i < rhs.Length; i++) dy[i] = rhs[i](buffer);
            return dy;
        }
        var options = new OdeOptions { RelativeTolerance = 1e-10, AbsoluteTolerance = 1e-12 };
        var sol = Ode.DormandPrince(F, t0, y0, t1, options, c.Context.Cancellation);
        if (!sol.Success)
        {
            // Stiff problems need an implicit method.
            sol = Ode.Rosenbrock(F, t0, y0, t1, new OdeOptions { RelativeTolerance = 1e-8, AbsoluteTolerance = 1e-10 }, cancellationToken: c.Context.Cancellation);
            if (!sol.Success) throw new CalcException("The solver could not reach t1" + (sol.Message is null ? "" : ": " + sol.Message));
        }
        var end = sol.FinalState;
        return system ? new ListExpr([.. end.Select(v => (Expr)N(Clean(v)))]) : N(Clean(end[0]));
    }

    private static Expr PolyRoots(CallInfo c)
    {
        double[] ascending;
        var first = Evaluator.Eval(c[0], c.Context.WithApproximate());
        if (c.Count == 1)
        {
            if (first is not ListExpr coeffs || coeffs.Items.Length == 0) throw c.Error("give the coefficients as a list, for example {1, 0, -2} for x^2 - 2");
            ascending = [.. coeffs.Items.Reverse().Select(v => Real(c, v))];
        }
        else
        {
            var x = Symbol(c, 1);
            if (!c.Context.Functions.Contains("coeffs"))
                throw c.Error("give the coefficients as a list, for example polyroots({1, 0, -2})");
            var list = Evaluator.Eval(CallExpr.Of("coeffs", c[0], new SymbolExpr(x)), c.Context.WithFree(x));
            list = Evaluator.Eval(list, c.Context.WithApproximate());
            if (list is not ListExpr cl) throw c.Error("the first argument must be a polynomial in " + x);
            // coeffs lists the highest power first, like the list form.
            ascending = [.. cl.Items.Reverse().Select(v => Real(c, v))];
        }
        if (Polynomial.Degree(ascending) < 1) throw new CalcException("A constant has no roots");
        var roots = Polynomial.AllRoots(ascending, c.Context.Cancellation)
            .OrderBy(z => Math.Abs(z.Im) > 1e-12 ? 1 : 0).ThenBy(z => z.Re).ThenBy(z => z.Im);
        return new ListExpr([.. roots.Select(z => (Expr)ComplexNumber(z))]);
    }

    private static Expr Eigenvalues(CallInfo c)
    {
        var m = Square(c, 0);
        var values = m.IsSymmetric(1e-14)
            ? LinearAlgebra.SymmetricEigen(m).Values.Select(v => new NComplex(v, 0)).ToArray()
            : LinearAlgebra.Eigenvalues(m);
        var sorted = values.OrderBy(z => Math.Abs(z.Im) > 1e-12 ? 1 : 0).ThenBy(z => z.Re).ThenBy(z => z.Im);
        return new ListExpr([.. sorted.Select(z => (Expr)ComplexNumber(z))]);
    }

    private static Expr Eigenvectors(CallInfo c)
    {
        var m = Square(c, 0);
        var e = LinearAlgebra.Eigen(m, c.Context.Cancellation);
        int n = m.Rows;
        var rows = new Expr[n][];
        for (int i = 0; i < n; i++)
        {
            rows[i] = new Expr[e.Values.Length];
            for (int j = 0; j < e.Values.Length; j++) rows[i][j] = ComplexNumber(e.Vectors[i, j]);
        }
        return MatrixExpr.FromRows([.. rows.Select(r => (IReadOnlyList<Expr>)r)]);
    }

    private static Expr CharPoly(CallInfo c)
    {
        var m = Square(c, 0);
        string x = c.Count == 2 ? (c[1] as SymbolExpr)?.Name ?? throw c.Error("argument 2 must be a variable name") : "x";
        // Berkowitz gives the coefficients (highest power first or ascending); evaluate at a test point to tell which.
        var coeffs = LinearAlgebra.CharacteristicPolynomial(m);
        var asc = OrientAscending(coeffs, m);
        Expr sum = N(0);
        var terms = new List<Expr>();
        for (int k = 0; k < asc.Length; k++)
        {
            double a = Clean(asc[k]);
            if (a == 0) continue;
            Expr power = k == 0 ? N(1) : k == 1 ? new SymbolExpr(x) : new PowExpr(new SymbolExpr(x), Expr.Int(k));
            terms.Add(Evaluator.Eval(new MulExpr([N(a), power]), c.Context.WithFree(x)));
        }
        return terms.Count == 0 ? N(0) : Evaluator.Eval(new AddExpr([.. terms]), c.Context.WithFree(x));
    }

    /// <summary>Returns the characteristic polynomial coefficients in ascending order, monic in the top power.</summary>
    private static double[] OrientAscending(double[] coeffs, NMatrix m)
    {
        // det(t I - A) at t = 0 is (-1)^n det(A); the leading coefficient is 1.
        if (coeffs.Length > 0 && Math.Abs(coeffs[^1] - 1) < 1e-12) return coeffs;
        return [.. coeffs.Reverse()];
    }

    private static Expr SingularValues(CallInfo c)
    {
        var svd = new SvdDecomposition(ToMatrix(c, 0));
        return new ListExpr([.. svd.S.Select(v => (Expr)N(Clean(v)))]);
    }

    private static Expr Lu(CallInfo c)
    {
        var lu = new LuDecomposition(Square(c, 0));
        return new ListExpr([FromMatrix(lu.L), FromMatrix(lu.U), FromMatrix(lu.P)]);
    }

    private static Expr Qr(CallInfo c)
    {
        var qr = new QrDecomposition(ToMatrix(c, 0));
        return new ListExpr([FromMatrix(qr.Q), FromMatrix(qr.R)]);
    }

    private static Expr Fft(CallInfo c, bool inverse)
    {
        if (c[0] is not ListExpr l || l.Items.Length == 0) throw c.Error("give a list of numbers");
        var data = l.Items.Select(v =>
        {
            var n = Evaluator.Numeric(v, c.Context) ?? throw c.Error("every item must be a number");
            return new NComplex(n.Re, n.Im);
        }).ToArray();
        var result = inverse ? Fourier.Inverse(data) : Fourier.Forward(data);
        return new ListExpr([.. result.Select(z => (Expr)ComplexNumber(z))]);
    }

    // ------------------------------------------------------------------ helpers

    private static FunctionDefinition Def(string name, Func<CallInfo, Expr?> eval, string category, string signature, string description,
        string example = "", int min = 1, int max = 1, bool listable = true, bool hold = false, string[]? aliases = null) => new()
    {
        Name = name,
        Evaluate = c => Guard(c, eval),
        Category = category,
        Signature = signature,
        Description = description,
        Example = example,
        MinArgs = min,
        MaxArgs = max,
        Listable = listable,
        HoldArgs = hold,
        Aliases = aliases ?? [],
    };

    /// <summary>Turns numeric-library exceptions into calculator errors with plain messages.</summary>
    private static Expr? Guard(CallInfo c, Func<CallInfo, Expr?> eval)
    {
        try
        {
            return eval(c);
        }
        catch (ConvergenceException ex)
        {
            throw new CalcException($"{c.Function.Name}: {ex.Message}");
        }
        catch (ArgumentException ex)
        {
            throw new CalcException($"{c.Function.Name}: {ex.Message}");
        }
        catch (InvalidOperationException ex)
        {
            throw new CalcException($"{c.Function.Name}: {ex.Message}");
        }
    }

    private static NumberExpr N(double x) => new(Number.Approx(x));

    private static Expr ComplexNumber(NComplex z)
    {
        double re = Clean(z.Re), im = Clean(z.Im);
        if (Math.Abs(im) <= 1e-13 * Math.Max(1, Math.Abs(re))) im = 0;
        if (Math.Abs(re) <= 1e-13 * Math.Max(1, Math.Abs(im))) re = 0;
        return new NumberExpr(Number.Approx(re, im));
    }

    /// <summary>Rounds away last-bit noise: 2.0000000000000004 becomes 2, 1e-17 next to 1 becomes 0.</summary>
    private static double Clean(double v)
    {
        if (!double.IsFinite(v)) return v;
        double r = Math.Round(v);
        if (Math.Abs(v - r) <= 4e-15 * Math.Max(1, Math.Abs(v))) return r == 0 ? 0 : r;
        if (Math.Abs(v) < 1e-15) return 0;
        return v;
    }

    private static string Format(double x) => x.ToString("G10", System.Globalization.CultureInfo.InvariantCulture);

    private static string Symbol(CallInfo c, int i) =>
        c[i] is SymbolExpr s ? s.Name : throw c.Error($"argument {i + 1} must be a variable name, not {c[i]}");

    /// <summary>left - right for an equation, the expression itself otherwise (solve for zero).</summary>
    private static Expr Residual(Expr e) => e is RelationExpr { Op: RelOp.Equal } r ? new AddExpr([r.Left, new MulExpr([Expr.Int(-1), r.Right])]) : e;

    private static double Real(CallInfo c, Expr e)
    {
        if (e is NumberExpr n && n.Value.IsReal) return n.Value.Re;
        if (e is ConstantExpr k) return k.ApproxValue;
        var v = Evaluator.Numeric(e, c.Context);
        if (v is { IsReal: true } num) return num.Re;
        throw c.Error($"{e} must be a real number");
    }

    private static double Bound(CallInfo c, int i)
    {
        var v = Evaluator.Eval(c[i], c.Context.WithApproximate());
        return v switch
        {
            ConstantExpr { Constant: ConstantKind.Infinity } => double.PositiveInfinity,
            ConstantExpr { Constant: ConstantKind.NegativeInfinity } => double.NegativeInfinity,
            _ => Real(c, v),
        };
    }

    private static NMatrix ToMatrix(CallInfo c, int i)
    {
        var v = c[i] is MatrixExpr ? c[i] : Evaluator.Eval(c[i], c.Context);
        if (v is not MatrixExpr mx) throw c.Error($"argument {i + 1} must be a matrix like [[1, 2], [3, 4]]");
        var m = new NMatrix(mx.Rows, mx.Cols);
        for (int r = 0; r < mx.Rows; r++)
            for (int k = 0; k < mx.Cols; k++)
            {
                var n = Evaluator.Numeric(mx[r, k], c.Context);
                if (n is not { IsReal: true } num) throw c.Error("every entry must be a real number");
                m[r, k] = num.Re;
            }
        return m;
    }

    private static NMatrix Square(CallInfo c, int i)
    {
        var m = ToMatrix(c, i);
        if (!m.IsSquare) throw c.Error($"the matrix must be square, not {m.Rows}×{m.Columns}");
        return m;
    }

    private static Expr FromMatrix(NMatrix m)
    {
        var rows = new IReadOnlyList<Expr>[m.Rows];
        for (int r = 0; r < m.Rows; r++)
        {
            var row = new Expr[m.Columns];
            for (int k = 0; k < m.Columns; k++) row[k] = N(Clean(m[r, k]));
            rows[r] = row;
        }
        return MatrixExpr.FromRows(rows);
    }
}
