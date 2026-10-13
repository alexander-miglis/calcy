using System.Text;
using Calcy.Core;
using Calcy.Core.Evaluation;

namespace Calcy.Mcp;

/// <summary>Builds the Calcy MCP server: tools, resources and prompts over one shared calculator session.</summary>
public static class CalcyMcp
{
    public const string FunctionUriPrefix = "calcy://reference/functions/";
    public const string FormulaUriPrefix = "calcy://formulas/";

    /// <summary>What a model should know before it uses the tools.</summary>
    public const string Instructions =
        "Calcy is an exact calculator and computer algebra system. Use its tools for every calculation instead of doing arithmetic yourself. " +
        "Results are exact when possible (fractions, roots, multiples of pi) and come with a decimal value, plain text you can pass back in, and LaTeX. " +
        "Syntax: ^ for powers, sqrt(x), root(x, n), abs(x), log(x) is base 10, ln(x), pi, e, i, inf; implied multiplication works (2x, 3(x + 1)) " +
        "and binds tighter than division, so 1/2x means 1/(2x). Lists use braces {1, 2, 3}, matrices use rows [[1, 2], [3, 4]]. " +
        "Variables and functions defined with the define tool stay available to later calls. Read calcy://guide/syntax for the full syntax.";

    /// <summary>Creates a server around a session (a fresh full-engine session when none is given).</summary>
    public static McpServer CreateServer(McpServerOptions? options = null, CalcSession? session = null, IEnumerable<McpTool>? extraTools = null)
    {
        session ??= Calcy.Engine.CalcyEngine.CreateSession(new CalcSettings());
        options ??= new McpServerOptions();
        if (options.Instructions is null)
        {
            options = new McpServerOptions
            {
                Name = options.Name,
                Title = options.Title,
                Version = options.Version,
                Instructions = Instructions,
                ReadOnly = options.ReadOnly,
                AllowCode = options.AllowCode,
                DisabledTools = options.DisabledTools,
                ToolTimeout = options.ToolTimeout,
                Audit = options.Audit,
                Log = options.Log,
            };
        }
        var tools = new CalcTools(session);
        return new McpServer(options, [.. tools.All(), .. extraTools ?? []], Resources(tools), Templates(session), Prompts());
    }

    private static IEnumerable<McpResource> Resources(CalcTools tools) =>
    [
        new()
        {
            Uri = "calcy://guide/syntax",
            Name = "syntax",
            Title = "How to type maths in Calcy",
            Description = "Operators, functions, lists, matrices, definitions and the rules for implied multiplication.",
            MimeType = "text/markdown",
            Read = () => SyntaxGuide,
        },
        new()
        {
            Uri = "calcy://reference/functions",
            Name = "functions",
            Title = "Function reference",
            Description = "Every built-in function, grouped by category.",
            MimeType = "text/markdown",
            Read = () => FunctionReference(tools.Session.Functions),
        },
        new()
        {
            Uri = "calcy://formulas",
            Name = "formulas",
            Title = "Formula collection",
            Description = "Every formula in the collection by category, with ids for get_formula and solve_formula.",
            MimeType = "text/markdown",
            Read = FormulaIndex,
        },
        new()
        {
            Uri = "calcy://session/variables",
            Name = "variables",
            Title = "Session variables",
            Description = "Variables and functions defined in the shared session.",
            MimeType = "application/json",
            Read = () => tools.VariablesJson().ToJsonString(McpJson.Options),
        },
        new()
        {
            Uri = "calcy://session/settings",
            Name = "settings",
            Title = "Session settings",
            Description = "Angle unit, implied multiplication and complex mode.",
            MimeType = "application/json",
            Read = () => tools.SettingsJson().ToJsonString(McpJson.Options),
        },
    ];

