using Avalonia.Controls;
using Calcy.Modes.Matrix.Views;
using Calcy.UI.Services;

namespace Calcy.Modes.Matrix;

/// <summary>Matrix mode: a library of named matrices, a grid editor, exact linear algebra with steps, and result cards.</summary>
public sealed class MatrixMode : ICalcyMode
{
    /// <inheritdoc />
    public string Id => "matrix";

    /// <inheritdoc />
    public string Title => "Matrix";

    /// <inheritdoc />
    public string Description => "Edit matrices, det, inverse, eigen";

    /// <inheritdoc />
    public string Shortcut => "Ctrl+6";

    /// <inheritdoc />
    public string AccentToken => "Head";

    /// <inheritdoc />
    public string Icon => "matrix";

    /// <inheritdoc />
    public Control CreateView(AppServices services) => new MatrixModeView(services);
}
