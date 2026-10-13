using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Calcy.Core;
using Calcy.Core.Printing;
using Calcy.Engine;
using Calcy.Mcp;

namespace Calcy.Cli;

/// <summary>Options shared by every command.</summary>
internal sealed class CliOptions
{
    public bool Json { get; set; }
    public bool Ascii { get; set; }
    public bool DecimalOnly { get; set; }
    public bool ExactOnly { get; set; }
    public bool Latex { get; set; }
    public CalcSettings Settings { get; } = new();

    // mcp
    public bool ReadOnly { get; set; }
    public bool AllowCode { get; set; }
    public HashSet<string> Disabled { get; } = new(StringComparer.Ordinal);
    public int TimeoutSeconds { get; set; } = 30;
    public string? AuditLog { get; set; }
}

/// <summary>
/// The calcy-cli command line: an interactive REPL, one-shot evaluation for scripts, a function catalogue and the
/// MCP stdio server. Everything takes its streams as parameters so it can be tested without a console.
/// </summary>
public sealed class CliApp(TextReader input, TextWriter output, TextWriter error)
{
    /// <summary>The product version, including a pre-release suffix such as 0.2.0-pre.14 (build metadata after + is dropped).</summary>
    public static string Version =>
        (typeof(CliApp).Assembly.GetCustomAttributes(typeof(System.Reflection.AssemblyInformationalVersionAttribute), false)
            .OfType<System.Reflection.AssemblyInformationalVersionAttribute>().FirstOrDefault()?.InformationalVersion ?? "0.0.0").Split('+')[0];

    private const string Usage = """
        calcy-cli: exact maths in the terminal, and an MCP server for AI agents.

        Usage:
          calcy-cli                     interactive calculator (reads lines from stdin when piped)
          calcy-cli eval EXPR [EXPR...] evaluate expressions in one session and print the results
          calcy-cli functions [SEARCH]  list built-in functions
          calcy-cli mcp [OPTIONS]       run the MCP server on stdin/stdout
          calcy-cli mcp-config          print the configuration to add Calcy to an MCP client
          calcy-cli bug                 how to report a bug, with the details to include
          calcy-cli --version

        Calculation options:
          --deg | --rad | --grad        angle unit (default radians)
          --ti                          implied multiplication like TI and Desmos: 1/2x = x/2 (default Casio: 1/(2x))
          --digits N                    significant digits for decimals (default 10)
          --decimal                     print decimals only
          --exact                       print exact results only
          --latex                       also print LaTeX
          --ascii                       plain ASCII output (sqrt instead of the root sign, pi instead of the letter)
          --json                        machine-readable output (eval)

        MCP options:
          --read-only                   refuse tools that change variables or settings
          --disable TOOL[,TOOL...]      hide tools
          --timeout SECONDS             time limit per tool call (default 30)
          --audit-log FILE              append one JSON line per tool call to FILE
        """;

    /// <summary>Runs the command line and returns the process exit code.</summary>
    public async Task<int> RunAsync(string[] args, bool interactive, CancellationToken ct = default)
    {
        var options = new CliOptions();
        var rest = new List<string>();
        try
        {
            ParseOptions(args, options, rest);
        }
        catch (ArgumentException ex)
        {
            error.WriteLine("calcy-cli: " + ex.Message);
            error.WriteLine("Run calcy-cli --help for usage.");
            return 2;
        }
        string command = rest.Count > 0 ? rest[0] : "";
        switch (command)
        {
            case "--help" or "-h" or "help" or "/?":
                output.WriteLine(Usage);
                return 0;
            case "--version" or "-v" or "version":
                output.WriteLine("calcy-cli " + Version);
                return 0;
            case "eval" or "-e":
                if (rest.Count < 2)
                {
                    error.WriteLine("calcy-cli: eval needs at least one expression, for example: calcy-cli eval \"1/3 + 1/6\"");
                    return 2;
                }
                return Eval(rest.Skip(1), options);
            case "functions" or "funcs":
                ListFunctions(CalcyEngine.CreateSession(options.Settings), string.Join(' ', rest.Skip(1)));
                return 0;
            case "mcp":
                return await RunMcpAsync(options, ct).ConfigureAwait(false);
            case "mcp-config":
                PrintMcpConfig();
                return 0;
            case "bug" or "report-bug":
                PrintBugReport();
                return 0;
            case "":
                return interactive ? new Repl(input, output, options).Run() : Eval(ReadLines(input), options);
            default:
                // calcy-cli "2 + 2" works like eval.
                if (!command.StartsWith("--", StringComparison.Ordinal)) return Eval(rest, options);
                error.WriteLine($"calcy-cli: unknown command '{command}'. Run calcy-cli --help for usage.");
                return 2;
        }
    }

