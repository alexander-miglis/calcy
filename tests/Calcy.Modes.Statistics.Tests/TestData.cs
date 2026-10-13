using Calcy.Core;
using Calcy.Engine;
using Calcy.Modes.Statistics.Logic;

namespace Calcy.Modes.Statistics.Tests;

/// <summary>Shared data for the logic tests: the sample data of the design board.</summary>
internal static class TestData
{
    /// <summary>Hours of study (L1 on the design board).</summary>
    public static readonly double[] Hours = [1, 2, 2, 3, 4, 5, 5, 6, 7, 8];

    /// <summary>Test scores (L2 on the design board).</summary>
    public static readonly double[] Scores = [52, 55, 60, 61, 66, 70, 72, 75, 80, 84];

    public static CalcSession Session() => CalcyEngine.CreateSession();

    /// <summary>A table with the design data in L1 and L2, shared with a fresh session.</summary>
    public static StatTable DesignTable(CalcSession? session = null)
    {
        var t = new StatTable(session ?? Session());
        t.AddColumn("L1").Caption = "hours";
        t.AddColumn("L2").Caption = "score";
        t.SetNumbers("L1", Hours);
        t.SetNumbers("L2", Scores);
        return t;
    }

    public static DataSet DesignData() => DataSet.Of(("L1", Hours), ("L2", Scores));

    /// <summary>Evaluates a USE text in a session that holds the design lists and checks that it works.</summary>
    public static CalcResult EvaluateUse(string use)
    {
        var s = Session();
        DesignTable(s);
        var r = s.Evaluate(use);
        Assert.False(r.IsError, $"{use}: {r.ErrorMessage}");
        return r;
    }
}