    private static IEnumerable<McpResourceTemplate> Templates(CalcSession session) =>
    [
        new()
        {
            UriTemplate = FormulaUriPrefix + "{id}",
            Name = "formula",
            Title = "One formula",
            Description = "A formula from the collection with its explanation, variables and a worked example.",
            MimeType = "text/markdown",
            TryRead = uri =>
            {
                if (!uri.StartsWith(FormulaUriPrefix, StringComparison.Ordinal)) return null;
                var f = Calcy.Formulas.FormulaLibrary.Default.Get(Uri.UnescapeDataString(uri[FormulaUriPrefix.Length..]));
                return f is null ? null : CalcTools.FormulaMarkdown(f);
            },
        },
        new()
        {
            UriTemplate = FunctionUriPrefix + "{name}",
            Name = "function",
            Title = "One function",
            Description = "How to call one built-in function, with an example.",
            MimeType = "text/markdown",
            TryRead = uri =>
            {
                if (!uri.StartsWith(FunctionUriPrefix, StringComparison.Ordinal)) return null;
                var name = Uri.UnescapeDataString(uri[FunctionUriPrefix.Length..]);
                return session.Functions.TryGet(name, out var f) ? FunctionMarkdown(f) : null;
            },
        },
    ];

    private static IEnumerable<McpPrompt> Prompts() =>
    [
        new()
        {
            Name = "check_solution",
            Title = "Check my solution",
            Description = "Checks a worked solution line by line with Calcy and points out the first mistake.",
            Arguments = [new("problem", "The question that was solved.", true), new("solution", "The worked solution to check.", true)],
            Render = a =>
                $"Check this solution step by step. Use the Calcy tools to verify every calculation and every transformation; do not trust mental arithmetic. " +
                $"For each step say whether it is correct. Stop at the first mistake, explain it kindly and show the correct step.\n\nProblem:\n{a["problem"]}\n\nSolution:\n{a["solution"]}",
        },
        new()
        {
            Name = "explain",
            Title = "Explain this",
            Description = "Explains an expression or result in plain language, with the steps Calcy can verify.",
            Arguments = [new("expression", "The expression, equation or result to explain.", true), new("level", "Who it is for, for example 'year 9' or 'first-year university'.", false)],
            Render = a =>
                $"Explain {a["expression"]} for {(a.TryGetValue("level", out var l) ? l : "a secondary school student")}. " +
                "Use the Calcy tools to compute the values and transformations you mention, and show the key steps.",
        },
        new()
        {
            Name = "practice_problems",
            Title = "Practice problems",
            Description = "Makes practice problems like an example, with answers checked by Calcy.",
            Arguments = [new("example", "An example problem.", true), new("count", "How many problems (default 5).", false)],
            Render = a =>
                $"Write {(a.TryGetValue("count", out var n) ? n : "5")} practice problems like this one, of similar difficulty, with varied numbers that give clean answers: {a["example"]}\n" +
                "Solve each one with the Calcy tools and list the answers separately at the end.",
        },
        new()
        {
            Name = "fit_model",
            Title = "Fit a model to data",
            Description = "Finds a good model for paired data and explains how well it fits.",
            Arguments = [new("x", "The x values, comma separated.", true), new("y", "The y values, comma separated.", true)],
            Render = a =>
                $"Fit a model to this data. x: {a["x"]}\ny: {a["y"]}\n" +
                "Use the Calcy tools to describe the data, try sensible models (linear, quadratic, exponential, power), compare how well they fit, and recommend one.",
        },
    ];

    private static string FormulaIndex()
    {
        var lib = Calcy.Formulas.FormulaLibrary.Default;
        var sb = new StringBuilder("# Calcy formula collection\n");
        foreach (var cat in lib.Categories)
        {
            sb.Append("\n## ").Append(cat.Name).Append(" (`").Append(cat.Id).Append("`)\n\n");
            foreach (var f in lib.ByCategory(cat.Id)) sb.Append("- `").Append(f.Id).Append("`: ").Append(f.Name).Append(", `").Append(f.Equation).Append("`\n");
        }
        return sb.ToString();
    }

