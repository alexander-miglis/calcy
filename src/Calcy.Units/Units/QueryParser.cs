using Calcy.Core.Numbers;

namespace Calcy.Units;

/// <summary>
/// Reads conversion requests: one or more value-unit terms ("5 km", "3 ft 4 in", "1 h 30 min", "5' 11\""),
/// optionally followed by "to", "in", "into", "as", "->" or "=" and one or more target units ("mi", "ft in").
/// Ambiguous unit texts are resolved so that every term and target has the same dimension.
/// </summary>
internal sealed class QueryParser(UnitCatalog catalog)
{
    private static readonly string[] Separators = [" to ", " into ", " as ", "->", "→", "=>", " in ", "="];
    private static readonly char[] OperatorEnds = ['/', '*', '·', '^', '×', '('];

    private sealed record RawTerm(BigRational Value, string NumberText, string UnitText, IReadOnlyList<Unit> Candidates);

    public ConversionQuery Parse(string text)
    {
        var s = UnitText.Normalize(text).Replace("?", "", StringComparison.Ordinal).Trim();
        while (s.EndsWith('=')) s = s[..^1].TrimEnd();
        if (s.Length == 0) throw new ConversionException("Type a value with a unit, for example 5 km to mi");

        string? best = null;
        int bestRank = -1;
        void Note(string message, int rank)
        {
            if (rank > bestRank) { best = message; bestRank = rank; }
        }

        foreach (var sep in Separators)
        {
            // Try every occurrence from the right, so "3 ft 4 in in cm" splits before "cm" (occurrences may overlap).
            for (int at = s.LastIndexOf(sep, StringComparison.OrdinalIgnoreCase); at > 0;
                 at = s.LastIndexOf(sep, at + sep.Length - 2, StringComparison.OrdinalIgnoreCase))
            {
                var left = s[..at].Trim();
                var right = s[(at + sep.Length)..].Trim();
                if (left.Length == 0 || right.Length == 0) continue;
                if (!TryParseTerms(left, out var terms, out var error)) { Note(error, 1); continue; }
                if (!TryParseTargets(right, out var targets, out error)) { Note(error, 2); continue; }
                if (TryResolve(terms, targets, left, out var query, out error)) return query;
                Note(error, 3);
            }
        }

        if (TryParseTerms(s, out var only, out var err) && TryResolve(only, [], s, out var q, out err)) return q;
        Note(err, 0);
        throw new ConversionException(best!);
    }

    private bool TryParseTerms(string s, out List<RawTerm> terms, out string error)
    {
        terms = [];
        error = "";
        int pos = 0;
        while (pos < s.Length)
        {
            while (pos < s.Length && s[pos] == ' ') pos++;
            if (pos >= s.Length) break;
            if (!NumberReader.TryRead(s, ref pos, out var value, out var numberText))
            {
                error = terms.Count > 0 ? $"Expected a number before \"{s[pos..].Trim()}\""
                    : catalog.FindCandidates(s).Count > 0 ? $"Start with a number, for example 5 {s}"
                    : "Start with a number, for example 5 km to mi";
                return false;
            }
            int unitStart = pos;
            int i = pos;
            while (i < s.Length && !IsTermStart(s, i, unitStart)) i++;
            var unitText = s[unitStart..i].Trim();
            if (unitText.Length == 0)
            {
                error = terms.Count == 0
                    ? $"Add a unit, for example {numberText.Trim()} km to mi"
                    : $"Add a unit after {numberText.Trim()}";
                return false;
            }
            var candidates = catalog.FindCandidates(unitText);
            if (candidates.Count == 0)
            {
                error = unitText.StartsWith(',') ? "Use a point for decimals, for example 3.5" : $"Unknown unit: {unitText}";
                return false;
            }
            terms.Add(new RawTerm(value, numberText.Trim(), unitText, candidates));
            pos = i;
        }
        if (terms.Count == 0)
        {
            error = "Type a value with a unit, for example 5 km";
            return false;
        }
        return true;
    }

