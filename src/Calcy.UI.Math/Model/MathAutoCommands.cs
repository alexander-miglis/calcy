namespace Calcy.UI.Math.Model;

/// <summary>What a typed word turns into.</summary>
public enum MathAutoCommandKind
{
    /// <summary>A symbol such as π or θ.</summary>
    Symbol,

    /// <summary>A keyword shown as a word (and, or, not, xor, mod).</summary>
    Word,

    /// <summary>A template such as √ or Σ.</summary>
    Template,
}

/// <summary>A word that the editor converts while typing (pi gives π, sqrt gives √□, sum gives Σ).</summary>
/// <param name="Name">The typed letters.</param>
/// <param name="Kind">What it becomes.</param>
/// <param name="Symbol">The symbol or word text (for Symbol and Word).</param>
/// <param name="Template">The template (for Template).</param>
/// <param name="RequiresWordStart">Only converted when the letters are not preceded by another letter.</param>
public sealed record MathAutoCommand(string Name, MathAutoCommandKind Kind, string Symbol, MathTemplateKind Template, bool RequiresWordStart);

/// <summary>The words the editor converts while typing.</summary>
public static class MathAutoCommands
{
    private static readonly (string Name, string Letter)[] GreekLetters =
    [
        ("alpha", "α"), ("beta", "β"), ("gamma", "γ"), ("delta", "δ"), ("epsilon", "ε"), ("zeta", "ζ"), ("eta", "η"),
        ("theta", "θ"), ("iota", "ι"), ("kappa", "κ"), ("lambda", "λ"), ("mu", "μ"), ("nu", "ν"), ("xi", "ξ"),
        ("rho", "ρ"), ("sigma", "σ"), ("tau", "τ"), ("upsilon", "υ"), ("phi", "φ"), ("chi", "χ"), ("psi", "ψ"),
        ("omega", "ω"), ("Gamma", "Γ"), ("Delta", "Δ"), ("Theta", "Θ"), ("Lambda", "Λ"), ("Xi", "Ξ"), ("Sigma", "Σ"),
        ("Phi", "Φ"), ("Psi", "Ψ"), ("Omega", "Ω"),
    ];

    static MathAutoCommands()
    {
        var list = new List<MathAutoCommand>();
        foreach (var (name, letter) in GreekLetters) list.Add(new(name, MathAutoCommandKind.Symbol, letter, default, true));
        list.Add(new("pi", MathAutoCommandKind.Symbol, "π", default, true));
        list.Add(new("inf", MathAutoCommandKind.Symbol, "∞", default, true));
        list.Add(new("infinity", MathAutoCommandKind.Symbol, "∞", default, true));
        foreach (var w in new[] { "and", "or", "not", "xor", "mod" }) list.Add(new(w, MathAutoCommandKind.Word, w, default, true));
        list.Add(new("sqrt", MathAutoCommandKind.Template, "", MathTemplateKind.Sqrt, false));
        list.Add(new("cbrt", MathAutoCommandKind.Template, "", MathTemplateKind.NthRoot, false));
        list.Add(new("nthroot", MathAutoCommandKind.Template, "", MathTemplateKind.NthRoot, false));
        list.Add(new("abs", MathAutoCommandKind.Template, "", MathTemplateKind.Abs, false));
        list.Add(new("int", MathAutoCommandKind.Template, "", MathTemplateKind.DefiniteIntegral, false));
        list.Add(new("sum", MathAutoCommandKind.Template, "", MathTemplateKind.Sum, false));
        list.Add(new("prod", MathAutoCommandKind.Template, "", MathTemplateKind.Product, false));
        list.Add(new("lim", MathAutoCommandKind.Template, "", MathTemplateKind.Limit, false));
        list.Add(new("diff", MathAutoCommandKind.Template, "", MathTemplateKind.Derivative, false));
        All = list;
        ByName = list.ToDictionary(c => c.Name, StringComparer.Ordinal);
    }

    /// <summary>Every auto command.</summary>
    public static IReadOnlyList<MathAutoCommand> All { get; }

    /// <summary>Lookup by typed name.</summary>
    public static IReadOnlyDictionary<string, MathAutoCommand> ByName { get; }
}
