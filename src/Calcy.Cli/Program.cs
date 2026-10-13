using System.Text;
using Calcy.Cli;

var utf8 = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);
bool mcp = args.Contains("mcp");

TextReader input;
TextWriter output;
if (mcp)
{
    // MCP stdio: raw UTF-8 streams, "\n" line endings, nothing but protocol messages on stdout.
    input = new StreamReader(Console.OpenStandardInput(), utf8);
    output = new StreamWriter(Console.OpenStandardOutput(), utf8) { NewLine = "\n", AutoFlush = false };
}
else
{
    try
    {
        Console.OutputEncoding = utf8;
        Console.InputEncoding = utf8;
    }
    catch (IOException)
    {
        // Some hosts do not allow changing the console encoding.
    }
    input = Console.In;
    output = Console.Out;
}

using var cts = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) =>
{
    if (!mcp) return;
    e.Cancel = true;
    cts.Cancel();
};

var app = new CliApp(input, output, Console.Error);
int code = await app.RunAsync(args, interactive: !Console.IsInputRedirected, cts.Token);
await output.FlushAsync();
return code;
