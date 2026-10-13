using System.Globalization;
using System.Text;
using System.Text.Json.Nodes;
using Calcy.Core;
using Calcy.Core.Evaluation;
using Calcy.Core.Expressions;
using Calcy.Core.Parsing;
using Calcy.Core.Printing;
using static Calcy.Mcp.Schema;

namespace Calcy.Mcp;

/// <summary>
/// The maths tools of the MCP server, all working on one shared <see cref="CalcSession"/> so variables and functions
/// defined by one call can be used by the next. Tools that need a module (solve needs the algebra system, for example)
/// are only offered when that module is part of the build.
/// </summary>
public sealed partial class CalcTools(CalcSession session)
{
    private static readonly PrintOptions Display = new() { Unicode = true, Numbers = new NumberFormatOptions { Digits = 15, Unicode = true } };

    public CalcSession Session { get; } = session;

    /// <summary>Every tool, in the order clients list them.</summary>
    public IReadOnlyList<McpTool> All() =>
    [
        Evaluate(), Solve(), Simplify(), Expand(), Factor(), Differentiate(), Integrate(), Limit(), Series(),
        MatrixTool(), LinearSolve(), DescribeData(), Define(), GetVariables(), ClearVariables(), Settings(),
        ListFunctions(), FunctionHelp(),
        Distribution(), Regress(), HypothesisTest(), ConfidenceInterval(), FinanceTvm(), CashFlowTool(),
        SearchFormulas(), GetFormula(), SolveFormula(), UnitConvert(), DateCalc(), Plot2D(), RunCode(),
    ];

    // ---------------------------------------------------------------- tools

    private McpTool Evaluate() => new()
    {
        Name = "evaluate",
        Title = "Evaluate",
        Description =
            "Calculates an expression exactly (fractions, roots, multiples of pi stay exact) and also gives the decimal value. " +
            "Use it for any arithmetic instead of computing in your head. Syntax: 2^10, sqrt(8), 3!, nCr(10, 3), sin(pi/6), " +
            "log(100) is base 10, ln(e), 1/2x means 1/(2x), {1, 2, 3} is a list, [[1, 2], [3, 4]] a matrix, i the imaginary unit. " +
            "Use variables to substitute values for this call only.",
        InputSchema = Object(
            Req("expression", Expression("The expression, for example \"1/3 + 1/6\" or \"sqrt(12)\" or \"sum(k^2, k, 1, 100)\".")),
            Opt("approximate", Boolean("Give only the decimal value.", false)),
            Opt("variables", new JsonObject
            {
                ["type"] = "object",
                ["description"] = "Values for names in the expression, for this call only, for example {\"x\": 2, \"r\": \"sqrt(2)\"}.",
                ["additionalProperties"] = new JsonObject { ["type"] = new JsonArray("string", "number") },
            })),
        OutputSchema = MathResult(),
        Handler = c => Guard(() =>
        {
            var text = c.String("expression");
            Statement stmt;
            try
            {
                stmt = Session.Parse(text);
            }
            catch (CalcException ex)
            {
                return ParseError("expression", text, ex);
            }
            if (stmt is AssignStatement or FunctionDefStatement)
                return ToolResult.Error("This is a definition. Use the define tool to store it, or use == to compare.");
            var expr = ((ExprStatement)stmt).Expression;
            bool approx = c.Bool("approximate", false);
            var ctx = Session.Context(approx, c.Token).WithLocals(Locals(c));
            var value = Evaluator.Eval(expr, ctx);
            return Result(value, c, approx);
        }),
    };

