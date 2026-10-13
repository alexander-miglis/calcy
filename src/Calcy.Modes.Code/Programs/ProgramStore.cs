using Calcy.Modes.Code.Editor;

namespace Calcy.Modes.Code.Programs;

/// <summary>One of the user's programs.</summary>
public sealed class ProgramFile
{
    /// <summary>The file name, for example "primes.py".</summary>
    public string Name { get; set; } = "main.py";

    /// <summary>The Python source.</summary>
    public string Source { get; set; } = "";
}

/// <summary>A finished run kept in the console between sessions.</summary>
public sealed class SavedRun
{
    /// <summary>The run number shown as #n.</summary>
    public int Number { get; set; }

    /// <summary>The program's file name, or "REPL".</summary>
    public string File { get; set; } = "";

    /// <summary>Duration and steps, or how it stopped.</summary>
    public string Meta { get; set; } = "";

    /// <summary>The end of the output.</summary>
    public string Output { get; set; } = "";

    /// <summary>True when the run failed or was stopped.</summary>
    public bool Failed { get; set; }
}

/// <summary>Everything Code mode keeps in the preferences (Prefs mode state "code").</summary>
public sealed class CodeState
{
    /// <summary>The user's programs in tab order.</summary>
    public List<ProgramFile> Programs { get; set; } = [];

    /// <summary>The open program.</summary>
    public string? Open { get; set; }

    /// <summary>Caret and scroll position per program.</summary>
    public Dictionary<string, EditorViewState> Views { get; set; } = [];

    /// <summary>REPL input history, oldest first.</summary>
    public List<string> History { get; set; } = [];

    /// <summary>The most recent run cards, newest first.</summary>
    public List<SavedRun> Runs { get; set; } = [];

    /// <summary>The number the next run gets.</summary>
    public int NextRun { get; set; } = 1;

    /// <summary>The side panel shown: "canvas", "graph" or "vars".</summary>
    public string? Panel { get; set; }
}

/// <summary>The user's programs: unique names, new, rename, duplicate and delete.</summary>
public sealed class ProgramStore
{
    /// <summary>Longest file name allowed.</summary>
    public const int MaxNameLength = 40;

    private readonly List<ProgramFile> _programs = [];

    /// <summary>Raised after programs are added, renamed or removed.</summary>
    public event EventHandler? Changed;

    /// <summary>The programs in tab order.</summary>
    public IReadOnlyList<ProgramFile> Programs => _programs;

    /// <summary>Replaces all programs (used when restoring state).</summary>
    public void Load(IEnumerable<ProgramFile> programs)
    {
        _programs.Clear();
        foreach (var p in programs)
        {
            if (string.IsNullOrWhiteSpace(p.Name)) continue;
            var name = Clean(p.Name) ?? "main.py";
            _programs.Add(new ProgramFile { Name = UniqueName(name), Source = p.Source ?? "" });
        }
        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>The program with this name, or null.</summary>
    public ProgramFile? Find(string? name) => _programs.FirstOrDefault(p => string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// Cleans a name typed by the user: trims it, adds ".py" and rejects path characters. Returns null when nothing usable is left.
    /// </summary>
    public static string? Clean(string? name)
    {
        if (string.IsNullOrWhiteSpace(name)) return null;
        var t = name.Trim();
        foreach (var c in Path.GetInvalidFileNameChars().Concat(['/', '\\', ':', '*', '?', '"', '<', '>', '|']))
            t = t.Replace(c.ToString(), "");
        t = t.Trim().TrimStart('.');
        if (t.EndsWith(".py", StringComparison.OrdinalIgnoreCase)) t = t[..^3].TrimEnd();
        if (t.Length == 0) return null;
        if (t.Length > MaxNameLength - 3) t = t[..(MaxNameLength - 3)].TrimEnd();
        return t + ".py";
    }

    /// <summary>A name not used by another program: "name.py", "name 2.py", "name 3.py" and so on.</summary>
    public string UniqueName(string wanted, ProgramFile? except = null)
    {
        var clean = Clean(wanted) ?? "main.py";
        bool Taken(string n) => _programs.Any(p => p != except && string.Equals(p.Name, n, StringComparison.OrdinalIgnoreCase));
        if (!Taken(clean)) return clean;
        var stem = clean[..^3];
        for (int i = 2; ; i++)
        {
            var candidate = $"{stem} {i}.py";
            if (!Taken(candidate)) return candidate;
        }
    }

    /// <summary>Adds a program with a unique name after the given program (or at the end).</summary>
    public ProgramFile Add(string name, string source, ProgramFile? after = null)
    {
        var p = new ProgramFile { Name = UniqueName(name), Source = source };
        int at = after is null ? _programs.Count : _programs.IndexOf(after) + 1;
        _programs.Insert(Math.Clamp(at, 0, _programs.Count), p);
        Changed?.Invoke(this, EventArgs.Empty);
        return p;
    }

    /// <summary>Renames a program; returns false when the name is not usable.</summary>
    public bool Rename(ProgramFile program, string name)
    {
        var clean = Clean(name);
        if (clean is null || !_programs.Contains(program)) return false;
        program.Name = UniqueName(clean, program);
        Changed?.Invoke(this, EventArgs.Empty);
        return true;
    }

    /// <summary>Copies a program next to itself as "name 2.py".</summary>
    public ProgramFile Duplicate(ProgramFile program) => Add(program.Name, program.Source, program);

    /// <summary>Removes a program and returns the one to show next. The last program is replaced by an empty main.py.</summary>
    public ProgramFile Delete(ProgramFile program)
    {
        int index = _programs.IndexOf(program);
        if (index < 0) return _programs.FirstOrDefault() ?? Add("main.py", "");
        _programs.RemoveAt(index);
        if (_programs.Count == 0)
        {
            var fresh = new ProgramFile { Name = "main.py", Source = "" };
            _programs.Add(fresh);
        }
        Changed?.Invoke(this, EventArgs.Empty);
        return _programs[Math.Clamp(index - 1, 0, _programs.Count - 1)];
    }
}
