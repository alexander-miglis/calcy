using System.Collections.Immutable;
using System.Text.RegularExpressions;
using Calcy.Core;
using Calcy.Core.Evaluation;
using Calcy.Core.Expressions;
using Calcy.Core.Parsing;

namespace Calcy.Graphing;

/// <summary>
/// Turns a line of graph input into a classified, compiled <see cref="GraphItem"/> using the session's parser and
/// evaluator: functions, x = g(y), polar, parametric, implicit relations, inequalities, points, slope fields,
/// families (expressions containing lists), domain restrictions and free parameters for sliders.
/// </summary>
public static partial class GraphParser
{
    /// <summary>Names that are plot variables rather than parameters.</summary>
    public static readonly string[] PlotVariableNames = ["x", "y", "r", "θ", "t"];

    /// <summary>The most curves one family may produce.</summary>
    public const int MaxFamilySize = 64;

    [GeneratedRegex(@"^\s*(?:y\s*['′]|dy\s*/\s*dx)\s*=\s*(?<rhs>.+?)\s*$")]
    private static partial Regex SlopeFieldPattern();

    /// <summary>
    /// Classifies and compiles <paramref name="text"/>. A one-variable definition such as f(x) = x² is plotted and,
    /// when <paramref name="defineFunctions"/> is true, also defined in the session.
    /// </summary>
    public static GraphItem Parse(string? text, CalcSession session, bool defineFunctions = true)
    {
        text ??= "";
        var trimmed = text.Trim();
        if (trimmed.Length == 0) return new GraphItem(text, GraphKind.Empty);
        int offset = text.IndexOf(trimmed[0]);
        var parseCtx = session.ContextWith(PlotVariableNames);
        var eval = session.Context();
        try
        {
            var slope = SlopeFieldPattern().Match(text);
            if (slope.Success)
            {
                var rhsGroup = slope.Groups["rhs"];
                Expr rhs;
                try
                {
                    rhs = Parser.ParseExpression(rhsGroup.Value, parseCtx);
                }
                catch (CalcException ex)
                {
                    return Failure(text, ex.Message, ex.Position < 0 ? -1 : ex.Position + rhsGroup.Index, ex.Length);
                }
                return new Builder(text, eval).SlopeField(rhs);
            }

            Statement? stmt = null;
            try
            {
                stmt = Parser.ParseStatement(trimmed, parseCtx);
            }
            catch (CalcException)
            {
                // Fall through: the expression parser gives the error message users should see.
            }
            if (stmt is FunctionDefStatement fd && !session.Functions.Contains(fd.Name) && fd.Name is not ("x" or "t" or "θ"))
                return new Builder(text, eval).Definition(fd, session, defineFunctions);

            Expr expr;
            try
            {
                expr = Parser.ParseExpression(trimmed, parseCtx);
            }
            catch (CalcException ex)
            {
                return Failure(text, ex.Message, ex.Position < 0 ? -1 : ex.Position + offset, ex.Length);
            }
            return new Builder(text, eval).Classify(expr);
        }
        catch (CalcException ex)
        {
            return Failure(text, ex.Message, -1, 0);
        }
        catch (Exception ex) when (ex is ArithmeticException or InsufficientExecutionStackException or OperationCanceledException)
        {
            return Failure(text, ex is InsufficientExecutionStackException ? "The expression is nested too deeply" : "This expression cannot be graphed", -1, 0);
        }
    }

    internal static GraphItem Failure(string text, string message, int position = -1, int length = 0) =>
        new(text, GraphKind.Error) { Error = message, ErrorPosition = position, ErrorLength = length, KindLabel = "error" };

    private static bool IsPlotVariable(string name) => Array.IndexOf(PlotVariableNames, name) >= 0;

    private static bool IsSymbol(Expr e, string name) => e is SymbolExpr s && s.Name == name;

    private sealed class Builder(string text, EvalContext eval)
    {
        private EvalContext Prep => eval.WithApproximate(false).WithFree(PlotVariableNames);

        private Expr Prepare(Expr e) => Evaluator.Eval(e, Prep);

        private Expr? _raw;