    private McpTool Solve() => new()
    {
        Name = "solve",
        Title = "Solve equations",
        Description =
            "Solves an equation, an inequality or a system exactly, falling back to numeric roots when there is no closed form. " +
            "Write equations with =, for example \"x^2 - 5x + 6 = 0\"; an expression without = is solved for zero. " +
            "For a system pass several equations and the variables.",
        InputSchema = Object(
            Opt("equation", Expression("One equation, for example \"2x + 3 = 11\" or \"sin(x) = 1/2\".")),
            Opt("equations", Array(String("An equation."), "Several equations for a system, for example [\"x + y = 3\", \"x - y = 1\"].")),
            Opt("variable", String("The unknown (default: x, or the only unknown).")),
            Opt("variables", Array(String("An unknown."), "The unknowns of a system.")),
            Opt("numeric", Boolean("Only look for decimal solutions.", false)),
            Opt("guess", Expression("A starting value for the numeric method."))),
        OutputSchema = MathResult(),
        IsAvailable = () => Has("solve") || Has("nsolve"),
        Handler = c => Guard(() =>
        {
            var eqs = new List<Expr>();
            if (c.Has("equation")) eqs.Add(Parse(c, "equation"));
            if (c.Node("equations") is JsonArray arr)
                for (int k = 0; k < arr.Count; k++) eqs.Add(ParseText(ItemText(arr[k], "equations"), $"equations[{k}]"));
            if (eqs.Count == 0) throw new ToolArgumentException("Give the equation to solve.");
            var vars = new List<SymbolExpr>();
            if (c.Has("variable")) vars.Add(VariableArg(c, "variable"));
            if (c.Node("variables") is JsonArray va)
                for (int k = 0; k < va.Count; k++) vars.Add(VariableFrom(ItemText(va[k], "variables"), "variables"));
            if (vars.Count == 0)
            {
                if (eqs.Count == 1) vars.Add(GuessVariable(eqs[0]));
                else vars.AddRange(GuessVariables(eqs));
            }
            Expr target = eqs.Count == 1 ? eqs[0] : new ListExpr([.. eqs]);
            Expr unknowns = vars.Count == 1 ? vars[0] : new ListExpr([.. vars]);
            var ctx = Session.Context(false, c.Token).WithFree(vars.Select(v => v.Name));
            var notes = new List<string>();
            bool numeric = c.Bool("numeric", false) || !Has("solve");
            if (!numeric)
            {
                try
                {
                    var exact = Evaluator.Eval(CallExpr.Of("solve", target, unknowns), ctx);
                    if (!IsUnevaluated(exact, "solve")) return Result(exact, c, false, notes);
                    notes.Add("No exact solution was found, so these are numeric.");
                }
                catch (CalcException ex) when (Has("nsolve"))
                {
                    notes.Add("Exact solving failed (" + ex.Message + "), so these are numeric.");
                }
            }
            if (!Has("nsolve")) throw new CalcException("Calcy could not solve this exactly");
            var args = new List<Expr> { target, unknowns };
            if (c.Has("guess")) args.Add(Parse(c, "guess"));
            var approx = Evaluator.Eval(new CallExpr("nsolve", [.. args]), ctx.WithApproximate());
            return Result(approx, c, true, notes);
        }),
    };

    private McpTool Simplify() => Unary("simplify", "Simplify", "simplify",
        "Simplifies an expression: collects like terms, cancels common factors, simplifies roots and powers.",
        "The expression, for example \"(x^2 - 1)/(x - 1)\".");

    private McpTool Expand() => Unary("expand", "Expand", "expand",
        "Multiplies out products and powers: (x + 1)^3 becomes x^3 + 3x^2 + 3x + 1.",
        "The expression, for example \"(a + b)^4\".");

    private McpTool Factor() => Unary("factor", "Factor", "factor",
        "Factors a whole number into primes, or a polynomial into irreducible factors over the rationals.",
        "A whole number such as 360 or a polynomial such as \"x^4 - 1\".");

    private McpTool Differentiate() => new()
    {
        Name = "differentiate",
        Title = "Differentiate",
        Description = "Exact derivative of an expression, optionally of higher order and optionally evaluated at a point.",
        InputSchema = Object(
            Req("expression", Expression("The function, for example \"x^2 sin(x)\".")),
            Opt("variable", String("Differentiate with respect to this variable (default x).")),
            Opt("order", Integer("How many times to differentiate.", 1, 20, 1)),
            Opt("at", Expression("Evaluate the derivative at this value of the variable."))),
        OutputSchema = MathResult(),
        IsAvailable = () => Has("diff"),
        Handler = c => Guard(() =>
        {
            var f = Parse(c, "expression");
            var x = c.Has("variable") ? VariableArg(c, "variable") : GuessVariable(f);
            int order = c.Int("order", 1, 1, 20);
            var ctx = Session.Context(false, c.Token).WithFree(x.Name);
            Expr call = order == 1 ? CallExpr.Of("diff", f, x) : CallExpr.Of("diff", f, x, Expr.Int(order));
            var d = Evaluator.Eval(call, ctx);
            if (c.Has("at"))
            {
                var at = Evaluator.Eval(Parse(c, "at"), Session.Context(false, c.Token));
                var value = Evaluator.Eval(d, Session.Context(false, c.Token).WithLocal(x.Name, at));
                var data = Describe(value, c.Token);
                data["derivative"] = Describe(d, c.Token);
                return ToolResult.Data(data, Session.Print(value));
            }
            return Result(d, c, false);
        }),
    };

