namespace Calcy.Modes.Solver.Logic;

/// <summary>
/// One result card (design decision D5): what was solved and the answer as text lines, plus everything the card's
/// buttons need (COPY, COPY LATEX, USE, PLOT, EDIT). Plain data, so it is saved with the mode state as JSON.
/// </summary>
public sealed class SolverCard
{
    /// <summary>The running number shown as #n.</summary>
    public int Number { get; set; }

    /// <summary>The tool that made the card.</summary>
    public SolverTool Tool { get; set; }

    /// <summary>The kind shown in the header (POLYNOMIAL, SYSTEM 3 × 3, ...).</summary>
    public string Title { get; set; } = "";

    /// <summary>When it was solved (HH:mm).</summary>
    public string Time { get; set; } = "";

    /// <summary>What was solved, as on-screen text.</summary>
    public string Input { get; set; } = "";

    /// <summary>The answer, one line each.</summary>
    public List<string> Lines { get; set; } = [];

    /// <summary>Decimal forms of the answer, or null.</summary>
    public string? Approx { get; set; }

    /// <summary>A short explanation in words (no real solution because Δ &lt; 0, found numerically ...), or null.</summary>
    public string? Note { get; set; }

    /// <summary>True for a problem card (red border, EDIT and FIX IT instead of the result buttons).</summary>
    public bool IsError { get; set; }

    /// <summary>The text COPY puts on the clipboard.</summary>
    public string? CopyText { get; set; }

    /// <summary>The LaTeX COPY LATEX puts on the clipboard.</summary>
    public string? LatexText { get; set; }

    /// <summary>The text USE sends to Advanced mode (null hides USE).</summary>
    public string? UseText { get; set; }

    /// <summary>What PLOT sends to Graphing (empty hides PLOT).</summary>
    public List<string> PlotTexts { get; set; } = [];

    /// <summary>A corrected input offered by FIX IT (error cards), or null.</summary>
    public string? Hint { get; set; }

    /// <summary>The inputs that produced the card, so EDIT can put them back.</summary>
    public SolverSnapshot? Request { get; set; }
}

/// <summary>The inputs of one tool at the time of a solve (only the field of that tool is set).</summary>
public sealed class SolverSnapshot
{
    /// <summary>Equation inputs.</summary>
    public EquationRequest? Equation { get; set; }

    /// <summary>Polynomial inputs.</summary>
    public PolynomialRequest? Polynomial { get; set; }

    /// <summary>Linear system inputs.</summary>
    public LinearSystemRequest? Linear { get; set; }

    /// <summary>Nonlinear system inputs.</summary>
    public NonlinearSystemRequest? Nonlinear { get; set; }

    /// <summary>Inequality inputs.</summary>
    public InequalityRequest? Inequality { get; set; }
}

/// <summary>Everything the Solver screen remembers between sessions (stored with Preferences.SetModeState("solver", ...)).</summary>
public sealed class SolverState
{
    /// <summary>The most cards kept.</summary>
    public const int MaxCards = 60;

    /// <summary>The tab that was open.</summary>
    public SolverTool Tool { get; set; } = SolverTool.Polynomial;

    /// <summary>ANY EQUATION inputs.</summary>
    public EquationRequest Equation { get; set; } = new();

    /// <summary>POLYNOMIAL inputs.</summary>
    public PolynomialRequest Polynomial { get; set; } = new();

    /// <summary>LINEAR SYSTEM inputs.</summary>
    public LinearSystemRequest Linear { get; set; } = new();

    /// <summary>NONLINEAR SYSTEM inputs.</summary>
    public NonlinearSystemRequest Nonlinear { get; set; } = new();

    /// <summary>INEQUALITY inputs.</summary>
    public InequalityRequest Inequality { get; set; } = new();

    /// <summary>The cards, oldest first.</summary>
    public List<SolverCard> Cards { get; set; } = [];

    /// <summary>The number of the last card made.</summary>
    public int Counter { get; set; }

    /// <summary>Repairs missing or out-of-range values after loading.</summary>
    public SolverState Normalize()
    {
        if (!Enum.IsDefined(Tool)) Tool = SolverTool.Polynomial;
        Equation ??= new();
        Equation.Equation ??= "";
        Equation.Range ??= "";
        Polynomial = (Polynomial ?? new()).Normalize();
        Linear = (Linear ?? new()).Normalize();
        Nonlinear = (Nonlinear ?? new()).Normalize();
        Inequality ??= new();
        Inequality.Inequality ??= "";
        Inequality.Range ??= "";
        Cards ??= [];
        Cards.RemoveAll(c => c is null);
        foreach (var c in Cards)
        {
            c.Lines ??= [];
            c.PlotTexts ??= [];
            c.Title ??= "";
            c.Input ??= "";
            c.Time ??= "";
        }
        if (Cards.Count > MaxCards) Cards.RemoveRange(0, Cards.Count - MaxCards);
        Counter = Math.Max(Counter, Cards.Count == 0 ? 0 : Cards.Max(c => c.Number));
        return this;
    }
}