        /// <summary>Free names of a prepared expression, in the order they appear in the typed text.</summary>
        private List<string> FreeNames(Expr e, IEnumerable<string> exclude)
        {
            var ex = new HashSet<string>(exclude);
            var order = _raw?.FreeSymbols() ?? [];
            return [.. e.FreeSymbols().Where(n => !ex.Contains(n)).OrderBy(n =>
            {
                int i = IndexOf(order, n);
                return i < 0 ? int.MaxValue : i;
            })];
        }

        private static int IndexOf(IReadOnlyList<string> list, string name)
        {
            for (int i = 0; i < list.Count; i++)
                if (list[i] == name) return i;
            return -1;
        }

        // ------------------------------------------------------------------ classification

        public GraphItem Classify(Expr expr)
        {
            _raw ??= expr;
            switch (expr)
            {
                case RelationExpr { Op: RelOp.Equal } eq:
                    return Equation(eq.Left, eq.Right);
                case RelationExpr { Op: RelOp.NotEqual }:
                    return Failure(text, "≠ cannot be shaded: use <, ≤, > or ≥");
                case RelationExpr ineq:
                    return Inequality(ineq);
                case LogicExpr { Op: LogicOp.And } conj when conj.Operands.All(o => o is RelationExpr { Op: not (RelOp.Equal or RelOp.NotEqual) }):
                    return Region([.. conj.Operands.Cast<RelationExpr>()]);
                case LogicExpr:
                    return Failure(text, "Only inequalities joined by 'and' can be shaded, for example 0 < y < x");
                case TupleExpr t:
                    return Tuple(t, null, null);
                case PiecewiseExpr { Cases.Length: 1, Otherwise: null } pw when pw.Cases[0].Value is TupleExpr tv:
                    return Tuple(tv, null, pw.Cases[0].Condition);
                case ListExpr l when l.Count > 0 && l.Items.All(i => i is TupleExpr):
                    return PointsFrom(Prepare(l), null);
                case BoolExpr:
                    return Failure(text, "This is a true or false statement, not something to graph");
                case StringExpr:
                    return Failure(text, "Text cannot be graphed");
                default:
                    if (expr.ContainsSymbol("y"))
                        return Failure(text, "Add '=' to make an equation, for example y = … or … = 0");
                    return Function(expr, "x", null, null);
            }
        }

        private GraphItem Equation(Expr l, Expr r)
        {
            if (IsSymbol(l, "y") && !r.ContainsSymbol("y")) return Function(r, "x", null, null);
            if (IsSymbol(r, "y") && !l.ContainsSymbol("y")) return Function(l, "x", null, null);
            if (IsSymbol(l, "x") && !r.ContainsSymbol("x")) return r.ContainsSymbol("y") ? FunctionOfY(r) : VerticalLine(r);
            if (IsSymbol(r, "x") && !l.ContainsSymbol("x")) return l.ContainsSymbol("y") ? FunctionOfY(l) : VerticalLine(l);
            if (IsSymbol(l, "r") && !r.ContainsSymbol("r")) return Polar(r);
            if (IsSymbol(r, "r") && !l.ContainsSymbol("r")) return Polar(l);
            bool hasXY = l.ContainsSymbol("x") || l.ContainsSymbol("y") || r.ContainsSymbol("x") || r.ContainsSymbol("y");
            if (l is SymbolExpr name && !IsPlotVariable(name.Name) && !hasXY)
            {
                if (r is TupleExpr rt) return Tuple(rt, name.Name, null);
                if (r is PiecewiseExpr { Cases.Length: 1, Otherwise: null } rp && rp.Cases[0].Value is TupleExpr rpt) return Tuple(rpt, name.Name, rp.Cases[0].Condition);
                if (r is ListExpr rl && rl.Count > 0 && rl.Items.All(i => i is TupleExpr)) return PointsFrom(Prepare(rl), name.Name);
                return Variable(name.Name, r);
            }
            if (!hasXY) return Failure(text, "This equation has no x or y to graph");
            return Implicit(Algebra.Sub(l, r));
        }

        private GraphItem Inequality(RelationExpr rel)
        {
            var (l, r, op) = (rel.Left, rel.Right, rel.Op);
            if (IsSymbol(l, "y") && !r.ContainsSymbol("y")) return Shaded(GraphKind.InequalityY, r, "x", op);
            if (IsSymbol(r, "y") && !l.ContainsSymbol("y")) return Shaded(GraphKind.InequalityY, l, "x", RelationExpr.Flip(op));
            if (IsSymbol(l, "x") && !r.ContainsSymbol("x")) return Shaded(GraphKind.InequalityX, r, "y", op);
            if (IsSymbol(r, "x") && !l.ContainsSymbol("x")) return Shaded(GraphKind.InequalityX, l, "y", RelationExpr.Flip(op));
            return Region([rel]);
        }

