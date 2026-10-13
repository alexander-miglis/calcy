using Calcy.Statistics;

namespace Calcy.Modes.Statistics.Logic;

/// <summary>A saved list.</summary>
public sealed class ColumnState
{
    /// <summary>The list name.</summary>
    public string Name { get; set; } = "";

    /// <summary>The caption.</summary>
    public string Caption { get; set; } = "";

    /// <summary>The cell texts.</summary>
    public List<string> Cells { get; set; } = [];

    /// <summary>The live formula, or null.</summary>
    public string? Formula { get; set; }
}

/// <summary>Everything the Statistics screen keeps between sessions (saved with the preferences under "statistics").</summary>
public sealed class StatsModeState
{
    /// <summary>The most cards kept.</summary>
    public const int MaxCards = 60;

    /// <summary>The lists.</summary>
    public List<ColumnState> Columns { get; set; } = [];

    /// <summary>The open tab.</summary>
    public StatTab Tab { get; set; } = StatTab.Regression;

    /// <summary>1-Var settings.</summary>
    public OneVarSettings OneVar { get; set; } = new();

    /// <summary>2-Var settings.</summary>
    public TwoVarSettings TwoVar { get; set; } = new();

    /// <summary>Regression settings.</summary>
    public RegressionSettings Regression { get; set; } = new();

    /// <summary>Test settings.</summary>
    public TestSettings Test { get; set; } = new();

    /// <summary>Distribution settings.</summary>
    public DistributionSettings Distribution { get; set; } = new();

    /// <summary>Plot settings.</summary>
    public PlotSettings Plot { get; set; } = new();

    /// <summary>The text in the column formula box.</summary>
    public string Formula { get; set; } = "";

    /// <summary>The cards, newest first.</summary>
    public List<StatCard> Cards { get; set; } = [];

    /// <summary>The number the next card gets.</summary>
    public int NextCardNumber { get; set; } = 1;

    /// <summary>
    /// The first-run state: the sample data of the design board (hours of study and test scores) in L1 and L2, a linear
    /// regression open, a t test of the scores against 65 and the standard normal between -1.96 and 1.96.
    /// </summary>
    public static StatsModeState CreateDefault() => new()
    {
        Columns =
        [
            new ColumnState { Name = "L1", Caption = "hours", Cells = ["1", "2", "2", "3", "4", "5", "5", "6", "7", "8"] },
            new ColumnState { Name = "L2", Caption = "score", Cells = ["52", "55", "60", "61", "66", "70", "72", "75", "80", "84"] },
        ],
        Tab = StatTab.Regression,
        OneVar = new OneVarSettings { List = "L2", Method = QuartileMethod.TI },
        Test = new TestSettings { Kind = TestKind.TTest, Fields = new Dictionary<string, string> { ["mu0"] = "65", ["list"] = "L2" } },
        Plot = new PlotSettings { Kind = PlotKind.Histogram, List = "L2", List2 = null },
    };
}
