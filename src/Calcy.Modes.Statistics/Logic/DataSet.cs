using System.Globalization;

namespace Calcy.Modes.Statistics.Logic;

/// <summary>Raised when the chosen lists cannot be used (missing, empty, text in a number list, bad frequencies).</summary>
public sealed class StatDataException(string message) : Exception(message);

/// <summary>One list as read for an analysis: its header and parsed cells.</summary>
/// <param name="Name">The list name (L1).</param>
/// <param name="Header">Name and caption ("L1 hours").</param>
/// <param name="Cells">The parsed cells.</param>
public sealed record ColumnData(string Name, string Header, IReadOnlyList<CellValue> Cells);

/// <summary>The numbers of one list, with frequencies when a frequency list was chosen.</summary>
/// <param name="Values">The values, in list order.</param>
/// <param name="Frequencies">How often each value occurs, or null.</param>
/// <param name="Rows">The row of every value in the list.</param>
/// <param name="Name">The list name.</param>
/// <param name="FrequencyName">The frequency list name, or null.</param>
/// <param name="Skipped">Rows left out because a cell was empty.</param>
public sealed record Sample(double[] Values, double[]? Frequencies, int[] Rows, string Name, string? FrequencyName, int Skipped)
{
    /// <summary>The number of observations (sum of the frequencies).</summary>
    public double Count => Frequencies?.Sum() ?? Values.Length;

    /// <summary>Every value repeated by its (whole) frequency, for plots that draw single observations.</summary>
    public double[] Expanded()
    {
        if (Frequencies is null) return Values;
        var list = new List<double>();
        for (int i = 0; i < Values.Length; i++)
        {
            int f = (int)Math.Round(Frequencies[i]);
            for (int k = 0; k < f && list.Count < StatTable.MaxRows * 10; k++) list.Add(Values[i]);
        }
        return [.. list];
    }
}

/// <summary>Paired values (x, y) from two lists, with optional frequencies.</summary>
/// <param name="X">x values.</param>
/// <param name="Y">y values.</param>
/// <param name="Frequencies">Frequencies, or null.</param>
/// <param name="Rows">The row of every pair.</param>
/// <param name="XName">x list name.</param>
/// <param name="YName">y list name.</param>
/// <param name="FrequencyName">Frequency list name, or null.</param>
/// <param name="Skipped">Rows left out because one of the cells was empty.</param>
public sealed record Paired(double[] X, double[] Y, double[]? Frequencies, int[] Rows, string XName, string YName, string? FrequencyName, int Skipped);

/// <summary>
/// A snapshot of the lists taken on the UI thread, so analyses can run in the background while the user keeps editing.
/// The readers check the cells and explain problems in plain words.
/// </summary>
public sealed class DataSet
{
    private readonly Dictionary<string, ColumnData> _columns;

    /// <summary>Creates a snapshot from columns.</summary>
    public DataSet(IEnumerable<ColumnData> columns)
    {
        var list = columns.ToList();
        Names = [.. list.Select(c => c.Name)];
        _columns = list.ToDictionary(c => c.Name, StringComparer.Ordinal);
    }

    /// <summary>Takes a snapshot of a table (parses cells on the calling thread).</summary>
    public static DataSet From(StatTable table) =>
        new(table.Columns.Select(c => new ColumnData(c.Name, c.Header, c.Values(table.Session))));

    /// <summary>A snapshot of plain number lists (for tests).</summary>
    public static DataSet Of(params (string Name, double[] Values)[] lists) =>
        new(lists.Select(l => new ColumnData(l.Name, l.Name,
            [.. l.Values.Select(v => new CellValue(CellKind.Number, v, null, v.ToString("R", CultureInfo.InvariantCulture)))])));

    /// <summary>The list names, left to right.</summary>
    public IReadOnlyList<string> Names { get; }

    /// <summary>A list, or null.</summary>
    public ColumnData? this[string? name] => name is not null && _columns.TryGetValue(name, out var c) ? c : null;

    /// <summary>The header of a list ("L1 hours"), or the name when unknown.</summary>
    public string HeaderOf(string name) => this[name]?.Header ?? name;

    private ColumnData Require(string? name, string role)
    {
        if (string.IsNullOrWhiteSpace(name)) throw new StatDataException($"Pick a list for {role}");
        var c = this[name] ?? throw new StatDataException($"There is no list called {name}");
        if (c.Cells.All(v => v.Kind == CellKind.Empty)) throw new StatDataException($"{name} is empty: type or paste some numbers");
        return c;
    }

    private static string Quote(string text) => text.Length > 16 ? text[..14] + "…" : text;

    private static double Number(ColumnData c, int row)
    {
        var v = c.Cells[row];
        if (v.Kind == CellKind.Text) throw new StatDataException($"{c.Name} row {row + 1} holds text ('{Quote(v.Text)}'): only numbers can be used here");
        return v.Number;
    }