    private McpTool Integrate() => new()
    {
        Name = "integrate",
        Title = "Integrate",
        Description =
            "Antiderivative, or a definite integral when lower and upper are given. Definite integrals are exact when possible and " +
            "otherwise computed numerically to about 15 digits. Bounds may be inf or -inf.",
        InputSchema = Object(
            Req("expression", Expression("The integrand, for example \"x e^x\".")),
            Opt("variable", String("The integration variable (default x).")),
            Opt("lower", Expression("Lower bound for a definite integral.")),
            Opt("upper", Expression("Upper bound for a definite integral.")),
            Opt("numeric", Boolean("Skip the exact method and integrate numerically.", false))),
        OutputSchema = MathResult(),
        IsAvailable = () => Has("integrate") || Has("nint"),
        Handler = c => Guard(() =>
        {
            var f = Parse(c, "expression");
            var x = c.Has("variable") ? VariableArg(c, "variable") : GuessVariable(f);
            bool hasLower = c.Has("lower"), hasUpper = c.Has("upper");
            if (hasLower != hasUpper) throw new ToolArgumentException("Give both lower and upper, or neither.");
            var ctx = Session.Context(false, c.Token).WithFree(x.Name);
            var notes = new List<string>();
            if (!hasLower)
            {
                if (!Has("integrate")) throw new CalcException("Antiderivatives need the algebra module, which is not in this build");
                var anti = Evaluator.Eval(CallExpr.Of("integrate", f, x), ctx);
                if (IsUnevaluated(anti, "integrate")) throw new CalcException("Calcy found no antiderivative in closed form");
                notes.Add("Add a constant of integration C.");
                return Result(anti, c, false, notes);
            }
            var a = Parse(c, "lower");
            var b = Parse(c, "upper");
            if (!c.Bool("numeric", false) && Has("integrate"))
            {
                try
                {
                    var exact = Evaluator.Eval(CallExpr.Of("integrate", f, x, a, b), ctx);
                    if (!IsUnevaluated(exact, "integrate")) return Result(exact, c, false, notes);
                }
                catch (CalcException ex) when (Has("nint"))
                {
                    notes.Add("Exact integration failed (" + ex.Message + ").");
                }
            }
            if (!Has("nint")) throw new CalcException("Calcy could not integrate this exactly");
            notes.Add("Computed numerically.");
            var approx = Evaluator.Eval(CallExpr.Of("nint", f, x, a, b), ctx.WithApproximate());
            return Result(approx, c, true, notes);
        }),
    };

    private McpTool Limit() => new()
    {
        Name = "limit",
        Title = "Limit",
        Description = "The limit of an expression as the variable approaches a point (which may be inf or -inf), from both sides or one side.",
        InputSchema = Object(
            Req("expression", Expression("The expression, for example \"sin(x)/x\".")),
            Req("point", Expression("Where the variable goes, for example 0 or inf.")),
            Opt("variable", String("The variable (default x).")),
            Opt("direction", String("From which side.", "both", "left", "right"))),
        OutputSchema = MathResult(),
        IsAvailable = () => Has("limit"),
        Handler = c => Guard(() =>
        {
            var f = Parse(c, "expression");
            var x = c.Has("variable") ? VariableArg(c, "variable") : GuessVariable(f);
            var a = Parse(c, "point");
            var dir = c.Choice("direction", "both", "both", "left", "right");
            Expr call = dir == "both" ? CallExpr.Of("limit", f, x, a) : CallExpr.Of("limit", f, x, a, new StringExpr(dir == "left" ? "-" : "+"));
            var value = Evaluator.Eval(call, Session.Context(false, c.Token).WithFree(x.Name));
            return Result(value, c, false);
        }),
    };

    private McpTool Series() => new()
    {
        Name = "series",
        Title = "Taylor series",
        Description = "Taylor (or Maclaurin) polynomial of an expression around a point, up to the given order.",
        InputSchema = Object(
            Req("expression", Expression("The function, for example \"e^x\" or \"ln(1 + x)\".")),
            Opt("variable", String("The variable (default x).")),
            Opt("point", Expression("Expand around this point (default 0).")),
            Opt("order", Integer("Highest power to keep.", 0, 50, 5))),
        OutputSchema = MathResult(),
        IsAvailable = () => Has("taylor"),
        Handler = c => Guard(() =>
        {
            var f = Parse(c, "expression");
            var x = c.Has("variable") ? VariableArg(c, "variable") : GuessVariable(f);
            var a = c.Has("point") ? Parse(c, "point") : Expr.Int(0);
            int n = c.Int("order", 5, 0, 50);
            var value = Evaluator.Eval(CallExpr.Of("taylor", f, x, a, Expr.Int(n)), Session.Context(false, c.Token).WithFree(x.Name));
            return Result(value, c, false);
        }),
    };

    private static readonly string[] MatrixOperations =
        ["determinant", "inverse", "transpose", "rref", "ref", "rank", "trace", "multiply", "add", "subtract", "power", "eigenvalues", "eigenvectors", "characteristic_polynomial"];

