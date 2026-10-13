using Calcy.Statistics;

namespace Calcy.Modes.Finance.Logic;

/// <summary>A ready-made TVM problem with one field left blank, so picking it shows the answer straight away.</summary>
/// <param name="Id">Stable id.</param>
/// <param name="Name">Name on the picker.</param>
/// <param name="Description">One sentence saying what the numbers mean, including the signs.</param>
/// <param name="Inputs">The field values; the blank field is the one that gets solved.</param>
public sealed record TvmPreset(string Id, string Name, string Description, TvmInputs Inputs)
{
    /// <summary>The variable this preset solves for (its blank field).</summary>
    public TvmVariable Solves => TvmWorksheet.Variables.First(v => string.IsNullOrWhiteSpace(Inputs.Get(v)));
}

/// <summary>The presets offered on the TVM tab.</summary>
public static class TvmPresets
{
    /// <summary>Mortgage, car loan, savings plan and retirement, in picker order.</summary>
    public static readonly IReadOnlyList<TvmPreset> All =
    [
        new("mortgage", "Mortgage",
            "Borrow 300,000 for 30 years at 6.5 %. PV is positive (you receive the loan), PMT comes out negative (you pay it).",
            new TvmInputs { N = "30*12", InterestRate = "6.5", PresentValue = "300000", Payment = "", FutureValue = "0" }),
        new("car", "Car loan",
            "Borrow 25,000 for 5 years at 7.9 %. The monthly payment comes out negative because you pay it.",
            new TvmInputs { N = "5*12", InterestRate = "7.9", PresentValue = "25000", Payment = "", FutureValue = "0" }),
        new("savings", "Savings",
            "Pay in 200 a month for 10 years at 4 %. PMT is negative (you pay in), FV comes out positive (you get it back).",
            new TvmInputs { N = "10*12", InterestRate = "4", PresentValue = "0", Payment = "-200", FutureValue = "" }),
        new("retirement", "Retirement",
            "Put 500,000 in at 5 % and spend it over 25 years. PV is negative (you pay it in), PMT comes out positive (your income).",
            new TvmInputs { N = "25*12", InterestRate = "5", PresentValue = "-500000", Payment = "", FutureValue = "0" }),
    ];

    /// <summary>Finds a preset by id.</summary>
    public static TvmPreset? Find(string id) => All.FirstOrDefault(p => p.Id == id);

    /// <summary>A fresh copy of a preset's inputs with monthly payments at the end of each month.</summary>
    public static TvmInputs Fill(TvmPreset preset)
    {
        ArgumentNullException.ThrowIfNull(preset);
        var x = preset.Inputs.Clone();
        x.PaymentsPerYear = "12";
        x.CompoundingsPerYear = "12";
        x.Timing = PaymentTiming.End;
        return x;
    }
}
