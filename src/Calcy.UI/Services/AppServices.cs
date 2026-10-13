using System.Text.Json;
using System.Text.Json.Serialization;
using Avalonia.Controls;
using Calcy.Core;
using Calcy.UI.Theming;

namespace Calcy.UI.Services;

/// <summary>A mode (Simple, Advanced, Graphing, ...). Each mode has its own screen.</summary>
public interface ICalcyMode
{
    /// <summary>Stable id, lower case (used for navigation and preferences).</summary>
    string Id { get; }

    string Title { get; }
    string Description { get; }

    /// <summary>Keyboard shortcut text shown in the mode menu, for example "Ctrl+2".</summary>
    string Shortcut { get; }

    /// <summary>Colour token used for the mode's dot in the menu (for example "Sel" or "G2").</summary>
    string AccentToken { get; }

    /// <summary>PixelIcon kind.</summary>
    string Icon { get; }

    /// <summary>Creates the screen. Called once; the view is kept while switching modes.</summary>
    Control CreateView(AppServices services);
}

/// <summary>Implemented by mode views that accept data from another mode (for example "plot this").</summary>
public interface IReceivesPayload
{
    void Receive(object payload);
}

/// <summary>Implemented by views that run work in the background (lets tests and screenshots wait for it).</summary>
public interface IBusy
{
    bool IsBusy { get; }
}

/// <summary>Implemented by views that want keyboard focus in a specific place when they are shown.</summary>
public interface IFocusFirst
{
    void FocusFirst();
}

/// <summary>Ask a mode to plot expressions (Graphing) or show them.</summary>
public sealed record PlotRequest(IReadOnlyList<string> Expressions);

/// <summary>Ask a mode to put text in its input (and optionally evaluate it).</summary>
public sealed record InputRequest(string Text, bool Evaluate = false);

/// <summary>Open a formula from the formula collection.</summary>
public sealed record FormulaRequest(string FormulaId);

/// <summary>Send numeric data (for example a list or table) to Statistics or Matrix.</summary>
public sealed record DataRequest(string Name, IReadOnlyList<IReadOnlyList<double>> Columns, IReadOnlyList<string>? ColumnNames = null);

public sealed record NavigationRequest(string ModeId, object? Payload);

/// <summary>Everything shared between modes: the calculator session, settings, colour scheme and navigation.</summary>
public sealed class AppServices
{
    public CalcSettings Settings { get; }
    public CalcSession Session { get; }
    public ThemeManager Theme { get; }
    public Preferences Prefs { get; }
    public IReadOnlyList<ICalcyMode> Modes { get; set; } = [];

    public event EventHandler<NavigationRequest>? NavigationRequested;

    /// <summary>Raised when calculator settings change (angle unit, number format, ...), so screens can refresh.</summary>
    public event EventHandler? SettingsChanged;

    /// <summary>Shown briefly in the status bar.</summary>
    public event EventHandler<string>? StatusMessage;

    public AppServices(CalcSession session, ThemeManager theme, Preferences prefs)
    {
        Session = session;
        Settings = session.Settings;
        Theme = theme;
        Prefs = prefs;
        prefs.ApplyTo(Settings);
    }

    public void Navigate(string modeId, object? payload = null) =>
        NavigationRequested?.Invoke(this, new NavigationRequest(modeId, payload));

    public void NotifySettingsChanged()
    {
        Prefs.CaptureFrom(Settings);
        Prefs.Save();
        SettingsChanged?.Invoke(this, EventArgs.Empty);
    }

    public void ShowStatus(string message) => StatusMessage?.Invoke(this, message);

    public void SetScheme(string schemeId)
    {
        Theme.Apply(schemeId);
        Prefs.SchemeId = schemeId;
        Prefs.Save();
    }
}

/// <summary>User preferences stored as JSON in the user's application data folder (works on Windows, macOS and Linux).</summary>
public sealed class Preferences
{
    public string SchemeId { get; set; } = ColorSchemes.DefaultId;
    public string LastMode { get; set; } = "advanced";
    public AngleMode Angle { get; set; } = AngleMode.Radian;
    public ResultPreference Results { get; set; } = ResultPreference.ExactFirst;
    public NumberFormatMode Format { get; set; } = NumberFormatMode.Float;
    public int Digits { get; set; } = 10;
    public ImplicitMultiplication Implicit { get; set; } = ImplicitMultiplication.BindsTighter;
    public ComplexMode Complex { get; set; } = ComplexMode.Auto;
    public bool Dither { get; set; } = true;
    public bool KeySounds { get; set; }
    public bool McpEnabled { get; set; }
    public double UiScale { get; set; } = 1.0;
    public double WindowWidth { get; set; } = 1360;
    public double WindowHeight { get; set; } = 880;

    /// <summary>Free-form per-mode state (for example the last graph window), keyed by mode id.</summary>
    public Dictionary<string, JsonElement> ModeState { get; set; } = [];

    [JsonIgnore] public string? FilePath { get; private set; }

    private static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    public static string DefaultPath()
    {
        var dir = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData, Environment.SpecialFolderOption.Create);
        if (string.IsNullOrEmpty(dir)) dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".config");
        return Path.Combine(dir, "Calcy", "settings.json");
    }

    public static Preferences Load(string? path = null)
    {
        path ??= DefaultPath();
        try
        {
            if (File.Exists(path))
            {
                var p = JsonSerializer.Deserialize<Preferences>(File.ReadAllText(path), Json) ?? new Preferences();
                p.FilePath = path;
                return p;
            }
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            // A broken preferences file must never stop the app; start fresh.
        }
        return new Preferences { FilePath = path };
    }

    /// <summary>Stops writing to disk (used for screenshots and tests).</summary>
    public void Disable() => FilePath = null;

    public void Save()
    {
        if (FilePath is null) return;
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            var tmp = FilePath + ".tmp";
            File.WriteAllText(tmp, JsonSerializer.Serialize(this, Json));
            File.Move(tmp, FilePath, overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Preferences are a convenience; ignore write failures.
        }
    }

    public void ApplyTo(CalcSettings s)
    {
        s.Angle = Angle;
        s.Results = Results;
        s.Format = Format;
        s.Digits = Math.Clamp(Digits, 1, 30);
        s.Implicit = Implicit;
        s.Complex = Complex;
    }

    public void CaptureFrom(CalcSettings s)
    {
        Angle = s.Angle;
        Results = s.Results;
        Format = s.Format;
        Digits = s.Digits;
        Implicit = s.Implicit;
        Complex = s.Complex;
    }

    public T? GetModeState<T>(string modeId)
    {
        if (!ModeState.TryGetValue(modeId, out var el)) return default;
        try { return el.Deserialize<T>(Json); }
        catch (JsonException) { return default; }
    }

    public void SetModeState<T>(string modeId, T value) => ModeState[modeId] = JsonSerializer.SerializeToElement(value, Json);
}