    private McpTool MatrixTool() => new()
    {
        Name = "matrix",
        Title = "Matrix operation",
        Description =
            "Exact matrix algebra: determinant, inverse, transpose, reduced row echelon form, rank, trace, products, sums, powers " +
            "and eigenvalues. Entries may be fractions, roots or symbols. Give the matrix as rows, for example [[1, 2], [3, 4]].",
        InputSchema = Object(
            Req("operation", String("What to do.", MatrixOperations)),
            Req("matrix", Matrix("The matrix A.")),
            Opt("other", Matrix("The second matrix B for multiply, add and subtract.")),
            Opt("exponent", Integer("The power n for power (negative uses the inverse).", -50, 50))),
        OutputSchema = MathResult(),
        Handler = c => Guard(() =>
        {
            var op = c.Choice("operation", "", MatrixOperations);
            var a = MatrixArg(c, "matrix");
            Expr call = op switch
            {
                "determinant" => CallExpr.Of("det", a),
                "inverse" => CallExpr.Of("inv", a),
                "transpose" => CallExpr.Of("transpose", a),
                "rref" => CallExpr.Of("rref", a),
                "ref" => CallExpr.Of("ref", a),
                "rank" => CallExpr.Of("rank", a),
                "trace" => CallExpr.Of("trace", a),
                "multiply" => new MulExpr([a, MatrixArg(c, "other")]),
                "add" => new AddExpr([a, MatrixArg(c, "other")]),
                "subtract" => new AddExpr([a, new MulExpr([Expr.Int(-1), MatrixArg(c, "other")])]),
                "power" => new PowExpr(a, Expr.Int(c.Has("exponent") ? c.Int("exponent", 1, -50, 50) : throw new ToolArgumentException("Give the exponent."))),
                "eigenvalues" => Needs("eigenvals", a),
                "eigenvectors" => Needs("eigenvects", a),
                _ => Needs("charpoly", a),
            };
            return Result(Evaluator.Eval(call, Session.Context(false, c.Token)), c, false);
        }),
    };

    private McpTool LinearSolve() => new()
    {
        Name = "linear_solve",
        Title = "Solve a linear system",
        Description = "Solves A·x = b exactly with Gaussian elimination. Reports when there is no solution or infinitely many.",
        InputSchema = Object(
            Req("matrix", Matrix("The coefficient matrix A, as rows.")),
            Req("vector", Data("The right-hand side b, one value per row of A."))),
        OutputSchema = MathResult(),
        Handler = c => Guard(() =>
        {
            var a = MatrixArg(c, "matrix");
            var b = DataArg(c, "vector");
            return Result(Evaluator.Eval(CallExpr.Of("linsolve", a, b), Session.Context(false, c.Token)), c, false);
        }),
    };

    private McpTool DescribeData() => new()
    {
        Name = "describe_data",
        Title = "Describe data",
        Description = "Summary statistics of a list of numbers: count, sum, mean, median, minimum, maximum, sample and population variance and standard deviation. Exact for exact input.",
        InputSchema = Object(Req("data", Data("The numbers, for example [2, 4, 4, 5, 7] or \"{1, 2, 3}\"."))),
        Handler = c => Guard(() =>
        {
            var data = DataArg(c, "data");
            var ctx = Session.Context(false, c.Token);
            var list = Evaluator.Eval(data, ctx);
            if (list is not ListExpr { Items.Length: > 0 } items) throw new ToolArgumentException("data must be a non-empty list of numbers.");
            var o = new JsonObject { ["count"] = items.Items.Length };
            void Stat(string key, string fn)
            {
                if (!Has(fn)) return;
                try
                {
                    var v = Evaluator.Eval(CallExpr.Of(fn, list), ctx);
                    o[key] = Brief(v, c.Token);
                }
                catch (CalcException ex)
                {
                    o[key] = new JsonObject { ["error"] = ex.Message };
                }
            }
            Stat("sum", "sum");
            Stat("mean", "mean");
            Stat("median", "median");
            Stat("min", "min");
            Stat("max", "max");
            if (items.Items.Length > 1)
            {
                Stat("sample_variance", "variance");
                Stat("sample_stdev", "stdev");
            }
            Stat("population_variance", "pvariance");
            Stat("population_stdev", "pstdev");
            return ToolResult.Data(o, $"{items.Items.Length} values");
        }),
    };