    public static string FunctionReference(FunctionRegistry functions)
    {
        var sb = new StringBuilder("# Calcy functions\n");
        foreach (var g in functions.All.Where(f => !f.Hidden).GroupBy(f => f.Category).OrderBy(g => g.Key))
        {
            sb.Append("\n## ").Append(g.Key).Append("\n\n");
            foreach (var f in g.OrderBy(f => f.Name))
            {
                sb.Append("- `").Append(f.Signature.Length > 0 ? f.Signature : f.Name).Append("`: ").Append(f.Description);
                if (f.Example.Length > 0) sb.Append(" Example: `").Append(f.Example).Append('`');
                sb.Append('\n');
            }
        }
        return sb.ToString();
    }

    public static string FunctionMarkdown(FunctionDefinition f)
    {
        var sb = new StringBuilder();
        sb.Append("# ").Append(f.Name).Append("\n\n");
        sb.Append('`').Append(f.Signature.Length > 0 ? f.Signature : f.Name).Append("`\n\n");
        sb.Append(f.Description).Append("\n\n");
        sb.Append("Category: ").Append(f.Category).Append('\n');
        if (f.Aliases.Length > 0) sb.Append("Also called: ").Append(string.Join(", ", f.Aliases)).Append('\n');
        if (f.Example.Length > 0) sb.Append("Example: `").Append(f.Example).Append("`\n");
        return sb.ToString();
    }

    public const string SyntaxGuide = """
        # Typing maths in Calcy

        ## Numbers and operators
        - `+ - * /` and `^` for powers; `2^3^2` is `2^(3^2)`.
        - Decimals are exact: `0.1 + 0.2` gives `3/10`. Use `approx(...)` or the `approximate` option for decimals.
        - `x!` factorial, `x%` percent, `30°` degrees, `5 mod 3` remainder.
        - Scientific notation: `6.02e23`. Other bases: `0xFF`, `0b1010`, `0o17`.

        ## Implied multiplication
        - `2x`, `3(x + 1)`, `(a + b)(a - b)`, `2pi r` all multiply.
        - It binds tighter than division (Casio style): `1/2x` means `1/(2x)`.
        - Unknown multi-letter names are split into letters: `ab` is `a*b`. Names that are defined, such as `ans`, stay whole.

        ## Functions
        - `sqrt(x)`, `cbrt(x)`, `root(x, n)`, `abs(x)` or `|x|`, `exp(x)`, `ln(x)`, `log(x)` (base 10), `log(x, b)`.
        - Trigonometry: `sin cos tan asin acos atan sinh cosh tanh`, radians unless the angle setting says otherwise.
        - A function of a single simple argument may skip brackets: `sin 2x` is `sin(2x)`, `sin^2 x` is `(sin x)^2`.
        - Counting: `nCr(n, r)`, `nPr(n, r)`, `gcd`, `lcm`, `isprime`, `ifactor`.

        ## Constants
        `pi` (or `π`), `e`, `i`, `inf`.

        ## Lists and matrices
        - Lists: `{1, 2, 3}`; functions apply to each item: `sqrt({4, 9})` is `{2, 3}`. Ranges: `[1...5]`.
        - `seq(k^2, k, 1, 10)`, `sum(k, k, 1, 100)`, `mean`, `median`, `stdev`, `sort`.
        - Matrices are rows: `[[1, 2], [3, 4]]`; `A^-1` or `inv(A)`, `det(A)`, `transpose(A)`, `rref(A)`.

        ## Equations, conditions and piecewise
        - `=` in an equation (`x^2 = 4`), `<`, `<=`, `>`, `>=`, `!=`; logic `and`, `or`, `not`.
        - Piecewise: `{x < 0: -x, x}` (the last value is the otherwise case).
        - Restrict a domain: `x^2 {0 < x < 5}`.

        ## Definitions (define tool)
        - `a = 5`, `f(x) = x^2 - 3x`, `g(x, y) = x y`; `5 -> b` stores too.
        - Definitions stay in the session for later calls; `ans` is the previous answer.
        """;
}
