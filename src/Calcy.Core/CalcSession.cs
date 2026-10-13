using Calcy.Core.Evaluation;
using Calcy.Core.Expressions;
using Calcy.Core.Numbers;
using Calcy.Core.Parsing;
using Calcy.Core.Printing;

namespace Calcy.Core;

public enum ResultKind
{
    Value,
    Assignment,
    FunctionDefinition,
    Error,
}

/// <summary>The outcome of evaluating one line of input.</summary>
public sealed record CalcResult
{
    public required string Input { get; init; }
    public required ResultKind Kind { get; init; }

    /// <summary>The parsed input (unsimplified), for showing what was understood.</summary>
    public Expr? Parsed { get; init; }

    /// <summary>The exact (symbolic) result.</summary>
    public Expr? Exact { get; init; }

    /// <summary>The decimal approximation, when it differs from the exact result.</summary>
    public Expr? Approx { get; init; }

    /// <summary>The assigned variable or defined function name.</summary>
    public string? Name { get; init; }

    public IReadOnlyList<string> Parameters { get; init; } = [];

    public string? ErrorMessage { get; init; }
    public int ErrorPosition { get; init; } = -1;
    public int ErrorLength { get; init; }
    public string? Hint { get; init; }

    public bool IsError => Kind == ResultKind.Error;

    /// <summary>True when the exact form is worth showing first (fraction, radical, π multiple), false when a decimal is clearer.</summary>
    public bool ExactIsNice { get; init; }
}

/// <summary>
/// A calculator session: settings, variables and functions, plus parsing and evaluation of input lines.
/// This is the main entry point used by every mode, the CLI and the MCP server.
/// </summary>
public sealed class CalcSession : IParseContext
{
    public CalcSettings Settings { get; }
    public CalcEnvironment Env { get; }
    public FunctionRegistry Functions { get; }

    public CalcSession(CalcSettings? settings = null, FunctionRegistry? functions = null, CalcEnvironment? env = null)
    {
        Settings = settings ?? new CalcSettings();
        Functions = functions ?? FunctionRegistry.CreateDefault();
        Env = env ?? new CalcEnvironment();
    }

    // IParseContext
    /// <summary>
    /// A name is a function unless the user stored a value under it: after C = [[1, 2], [3, 4]], C means that matrix,
    /// not the built-in combinations function.
    /// </summary>
    public bool IsFunction(string name)
    {
        if (Env.TryGetVariable(name, out var v)) return v is LambdaExpr;
        return Functions.Contains(name) || Env.TryGetFunction(name, out _);
    }
    public bool IsKnownName(string name) => Env.Variables.ContainsKey(name) || name == "ans";
    public ImplicitMultiplication Implicit => Settings.Implicit;
    public bool SplitUnknownNames => Settings.SplitUnknownNames;

    public Statement Parse(string input) => Parser.ParseStatement(input, this);
    public Expr ParseExpression(string input) => Parser.ParseExpression(input, this);

    /// <summary>A parse context that also treats the given names as known (for example graph variables).</summary>
    public IParseContext ContextWith(params string[] knownNames) => new ExtendedContext(this, knownNames);

    public EvalContext Context(bool approximate = false, CancellationToken ct = default) =>
        new(Settings, Env, Functions, approximate, ct);

    public Expr Evaluate(Expr e, bool approximate = false, CancellationToken ct = default) =>
        Evaluator.Eval(e, Context(approximate, ct));

    /// <summary>Evaluates text as an expression (no assignment) and returns the exact result.</summary>
    public Expr EvaluateExpression(string input, bool approximate = false, CancellationToken ct = default) =>
        Evaluate(ParseExpression(input), approximate, ct);

    /// <summary>Evaluates a line of input: an expression, a definition (a = 5, f(x) = x^2) or a store (5 → a).</summary>
    public CalcResult Evaluate(string input, CancellationToken ct = default)
    {
        Statement stmt;
        try
        {
            stmt = Parse(input);
        }
        catch (CalcException ex)
        {
            return Error(input, ex);
        }
        try
        {
            return stmt switch
            {
                AssignStatement a => Assign(input, a, ct),
                FunctionDefStatement f => DefineFunction(input, f, ct),
                ExprStatement e => Value(input, e.Expression, ct),
                _ => throw new CalcException("Unsupported input"),
            };
        }
        catch (CalcException ex)
        {
            return Error(input, ex);
        }
        catch (DivideByZeroException)
        {
            return Error(input, new CalcException("Division by zero"));
        }
        catch (OverflowException)
        {
            return Error(input, new CalcException("The number is too large"));
        }
        catch (OperationCanceledException)
        {
            return Error(input, new CalcException("Stopped"));
        }
        catch (InsufficientExecutionStackException)
        {
            return Error(input, new CalcException("The expression is nested too deeply"));
        }
    }

    private CalcResult Value(string input, Expr parsed, CancellationToken ct)
    {
        var exact = Evaluate(parsed, approximate: false, ct);
        CheckReal(exact);
        var approx = Approximate(exact, ct);
        Env.SetVariable("ans", exact);
        return new CalcResult
        {
            Input = input,
            Kind = ResultKind.Value,
            Parsed = parsed,
            Exact = exact,
            Approx = approx,
            ExactIsNice = IsNiceExact(exact),
        };
    }