    private McpTool Define() => new()
    {
        Name = "define",
        Title = "Define a variable or function",
        Description =
            "Stores a variable or a function in the shared Calcy session so later calls can use it: \"a = 5\", \"f(x) = x^2 - 3x\", " +
            "\"g(x, y) = x y\". Overwrites an existing definition with the same name.",
        InputSchema = Object(Req("definition", String("The definition, for example \"r = 3\" or \"f(x) = x^2 + 1\"."))),
        Annotations = new ToolAnnotations { ReadOnly = false, Destructive = true, Idempotent = true },
        Handler = c => Guard(() =>
        {
            var text = c.String("definition");
            var r = Session.Evaluate(text, c.Token);
            if (r.IsError) return CalcError(r.ErrorMessage ?? "Error", r.Hint);
            if (r.Kind == ResultKind.Value)
            {
                Session.Env.Remove("ans");
                return ToolResult.Error("Nothing was defined. Write name = value, or f(x) = expression.");
            }
            var o = new JsonObject
            {
                ["name"] = r.Name,
                ["kind"] = r.Kind == ResultKind.FunctionDefinition ? "function" : "variable",
            };
            if (r.Parameters.Count > 0) o["parameters"] = new JsonArray([.. r.Parameters.Select(p => (JsonNode)p)]);
            if (r.Exact is not null) o["value"] = Describe(r.Exact, c.Token);
            string shown = r.Kind == ResultKind.FunctionDefinition
                ? $"{r.Name}({string.Join(", ", r.Parameters)}) = {(r.Exact is null ? "" : Session.Print(r.Exact))}"
                : $"{r.Name} = {(r.Exact is null ? "" : Session.Print(r.Exact))}";
            o["shown"] = shown;
            return ToolResult.Data(o, shown);
        }),
    };

    private McpTool GetVariables() => new()
    {
        Name = "get_variables",
        Title = "List variables and functions",
        Description = "Lists the variables and functions defined in the shared Calcy session, with their values.",
        InputSchema = Object(),
        Handler = c => Guard(() => ToolResult.Data(VariablesJson(), $"{Session.Env.Variables.Count} variables, {Session.Env.Functions.Count} functions")),
    };

    private McpTool ClearVariables() => new()
    {
        Name = "clear_variables",
        Title = "Clear variables",
        Description = "Removes the named variables or functions from the session, or everything when all is true.",
        InputSchema = Object(
            Opt("names", Array(String("A variable or function name."), "Names to remove.")),
            Opt("all", Boolean("Remove every variable and function.", false))),
        Annotations = new ToolAnnotations { ReadOnly = false, Destructive = true, Idempotent = true },
        Handler = c => Guard(() =>
        {
            if (c.Bool("all", false))
            {
                Session.Env.Clear();
                return ToolResult.Data(new JsonObject { ["removed"] = "all" }, "cleared all");
            }
            if (c.Node("names") is not JsonArray names || names.Count == 0)
                throw new ToolArgumentException("Give names to remove, or set all to true.");
            var removed = new JsonArray();
            var missing = new JsonArray();
            foreach (var n in names)
            {
                var name = ItemText(n, "names").Trim();
                if (Session.Env.Remove(name)) removed.Add(name);
                else missing.Add(name);
            }
            return ToolResult.Data(new JsonObject { ["removed"] = removed, ["not_found"] = missing }, $"removed {removed.Count}");
        }),
    };

    private McpTool Settings() => new()
    {
        Name = "settings",
        Title = "Calculator settings",
        Description =
            "Reads or changes how Calcy calculates: the angle unit for trigonometry, how implied multiplication reads (Casio: 1/2x = 1/(2x); " +
            "TI: 1/2x = x/2) and whether complex results are allowed. Call without arguments to read the current settings.",
        InputSchema = Object(
            Opt("angle", String("Angle unit for sin, cos, tan and their inverses.", "radians", "degrees", "gradians")),
            Opt("implicit_multiplication", String("How 1/2x is read.", "casio", "ti")),
            Opt("complex", String("Complex numbers: auto (even roots of negatives give i), real (report non-real as errors), complex.", "auto", "real", "complex"))),
        Annotations = new ToolAnnotations { ReadOnly = false, Destructive = false, Idempotent = true },
        Handler = c => Guard(() =>
        {
            var s = Session.Settings;
            if (c.Has("angle"))
                s.Angle = c.Choice("angle", "radians", "radians", "degrees", "gradians") switch
                {
                    "degrees" => AngleMode.Degree,
                    "gradians" => AngleMode.Gradian,
                    _ => AngleMode.Radian,
                };
            if (c.Has("implicit_multiplication"))
                s.Implicit = c.Choice("implicit_multiplication", "casio", "casio", "ti") == "ti" ? ImplicitMultiplication.LeftToRight : ImplicitMultiplication.BindsTighter;
            if (c.Has("complex"))
                s.Complex = c.Choice("complex", "auto", "auto", "real", "complex") switch
                {
                    "real" => ComplexMode.RealOnly,
                    "complex" => ComplexMode.Complex,
                    _ => ComplexMode.Auto,
                };
            return ToolResult.Data(SettingsJson(), "settings");
        }),
    };

