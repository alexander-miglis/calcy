using Avalonia.Controls;
using Avalonia.Layout;
using Calcy.UI.Controls;
using Calcy.UI.Services;

namespace Calcy.App;

/// <summary>All modes in menu order (design decision D1/D3: a dropdown, every mode its own screen).</summary>
internal static class ModeCatalog
{
    public static IReadOnlyList<ICalcyMode> Create()
    {
        var list = new List<ICalcyMode>();
        void Add(ICalcyMode m) => list.Add(m);
        foreach (var m in Known()) Add(m);
        return list;
    }

    private static IEnumerable<ICalcyMode> Known()
    {
        yield return Find("simple") ?? new PlaceholderMode("simple", "Simple", "Everyday sums, memory keys, tape", "Ctrl+1", "Lcd", "simple");
        yield return Find("advanced") ?? new PlaceholderMode("advanced", "Advanced", "Scientific and algebra with result cards", "Ctrl+2", "Sel", "advanced");
        yield return Find("graphing") ?? new PlaceholderMode("graphing", "Graphing", "Plot, trace, roots, areas, sliders", "Ctrl+3", "G2", "graph");
        yield return Find("programmer") ?? new PlaceholderMode("programmer", "Programmer", "Hex, binary, bits, word sizes", "Ctrl+4", "Ok", "programmer");
        yield return Find("statistics") ?? new PlaceholderMode("statistics", "Statistics", "Data, regressions, tests, distributions", "Ctrl+5", "Mem", "stats");
        yield return Find("matrix") ?? new PlaceholderMode("matrix", "Matrix", "Edit matrices, det, inverse, eigen", "Ctrl+6", "Head", "matrix");
        yield return Find("solver") ?? new PlaceholderMode("solver", "Solver", "Polynomials, systems, any equation", "Ctrl+7", "Primary", "solver");
        yield return Find("finance") ?? new PlaceholderMode("finance", "Finance", "Loans, savings, cash flows", "Ctrl+8", "G5", "finance");
        yield return Find("convert") ?? new PlaceholderMode("convert", "Convert", "Units and dates", "Ctrl+9", "Key", "convert");
        yield return Find("formulas") ?? new PlaceholderMode("formulas", "Formulas", "Documented formula collection", "Ctrl+0", "G3", "formula");
        yield return Find("notebook") ?? new PlaceholderMode("notebook", "Notebook", "Text, maths and graphs in one document", "Ctrl+Shift+N", "G4", "notebook");
        yield return Find("code") ?? new PlaceholderMode("code", "Code", "Sandboxed Python", "Ctrl+E", "G1", "code");
    }

    /// <summary>Discovers mode implementations from loaded Calcy.Modes.* assemblies.</summary>
    private static ICalcyMode? Find(string id)
    {
        foreach (var asm in ModeAssemblies.Value)
        {
            foreach (var t in asm.GetTypes())
            {
                if (t.IsAbstract || !typeof(ICalcyMode).IsAssignableFrom(t) || t.GetConstructor(Type.EmptyTypes) is null) continue;
                var mode = (ICalcyMode)Activator.CreateInstance(t)!;
                if (mode.Id == id) return mode;
            }
        }
        return null;
    }

    private static readonly Lazy<List<System.Reflection.Assembly>> ModeAssemblies = new(() =>
    {
        var result = new List<System.Reflection.Assembly>();
        var dir = AppContext.BaseDirectory;
        foreach (var file in Directory.GetFiles(dir, "Calcy.Modes.*.dll"))
        {
            try { result.Add(System.Reflection.Assembly.LoadFrom(file)); }
            catch (Exception ex) when (ex is BadImageFormatException or FileLoadException) { }
        }
        return result;
    });
}

/// <summary>Stand-in screen used while a mode is not available in this build.</summary>
internal sealed class PlaceholderMode(string id, string title, string description, string shortcut, string accent, string icon) : ICalcyMode
{
    public string Id { get; } = id;
    public string Title { get; } = title;
    public string Description { get; } = description;
    public string Shortcut { get; } = shortcut;
    public string AccentToken { get; } = accent;
    public string Icon { get; } = icon;

    public Control CreateView(AppServices services) => new PixelPanel
    {
        Header = Title,
        Margin = new Avalonia.Thickness(24),
        HorizontalAlignment = HorizontalAlignment.Center,
        VerticalAlignment = VerticalAlignment.Center,
        Padding = new Avalonia.Thickness(24),
        Content = new TextBlock { Text = $"{Title} is not part of this build.", Classes = { "body" } },
    };
}