    private static double Frequency(ColumnData f, int row, string valueList)
    {
        var v = row < f.Cells.Count ? f.Cells[row] : CellValue.Empty;
        if (v.Kind == CellKind.Empty) throw new StatDataException($"The frequency {f.Name} row {row + 1} is empty, but {valueList} row {row + 1} has a value");
        if (v.Kind == CellKind.Text) throw new StatDataException($"The frequency {f.Name} row {row + 1} holds text ('{Quote(v.Text)}')");
        if (!(v.Number >= 0) || !double.IsFinite(v.Number)) throw new StatDataException($"Frequencies cannot be negative ({f.Name} row {row + 1} is {NumberText.Display(v.Number)})");
        return v.Number;
    }

    /// <summary>The numbers of one list (rows with an empty cell are skipped), with an optional frequency list.</summary>
    public Sample Single(string? list, string? frequency = null, string role = "the data")
    {
        var c = Require(list, role);
        var f = string.IsNullOrEmpty(frequency) ? null : Require(frequency, "the frequencies");
        var values = new List<double>();
        var freqs = f is null ? null : new List<double>();
        var rows = new List<int>();
        int skipped = 0;
        int length = Math.Max(c.Cells.Count, f?.Cells.Count ?? 0);
        for (int r = 0; r < length; r++)
        {
            var cell = r < c.Cells.Count ? c.Cells[r] : CellValue.Empty;
            if (cell.Kind == CellKind.Empty)
            {
                if (r < c.Cells.Count) skipped++;
                continue;
            }
            double v = Number(c, r);
            if (f is not null)
            {
                double w = Frequency(f, r, c.Name);
                freqs!.Add(w);
            }
            values.Add(v);
            rows.Add(r);
        }
        if (values.Count == 0) throw new StatDataException($"{c.Name} has no numbers");
        if (freqs is not null && !(freqs.Sum() > 0)) throw new StatDataException($"The frequencies in {f!.Name} add up to 0");
        return new Sample([.. values], freqs?.ToArray(), [.. rows], c.Name, f?.Name, skipped);
    }

    /// <summary>Pairs (x, y) from two lists; rows where either cell is empty are skipped.</summary>
    public Paired Pair(string? x, string? y, string? frequency = null)
    {
        var cx = Require(x, "x");
        var cy = Require(y, "y");
        var f = string.IsNullOrEmpty(frequency) ? null : Require(frequency, "the frequencies");
        var xs = new List<double>();
        var ys = new List<double>();
        var ws = f is null ? null : new List<double>();
        var rows = new List<int>();
        int skipped = 0;
        int length = Math.Max(cx.Cells.Count, cy.Cells.Count);
        for (int r = 0; r < length; r++)
        {
            var a = r < cx.Cells.Count ? cx.Cells[r] : CellValue.Empty;
            var b = r < cy.Cells.Count ? cy.Cells[r] : CellValue.Empty;
            if (a.Kind == CellKind.Empty || b.Kind == CellKind.Empty)
            {
                if (a.Kind != CellKind.Empty || b.Kind != CellKind.Empty) skipped++;
                continue;
            }
            double vx = Number(cx, r), vy = Number(cy, r);
            if (f is not null) ws!.Add(Frequency(f, r, cx.Name));
            xs.Add(vx);
            ys.Add(vy);
            rows.Add(r);
        }
        if (xs.Count == 0) throw new StatDataException($"{cx.Name} and {cy.Name} have no rows with both values");
        return new Paired([.. xs], [.. ys], ws?.ToArray(), [.. rows], cx.Name, cy.Name, f?.Name, skipped);
    }

    /// <summary>Several lists as groups (ANOVA, Kruskal-Wallis): empty cells are skipped.</summary>
    public double[][] Groups(IReadOnlyList<string> lists, int minimum = 2)
    {
        var names = lists.Where(n => !string.IsNullOrWhiteSpace(n)).Distinct().ToList();
        if (names.Count < minimum) throw new StatDataException($"Pick at least {minimum} lists");
        return [.. names.Select(n => Single(n).Values)];
    }

    /// <summary>The distinct values of a list (numbers or text) with their counts, in order of first appearance.</summary>
    public IReadOnlyList<(string Label, double Count, double? Value)> Categories(string? list, string? frequency = null)
    {
        var c = Require(list, "the categories");
        var f = string.IsNullOrEmpty(frequency) ? null : Require(frequency, "the frequencies");
        var order = new List<string>();
        var counts = new Dictionary<string, (double Count, double? Value)>(StringComparer.Ordinal);
        for (int r = 0; r < c.Cells.Count; r++)
        {
            var v = c.Cells[r];
            if (v.Kind == CellKind.Empty) continue;
            string key = v.Kind == CellKind.Number ? NumberText.Display(v.Number, 10) : v.Text;
            double w = f is null ? 1 : Frequency(f, r, c.Name);
            if (!counts.TryGetValue(key, out var entry))
            {
                order.Add(key);
                entry = (0, v.Kind == CellKind.Number ? v.Number : null);
            }
            counts[key] = (entry.Count + w, entry.Value);
        }
        if (order.Count == 0) throw new StatDataException($"{c.Name} is empty");
        // Numbers read best in numeric order; text keeps the order it was typed in.
        if (order.All(k => counts[k].Value is not null)) order = [.. order.OrderBy(k => counts[k].Value)];
        return [.. order.Select(k => (k, counts[k].Count, counts[k].Value))];
    }
}
