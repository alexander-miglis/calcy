using Calcy.Core;
using Calcy.Core.Evaluation;
using Calcy.Core.Expressions;
using Calcy.Core.Numbers;
using Calcy.Units;

namespace Calcy.Engine;

/// <summary>Unit conversion as a calculator function: convert(5, "km", "mi") or convert("170 cm to ft in").</summary>
public static class UnitsModule
{
    public static void Register(FunctionRegistry r)
    {
        if (r.Contains("convert")) return;
        r.Add(new FunctionDefinition
        {
            Name = "convert",
            Aliases = r.Contains("uconv") ? [] : ["uconv"],
            MinArgs = 1,
            MaxArgs = 3,
            Listable = true,
            Category = "Units",
            Signature = "convert(value, \"from\", \"to\") or convert(\"170 cm to ft in\")",
            Description = "Converts between units (length, area, volume, mass, time, speed, energy, temperature, data and more). Exact when the units are defined exactly.",
            Example = "convert(5, \"km\", \"mi\")",
            Evaluate = Convert,
        });
    }

    private static Expr? Convert(CallInfo c)
    {
        var catalog = UnitCatalog.Default;
        try
        {
            if (c.Count == 1)
            {
                if (c[0] is not StringExpr text) throw c.Error("write the conversion in quotes, for example convert(\"170 cm to ft in\")");
                var result = catalog.Evaluate(text.Value);
                return result.Parts.Count == 1 ? Value(result.Value, result.IsExact) : new StringExpr(result.Format());
            }
            if (c.Count != 3) throw c.Error("give a value, the unit it is in and the unit you want, for example convert(5, \"km\", \"mi\")");
            var from = UnitArg(c, 1, catalog);
            var to = UnitArg(c, 2, catalog);
            if (c[0] is not NumberExpr { Value.IsReal: true } n) throw c.Error("the value must be a real number");
            var value = n.Value.IsExact ? n.Value.ReQ : BigRational.FromDouble(n.Value.Re);
            var converted = catalog.Convert(value, from, to);
            return Value(converted.Value, converted.IsExact && n.Value.IsExact);
        }
        catch (ConversionException ex)
        {
            throw new CalcException("convert: " + ex.Message);
        }
        catch (KeyNotFoundException ex)
        {
            throw new CalcException("convert: " + ex.Message);
        }
    }

    private static Unit UnitArg(CallInfo c, int i, UnitCatalog catalog)
    {
        if (c[i] is not StringExpr s) throw c.Error($"write unit {i} in quotes, for example \"km\"");
        return catalog.Find(s.Value) ?? throw new CalcException($"convert: unknown unit \"{s.Value}\"");
    }

    private static Expr Value(BigRational v, bool exact) =>
        new NumberExpr(exact ? Number.Exact(v) : Number.Approx(v.ToDouble()));
}
