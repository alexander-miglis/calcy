namespace Calcy.Formulas;

/// <summary>A weighted bag of words describing one searchable item.</summary>
internal sealed class SearchDocument<T>
{
    public required T Item { get; init; }

    /// <summary>Normalised display name, used for phrase bonuses.</summary>
    public required string Name { get; init; }

    /// <summary>Normalised tags, used for phrase bonuses.</summary>
    public required string[] TagPhrases { get; init; }

    /// <summary>Compact equation or symbol text, used when the query looks like an equation.</summary>
    public string CompactEquation { get; init; } = "";

    /// <summary>Exact symbol (constants only), matched case-sensitively first.</summary>
    public string? Symbol { get; init; }

    /// <summary>Words with the weight of the field they come from.</summary>
    public required (string[] Words, double Weight)[] Fields { get; init; }
}

/// <summary>
/// Small in-memory ranked search. Every query word must match somewhere (exactly, as a prefix, as a substring,
/// or within one typo for words of four letters or more); matches in the name count most, then tags,
/// category, variable names, and finally the explanation text.
/// </summary>
internal sealed class SearchIndex<T>
{
    private readonly List<SearchDocument<T>> _docs;

    public SearchIndex(IEnumerable<SearchDocument<T>> docs) => _docs = docs.ToList();

    public List<(T Item, double Score)> Search(string? query)
    {
        var results = new List<(T Item, double Score, int Order)>();
        string phrase = SearchText.Normalize(query);
        string[] words = SearchText.QueryWords(query);
        if (words.Length == 0)
        {
            for (int i = 0; i < _docs.Count; i++) results.Add((_docs[i].Item, 0, i));
            return results.Select(r => (r.Item, r.Score)).ToList();
        }

        string compact = SearchText.CompactEquation(query);
        bool looksLikeEquation = compact.Length >= 3 && compact.IndexOfAny(['=', '+', '/', '^', '(']) >= 0;
        string rawTrimmed = (query ?? "").Trim();

        for (int d = 0; d < _docs.Count; d++)
        {
            var doc = _docs[d];
            double total = 0;
            bool all = true;
            foreach (string w in words)
            {
                double best = 0;
                foreach (var (fieldWords, weight) in doc.Fields)
                {
                    foreach (string fw in fieldWords)
                    {
                        double q = SearchText.MatchQuality(w, fw);
                        if (q > 0 && q * weight > best) best = q * weight;
                    }
                }
                if (best <= 0) { all = false; break; }
                total += best;
            }

            double bonus = 0;
            if (doc.Symbol is not null && rawTrimmed.Length > 0)
            {
                if (string.Equals(doc.Symbol, rawTrimmed, StringComparison.Ordinal)) bonus += 60;
                else if (string.Equals(doc.Symbol, rawTrimmed, StringComparison.OrdinalIgnoreCase)) bonus += 30;
            }
            if (looksLikeEquation && doc.CompactEquation.Length > 0 && doc.CompactEquation.Contains(compact, StringComparison.Ordinal))
                bonus += 40;

            if (!all && bonus <= 0) continue;

            if (phrase.Length > 0)
            {
                if (doc.Name == phrase) bonus += 40;
                else if (doc.Name.StartsWith(phrase, StringComparison.Ordinal)) bonus += 20;
                else if (phrase.Length >= 3 && doc.Name.Contains(phrase, StringComparison.Ordinal)) bonus += 12;

                double tagBonus = 0;
                foreach (string tag in doc.TagPhrases)
                {
                    if (tag == phrase) { tagBonus = 25; break; }
                    if (phrase.Length >= 3 && tag.Contains(phrase, StringComparison.Ordinal)) tagBonus = Math.Max(tagBonus, 8);
                }
                bonus += tagBonus;
            }

            results.Add((doc.Item, total + bonus, d));
        }

        return results
            .OrderByDescending(r => r.Score)
            .ThenBy(r => _docs[r.Order].Name.Length)
            .ThenBy(r => r.Order)
            .Select(r => (r.Item, r.Score))
            .ToList();
    }
}