    private CalcResult Assign(string input, AssignStatement a, CancellationToken ct)
    {
        var value = Evaluate(a.Value, approximate: false, ct);
        if (value.ContainsSymbol(a.Name))
            throw new CalcException($"{a.Name} cannot be defined in terms of itself before it has a value");
        CheckReal(value);
        Env.SetVariable(a.Name, value);
        return new CalcResult
        {
            Input = input,
            Kind = ResultKind.Assignment,
            Parsed = a.Value,
            Exact = value,
            Approx = Approximate(value, ct),
            Name = a.Name,
            ExactIsNice = IsNiceExact(value),
        };
    }

    private CalcResult DefineFunction(string input, FunctionDefStatement f, CancellationToken ct)
    {
        if (Functions.Contains(f.Name))
            throw new CalcException($"'{f.Name}' is a built-in function; pick another name");
        // Store the body as written so it follows later changes of the variables it uses.
        var uf = new UserFunction(f.Name, f.Parameters, f.Body);
        Env.SetFunction(uf);
        Expr shown;
        try
        {
            shown = Evaluator.Eval(f.Body, Context(false, ct).WithFree(f.Parameters));
        }
        catch (CalcException)
        {
            shown = f.Body;
        }
        return new CalcResult
        {
            Input = input,
            Kind = ResultKind.FunctionDefinition,
            Parsed = f.Body,
            Exact = shown,
            Name = f.Name,
            Parameters = f.Parameters,
            ExactIsNice = true,
        };
    }

    /// <summary>The decimal form of an exact result, or null when it would look the same.</summary>
    public Expr? Approximate(Expr exact, CancellationToken ct = default)
    {
        if (exact is NumberExpr { Value.IsExact: false }) return null;
        if (exact is NumberExpr { Value.IsExactInteger: true }) return null;
        if (exact is StringExpr or BoolExpr or LambdaExpr) return null;
        if (!ContainsExactNonInteger(exact)) return null;
        try
        {
            // Names left in an exact result are unknowns (x in "x = 2 or x = 3"); a stored x = 5 must not be substituted now.
            var approx = Evaluator.Eval(exact, Context(true, ct).WithFree(exact.FreeSymbols()));
            return approx.Equals(exact) ? null : approx;
        }
        catch (CalcException)
        {
            return null;
        }
    }

    private static bool ContainsExactNonInteger(Expr e) => e switch
    {
        NumberExpr n => n.Value.IsExact && !n.Value.IsExactInteger,
        ConstantExpr c => c.Constant is ConstantKind.Pi or ConstantKind.E,
        PowExpr or CallExpr => true,
        _ => e.Children.Any(ContainsExactNonInteger),
    };

    /// <summary>Fractions, radicals and multiples of π read well; unevaluated sin(1) or huge fractions do not.</summary>
    public static bool IsNiceExact(Expr e)
    {
        if (e.NodeCount() > 40) return false;
        bool nice = true;
        void Visit(Expr x)
        {
            switch (x)
            {
                case NumberExpr n when n.Value.IsExact:
                    if (n.Value.ReQ.BitSize > 120 && !n.Value.IsExactInteger) nice = false;
                    break;
                case CallExpr c when c.Name is not ("abs" or "factorial") && c.Args.All(a => a.IsConstantValue()):
                    nice = false;
                    break;
            }
            foreach (var ch in x.Children) Visit(ch);
        }
        Visit(e);
        return nice;
    }

    private void CheckReal(Expr e)
    {
        if (Settings.Complex != ComplexMode.RealOnly) return;
        if (ContainsNonReal(e))
            throw new CalcException("The result is not a real number (switch Complex results to Auto in Settings to see it)");
    }

    private static bool ContainsNonReal(Expr e) => e switch
    {
        NumberExpr n => !n.Value.IsReal,
        _ => e.Children.Any(ContainsNonReal),
    };

    private static CalcResult Error(string input, CalcException ex) => new()
    {
        Input = input,
        Kind = ResultKind.Error,
        ErrorMessage = ex.Message,
        ErrorPosition = ex.Position,
        ErrorLength = ex.Length,
        Hint = ex.Hint,
    };

    /// <summary>Formats a result for display according to the settings (exact first or decimal first).</summary>
    public (string Primary, string? Secondary) Format(CalcResult r, bool unicode = true)
    {
        if (r.IsError) return (r.ErrorMessage ?? "Error", null);
        var opts = new PrintOptions { Unicode = unicode, Numbers = NumberFormatOptions.From(Settings, unicode) };
        string exact = r.Exact is null ? "" : ExprPrinter.Print(r.Exact, opts);
        string? approx = r.Approx is null ? null : ExprPrinter.Print(r.Approx, opts);
        if (approx is null || approx == exact) return (exact, null);
        bool decimalFirst = Settings.Results == ResultPreference.DecimalFirst || !r.ExactIsNice;
        return decimalFirst ? (approx, exact) : (exact, approx);
    }

    public string Print(Expr e, bool unicode = true) =>
        ExprPrinter.Print(e, new PrintOptions { Unicode = unicode, Numbers = NumberFormatOptions.From(Settings, unicode) });

    private sealed class ExtendedContext(CalcSession s, string[] names) : IParseContext
    {
        public bool IsFunction(string name) => !names.Contains(name) && s.IsFunction(name);
        public bool IsKnownName(string name) => names.Contains(name) || s.IsKnownName(name);
        public ImplicitMultiplication Implicit => s.Implicit;
        public bool SplitUnknownNames => s.SplitUnknownNames;
    }
}
