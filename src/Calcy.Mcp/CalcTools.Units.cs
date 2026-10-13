using System.Text.Json.Nodes;
using Calcy.Core.Expressions;
using Calcy.Core.Numbers;
using Calcy.Core.Printing;
using Calcy.Units;
using static Calcy.Mcp.Schema;

namespace Calcy.Mcp;

/// <summary>Unit conversion and date arithmetic tools.</summary>
public sealed partial class CalcTools
{
    private McpTool UnitConvert() => new()
    {
        Name = "unit_convert",
        Title = "Convert units",
        Description =
            "Converts between units: length, area, volume, mass, time, speed, acceleration, force, pressure, energy, power, temperature, angle, frequency, " +
            "data, density, fuel economy, electrical and cooking units, with SI and binary prefixes. Either a query such as \"170 cm to ft in\" or " +
            "\"3 ft 4 in to cm\", or value, from and to. Results are exact when both units are defined exactly.",
        InputSchema = Object(
            Opt("query", String("A whole conversion, for example \"60 mph to km/h\" or \"98.6 F to C\".")),
            Opt("value", Expression("The amount, for example 5 or \"1/3\".")),
            Opt("from", String("The unit of the value, for example km, °F, kWh, GiB.")),
            Opt("to", String("The unit wanted."))),
        Handler = c => Guard(() =>
        {
            var catalog = UnitCatalog.Default;
            ConversionResult result;
            try
            {
                if (c.Has("query")) result = catalog.Evaluate(c.String("query"));
                else
                {
                    var from = catalog.Find(c.String("from")) ?? throw new ToolArgumentException($"Unknown unit '{c.String("from")}'.");
                    var to = catalog.Find(c.String("to")) ?? throw new ToolArgumentException($"Unknown unit '{c.String("to")}'.");
                    var v = Core.Evaluation.Evaluator.Eval(Parse(c, "value"), Session.Context(false, c.Token));
                    if (v is not NumberExpr { Value.IsReal: true } n) throw new ToolArgumentException("value must be a real number.");
                    var q = n.Value.IsExact ? n.Value.ReQ : BigRational.FromDouble(n.Value.Re);
                    result = catalog.Convert(q, from, to);
                }
            }
            catch (ConversionException ex)
            {
                return ToolResult.Error(ex.Message);
            }
            var exactValue = new NumberExpr(result.IsExact ? Number.Exact(result.Value) : Number.Approx(result.Value.ToDouble()));
            var o = new JsonObject
            {
                ["result"] = result.Format(),
                ["description"] = result.Describe(),
                ["value"] = Brief(exactValue, c.Token),
                ["unit"] = result.Target.Symbol,
                ["exact"] = result.IsExact,
            };
            if (result.Category is { } cat) o["category"] = cat.Name;
            try
            {
                o["formula"] = catalog.Formula(result.Input.Unit, result.Target);
            }
            catch (ConversionException)
            {
                // Mixed or unusual targets have no single formula.
            }
            return ToolResult.Data(o, result.Describe());
        }),
    };

    private McpTool DateCalc() => new()
    {
        Name = "date_calc",
        Title = "Dates and durations",
        Description =
            "Date arithmetic: days, weeks, weekdays and years-months-days between two dates (from and to, as YYYY-MM-DD), or a request such as " +
            "\"2026-10-13 + 100 days\", \"today - 6 weeks\" or \"1:30:00 + 45 min\". Month and year steps stop at the end of short months.",
        InputSchema = Object(
            Opt("request", String("A date or duration request.")),
            Opt("from", String("Start date (YYYY-MM-DD or today).")),
            Opt("to", String("End date (YYYY-MM-DD or today)."))),
        Handler = c => Guard(() =>
        {
            DateRequest request;
            if (c.Has("request"))
            {
                if (!DateTools.TryParseRequest(c.String("request"), out var parsed))
                    return ToolResult.Error("Could not read that request. Examples: \"2026-10-13 + 100 days\", \"2026-01-01 to 2026-12-25\", \"1:30:00 + 45 min\".");
                request = parsed;
            }
            else
            {
                if (!DateTools.TryParseDate(c.String("from"), out var a)) return ToolResult.Error("from must be a date like 2026-10-13.");
                if (!DateTools.TryParseDate(c.String("to"), out var b)) return ToolResult.Error("to must be a date like 2026-10-13.");
                request = new DateRequest.Between(a, b);
            }
            JsonObject o;
            switch (request)
            {
                case DateRequest.Between(var from, var to):
                {
                    int days = DateTools.DaysBetween(from, to);
                    var (weeks, rest) = DateTools.WeeksAndDays(Math.Abs(days));
                    var span = DateTools.YearsMonthsDays(from, to);
                    o = new JsonObject
                    {
                        ["from"] = DateTools.Format(from),
                        ["to"] = DateTools.Format(to),
                        ["days"] = days,
                        ["weeks_and_days"] = $"{weeks} weeks {rest} days",
                        ["weekdays"] = DateTools.Weekdays(from, to),
                        ["years_months_days"] = span.ToString(),
                    };
                    break;
                }
                case DateRequest.Shift(var date, var amount, var step):
                {
                    var result = DateTools.Add(date, amount, step);
                    o = new JsonObject
                    {
                        ["date"] = DateTools.Format(result),
                        ["day"] = DateTools.DayName(result),
                        ["iso_week"] = DateTools.IsoWeekText(result),
                        ["start"] = DateTools.Format(date),
                    };
                    break;
                }
                case DateRequest.Durations(var first, var second, var subtract):
                {
                    var d = DateTools.Combine(first, second, subtract);
                    o = new JsonObject { ["duration"] = DateTools.FormatDuration(d), ["description"] = DateTools.DescribeDuration(d), ["seconds"] = d.TotalSeconds };
                    break;
                }
                default:
                    return ToolResult.Error("That kind of date request is not supported.");
            }
            return ToolResult.Data(o, o.First().Value?.ToString() ?? "date");
        }),
    };
}
