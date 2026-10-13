namespace Calcy.Modes.Solver.Logic;

/// <summary>The inputs of the ANY EQUATION tool.</summary>
public sealed class EquationRequest
{
    /// <summary>The equation as linear Calcy input (an expression alone means expression = 0).</summary>
    public string Equation { get; set; } = "cos(x) = x/3";

    /// <summary>The unknown picked by the user, or null to pick it automatically.</summary>
    public string? Unknown { get; set; }

    /// <summary>A search range such as [-10, 10]; empty solves exactly over all real numbers.</summary>
    public string Range { get; set; } = "[-10, 10]";

    /// <summary>Include complex solutions.</summary>
    public bool Complex { get; set; }

    /// <summary>Show the step-by-step explanation.</summary>
    public bool ShowSteps { get; set; }

    /// <summary>A copy.</summary>
    public EquationRequest Clone() => (EquationRequest)MemberwiseClone();
}

/// <summary>The inputs of the POLYNOMIAL tool.</summary>
public sealed class PolynomialRequest
{
    /// <summary>The lowest degree offered.</summary>
    public const int MinDegree = 2;

    /// <summary>The highest degree offered (like the TI and Casio polynomial apps).</summary>
    public const int MaxDegree = 6;

    /// <summary>The degree (2 to 6).</summary>
    public int Degree { get; set; } = 2;

    /// <summary>Coefficient texts by power: index k holds the coefficient of x^k (always 7 entries).</summary>
    public List<string> Coefficients { get; set; } = ["3", "-5", "1", "0", "0", "0", "0"];

    /// <summary>The coefficient of x^power.</summary>
    public string this[int power]
    {
        get => power >= 0 && power < Coefficients.Count ? Coefficients[power] : "";
        set
        {
            while (Coefficients.Count <= power) Coefficients.Add("0");
            Coefficients[power] = value;
        }
    }

    /// <summary>Fixes sizes after loading saved state.</summary>
    public PolynomialRequest Normalize()
    {
        Coefficients ??= [];
        while (Coefficients.Count <= MaxDegree) Coefficients.Add("0");
        for (int i = 0; i < Coefficients.Count; i++) Coefficients[i] ??= "";
        Degree = Math.Clamp(Degree, MinDegree, MaxDegree);
        return this;
    }

    /// <summary>A copy.</summary>
    public PolynomialRequest Clone() => new() { Degree = Degree, Coefficients = [.. Coefficients] };
}

/// <summary>The inputs of the LINEAR SYSTEM tool.</summary>
public sealed class LinearSystemRequest
{
    /// <summary>The most unknowns and equations offered.</summary>
    public const int Max = 6;

    /// <summary>The number of unknowns (2 to 6).</summary>
    public int Unknowns { get; set; } = 3;

    /// <summary>The number of equations (1 to 6).</summary>
    public int Equations { get; set; } = 3;

    /// <summary>True when the equations are typed instead of entered as a coefficient grid.</summary>
    public bool Typed { get; set; }

    /// <summary>The coefficient grid: 6 rows of 7 cells (6 coefficients and the right side, which is the last cell).</summary>
    public List<List<string>> Grid { get; set; } = DefaultGrid();

    /// <summary>The typed equations (6 entries).</summary>
    public List<string> Lines { get; set; } = ["2x + y - z = 8", "-3x - y + 2z = -11", "-2x + y + 2z = -3", "", "", ""];

    /// <summary>The coefficient of unknown j in equation i.</summary>
    public string Cell(int row, int col) => row < Grid.Count && col < Grid[row].Count ? Grid[row][col] : "";

    /// <summary>The right side of equation i (stored in the last column).</summary>
    public string Rhs(int row) => Cell(row, Max);

    /// <summary>The grid of the design: 2x + y − z = 8, −3x − y + 2z = −11, −2x + y + 2z = −3.</summary>
    public static List<List<string>> DefaultGrid()
    {
        var g = new List<List<string>>();
        for (int i = 0; i < Max; i++) g.Add([.. Enumerable.Repeat("", Max + 1)]);
        string[][] rows = [["2", "1", "-1", "8"], ["-3", "-1", "2", "-11"], ["-2", "1", "2", "-3"]];
        for (int i = 0; i < rows.Length; i++)
        {
            for (int j = 0; j < 3; j++) g[i][j] = rows[i][j];
            g[i][Max] = rows[i][3];
        }
        return g;
    }

    /// <summary>Fixes sizes after loading saved state.</summary>
    public LinearSystemRequest Normalize()
    {
        Grid ??= [];
        while (Grid.Count < Max) Grid.Add([]);
        for (int i = 0; i < Grid.Count; i++)
        {
            Grid[i] ??= [];
            while (Grid[i].Count <= Max) Grid[i].Add("");
            for (int j = 0; j < Grid[i].Count; j++) Grid[i][j] ??= "";
        }
        Lines ??= [];
        while (Lines.Count < Max) Lines.Add("");
        for (int i = 0; i < Lines.Count; i++) Lines[i] ??= "";
        Unknowns = Math.Clamp(Unknowns, 2, Max);
        Equations = Math.Clamp(Equations, 1, Max);
        return this;
    }

    /// <summary>A copy.</summary>
    public LinearSystemRequest Clone() => new()
    {
        Unknowns = Unknowns,
        Equations = Equations,
        Typed = Typed,
        Grid = [.. Grid.Select(r => r.ToList())],
        Lines = [.. Lines],
    };

    /// <summary>The unknowns of the grid: x, y, z, w, u, v.</summary>
    public static IReadOnlyList<string> GridNames(int count) => [.. new[] { "x", "y", "z", "w", "u", "v" }.Take(count)];
}

/// <summary>The inputs of the NONLINEAR SYSTEM tool.</summary>
public sealed class NonlinearSystemRequest
{
    /// <summary>The most equations offered.</summary>
    public const int Max = 6;

    /// <summary>The number of equations shown (1 to 6).</summary>
    public int Count { get; set; } = 2;

    /// <summary>The typed equations (6 entries).</summary>
    public List<string> Lines { get; set; } = ["x^2 + y^2 = 25", "y = x + 1", "", "", "", ""];

    /// <summary>Optional starting guesses by unknown name.</summary>
    public Dictionary<string, string> Guesses { get; set; } = [];

    /// <summary>Fixes sizes after loading saved state.</summary>
    public NonlinearSystemRequest Normalize()
    {
        Lines ??= [];
        while (Lines.Count < Max) Lines.Add("");
        for (int i = 0; i < Lines.Count; i++) Lines[i] ??= "";
        Guesses ??= [];
        Count = Math.Clamp(Count, 1, Max);
        return this;
    }

    /// <summary>A copy.</summary>
    public NonlinearSystemRequest Clone() => new() { Count = Count, Lines = [.. Lines], Guesses = new Dictionary<string, string>(Guesses) };
}

/// <summary>The inputs of the INEQUALITY tool.</summary>
public sealed class InequalityRequest
{
    /// <summary>The inequality as linear Calcy input.</summary>
    public string Inequality { get; set; } = "x^2 - x - 6 < 0";

    /// <summary>The unknown picked by the user, or null to pick it automatically.</summary>
    public string? Unknown { get; set; }

    /// <summary>The range searched when the inequality has no exact solution (empty means -10 to 10).</summary>
    public string Range { get; set; } = "";

    /// <summary>A copy.</summary>
    public InequalityRequest Clone() => (InequalityRequest)MemberwiseClone();
}