        // ------------------------------------------------------------------ builders

        private GraphItem Function(Expr e, string variable, string? label, UserFunction? defined)
        {
            var prepared = Prepare(e);
            var free = FreeNames(prepared, [variable]);
            if (free.Contains("y") && variable == "x")
                return Failure(text, "y appears on both sides: write it as an equation in x and y, for example x² + y² = 9");
            var members = Expand(prepared, free.Append(variable), out var error);
            if (error is not null) return Failure(text, error);
            var compiled = members.Select((m, i) => new GraphMember(i, eval) { Main = Compile(m, [variable], free) }).ToList();
            return new GraphItem(text, GraphKind.Function)
            {
                KindLabel = FamilyLabel("function", compiled.Count),
                Parameters = free,
                Members = compiled,
                Variable = variable,
                Label = label,
                DefinedFunction = defined,
            };
        }

        private GraphItem FunctionOfY(Expr e)
        {
            var prepared = Prepare(e);
            var free = FreeNames(prepared, ["y"]);
            var members = Expand(prepared, free.Append("y"), out var error);
            if (error is not null) return Failure(text, error);
            return new GraphItem(text, GraphKind.FunctionOfY)
            {
                KindLabel = FamilyLabel("function of y", members.Count),
                Parameters = free,
                Members = [.. members.Select((m, i) => new GraphMember(i, eval) { Main = Compile(m, ["y"], free) })],
                Variable = "y",
            };
        }

        private GraphItem VerticalLine(Expr e)
        {
            var prepared = Prepare(e);
            var free = FreeNames(prepared, []);
            var members = Expand(prepared, free, out var error);
            if (error is not null) return Failure(text, error);
            return new GraphItem(text, GraphKind.VerticalLine)
            {
                KindLabel = FamilyLabel("line", members.Count),
                Parameters = free,
                Members = [.. members.Select((m, i) => new GraphMember(i, eval) { Main = Compile(m, [], free) })],
            };
        }

        private GraphItem Polar(Expr e)
        {
            var prepared = Prepare(e);
            var free = FreeNames(prepared, ["θ"]);
            if (free.Contains("x") || free.Contains("y")) return Failure(text, "Polar curves use θ: write r = f(θ)");
            var members = Expand(prepared, free.Append("θ"), out var error);
            if (error is not null) return Failure(text, error);
            var (lo, hi, fromText) = Range(e, "θ", eval.Settings.Angle == AngleMode.Degree ? 360 : eval.Settings.Angle == AngleMode.Gradian ? 400 : 2 * Math.PI);
            return new GraphItem(text, GraphKind.Polar)
            {
                KindLabel = FamilyLabel("polar", members.Count),
                Parameters = free,
                Members = [.. members.Select((m, i) => new GraphMember(i, eval) { Main = Compile(m, ["θ"], free) })],
                Variable = "θ",
                AngleToRadians = eval.Settings.ToRadians,
                TMin = lo,
                TMax = hi,
                RangeFromText = fromText,
            };
        }

        private GraphItem Implicit(Expr f)
        {
            var prepared = Prepare(f);
            var free = FreeNames(prepared, ["x", "y"]);
            if (prepared.IsConstantValue())
                return Failure(text, IsZeroConstant(prepared) ? "This equation holds for every x and y" : "This equation has no solutions");
            var members = Expand(prepared, free.Concat(["x", "y"]), out var error);
            if (error is not null) return Failure(text, error);
            return new GraphItem(text, GraphKind.Implicit)
            {
                KindLabel = FamilyLabel("implicit", members.Count),
                Parameters = free,
                Members = [.. members.Select((m, i) => new GraphMember(i, eval) { Main = Compile(m, ["x", "y"], free) })],
            };
        }

