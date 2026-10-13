using Calcy.Graphing;

namespace Calcy.Modes.Notebook.Model;

/// <summary>Starter notebooks.</summary>
public static class SampleNotebooks
{
    /// <summary>The pendulum lab from the design board: text, a maths model with a slider, measurements, a graph and code.</summary>
    public static NotebookDocument PendulumLab()
    {
        var intro = new TextCell("""
            # Pendulum lab

            We measured the period of a pendulum and compare it with the small-angle model $T = 2π·sqrt(L/g)$. Change the length below and every result in this notebook follows.
            """);

        var model = new MathCell(["L = 0.8", "g = 9.81", "T = 2*pi*sqrt(L/g)"]) { Caption = "Model" };
        model.Lines[0].SetRange(0.1, 2, 0.01);
        model.Lines[0].ShowSlider = true;
        model.Lines[2].DecimalFirst = true;

        var data = new TableCell(5) { Caption = "Measured periods at L = 0.80 m" };
        data.AddRow("Tm", ["1.81", "1.79", "1.80", "1.82", "1.79"]);
        data.AddSummary("Tbar = mean(Tm)").DecimalFirst = true;
        data.AddSummary("dev = abs(Tbar - T)/T*100").DecimalFirst = true;

        var graph = new GraphCell(["y = 2*pi*sqrt(x/g)", "(L, T)", "(0.8, Tbar)"], new Viewport(0, 2, 0, 3))
        {
            Caption = "T(L) with the current length",
        };

        var code = new CodeCell("""
            import calc
            T = calc.n("T")
            print(f"10 swings take {10*T:.1f} s")
            """);

        var conclusion = new TextCell("""
            ## Conclusion

            The measured mean is within $=round(dev, 1)$ % of the model, so the small-angle approximation holds for our 10° release angle.
            """);

        return new NotebookDocument("Pendulum lab", [intro, model, data, graph, code, conclusion]);
    }
}