    private static IEnumerable<string> ReadLines(TextReader r)
    {
        while (r.ReadLine() is { } line) yield return line;
    }

    internal static void ParseOptions(string[] args, CliOptions o, List<string> rest)
    {
        for (int i = 0; i < args.Length; i++)
        {
            var a = args[i];
            string Next() => i + 1 < args.Length ? args[++i] : throw new ArgumentException($"{a} needs a value");
            switch (a)
            {
                case "--json": o.Json = true; break;
                case "--ascii": o.Ascii = true; break;
                case "--decimal": o.DecimalOnly = true; o.Settings.Results = ResultPreference.DecimalFirst; break;
                case "--exact": o.ExactOnly = true; break;
                case "--latex": o.Latex = true; break;
                case "--deg": o.Settings.Angle = AngleMode.Degree; break;
                case "--rad": o.Settings.Angle = AngleMode.Radian; break;
                case "--grad": o.Settings.Angle = AngleMode.Gradian; break;
                case "--ti": o.Settings.Implicit = ImplicitMultiplication.LeftToRight; break;
                case "--casio": o.Settings.Implicit = ImplicitMultiplication.BindsTighter; break;
                case "--digits":
                    if (!int.TryParse(Next(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var d) || d < 1 || d > 30)
                        throw new ArgumentException("--digits must be a whole number from 1 to 30");
                    o.Settings.Digits = d;
                    break;
                case "--read-only": o.ReadOnly = true; break;
                case "--allow-code": o.AllowCode = true; break;
                case "--disable":
                    foreach (var t in Next().Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)) o.Disabled.Add(t);
                    break;
                case "--timeout":
                    if (!int.TryParse(Next(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var s) || s < 1 || s > 3600)
                        throw new ArgumentException("--timeout must be a number of seconds from 1 to 3600");
                    o.TimeoutSeconds = s;
                    break;
                case "--audit-log": o.AuditLog = Next(); break;
                default: rest.Add(a); break;
            }
        }
    }

    private int Eval(IEnumerable<string> lines, CliOptions o)
    {
        var session = CalcyEngine.CreateSession(o.Settings);
        int exit = 0;
        var json = new JsonArray();
        foreach (var raw in lines)
        {
            // Windows tools often start piped text with a byte order mark.
            var line = raw.Trim().TrimStart('\uFEFF').Trim();
            if (line.Length == 0 || line.StartsWith('#')) continue;
            var r = session.Evaluate(line);
            if (o.Json)
            {
                json.Add(ResultJson(session, r));
                if (r.IsError) exit = 1;
                continue;
            }
            if (r.IsError)
            {
                exit = 1;
                error.WriteLine($"error: {line}");
                if (r.ErrorPosition >= 0 && r.ErrorPosition <= line.Length)
                    error.WriteLine("       " + new string(' ', r.ErrorPosition) + "^");
                error.WriteLine("       " + r.ErrorMessage + (string.IsNullOrEmpty(r.Hint) ? "" : " (try: " + r.Hint + ")"));
                continue;
            }
            output.WriteLine(FormatResult(session, r, o));
            if (o.Latex && r.Exact is not null) output.WriteLine("latex: " + LatexPrinter.ToLatex(r.Exact));
        }
        if (o.Json) output.WriteLine(json.ToJsonString(JsonOptions));
        return exit;
    }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        WriteIndented = true,
    };

