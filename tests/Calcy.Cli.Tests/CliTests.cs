using System.Text.Json.Nodes;
using Calcy.Cli;

namespace Calcy.Cli.Tests;

public class CliTests
{
    private static async Task<(int Code, string Out, string Err)> Run(string stdin, bool interactive, params string[] args)
    {
        var output = new StringWriter();
        var error = new StringWriter();
        var app = new CliApp(new StringReader(stdin), output, error);
        int code = await app.RunAsync(args, interactive);
        return (code, output.ToString().Replace("\r\n", "\n"), error.ToString().Replace("\r\n", "\n"));
    }

    [Fact]
    public async Task Eval_PrintsExactAndDecimal()
    {
        var (code, output, _) = await Run("", false, "eval", "1/3 + 1/6", "sqrt(72)", "2^64");
        Assert.Equal(0, code);
        Assert.Equal("1/2 ≈ 0.5\n6√2 ≈ 8.485281374\n18446744073709551616\n", output);
    }

    [Fact]
    public async Task Eval_KeepsDefinitionsBetweenExpressions()
    {
        var (code, output, _) = await Run("", false, "eval", "a = 5", "f(x) = x^2 - 5x + 6", "f(a)");
        Assert.Equal(0, code);
        Assert.Equal("a = 5\nf(x) = x² - 5x + 6\n6\n", output);
    }

    [Fact]
    public async Task Eval_ErrorsGoToStderrWithACaret()
    {
        var (code, output, err) = await Run("", false, "eval", "1 + (2");
        Assert.Equal(1, code);
        Assert.Equal("", output);
        Assert.Contains("^", err);
        Assert.Contains("A bracket is still open", err);
        Assert.Contains("try: 1 + (2)", err);
    }

    [Fact]
    public async Task Eval_Options()
    {
        var (_, deg, _) = await Run("", false, "eval", "sin(30)", "--deg");
        Assert.Equal("1/2 ≈ 0.5\n", deg);
        var (_, ti, _) = await Run("", false, "--ti", "eval", "1/2x");
        Assert.Equal("x/2 ≈ 0.5x\n", ti);
        var (_, dec, _) = await Run("", false, "eval", "1/3", "--decimal");
        Assert.Equal("0.3333333333\n", dec);
        var (_, ascii, _) = await Run("", false, "eval", "sqrt(8)", "--ascii", "--exact");
        Assert.Equal("2*sqrt(2)\n", ascii);
    }

    [Fact]
    public async Task Eval_Json()
    {
        var (code, output, _) = await Run("", false, "eval", "1/4", "1 +", "--json");
        Assert.Equal(1, code);
        var arr = JsonNode.Parse(output)!.AsArray();
        Assert.Equal("1/4", (string)arr[0]!["result"]!);
        Assert.Equal("0.25", (string)arr[0]!["decimal"]!);
        Assert.Equal(@"\frac{1}{4}", (string)arr[0]!["latex"]!);
        Assert.NotNull(arr[1]!["error"]);
    }

    [Fact]
    public async Task BareExpression_WorksLikeEval()
    {
        var (code, output, _) = await Run("", false, "2 + 2");
        Assert.Equal(0, code);
        Assert.Equal("4\n", output);
        var (_, negative, _) = await Run("", false, "-5 + 3");
        Assert.Equal("-2\n", negative);
    }

    [Fact]
    public async Task PipedInput_EvaluatesEachLine()
    {
        var (code, output, _) = await Run("r = 3\n# a comment\n\npi r^2\n", false);
        Assert.Equal(0, code);
        Assert.Equal("r = 3\n9π ≈ 28.27433388\n", output);
    }

    [Fact]
    public async Task Repl_CommandsAndResults()
    {
        var script = string.Join("\n", ":deg", "sin(30)", "a = 2", ":vars", ":help nCr", "1 +", ":latex", "1/2", ":nope", ":q", "never");
        var (code, output, _) = await Run(script, true);
        Assert.Equal(0, code);
        Assert.Contains("Angles in degrees.", output);
        Assert.Contains("  1/2 ≈ 0.5", output);
        Assert.Contains("  a = 2", output);
        Assert.Contains("nCr(n, r)", output);
        Assert.Contains("error:", output);
        Assert.Contains(@"latex: \frac{1}{2}", output);
        Assert.Contains("Unknown command :nope", output);
        Assert.DoesNotContain("never", output);
    }

    [Fact]
    public async Task Functions_Search()
    {
        var (code, output, _) = await Run("", false, "functions", "prime");
        Assert.Equal(0, code);
        Assert.Contains("isprime", output);
        Assert.DoesNotContain("sinh", output);
    }

    [Fact]
    public async Task Mcp_ServesOverStdio()
    {
        var input = """{"jsonrpc":"2.0","id":1,"method":"tools/call","params":{"name":"evaluate","arguments":{"expression":"sqrt(2)^2"}}}""" + "\n";
        var (code, output, err) = await Run(input, false, "mcp", "--read-only");
        Assert.Equal(0, code);
        Assert.Contains("MCP server ready", err);
        Assert.Contains("read-only", err);
        var response = JsonNode.Parse(output.Trim())!;
        Assert.Equal("2", (string)response["result"]!["structuredContent"]!["result"]!);
    }

    [Fact]
    public async Task Mcp_AuditLogIsWritten()
    {
        var dir = Path.Combine(Path.GetTempPath(), "calcy-cli-tests-" + Guid.NewGuid().ToString("N"));
        var log = Path.Combine(dir, "audit.jsonl");
        try
        {
            var input = """{"jsonrpc":"2.0","id":1,"method":"tools/call","params":{"name":"evaluate","arguments":{"expression":"6*7"}}}""" + "\n";
            var (code, _, _) = await Run(input, false, "mcp", "--audit-log", log);
            Assert.Equal(0, code);
            var line = JsonNode.Parse(File.ReadAllLines(log).Single())!;
            Assert.Equal("evaluate", (string)line["tool"]!);
            Assert.Equal("42", (string)line["summary"]!);
        }
        finally
        {
            if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public async Task McpConfig_PrintsClientConfiguration()
    {
        var (code, output, _) = await Run("", false, "mcp-config");
        Assert.Equal(0, code);
        Assert.Contains("\"mcpServers\"", output);
        Assert.Contains("claude mcp add calcy", output);
    }

    [Fact]
    public async Task BadOptions_ExitWithUsageHint()
    {
        var (code, _, err) = await Run("", false, "eval", "1", "--digits", "abc");
        Assert.Equal(2, code);
        Assert.Contains("--digits", err);
        var (code2, _, err2) = await Run("", false, "--frobnicate");
        Assert.Equal(2, code2);
        Assert.Contains("unknown command", err2);
    }

    [Fact]
    public async Task HelpAndVersion()
    {
        var (_, help, _) = await Run("", false, "--help");
        Assert.Contains("calcy-cli mcp", help);
        var (_, version, _) = await Run("", false, "--version");
        Assert.StartsWith("calcy-cli ", version);
    }
}