        private GraphItem Shaded(GraphKind kind, Expr boundary, string variable, RelOp op)
        {
            var prepared = Prepare(boundary);
            var free = FreeNames(prepared, [variable]);
            if (kind == GraphKind.InequalityY && free.Contains("y")) return Region([new RelationExpr(op, new SymbolExpr("y"), boundary)]);
            var members = Expand(prepared, free.Append(variable), out var error);
            if (error is not null) return Failure(text, error);
            return new GraphItem(text, kind)
            {
                KindLabel = FamilyLabel("inequality", members.Count),
                Parameters = free,
                Members = [.. members.Select((m, i) => new GraphMember(i, eval) { Main = Compile(m, [variable], free) })],
                Variable = variable,
                Relation = op,
            };
        }

        private GraphItem Region(IReadOnlyList<RelationExpr> relations)
        {
            var parts = relations.Select(r => Prepare(Algebra.Sub(r.Left, r.Right))).ToArray();
            var joint = new TupleExpr([.. parts]);
            var free = FreeNames(joint, ["x", "y"]);
            if (!joint.ContainsSymbol("x") && !joint.ContainsSymbol("y")) return Failure(text, "This inequality has no x or y to shade");
            var members = Expand(joint, free.Concat(["x", "y"]), out var error);
            if (error is not null) return Failure(text, error);
            var list = new List<GraphMember>();
            for (int i = 0; i < members.Count; i++)
            {
                var items = members[i] is TupleExpr t ? t.Items : [members[i]];
                var conds = items.Select((f, k) => new RegionCondition(Compile(f, ["x", "y"], free), relations[Math.Min(k, relations.Count - 1)].Op)).ToList();
                list.Add(new GraphMember(i, eval) { Conditions = conds });
            }
            return new GraphItem(text, GraphKind.Region)
            {
                KindLabel = FamilyLabel("inequality", list.Count),
                Parameters = free,
                Members = list,
                Relation = relations[0].Op,
            };
        }

        private GraphItem Tuple(TupleExpr t, string? label, Expr? restriction)
        {
            if (t.Items.Length != 2) return Failure(text, t.Items.Length == 3 ? "3D points belong in 3D graphing; use (x, y) here" : "A point needs two coordinates, like (2, 3)");
            bool usesT = t.ContainsSymbol("t");
            if (t.ContainsSymbol("x") || t.ContainsSymbol("y"))
                return Failure(text, "A point cannot use x or y; use t for a parametric curve, like (cos(t), sin(t))");
            if (!usesT) return PointsFrom(Prepare(t), label);
            var prepared = Prepare(t);
            var free = FreeNames(prepared, ["t"]);
            var members = Expand(prepared, free.Append("t"), out var error);
            if (error is not null) return Failure(text, error);
            var list = new List<GraphMember>();
            for (int i = 0; i < members.Count; i++)
            {
                if (members[i] is not TupleExpr mt || mt.Items.Length != 2) return Failure(text, "A parametric curve needs (x(t), y(t))");
                var x = mt.Items[0];
                var y = mt.Items[1];
                if (restriction is not null)
                {
                    x = new PiecewiseExpr([(restriction, x)], null);
                    y = new PiecewiseExpr([(restriction, y)], null);
                }
                list.Add(new GraphMember(i, eval) { X = Compile(Prepare(x), ["t"], free), Y = Compile(Prepare(y), ["t"], free) });
            }
            var (lo, hi, fromText) = restriction is null ? (0, DefaultParametricEnd(), false) : RangeFrom(restriction, "t", DefaultParametricEnd());
            return new GraphItem(text, GraphKind.Parametric)
            {
                KindLabel = FamilyLabel("parametric", list.Count),
                Parameters = free,
                Members = list,
                Variable = "t",
                Label = label,
                TMin = lo,
                TMax = hi,
                RangeFromText = fromText,
            };
        }

        private double DefaultParametricEnd() => eval.Settings.Angle switch
        {
            AngleMode.Degree => 360,
            AngleMode.Gradian => 400,
            _ => 2 * Math.PI,
        };

