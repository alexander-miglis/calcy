namespace Calcy.Python.Tests;

public class SmokeTests
{
    [Fact]
    public void HelloWorld()
    {
        Assert.Equal("hello 3\n", PyRun.Out("x = 1 + 2\nprint('hello', x)"));
    }

    [Fact]
    public void FunctionsAndLoops()
    {
        var code = """
def fib(n):
    a, b = 0, 1
    for _ in range(n):
        a, b = b, a + b
    return a

print([fib(i) for i in range(10)])
total = 0
i = 0
while i < 5:
    total += i
    i += 1
print(total)
""";
        Assert.Equal("[0, 1, 1, 2, 3, 5, 8, 13, 21, 34]\n10\n", PyRun.Out(code));
    }
}
