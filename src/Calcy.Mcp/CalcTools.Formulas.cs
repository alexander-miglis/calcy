using System.Globalization;
using System.Text;
using System.Text.Json.Nodes;
using Calcy.Core;
using Calcy.Core.Expressions;
using Calcy.Engine;
using Calcy.Formulas;
using static Calcy.Mcp.Schema;

namespace Calcy.Mcp;

/// <summary>Tools over the documented formula collection.</summary>
public sealed partial class CalcTools
{
    private static FormulaLibrary Formulas => FormulaLibrary.Default;

    private McpTool SearchFormulas() => new()
    {
        Name = "search_formulas",
        Title = "Search the formula collection",
        Description =
            "Finds formulas in Calcy's documented collection (algebra, geometry, trigonometry, calculus, statistics, mechanics, waves, electricity, " +
            "thermodynamics, modern physics, chemistry, finance, everyday). Search by name, topic or a typed equation such as \"E=mc^2\"; small typos are fine.",
        InputSchema = Object(
            Opt("query", String("What to look for, for example \"kinetic energy\", \"pythagoras\" or \"half life\".")),
            Opt("category", String("Only this category id, for example mechanics.")),
            Opt("limit", Integer("Maximum results.", 1, 100, 15))),
        Handler = c => Guard(() =>
        {
            var q = c.OptionalString("query")?.Trim() ?? "";
            var cat = c.OptionalString("category")?.Trim();
            int limit = c.Int("limit", 15, 1, 100);
            IEnumerable<Formula> found = q.Length > 0 ? Formulas.Search(q) : Formulas.All;
            if (!string.IsNullOrEmpty(cat)) found = found.Where(f => f.Category.Equals(cat, StringComparison.OrdinalIgnoreCase));
            var list = found.Take(limit).ToList();
            var arr = new JsonArray();
            foreach (var f in list)
                arr.Add(new JsonObject
                {
                    ["id"] = f.Id,
                    ["name"] = f.Name,
                    ["equation"] = f.Equation,
                    ["category"] = f.Category,
                    ["level"] = f.Level == FormulaLevel.School ? "school" : "university",
                });
            var o = new JsonObject { ["count"] = list.Count, ["formulas"] = arr };
            if (list.Count == 0)
                o["categories"] = new JsonArray([.. Formulas.Categories.Select(k => (JsonNode)k.Id)]);
            return ToolResult.Data(o, $"{list.Count} formulas");
        }),
    };

    private McpTool GetFormula() => new()
    {
        Name = "get_formula",
        Title = "Explain a formula",
        Description = "The full entry for one formula: equation, LaTeX, every variable with its unit, the explanation, when to use it, assumptions, a worked example and related formulas.",
        InputSchema = Object(Req("id", String("The formula id from search_formulas, for example suvat-v-u-a-t."))),
        Handler = c => Guard(() =>
        {
            var f = FindFormula(c.String("id"));
            return ToolResult.Data(FormulaJson(f), f.Name);
        }),
    };

    private McpTool SolveFormula() => new()
    {
        Name = "solve_formula",
        Title = "Solve a formula",
        Description =
            "Solves a formula from the collection for any one of its variables, given values for the others (in the units the formula lists). " +
            "Physical constants are filled in automatically; solutions outside a variable's allowed range (for example a negative time) are dropped.",
        InputSchema = Object(
            Req("id", String("The formula id.")),
            Req("solve_for", String("The variable symbol to find, for example a.")),
            Req("values", new JsonObject
            {
                ["type"] = "object",
                ["description"] = "Known values by symbol, for example {\"v\": 25, \"u\": 5, \"t\": 8}. Values may be expressions such as \"sqrt(2)\".",
                ["additionalProperties"] = new JsonObject { ["type"] = new JsonArray("string", "number") },
            })),
        Handler = c => Guard(() =>
        {
            var f = FindFormula(c.String("id"));
            var unknown = c.String("solve_for").Trim();
            if (f.GetVariable(unknown) is null)
                throw new ToolArgumentException($"'{f.Name}' has no variable {unknown}. Its variables are {string.Join(", ", f.Variables.Select(v => v.Symbol))}.");
            var known = new Dictionary<string, Expr>(StringComparer.Ordinal);
            if (c.Node("values") is JsonObject values)
            {
                foreach (var (symbol, node) in values)
                {
                    if (f.GetVariable(symbol) is null)
                        throw new ToolArgumentException($"'{f.Name}' has no variable {symbol}. Its variables are {string.Join(", ", f.Variables.Select(v => v.Symbol))}.");
                    known[symbol] = ParseText(ItemText(node, "values." + symbol), "values." + symbol);
                }
            }
            var result = FormulaSolver.Solve(Session, f, known, unknown, Formulas, c.Token);
            var sols = new JsonArray();
            foreach (var s in result.Solutions)
            {
                var d = Describe(s.Value, c.Token);
                d["unit"] = result.Unit;
                sols.Add(d);
            }
            var o = new JsonObject
            {
                ["formula"] = f.Name,
                ["equation"] = f.Equation,
                ["variable"] = unknown,
                ["unit"] = result.Unit,
                ["solutions"] = sols,
            };
            if (result.Notes.Count > 0) o["notes"] = new JsonArray([.. result.Notes.Select(n => (JsonNode)n)]);
            var first = result.Solutions[0];
            return ToolResult.Data(o, $"{unknown} = {first.Approx.ToString("G10", CultureInfo.InvariantCulture)} {result.Unit}".TrimEnd());
        }),
    };