        private GraphItem PointsFrom(Expr prepared, string? label)
        {
            var coords = new List<(Expr X, Expr Y)>();
            switch (prepared)
            {
                case TupleExpr { Items.Length: 2 } t when t.Items[0] is ListExpr || t.Items[1] is ListExpr:
                {
                    var xs = t.Items[0] as ListExpr;
                    var ys = t.Items[1] as ListExpr;
                    int n = Math.Max(xs?.Count ?? 1, ys?.Count ?? 1);
                    if (xs is not null && ys is not null && xs.Count != ys.Count)
                        return Failure(text, $"The two lists have different lengths ({xs.Count} and {ys.Count})");
                    for (int i = 0; i < n; i++) coords.Add((xs is null ? t.Items[0] : xs.Items[i], ys is null ? t.Items[1] : ys.Items[i]));
                    break;
                }
                case TupleExpr { Items.Length: 2 } t:
                    coords.Add((t.Items[0], t.Items[1]));
                    break;
                case ListExpr l when l.Items.All(i => i is TupleExpr { Items.Length: 2 }):
                    foreach (var item in l.Items.Cast<TupleExpr>()) coords.Add((item.Items[0], item.Items[1]));
                    break;
                default:
                    return Failure(text, "Points are written (2, 3) or {(1, 2), (3, 4)}");
            }
            if (coords.Count > 100_000) return Failure(text, "Too many points (at most 100000)");
            var free = new List<string>();
            foreach (var (x, y) in coords)
                foreach (var n in x.FreeSymbols().Concat(y.FreeSymbols()))
                    if (!free.Contains(n)) free.Add(n);
            if (free.Contains("x") || free.Contains("y")) return Failure(text, "A point cannot use x or y");
            var compiled = coords.Select(c => (Compile(c.X, [], free), Compile(c.Y, [], free))).ToList();
            bool single = coords.Count == 1;
            return new GraphItem(text, single ? GraphKind.Point : GraphKind.Points)
            {
                KindLabel = single ? "point" : $"{coords.Count} points",
                Parameters = free,
                Members = [new GraphMember(0, eval) { PointCoordinates = compiled }],
                Label = label,
            };
        }

        private GraphItem Variable(string name, Expr value)
        {
            var prepared = Prepare(value);
            if (prepared is not NumberExpr && prepared.IsConstantValue())
                prepared = Evaluator.Eval(prepared, eval.WithApproximate());
            if (prepared is NumberExpr { Value.IsReal: true } n)
                return new GraphItem(text, GraphKind.Variable) { KindLabel = "slider", Label = name, VariableValue = n.Value.Re };
            return Failure(text, $"A slider needs a number, for example {name} = 2");
        }

        public GraphItem Definition(FunctionDefStatement fd, CalcSession session, bool define)
        {
            _raw = fd.Body;
            var uf = new UserFunction(fd.Name, fd.Parameters, fd.Body);
            if (fd.Name is "y" or "r" && fd.Parameters.Length == 1)
            {
                var v = fd.Parameters[0];
                var body = v == (fd.Name == "y" ? "x" : "θ") ? fd.Body : Rename(fd.Body, v, fd.Name == "y" ? "x" : "θ");
                return fd.Name == "y" ? Function(body, "x", null, null) : Polar(body);
            }
            if (define)
            {
                bool same = session.Env.TryGetFunction(fd.Name, out var existing) && SameFunction(existing, uf);
                if (!same) session.Env.SetFunction(uf);
            }
            if (fd.Parameters.Length != 1)
                return new GraphItem(text, GraphKind.Definition) { KindLabel = "definition", Label = fd.Name, DefinedFunction = uf };
            var item = Function(fd.Body, fd.Parameters[0], fd.Name, uf);
            return item;
        }

        public GraphItem SlopeField(Expr rhs)
        {
            _raw = rhs;
            var prepared = Prepare(rhs);
            var free = FreeNames(prepared, ["x", "y"]);
            return new GraphItem(text, GraphKind.SlopeField)
            {
                KindLabel = "slope field",
                Parameters = free,
                Members = [new GraphMember(0, eval) { Main = Compile(prepared, ["x", "y"], free) }],
            };
        }

        // ------------------------------------------------------------------ helpers

        private CompiledExpr Compile(Expr prepared, IReadOnlyList<string> plotVariables, IReadOnlyList<string> parameters) =>
            new(prepared, plotVariables, parameters, eval);

        private static string FamilyLabel(string label, int count) => count > 1 ? $"{label} ×{count}" : label;

        private static bool IsZeroConstant(Expr e) => e is NumberExpr { Value.IsZero: true };

        private static Expr Rename(Expr e, string from, string to) =>
            e.Map(x => x is SymbolExpr s && s.Name == from ? new SymbolExpr(to) : x);