    internal static JsonObject ResultJson(CalcSession s, CalcResult r)
    {
        var o = new JsonObject { ["input"] = r.Input };
        if (r.IsError)
        {
            o["error"] = r.ErrorMessage;
            if (r.ErrorPosition >= 0) o["position"] = r.ErrorPosition;
            if (r.Hint is not null) o["hint"] = r.Hint;
            return o;
        }
        o["kind"] = r.Kind switch { ResultKind.Assignment => "assignment", ResultKind.FunctionDefinition => "function", _ => "value" };
        if (r.Name is not null) o["name"] = r.Name;
        if (r.Exact is not null)
        {
            o["result"] = s.Print(r.Exact);
            o["input_form"] = ExprPrinter.ToInput(r.Exact);
            o["latex"] = LatexPrinter.ToLatex(r.Exact);
        }
        if (r.Approx is not null) o["decimal"] = s.Print(r.Approx);
        return o;
    }

    /// <summary>One line for a result: "1/2 ≈ 0.5", "a = 5" or "f(x) = x² + 1".</summary>
    internal static string FormatResult(CalcSession s, CalcResult r, CliOptions o)
    {
        bool unicode = !o.Ascii;
        string Print(Core.Expressions.Expr e) => s.Print(e, unicode);
        switch (r.Kind)
        {
            case ResultKind.FunctionDefinition:
                return $"{r.Name}({string.Join(", ", r.Parameters)}) = {(r.Exact is null ? "" : Print(r.Exact))}";
            case ResultKind.Assignment:
                return $"{r.Name} = {Value(r)}";
            default:
                return Value(r);
        }

        string Value(CalcResult res)
        {
            if (res.Exact is null) return "";
            string exact = Print(res.Exact);
            string? approx = res.Approx is null ? null : Print(res.Approx);
            if (approx is null || approx == exact) return exact;
            if (o.DecimalOnly) return approx;
            if (o.ExactOnly) return exact;
            string approxSign = unicode ? " ≈ " : " ~ ";
            return res.ExactIsNice ? exact + approxSign + approx : approx + approxSign + exact;
        }
    }

    internal void ListFunctions(CalcSession session, string search)
    {
        var q = search.Trim();
        var groups = session.Functions.All
            .Where(f => !f.Hidden)
            .Where(f => q.Length == 0 || f.Name.Contains(q, StringComparison.OrdinalIgnoreCase) || f.Description.Contains(q, StringComparison.OrdinalIgnoreCase)
                        || f.Category.Contains(q, StringComparison.OrdinalIgnoreCase) || f.Aliases.Any(a => a.Contains(q, StringComparison.OrdinalIgnoreCase)))
            .GroupBy(f => f.Category)
            .OrderBy(g => g.Key);
        int count = 0;
        foreach (var g in groups)
        {
            output.WriteLine(g.Key.ToUpperInvariant());
            foreach (var f in g.OrderBy(f => f.Name))
            {
                var sig = f.Signature.Length > 0 ? f.Signature : f.Name;
                output.WriteLine($"  {sig,-34} {f.Description}");
                count++;
            }
            output.WriteLine();
        }
        if (count == 0) output.WriteLine($"No functions match '{q}'.");
    }

