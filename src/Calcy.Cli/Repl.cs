using System.Globalization;
using Calcy.Core;
using Calcy.Core.Printing;
using Calcy.Engine;

namespace Calcy.Cli;

/// <summary>The interactive calculator: one line in, one result out, with : commands for settings and help.</summary>
internal sealed class Repl(TextReader input, TextWriter output, CliOptions options)
{
    private readonly CalcSession _session = CalcyEngine.CreateSession(options.Settings);
    private bool _latex = options.Latex;

    private const string Help = """
        Type an expression and press Enter. Examples:
          1/3 + 1/6          sqrt(72)          sin(pi/6)          2^100
          a = 5              f(x) = x^2 - 3x    f(a)               {1, 2, 3}^2
          solve(x^2 = 2, x)  [[1, 2], [3, 4]]^-1                  nCr(10, 3)

        Commands:
          :vars              list variables and functions      :clear [name]   remove one or all
          :funcs [search]    list built-in functions           :help NAME      help for one function
          :deg :rad :grad    angle unit                         :ti :casio      how 1/2x is read
          :exact :decimal    which form to show first           :digits N       decimal digits
          :latex             toggle LaTeX output                :settings       show the settings
          :q                 quit (or press Ctrl+D / Ctrl+Z)
        """;

    public int Run()
    {
        output.WriteLine($"Calcy {CliApp.Version}: exact maths in your terminal. Type :help for help, :q to quit.");
        while (true)
        {
            output.Write("> ");
            output.Flush();
            var line = input.ReadLine();
            if (line is null)
            {
                output.WriteLine();
                return 0;
            }
            line = line.Trim().TrimStart('\uFEFF').Trim();
            if (line.Length == 0) continue;
            if (line is "exit" or "quit" or ":q" or ":quit" or ":exit") return 0;
            if (line.StartsWith(':'))
            {
                Command(line);
                continue;
            }
            var r = _session.Evaluate(line);
            if (r.IsError)
            {
                if (r.ErrorPosition >= 0 && r.ErrorPosition <= line.Length)
                    output.WriteLine("  " + new string(' ', r.ErrorPosition) + "^");
                output.WriteLine("  error: " + r.ErrorMessage + (string.IsNullOrEmpty(r.Hint) ? "" : " (try: " + r.Hint + ")"));
                continue;
            }
            output.WriteLine("  " + CliApp.FormatResult(_session, r, options));
            if (_latex && r.Exact is not null) output.WriteLine("  latex: " + LatexPrinter.ToLatex(r.Exact));
        }
    }

    private void Command(string line)
    {
        var parts = line[1..].Split(' ', 2, StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        var name = parts.Length > 0 ? parts[0].ToLowerInvariant() : "";
        var arg = parts.Length > 1 ? parts[1] : "";
        var s = _session.Settings;
        switch (name)
        {
            case "help" or "h" or "?":
                if (arg.Length == 0) output.WriteLine(Help);
                else if (_session.Functions.TryGet(arg, out var f))
                {
                    output.WriteLine("  " + (f.Signature.Length > 0 ? f.Signature : f.Name));
                    output.WriteLine("  " + f.Description);
                    if (f.Example.Length > 0) output.WriteLine("  example: " + f.Example);
                    if (f.Aliases.Length > 0) output.WriteLine("  also: " + string.Join(", ", f.Aliases));
                }
                else output.WriteLine($"  There is no function called '{arg}'. Try :funcs {arg}");
                break;
            case "vars":
                if (_session.Env.Variables.Count == 0 && _session.Env.Functions.Count == 0) output.WriteLine("  Nothing defined yet. Try a = 5 or f(x) = x^2.");
                foreach (var (n, v) in _session.Env.Variables.OrderBy(kv => kv.Key, StringComparer.Ordinal))
                    output.WriteLine($"  {n} = {_session.Print(v, !options.Ascii)}");
                foreach (var (n, fn) in _session.Env.Functions.OrderBy(kv => kv.Key, StringComparer.Ordinal))
                    output.WriteLine($"  {n}({string.Join(", ", fn.Parameters)}) = {_session.Print(fn.Body, !options.Ascii)}");
                break;
            case "clear":
                if (arg.Length == 0)
                {
                    _session.Env.Clear();
                    output.WriteLine("  Cleared all variables and functions.");
                }
                else output.WriteLine(_session.Env.Remove(arg) ? $"  Removed {arg}." : $"  {arg} is not defined.");
                break;
            case "funcs" or "functions":
                new CliApp(TextReader.Null, output, output).ListFunctions(_session, arg);
                break;
            case "deg": s.Angle = AngleMode.Degree; output.WriteLine("  Angles in degrees."); break;
            case "rad": s.Angle = AngleMode.Radian; output.WriteLine("  Angles in radians."); break;
            case "grad": s.Angle = AngleMode.Gradian; output.WriteLine("  Angles in gradians."); break;
            case "ti": s.Implicit = ImplicitMultiplication.LeftToRight; output.WriteLine("  1/2x now means x/2 (TI and Desmos)."); break;
            case "casio": s.Implicit = ImplicitMultiplication.BindsTighter; output.WriteLine("  1/2x now means 1/(2x) (Casio)."); break;
            case "exact":
                s.Results = ResultPreference.ExactFirst;
                options.DecimalOnly = false;
                output.WriteLine("  Exact results first.");
                break;
            case "decimal":
                s.Results = ResultPreference.DecimalFirst;
                options.DecimalOnly = true;
                output.WriteLine("  Decimal results.");
                break;
            case "digits":
                if (int.TryParse(arg, NumberStyles.Integer, CultureInfo.InvariantCulture, out var d) && d is >= 1 and <= 30)
                {
                    s.Digits = d;
                    output.WriteLine($"  Showing {d} significant digits.");
                }
                else output.WriteLine("  Usage: :digits N (1 to 30)");
                break;
            case "latex":
                _latex = !_latex;
                output.WriteLine(_latex ? "  LaTeX on." : "  LaTeX off.");
                break;
            case "settings":
                output.WriteLine($"  angle: {s.Angle}, 1/2x: {(s.Implicit == ImplicitMultiplication.BindsTighter ? "1/(2x)" : "x/2")}, results: {s.Results}, digits: {s.Digits}, complex: {s.Complex}");
                break;
            default:
                output.WriteLine($"  Unknown command :{name}. Type :help for the list.");
                break;
        }
    }
}
