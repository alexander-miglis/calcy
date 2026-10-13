using Avalonia.Controls;
using Calcy.UI.Services;

namespace Calcy.Modes.Notebook;

/// <summary>Notebook mode: text, maths, tables, graphs and Python in one reactive document.</summary>
public sealed class NotebookMode : ICalcyMode
{
    /// <inheritdoc />
    public string Id => NotebookView.ModeId;

    /// <inheritdoc />
    public string Title => "Notebook";

    /// <inheritdoc />
    public string Description => "Text, maths and graphs in one document";

    /// <inheritdoc />
    public string Shortcut => "Ctrl+Shift+N";

    /// <inheritdoc />
    public string AccentToken => "G4";

    /// <inheritdoc />
    public string Icon => "notebook";

    /// <inheritdoc />
    public Control CreateView(AppServices services) => new NotebookView(services);
}