    private async Task<int> RunMcpAsync(CliOptions o, CancellationToken ct)
    {
        StreamWriter? audit = null;
        var auditGate = new object();
        try
        {
            if (o.AuditLog is not null)
            {
                var full = Path.GetFullPath(o.AuditLog);
                Directory.CreateDirectory(Path.GetDirectoryName(full)!);
                audit = new StreamWriter(new FileStream(full, FileMode.Append, FileAccess.Write, FileShare.Read), new UTF8Encoding(false)) { AutoFlush = true };
            }
            var options = new McpServerOptions
            {
                ReadOnly = o.ReadOnly,
                AllowCode = o.AllowCode,
                DisabledTools = o.Disabled,
                ToolTimeout = TimeSpan.FromSeconds(o.TimeoutSeconds),
                Log = error,
                Audit = audit is null ? null : e =>
                {
                    var line = new JsonObject
                    {
                        ["time"] = e.Time.ToString("o", CultureInfo.InvariantCulture),
                        ["client"] = e.Client,
                        ["tool"] = e.Tool,
                        ["arguments"] = e.Arguments,
                        ["error"] = e.IsError,
                        ["summary"] = e.Summary,
                        ["ms"] = (long)e.Duration.TotalMilliseconds,
                    }.ToJsonString(new JsonSerializerOptions { Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping });
                    lock (auditGate) audit.WriteLine(line);
                },
            };
            var server = CalcyMcp.CreateServer(options, CalcyEngine.CreateSession(o.Settings));
            error.WriteLine($"calcy-cli {Version}: MCP server ready on stdin/stdout ({server.VisibleTools.Count()} tools{(o.ReadOnly ? ", read-only" : "")}).");
            await server.RunAsync(input, output, ct).ConfigureAwait(false);
            return 0;
        }
        catch (IOException ex)
        {
            error.WriteLine("calcy-cli: " + ex.Message);
            return 1;
        }
        catch (UnauthorizedAccessException ex)
        {
            error.WriteLine("calcy-cli: " + ex.Message);
            return 1;
        }
        finally
        {
            audit?.Dispose();
        }
    }

    /// <summary>Where to report bugs, with a pre-filled form link and the details to paste.</summary>
    private void PrintBugReport()
    {
        string system = $"{System.Runtime.InteropServices.RuntimeInformation.OSDescription.Trim()} ({System.Runtime.InteropServices.RuntimeInformation.OSArchitecture})";
        string url = "https://github.com/alexander-miglis/calcy/issues/new?template=bug_report.yml" +
                     $"&version={Uri.EscapeDataString("calcy-cli " + Version)}&os={Uri.EscapeDataString(system)}&mode=cli";
        output.WriteLine("Report a bug (nothing is sent until you submit the form):");
        output.WriteLine("  " + url);
        output.WriteLine();
        output.WriteLine("Details to include:");
        output.WriteLine($"  calcy-cli {Version}");
        output.WriteLine($"  System: {system}");
    }

    private void PrintMcpConfig()
    {
        var (command, args) = SelfCommand();
        var config = new JsonObject
        {
            ["mcpServers"] = new JsonObject
            {
                ["calcy"] = new JsonObject
                {
                    ["command"] = command,
                    ["args"] = new JsonArray([.. args.Append("mcp").Select(a => (JsonNode)a)]),
                },
            },
        };
        output.WriteLine("Add this to your MCP client configuration (Claude Desktop: claude_desktop_config.json; VS Code and Cursor: mcp.json):");
        output.WriteLine();
        output.WriteLine(config.ToJsonString(JsonOptions));
        output.WriteLine();
        output.WriteLine("For Claude Code, run:");
        output.WriteLine("  claude mcp add calcy -- " + string.Join(' ', new[] { command }.Concat(args).Append("mcp").Select(Quote)));
    }

    private static (string Command, string[] Args) SelfCommand()
    {
        var process = Environment.ProcessPath ?? "calcy-cli";
        if (Path.GetFileNameWithoutExtension(process).Equals("dotnet", StringComparison.OrdinalIgnoreCase))
            return (process, [Path.Combine(AppContext.BaseDirectory, "calcy-cli.dll")]);
        return (process, []);
    }

    private static string Quote(string s) => s.Contains(' ') ? "\"" + s + "\"" : s;
}