    private McpTool ListFunctions() => new()
    {
        Name = "list_functions",
        Title = "List functions",
        Description = "Searches Calcy's built-in functions by name, description or category (for example \"prime\", \"matrix\", \"distribution\").",
        InputSchema = Object(
            Opt("search", String("Words to look for.")),
            Opt("category", String("Only this category, for example Trigonometry or Lists."))),
        Handler = c => Guard(() =>
        {
            var q = c.OptionalString("search")?.Trim() ?? "";
            var cat = c.OptionalString("category")?.Trim() ?? "";
            var found = Session.Functions.All
                .Where(f => !f.Hidden)
                .Where(f => cat.Length == 0 || f.Category.Equals(cat, StringComparison.OrdinalIgnoreCase))
                .Where(f => q.Length == 0 || Matches(f, q))
                .OrderBy(f => f.Category).ThenBy(f => f.Name)
                .ToList();
            var arr = new JsonArray();
            foreach (var f in found.Take(150))
                arr.Add(new JsonObject
                {
                    ["name"] = f.Name,
                    ["signature"] = f.Signature.Length > 0 ? f.Signature : f.Name,
                    ["description"] = f.Description,
                    ["category"] = f.Category,
                });
            var o = new JsonObject { ["count"] = found.Count, ["functions"] = arr };
            if (found.Count > 150) o["note"] = "Only the first 150 are listed; search for something more specific.";
            o["categories"] = new JsonArray([.. Session.Functions.All.Where(f => !f.Hidden).Select(f => f.Category).Distinct().Order().Select(x => (JsonNode)x)]);
            return ToolResult.Data(o, $"{found.Count} functions");
        }),
    };

    private McpTool FunctionHelp() => new()
    {
        Name = "function_help",
        Title = "Function help",
        Description = "How to call one built-in function, with an example.",
        InputSchema = Object(Req("name", String("The function name, for example nCr or linsolve."))),
        Handler = c => Guard(() =>
        {
            var name = c.String("name").Trim().TrimEnd('(', ')');
            if (!Session.Functions.TryGet(name, out var f))
            {
                var close = Session.Functions.All.Where(x => !x.Hidden && x.Name.Contains(name, StringComparison.OrdinalIgnoreCase)).Select(x => x.Name).Take(8).ToList();
                return ToolResult.Error($"There is no function called '{name}'." + (close.Count > 0 ? " Did you mean: " + string.Join(", ", close) + "?" : ""));
            }
            return ToolResult.Data(FunctionJson(f), f.Name);
        }),
    };

    // ---------------------------------------------------------------- shared JSON for resources

    public JsonObject VariablesJson()
    {
        var vars = new JsonArray();
        foreach (var (name, value) in Session.Env.Variables.OrderBy(kv => kv.Key, StringComparer.Ordinal))
        {
            var d = Describe(value, CancellationToken.None, includeDecimal: false);
            d["name"] = name;
            vars.Add(d);
        }
        var funcs = new JsonArray();
        foreach (var (name, f) in Session.Env.Functions.OrderBy(kv => kv.Key, StringComparer.Ordinal))
        {
            funcs.Add(new JsonObject
            {
                ["name"] = name,
                ["parameters"] = new JsonArray([.. f.Parameters.Select(p => (JsonNode)p)]),
                ["body"] = ExprPrinter.ToInput(f.Body),
                ["shown"] = $"{name}({string.Join(", ", f.Parameters)}) = {Session.Print(f.Body)}",
            });
        }
        return new JsonObject { ["variables"] = vars, ["functions"] = funcs };
    }

    public JsonObject SettingsJson()
    {
        var s = Session.Settings;
        return new JsonObject
        {
            ["angle"] = s.Angle switch { AngleMode.Degree => "degrees", AngleMode.Gradian => "gradians", _ => "radians" },
            ["implicit_multiplication"] = s.Implicit == ImplicitMultiplication.LeftToRight ? "ti" : "casio",
            ["complex"] = s.Complex switch { ComplexMode.RealOnly => "real", ComplexMode.Complex => "complex", _ => "auto" },
        };
    }

    public static JsonObject FunctionJson(FunctionDefinition f)
    {
        var o = new JsonObject
        {
            ["name"] = f.Name,
            ["signature"] = f.Signature.Length > 0 ? f.Signature : f.Name,
            ["description"] = f.Description,
            ["category"] = f.Category,
        };
        if (f.Example.Length > 0) o["example"] = f.Example;
        if (f.Aliases.Length > 0) o["aliases"] = new JsonArray([.. f.Aliases.Select(a => (JsonNode)a)]);
        return o;
    }

    // ---------------------------------------------------------------- helpers

