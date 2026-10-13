using Avalonia.Controls;
using Calcy.UI.Services;

namespace Calcy.Modes.Code;

/// <summary>Code mode: a sandboxed Python editor with a console, a drawing canvas, graphs and a variables inspector.</summary>
public sealed class CodeMode : ICalcyMode
{
    /// <inheritdoc />
    public string Id => "code";

    /// <inheritdoc />
    public string Title => "Code";

    /// <inheritdoc />
    public string Description => "Sandboxed Python";

    /// <inheritdoc />
    public string Shortcut => "Ctrl+E";

    /// <inheritdoc />
    public string AccentToken => "G1";

    /// <inheritdoc />
    public string Icon => "code";

    /// <inheritdoc />
    public Control CreateView(AppServices services) => new CodeView(services);
}
