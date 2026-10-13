namespace Calcy.Python.Tests;

public class TracebackTests
{
    [Fact]
    public void UncaughtErrorHasCPythonStyleTraceback()
    {
        var (r, _) = PyRun.Run("""
def inner(x):
    return 10 / x

def outer():
    y = 0
    return inner(y)

print('start')
outer()
""");
        Assert.Equal(ExecutionStatus.Error, r.Status);
        var err = r.Error!;
        Assert.Equal("ZeroDivisionError", err.TypeName);
        Assert.Equal("division by zero", err.Message);
        Assert.Equal(2, err.Line);
        Assert.Equal("return 10 / x", err.SourceLine);
        Assert.Equal(new[] { 9, 6, 2 }, err.StackTrace.Select(f => f.Line).ToArray());
        Assert.Equal(new[] { "<module>", "outer", "inner" }, err.StackTrace.Select(f => f.FunctionName).ToArray());
        Assert.Equal("""
Traceback (most recent call last):
  File "<main>", line 9, in <module>
    outer()
  File "<main>", line 6, in outer
    return inner(y)
  File "<main>", line 2, in inner
    return 10 / x
ZeroDivisionError: division by zero

""".Replace("\r\n", "\n"), err.Traceback);
    }

    [Fact]
    public void LineNumbersInsideLoopsAndMethods()
    {
        var (r, _) = PyRun.Run("""
class Account:
    def __init__(self):
        self.balance = 0

    def withdraw(self, amount):
        if amount > self.balance:
            raise ValueError(f'insufficient funds: {self.balance}')
        self.balance -= amount

acct = Account()
for amount in [0, 5]:
    acct.withdraw(amount)
""");
        var err = r.Error!;
        Assert.Equal("ValueError", err.TypeName);
        Assert.Equal("insufficient funds: 0", err.Message);
        Assert.Equal(new[] { 12, 7 }, err.StackTrace.Select(f => f.Line).ToArray());
        Assert.Equal("withdraw", err.StackTrace[^1].FunctionName);
    }

    [Fact]
    public void ChainedExceptionsAreFormatted()
    {
        var (r, _) = PyRun.Run("""
try:
    {}['missing']
except KeyError as e:
    raise RuntimeError('lookup failed') from e
""");
        var tb = r.Error!.Traceback;
        Assert.Contains("KeyError: 'missing'", tb);
        Assert.Contains("The above exception was the direct cause of the following exception:", tb);
        Assert.EndsWith("RuntimeError: lookup failed\n", tb);

        var (r2, _) = PyRun.Run("""
try:
    1 / 0
except ZeroDivisionError:
    undefined_name
""");
        Assert.Contains("During handling of the above exception, another exception occurred:", r2.Error!.Traceback);
        Assert.Equal("NameError", r2.Error.TypeName);
        Assert.Equal("name 'undefined_name' is not defined", r2.Error.Message);
    }

    [Fact]
    public void UserExceptionTypeName()
    {
        var (r, _) = PyRun.Run("class BadThing(Exception):\n    pass\n\nraise BadThing('oops')");
        Assert.Equal("BadThing", r.Error!.TypeName);
        Assert.Equal(4, r.Error.Line);
        Assert.EndsWith("BadThing: oops\n", r.Error.Traceback);
    }

    [Fact]
    public void SyntaxErrorReportsLineColumnAndCaret()
    {
        var (r, _) = PyRun.Run("x = 1\nif x > 0\n    print(x)");
        var err = r.Error!;
        Assert.True(err.IsSyntaxError);
        Assert.Equal("SyntaxError", err.TypeName);
        Assert.Equal("expected ':'", err.Message);
        Assert.Equal(2, err.Line);
        Assert.Equal(9, err.Column);
        Assert.Equal("if x > 0", err.SourceLine);
        Assert.Equal("  File \"<main>\", line 2\n    if x > 0\n            ^\nSyntaxError: expected ':'\n", err.Traceback);
    }

    [Fact]
    public void IndentationErrorsAreReported()
    {
        var (r, _) = PyRun.Run("def f():\nreturn 1");
        Assert.Equal("IndentationError", r.Error!.TypeName);
        Assert.Equal("expected an indented block after function definition on line 1", r.Error.Message);
        Assert.Equal(2, r.Error.Line);

        var (r2, _) = PyRun.Run("if True:\n        a = 1\n    b = 2");
        Assert.Equal("IndentationError", r2.Error!.TypeName);
        Assert.Equal("unindent does not match any outer indentation level", r2.Error.Message);
    }

    [Fact]
    public void ErrorsInsideGeneratorsAndComprehensions()
    {
        var (r, _) = PyRun.Run("""
def gen():
    yield 1
    yield 1 / 0

total = sum(x for x in gen())
""");
        Assert.Equal("ZeroDivisionError", r.Error!.TypeName);
        Assert.Equal(new[] { 5, 5, 3 }, r.Error.StackTrace.Select(f => f.Line).ToArray());

        var (r2, _) = PyRun.Run("data = [1, 2, 0]\nvalues = [10 // d\n          for d in data]");
        Assert.Equal("ZeroDivisionError", r2.Error!.TypeName);
        Assert.Equal(2, r2.Error.Line);
    }

    [Fact]
    public void ExceptionInEvalReportsStringFile()
    {
        var (r, _) = PyRun.Run("exec('x = 1\\ny = x / 0')");
        Assert.Equal("ZeroDivisionError", r.Error!.TypeName);
        Assert.Equal("<string>", r.Error.StackTrace[^1].FileName);
        Assert.Equal(2, r.Error.StackTrace[^1].Line);
    }
}