    private bool Has(string function) => Session.Functions.Contains(function);

    private static bool Matches(FunctionDefinition f, string q) =>
        f.Name.Contains(q, StringComparison.OrdinalIgnoreCase) ||
        f.Aliases.Any(a => a.Contains(q, StringComparison.OrdinalIgnoreCase)) ||
        f.Description.Contains(q, StringComparison.OrdinalIgnoreCase) ||
        f.Category.Contains(q, StringComparison.OrdinalIgnoreCase);

    private Expr Needs(string function, params Expr[] args)
    {
        if (!Has(function)) throw new CalcException($"'{function}' is not available in this build of Calcy");
        return new CallExpr(function, [.. args]);
    }

    private McpTool Unary(string name, string title, string function, string description, string argDescription) => new()
    {
        Name = name,
        Title = title,
        Description = description,
        InputSchema = Object(Req("expression", Expression(argDescription))),
        OutputSchema = MathResult(),
        IsAvailable = () => Has(function),
        Handler = c => Guard(() =>
        {
            var e = Parse(c, "expression");
            return Result(Evaluator.Eval(CallExpr.Of(function, e), Session.Context(false, c.Token)), c, false);
        }),
    };

    private static bool IsUnevaluated(Expr e, string function) => e is CallExpr call && call.Name == function;

    private Expr Parse(ToolCall c, string arg) => ParseText(c.String(arg), arg);

    private Expr ParseText(string text, string arg)
    {
        try
        {
            return Session.ParseExpression(text);
        }
        catch (CalcException ex)
        {
            throw new ToolArgumentException(DescribeParseError(arg, text, ex));
        }
    }

    private static string ItemText(JsonNode? n, string arg) => n switch
    {
        JsonValue v when v.TryGetValue<string>(out var s) => s,
        JsonValue v when v.GetValueKind() == System.Text.Json.JsonValueKind.Number => v.ToJsonString(),
        _ => throw new ToolArgumentException($"Every item of '{arg}' must be text or a number."),
    };

    private static ToolResult ParseError(string arg, string text, CalcException ex) => ToolResult.Error(DescribeParseError(arg, text, ex));

    private static string DescribeParseError(string arg, string text, CalcException ex)
    {
        var sb = new StringBuilder($"Could not read {arg} \"{text}\": {ex.Message}");
        if (ex.Position >= 0 && ex.Position <= text.Length) sb.Append($" (at character {ex.Position + 1})");
        if (!string.IsNullOrEmpty(ex.Hint)) sb.Append(". Suggestion: ").Append(ex.Hint);
        return sb.ToString();
    }

    private SymbolExpr VariableArg(ToolCall c, string arg) => VariableFrom(c.String(arg), arg);

    private SymbolExpr VariableFrom(string text, string arg)
    {
        Expr e;
        try
        {
            e = Parser.ParseExpression(text.Trim(), Session.ContextWith(text.Trim()));
        }
        catch (CalcException)
        {
            throw new ToolArgumentException($"'{text}' is not a variable name ({arg}).");
        }
        return e as SymbolExpr ?? throw new ToolArgumentException($"'{text}' is not a single variable name ({arg}).");
    }

    /// <summary>x when present, else the only unknown, else x for constants.</summary>
    private SymbolExpr GuessVariable(Expr e)
    {
        var free = Unknowns(e);
        if (free.Contains("x") || free.Count == 0) return new SymbolExpr("x");
        if (free.Count == 1) return new SymbolExpr(free[0]);
        throw new ToolArgumentException($"Say which variable to use: the expression contains {string.Join(", ", free)}.");
    }

    private IEnumerable<SymbolExpr> GuessVariables(IEnumerable<Expr> eqs)
    {
        var all = eqs.SelectMany(Unknowns).Distinct().Order(StringComparer.Ordinal).ToList();
        if (all.Count == 0) throw new ToolArgumentException("The equations have no unknowns.");
        return all.Select(n => new SymbolExpr(n));
    }

    private List<string> Unknowns(Expr e) =>
        [.. e.FreeSymbols().Where(n => !Session.Env.Variables.ContainsKey(n) && n != "ans").Distinct()];

    private IEnumerable<KeyValuePair<string, Expr>> Locals(ToolCall c)
    {
        if (c.Node("variables") is not JsonObject vars) yield break;
        foreach (var (name, node) in vars)
        {
            var text = ItemText(node, "variables." + name);
            var value = Evaluator.Eval(ParseText(text, "variables." + name), Session.Context(false, c.Token));
            var symbol = VariableFrom(name, "variables");
            yield return new(symbol.Name, value);
        }
    }

