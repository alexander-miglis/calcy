namespace Calcy.Modes.Notebook.Model;

/// <summary>The user's notebooks: a list with one open notebook; new, rename, duplicate and delete.</summary>
public sealed class NotebookLibrary
{
    private readonly List<NotebookDocument> _notebooks = [];
    private NotebookDocument _current;

    private NotebookLibrary(IEnumerable<NotebookDocument> notebooks, string? currentId)
    {
        _notebooks.AddRange(notebooks);
        if (_notebooks.Count == 0) _notebooks.Add(CreateEmpty(UniqueName("Untitled notebook")));
        _current = _notebooks.FirstOrDefault(n => n.Id == currentId) ?? _notebooks[0];
    }

    /// <summary>The notebooks in list order.</summary>
    public IReadOnlyList<NotebookDocument> Notebooks => _notebooks;

    /// <summary>The open notebook.</summary>
    public NotebookDocument Current => _current;

    /// <summary>Raised when the list or the open notebook changes.</summary>
    public event EventHandler? Changed;

    /// <summary>A library with the sample notebook from the design board.</summary>
    public static NotebookLibrary CreateDefault() => new([SampleNotebooks.PendulumLab()], null);

    /// <summary>Restores a library; falls back to the sample notebook when nothing was saved.</summary>
    public static NotebookLibrary FromData(NotebookStoreData? data)
    {
        if (data?.Notebooks is not { Count: > 0 } list) return CreateDefault();
        var docs = new List<NotebookDocument>();
        foreach (var n in list)
        {
            try
            {
                docs.Add(NotebookDocument.FromData(n));
            }
            catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or NullReferenceException or FormatException)
            {
                // A damaged notebook is skipped rather than stopping the screen from opening.
            }
        }
        return docs.Count == 0 ? CreateDefault() : new NotebookLibrary(docs, data.Current);
    }

    /// <summary>Saved form.</summary>
    public NotebookStoreData ToData() => new()
    {
        Current = _current.Id,
        Notebooks = [.. _notebooks.Select(n => n.ToData())],
    };

    /// <summary>Opens a notebook.</summary>
    public void Select(NotebookDocument doc)
    {
        if (!_notebooks.Contains(doc) || doc == _current) return;
        _current = doc;
        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Adds a new notebook (a title and an empty maths cell) after the open one and opens it.</summary>
    public NotebookDocument Create(string? name = null)
    {
        var doc = CreateEmpty(UniqueName(string.IsNullOrWhiteSpace(name) ? "Untitled notebook" : name.Trim()));
        _notebooks.Insert(_notebooks.IndexOf(_current) + 1, doc);
        _current = doc;
        Changed?.Invoke(this, EventArgs.Empty);
        return doc;
    }

    /// <summary>Renames a notebook (names stay unique).</summary>
    public void Rename(NotebookDocument doc, string name)
    {
        if (string.IsNullOrWhiteSpace(name) || !_notebooks.Contains(doc)) return;
        name = name.Trim();
        if (name == doc.Name) return;
        doc.Name = UniqueName(name, doc);
        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Copies a notebook (inputs only), opens the copy and returns it.</summary>
    public NotebookDocument Duplicate(NotebookDocument doc)
    {
        if (!_notebooks.Contains(doc)) throw new InvalidOperationException("The notebook is not in the library.");
        var copy = doc.Clone(UniqueName(doc.Name + " copy"));
        _notebooks.Insert(_notebooks.IndexOf(doc) + 1, copy);
        _current = copy;
        Changed?.Invoke(this, EventArgs.Empty);
        return copy;
    }

    /// <summary>Deletes a notebook. The last notebook is replaced by an empty one, so there is always one open.</summary>
    public void Delete(NotebookDocument doc)
    {
        int i = _notebooks.IndexOf(doc);
        if (i < 0) return;
        _notebooks.RemoveAt(i);
        if (_notebooks.Count == 0) _notebooks.Add(CreateEmpty(UniqueName("Untitled notebook")));
        if (_current == doc) _current = _notebooks[Math.Min(i, _notebooks.Count - 1)];
        Changed?.Invoke(this, EventArgs.Empty);
    }

    private string UniqueName(string name, NotebookDocument? except = null)
    {
        bool Taken(string n) => _notebooks.Any(d => d != except && string.Equals(d.Name, n, StringComparison.OrdinalIgnoreCase));
        if (!Taken(name)) return name;
        for (int k = 2; ; k++)
        {
            var candidate = $"{name} {k}";
            if (!Taken(candidate)) return candidate;
        }
    }

    private static NotebookDocument CreateEmpty(string name) => new(name, [new TextCell("# " + name), new MathCell()]);
}