    /// <summary>A new term starts at a number that follows white space or a symbol such as ' or ° (5' 11", 45°30′).</summary>
    private static bool IsTermStart(string s, int i, int unitStart)
    {
        if (i <= unitStart || !NumberReader.StartsNumber(s, i)) return false;
        char before = s[i - 1];
        if (before is not (' ' or '\'' or '"' or '′' or '″' or '°')) return false;
        var soFar = s[unitStart..i].Trim();
        return soFar.Length > 0 && soFar[^1] is not ('/' or '*' or '·' or '^' or '×' or '(');
    }

    private bool TryParseTargets(string s, out List<IReadOnlyList<Unit>> targets, out string error)
    {
        targets = [];
        error = "";
        var whole = catalog.FindCandidates(s);
        if (whole.Count > 0)
        {
            targets.Add(whole);
            return true;
        }
        var pieces = s.Split([' ', '+', ','], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(p => !p.Equals("and", StringComparison.OrdinalIgnoreCase)).ToList();
        if (pieces.Count > 1)
        {
            foreach (var p in pieces)
            {
                var c = catalog.FindCandidates(p);
                if (c.Count == 0)
                {
                    error = $"Unknown unit: {p}";
                    return false;
                }
                targets.Add(c);
            }
            return true;
        }
        error = $"Unknown unit: {s}";
        return false;
    }

    private bool TryResolve(List<RawTerm> terms, List<IReadOnlyList<Unit>> targets, string inputText, out ConversionQuery query, out string error)
    {
        query = null!;
        error = "";
        foreach (var first in terms[0].Candidates)
        {
            var dim = first.Dimension;
            var chosen = new List<Unit> { first };
            bool ok = true;
            for (int k = 1; k < terms.Count && ok; k++)
            {
                var u = terms[k].Candidates.FirstOrDefault(c => c.Dimension == dim);
                if (u is null) ok = false;
                else chosen.Add(u);
            }
            var chosenTargets = new List<Unit>();
            foreach (var t in targets)
            {
                if (!ok) break;
                var u = t.FirstOrDefault(c => c.Dimension == dim);
                if (u is null) ok = false;
                else chosenTargets.Add(u);
            }
            if (!ok) continue;

            if (terms.Count > 1 && chosen.Any(u => u.Kind != UnitKind.Linear))
            {
                error = $"{chosen.First(u => u.Kind != UnitKind.Linear).Symbol} values cannot be added together; convert one value at a time";
                return false;
            }
            if (chosenTargets.Count > 1 && chosenTargets.Any(u => u.Kind != UnitKind.Linear))
            {
                error = "A mixed result needs units such as ft in or h min s";
                return false;
            }

            bool negative = terms[0].Value.IsNegative;
            var quantities = new List<Quantity>();
            for (int k = 0; k < terms.Count; k++)
            {
                var v = terms[k].Value;
                // "-1 h 30 min" means -(1 h 30 min).
                if (k > 0 && negative && terms[k].NumberText[0] is not ('-' or '+')) v = -v;
                quantities.Add(new Quantity(v, chosen[k]));
            }
            if (chosenTargets.Count > 1)
                chosenTargets = [.. chosenTargets.Distinct().OrderByDescending(u => u.Scale.ToRational())];
            query = new ConversionQuery(quantities, [.. terms.Select(t => t.NumberText)], chosenTargets, inputText);
            return true;
        }

        var a = terms[0].Candidates[0];
        var mismatch = terms.Skip(1).Select(t => t.Candidates[0]).FirstOrDefault(u => u.Dimension != a.Dimension);
        if (mismatch is not null)
        {
            error = $"{a.Symbol} and {mismatch.Symbol} cannot be added (different kinds of quantity)";
            return false;
        }
        var target = targets.Select(t => t[0]).FirstOrDefault(u => u.Dimension != a.Dimension) ?? targets[0][0];
        error = catalog.Incompatible(a, target).Message;
        return false;
    }
}