    private Expr MatrixArg(ToolCall c, string arg)
    {
        var node = c.Node(arg) ?? throw new ToolArgumentException($"Missing argument '{arg}'.");
        Expr e;
        if (node is JsonArray rows)
        {
            if (rows.Count == 0) throw new ToolArgumentException($"'{arg}' has no rows.");
            var sb = new StringBuilder("[");
            int width = -1;
            for (int r = 0; r < rows.Count; r++)
            {
                if (rows[r] is not JsonArray row || row.Count == 0) throw new ToolArgumentException($"Row {r + 1} of '{arg}' must be a non-empty list.");
                if (width >= 0 && row.Count != width) throw new ToolArgumentException($"Every row of '{arg}' must have {width} entries; row {r + 1} has {row.Count}.");
                width = row.Count;
                if (r > 0) sb.Append(", ");
                sb.Append('[');
                for (int k = 0; k < row.Count; k++)
                {
                    if (k > 0) sb.Append(", ");
                    sb.Append('(').Append(ItemText(row[k], arg)).Append(')');
                }
                sb.Append(']');
            }
            sb.Append(']');
            e = ParseText(sb.ToString(), arg);
        }
        else e = ParseText(ItemText(node, arg), arg);
        var value = Evaluator.Eval(e, Session.Context(false, c.Token));
        if (value is not MatrixExpr) throw new ToolArgumentException($"'{arg}' must be a matrix, for example [[1, 2], [3, 4]].");
        return value;
    }

    private Expr DataArg(ToolCall c, string arg)
    {
        var node = c.Node(arg) ?? throw new ToolArgumentException($"Missing argument '{arg}'.");
        if (node is JsonArray items)
        {
            var list = new List<Expr>(items.Count);
            for (int k = 0; k < items.Count; k++) list.Add(ParseText(ItemText(items[k], arg), $"{arg}[{k}]"));
            return new ListExpr([.. list]);
        }
        return ParseText(ItemText(node, arg), arg);
    }

    private ToolResult Result(Expr value, ToolCall c, bool approximate, List<string>? notes = null)
    {
        var o = Describe(value, c.Token, includeDecimal: !approximate);
        if (notes is { Count: > 0 }) o["notes"] = new JsonArray([.. notes.Select(n => (JsonNode)n)]);
        return ToolResult.Data(o, o["result"]?.GetValue<string>());
    }

    /// <summary>The standard description of a value: display text, typeable text, LaTeX, decimal, kind.</summary>
    public JsonObject Describe(Expr value, CancellationToken ct, bool includeDecimal = true)
    {
        var o = new JsonObject
        {
            ["result"] = ExprPrinter.Print(value, Display),
            ["input"] = ExprPrinter.ToInput(value),
            ["latex"] = LatexPrinter.ToLatex(value),
        };
        if (includeDecimal)
        {
            var approx = Session.Approximate(value, ct);
            if (approx is not null)
            {
                var d = ExprPrinter.Print(approx, Display);
                if (d != (string?)o["result"]) o["decimal"] = d;
            }
        }
        o["kind"] = Kind(value);
        o["exact"] = !ContainsDecimal(value);
        return o;
    }

    private JsonObject Brief(Expr value, CancellationToken ct)
    {
        var o = new JsonObject { ["result"] = ExprPrinter.Print(value, Display) };
        var approx = Session.Approximate(value, ct);
        if (approx is not null) o["decimal"] = ExprPrinter.Print(approx, Display);
        return o;
    }

    private static string Kind(Expr e) => e switch
    {
        NumberExpr or ConstantExpr => "number",
        RelationExpr or LogicExpr => "equation",
        ListExpr => "list",
        MatrixExpr => "matrix",
        BoolExpr => "boolean",
        StringExpr => "text",
        LambdaExpr => "function",
        _ when e.IsConstantValue() => "number",
        _ => "expression",
    };

    private static bool ContainsDecimal(Expr e) => e switch
    {
        NumberExpr n => !n.Value.IsExact,
        _ => e.Children.Any(ContainsDecimal),
    };

    private static ToolResult CalcError(string message, string? hint) =>
        ToolResult.Error(string.IsNullOrEmpty(hint) ? message : message + ". Suggestion: " + hint);

    private static ToolResult Guard(Func<ToolResult> body)
    {
        try
        {
            return body();
        }
        catch (CalcException ex)
        {
            return CalcError(ex.Message, ex.Hint);
        }
        catch (DivideByZeroException)
        {
            return ToolResult.Error("Division by zero");
        }
        catch (OverflowException)
        {
            return ToolResult.Error("The number is too large");
        }
        catch (InsufficientExecutionStackException)
        {
            return ToolResult.Error("The expression is nested too deeply");
        }
    }

    internal static string Invariant(double d) => d.ToString("R", CultureInfo.InvariantCulture);
}
