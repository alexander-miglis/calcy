using Calcy.Formulas;

namespace Calcy.Modes.Formulas.Logic;

/// <summary>The left column's two lists.</summary>
public enum FormulasTab
{
    /// <summary>Categories and formulas.</summary>
    Formulas,

    /// <summary>The physical constants.</summary>
    Constants,
}

/// <summary>The box texts and the chosen unknown of one formula.</summary>
public sealed class SavedInputs
{
    /// <summary>The formula id.</summary>
    public string FormulaId { get; set; } = "";

    /// <summary>The variable to solve for, or null for the default.</summary>
    public string? SolveFor { get; set; }

    /// <summary>Box text by variable symbol (empty boxes are left out).</summary>
    public Dictionary<string, string> Values { get; set; } = [];
}

/// <summary>What Formulas mode remembers between sessions (stored in the preferences under the mode id).</summary>
public sealed class FormulasState
{
    /// <summary>The formula shown when nothing was picked yet.</summary>
    public const string DefaultFormulaId = "suvat-v-u-a-t";

    /// <summary>How many formulas keep their box texts.</summary>
    public const int MaxSavedInputs = 100;

    /// <summary>How many cards are kept.</summary>
    public const int MaxCards = 200;

    /// <summary>The open formula.</summary>
    public string FormulaId { get; set; } = DefaultFormulaId;

    /// <summary>The list shown in the left column.</summary>
    public FormulasTab Tab { get; set; } = FormulasTab.Formulas;

    /// <summary>Box texts per formula, most recently used first.</summary>
    public List<SavedInputs> Inputs { get; set; } = [];

    /// <summary>The cards, newest first.</summary>
    public List<FormulaCard> Cards { get; set; } = [];

    /// <summary>The number the next card gets.</summary>
    public int NextNumber { get; set; } = 1;

    /// <summary>The saved inputs of a formula (created on first use), moved to the front of the list.</summary>
    public SavedInputs InputsFor(string formulaId)
    {
        ArgumentNullException.ThrowIfNull(formulaId);
        int i = Inputs.FindIndex(s => s.FormulaId == formulaId);
        SavedInputs saved;
        if (i >= 0)
        {
            saved = Inputs[i];
            if (i > 0)
            {
                Inputs.RemoveAt(i);
                Inputs.Insert(0, saved);
            }
        }
        else
        {
            saved = new SavedInputs { FormulaId = formulaId };
            Inputs.Insert(0, saved);
            if (Inputs.Count > MaxSavedInputs) Inputs.RemoveRange(MaxSavedInputs, Inputs.Count - MaxSavedInputs);
        }
        return saved;
    }

    /// <summary>Adds a card at the top, numbering it, and drops the oldest beyond <see cref="MaxCards"/>.</summary>
    public FormulaCard AddCard(FormulaAnswer answer, IReadOnlyDictionary<string, string> values, DateTime created)
    {
        var card = FormulaCard.From(answer, NextNumber++, values, created);
        Cards.Insert(0, card);
        if (Cards.Count > MaxCards) Cards.RemoveRange(MaxCards, Cards.Count - MaxCards);
        return card;
    }

    /// <summary>Repairs state read from an older or edited preferences file against the library.</summary>
    public FormulasState Normalize(FormulaLibrary library)
    {
        ArgumentNullException.ThrowIfNull(library);
        Inputs ??= [];
        Cards ??= [];
        if (string.IsNullOrEmpty(FormulaId) || library.Get(FormulaId) is null) FormulaId = DefaultFormulaId;
        if (!Enum.IsDefined(Tab)) Tab = FormulasTab.Formulas;
        Inputs.RemoveAll(s => s is null || library.Get(s.FormulaId) is null);
        foreach (var s in Inputs)
        {
            s.Values ??= [];
            var f = library.Get(s.FormulaId)!;
            if (s.SolveFor is not null && f.GetVariable(s.SolveFor) is null) s.SolveFor = null;
        }
        Cards.RemoveAll(c => c is null);
        foreach (var c in Cards)
        {
            c.Results ??= [];
            c.Notes ??= [];
            c.Uses ??= [];
            c.Values ??= [];
            c.Given ??= "";
            c.CopyText ??= "";
        }
        if (Cards.Count > MaxCards) Cards.RemoveRange(MaxCards, Cards.Count - MaxCards);
        int highest = Cards.Count == 0 ? 0 : Cards.Max(c => c.Number);
        if (NextNumber <= highest) NextNumber = highest + 1;
        if (NextNumber < 1) NextNumber = 1;
        return this;
    }
}