        private static bool SameFunction(UserFunction a, UserFunction b) =>
            a.Name == b.Name && a.Parameters.SequenceEqual(b.Parameters) && a.Body.Equals(b.Body);

        private (double Lo, double Hi, bool FromText) Range(Expr e, string variable, double defaultEnd) =>
            e is PiecewiseExpr { Cases.Length: 1, Otherwise: null } pw ? RangeFrom(pw.Cases[0].Condition, variable, defaultEnd) : (0, defaultEnd, false);

        /// <summary>Reads a range such as 0 &lt; t &lt; 3 from a restriction.</summary>
        private (double Lo, double Hi, bool FromText) RangeFrom(Expr condition, string variable, double defaultEnd)
        {
            double? lo = null, hi = null;
            IEnumerable<Expr> parts = condition is LogicExpr { Op: LogicOp.And } conj ? conj.Operands : [condition];
            foreach (var part in parts)
            {
                if (part is not RelationExpr rel) continue;
                bool left = IsSymbol(rel.Left, variable), right = IsSymbol(rel.Right, variable);
                var other = left ? rel.Right : right ? rel.Left : null;
                if (other is null) continue;
                double? value = Constant(other);
                if (value is null) continue;
                var op = left ? rel.Op : RelationExpr.Flip(rel.Op);
                if (op is RelOp.Less or RelOp.LessEqual) hi = value;
                else if (op is RelOp.Greater or RelOp.GreaterEqual) lo = value;
            }
            if (lo is null && hi is null) return (0, defaultEnd, false);
            double a = lo ?? 0, b = hi ?? (a + defaultEnd);
            return b > a ? (a, b, true) : (0, defaultEnd, false);
        }

        private double? Constant(Expr e)
        {
            try
            {
                var v = Evaluator.Eval(e, eval.WithApproximate());
                return v is NumberExpr { Value.IsReal: true } n && double.IsFinite(n.Value.Re) ? n.Value.Re : null;
            }
            catch (CalcException)
            {
                return null;
            }
        }

        // ------------------------------------------------------------------ families

        private IReadOnlyList<Expr> Expand(Expr prepared, IEnumerable<string> free, out string? error)
        {
            error = null;
            int n = ListLength(prepared, ref error);
            if (error is not null) return [];
            if (n <= 0) return [prepared];
            if (n > MaxFamilySize)
            {
                error = $"Too many curves in one family ({n}); the limit is {MaxFamilySize}";
                return [];
            }
            var ctx = eval.WithApproximate(false).WithFree(PlotVariableNames).WithFree(free);
            var result = new List<Expr>(n);
            for (int i = 0; i < n; i++)
            {
                var member = Pick(prepared, i);
                try
                {
                    member = Evaluator.Eval(member, ctx);
                }
                catch (CalcException)
                {
                    // Keep the unsimplified member; the compiler handles it.
                }
                result.Add(member);
            }
            return result;
        }

        private bool Consumes(CallExpr c) => eval.Functions.TryGet(c.Name, out var def) && !def.Listable;

        private int ListLength(Expr e, ref string? error)
        {
            if (e is ListExpr l) return l.Count;
            if (e is CallExpr c && Consumes(c)) return 0;
            int n = 0;
            foreach (var child in e.Children)
            {
                int k = ListLength(child, ref error);
                if (error is not null) return 0;
                if (k <= 0) continue;
                if (n == 0 || n == 1) n = Math.Max(n, k);
                else if (k != 1 && k != n)
                {
                    error = $"The lists in this expression have different lengths ({n} and {k})";
                    return 0;
                }
            }
            return n;
        }

        private Expr Pick(Expr e, int i)
        {
            if (e is ListExpr l) return l.Count == 1 ? l.Items[0] : l.Items[Math.Min(i, l.Count - 1)];
            if (e is CallExpr c && Consumes(c)) return e;
            var children = e.Children;
            if (children.Count == 0) return e;
            var picked = new Expr[children.Count];
            bool changed = false;
            for (int k = 0; k < children.Count; k++)
            {
                picked[k] = Pick(children[k], i);
                changed |= !ReferenceEquals(picked[k], children[k]);
            }
            return changed ? e.WithChildren(picked) : e;
        }
    }
}
