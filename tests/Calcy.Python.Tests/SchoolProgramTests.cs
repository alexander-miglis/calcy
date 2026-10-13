namespace Calcy.Python.Tests;

public class SchoolProgramTests
{
    [Fact]
    public void GuessingGameWithScriptedInput()
    {
        const string code = """
import random

random.seed(2024)
secret = random.randint(1, 100)
tries = 0
while True:
    guess = int(input('Guess a number between 1 and 100: '))
    tries += 1
    if guess < secret:
        print('Too low!')
    elif guess > secret:
        print('Too high!')
    else:
        print(f'Correct! The number was {secret}. You needed {tries} tries.')
        break
""";
        // binary search driven by the program's own feedback
        int lo = 1, hi = 100, guesses = 0;
        var engine = new PythonEngine();
        var transcript = new System.Text.StringBuilder();
        string last = "";
        engine.Output = s =>
        {
            transcript.Append(s);
            if (!s.StartsWith("Guess")) last = s;
        };
        int current = 0;
        engine.Input = prompt =>
        {
            if (last.StartsWith("Too low")) lo = current + 1;
            else if (last.StartsWith("Too high")) hi = current - 1;
            current = (lo + hi) / 2;
            guesses++;
            return current.ToString();
        };
        var r = engine.Execute(code);
        Assert.True(r.Success, r.ToString());
        Assert.Contains("Correct! The number was", transcript.ToString());
        Assert.InRange(guesses, 1, 7);
        Assert.Equal((long)current, engine.GetGlobal("secret"));
    }

    [Fact]
    public void AverageCalculatorWithInput()
    {
        const string code = """
n = int(input('How many numbers? '))
total = 0.0
for i in range(n):
    total += float(input(f'Number {i + 1}: '))
print(f'Average: {total / n:.2f}')
""";
        var output = PyRun.Out(code, inputs: new[] { "3", "4", "5.5", "10" });
        Assert.Equal("How many numbers? Number 1: Number 2: Number 3: Average: 6.50\n", output);
    }

    [Fact]
    public void InvalidInputIsHandledWithTryExcept()
    {
        const string code = """
while True:
    text = input('Enter an integer: ')
    try:
        value = int(text)
        break
    except ValueError:
        print(f'{text!r} is not an integer')
print('square:', value ** 2)
""";
        var output = PyRun.Out(code, new PythonEngineOptions { EchoInputPrompt = false }, new[] { "abc", "4.5", "12" });
        Assert.Equal("'abc' is not an integer\n'4.5' is not an integer\nsquare: 144\n", output);
    }

    [Fact]
    public void QuadraticSolverWithComplexRoots()
    {
        const string code = """
import cmath

def solve(a, b, c):
    d = cmath.sqrt(b * b - 4 * a * c)
    return (-b + d) / (2 * a), (-b - d) / (2 * a)

for coeffs in [(1, -3, 2), (1, 2, 5)]:
    r1, r2 = solve(*coeffs)
    print(r1, r2)
""";
        Assert.Equal("(2+0j) (1+0j)\n(-1+2j) (-1-2j)\n", PyRun.Out(code));
    }

    [Fact]
    public void PrimeFactorizationAndTable()
    {
        const string code = """
def factors(n):
    out = []
    d = 2
    while d * d <= n:
        while n % d == 0:
            out.append(d)
            n //= d
        d += 1
    if n > 1:
        out.append(n)
    return out

for n in [12, 97, 360, 2 ** 31 - 1]:
    print(f'{n:>10} = {" x ".join(map(str, factors(n)))}')
""";
        Assert.Equal("        12 = 2 x 2 x 3\n        97 = 97\n       360 = 2 x 2 x 2 x 3 x 3 x 5\n2147483647 = 2147483647\n", PyRun.Out(code));
    }

    [Fact]
    public void TurtleLikeDrawingWithStringGrid()
    {
        const string code = """
size = 5
grid = [[' '] * size for _ in range(size)]
for i in range(size):
    grid[i][i] = '\\'
    grid[i][size - 1 - i] = '/'
grid[size // 2][size // 2] = 'X'
print('\n'.join(''.join(row) for row in grid))
""";
        Assert.Equal("\\   /\n \\ / \n  X  \n / \\ \n/   \\\n", PyRun.Out(code));
    }
}