    private static Formula FindFormula(string id)
    {
        id = id.Trim();
        var f = Formulas.Get(id);
        if (f is not null) return f;
        var guesses = Formulas.Search(id, 5);
        throw new ToolArgumentException($"There is no formula with id '{id}'." +
            (guesses.Count > 0 ? " Did you mean: " + string.Join(", ", guesses.Select(g => g.Id)) + "?" : " Use search_formulas to find ids."));
    }

    internal static JsonObject FormulaJson(Formula f)
    {
        var vars = new JsonArray();
        foreach (var v in f.Variables)
        {
            var vo = new JsonObject { ["symbol"] = v.Symbol, ["name"] = v.Name, ["unit"] = v.Unit, ["description"] = v.Description };
            if (v.DefaultValue is double d) vo["default"] = d;
            if (v.Min is double min) vo["min"] = min;
            if (v.Max is double max) vo["max"] = max;
            if (v.Positive) vo["positive"] = true;
            if (v.Integer) vo["integer"] = true;
            vars.Add(vo);
        }
        var o = new JsonObject
        {
            ["id"] = f.Id,
            ["name"] = f.Name,
            ["category"] = f.Category,
            ["subcategory"] = f.Subcategory,
            ["level"] = f.Level == FormulaLevel.School ? "school" : "university",
            ["equation"] = f.Equation,
            ["latex"] = f.DisplayLatex,
            ["identity"] = f.IsIdentity,
            ["variables"] = vars,
            ["explanation"] = f.Explanation,
            ["when_to_use"] = f.WhenToUse,
            ["assumptions"] = new JsonArray([.. f.Assumptions.Select(a => (JsonNode)a)]),
        };
        var constants = Formulas.ConstantsFor(f);
        if (constants.Count > 0)
            o["constants"] = new JsonArray([.. constants.Select(k => (JsonNode)new JsonObject { ["symbol"] = k.Symbol, ["name"] = k.Name, ["value"] = k.Value, ["unit"] = k.Unit })]);
        if (f.Example is { } ex)
        {
            var given = new JsonObject();
            foreach (var (k, v) in ex.Given) given[k] = v;
            o["example"] = new JsonObject
            {
                ["story"] = ex.Story,
                ["given"] = given,
                ["solve_for"] = ex.SolveFor,
                ["result"] = ex.Result,
                ["unit"] = ex.Unit,
                ["steps"] = new JsonArray([.. ex.Steps.Select(s => (JsonNode)s)]),
            };
        }
        if (f.Related.Count > 0) o["related"] = new JsonArray([.. f.Related.Select(r => (JsonNode)r)]);
        if (f.Source is not null) o["source"] = f.Source;
        return o;
    }

    /// <summary>A formula as a readable Markdown page (for the calcy://formulas/{id} resource).</summary>
    public static string FormulaMarkdown(Formula f)
    {
        var sb = new StringBuilder();
        sb.Append("# ").Append(f.Name).Append("\n\n");
        sb.Append("`").Append(f.Equation).Append("`\n\n");
        if (f.DisplayLatex.Length > 0) sb.Append("$$").Append(f.DisplayLatex).Append("$$\n\n");
        sb.Append(f.Explanation).Append("\n\n");
        if (f.WhenToUse.Length > 0) sb.Append("**When to use:** ").Append(f.WhenToUse).Append("\n\n");
        sb.Append("| Symbol | Meaning | Unit |\n|---|---|---|\n");
        foreach (var v in f.Variables) sb.Append("| ").Append(v.Symbol).Append(" | ").Append(v.Name).Append(" | ").Append(v.Unit).Append(" |\n");
        if (f.Assumptions.Count > 0) sb.Append("\n**Assumes:** ").Append(string.Join("; ", f.Assumptions)).Append('\n');
        if (f.Example is { } ex)
        {
            sb.Append("\n## Example\n\n").Append(ex.Story).Append("\n\n");
            foreach (var step in ex.Steps) sb.Append("1. ").Append(step).Append('\n');
        }
        return sb.ToString();
    }
}
