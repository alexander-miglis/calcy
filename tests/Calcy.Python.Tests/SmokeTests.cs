namespace Calcy.Python.Tests;

public class SmokeTests
{
    [Fact]
    public void HelloWorld()
    {
        Assert.Equal("hello 3\n", PyRun.Out("x = 1 + 2\nprint('hello', x)"));
    }

    [Fact]
    public void CubeRootsAreRoundedTheSameOnEverySystem()
    {
        // Math.Cbrt follows the C library; glibc can be one unit in the last place off (cbrt(27) gave 3.0000000000000004).
        Assert.Equal("3.0 -2.0 1.2599210498948732 1.4422495703074083 0.1\n",
            PyRun.Out("import math\nprint(math.cbrt(27), math.cbrt(-8), math.cbrt(2), math.cbrt(3), math.cbrt(0.001))"));
        Assert.Equal("True\n", PyRun.Out("import math\nprint(all(math.cbrt(n ** 3) == n for n in range(1, 20001)))"));
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
