using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace Calcy.Modes.Matrix.Views;

/// <summary>
/// Draws a matrix as a bracketed grid with aligned columns in the maths font. Entries are given as text, row by row.
/// An optional vertical bar marks an augmented matrix [A | b]. Colours and fonts come from the scheme tokens.
/// </summary>
public sealed class MatrixView : Border
{
    /// <summary>The entries, row by row.</summary>
    public static readonly StyledProperty<IReadOnlyList<IReadOnlyList<string>>?> CellsProperty =
        AvaloniaProperty.Register<MatrixView, IReadOnlyList<IReadOnlyList<string>>?>(nameof(Cells));

    /// <summary>Font size of the entries.</summary>
    public static readonly StyledProperty<double> CellFontSizeProperty =
        AvaloniaProperty.Register<MatrixView, double>(nameof(CellFontSize), 17);

    /// <summary>Draws a bar before this column; -1 for none.</summary>
    public static readonly StyledProperty<int> AugmentAtProperty =
        AvaloniaProperty.Register<MatrixView, int>(nameof(AugmentAt), -1);

    /// <summary>Thickness of the bracket lines.</summary>
    public static readonly StyledProperty<double> BracketThicknessProperty =
        AvaloniaProperty.Register<MatrixView, double>(nameof(BracketThickness), 3);

    /// <summary>Space between columns.</summary>
    public static readonly StyledProperty<double> ColumnGapProperty =
        AvaloniaProperty.Register<MatrixView, double>(nameof(ColumnGap), 14);

    /// <summary>Space between rows.</summary>
    public static readonly StyledProperty<double> RowGapProperty =
        AvaloniaProperty.Register<MatrixView, double>(nameof(RowGap), 4);

    static MatrixView()
    {
        CellsProperty.Changed.AddClassHandler<MatrixView>((v, _) => v.Rebuild());
        CellFontSizeProperty.Changed.AddClassHandler<MatrixView>((v, _) => v.Rebuild());
        AugmentAtProperty.Changed.AddClassHandler<MatrixView>((v, _) => v.Rebuild());
        BracketThicknessProperty.Changed.AddClassHandler<MatrixView>((v, _) => v.Rebuild());
        ColumnGapProperty.Changed.AddClassHandler<MatrixView>((v, _) => v.Rebuild());
        RowGapProperty.Changed.AddClassHandler<MatrixView>((v, _) => v.Rebuild());
    }

    /// <summary>Creates an empty matrix view.</summary>
    public MatrixView()
    {
        HorizontalAlignment = HorizontalAlignment.Left;
        VerticalAlignment = VerticalAlignment.Center;
    }

    /// <summary>The entries, row by row.</summary>
    public IReadOnlyList<IReadOnlyList<string>>? Cells
    {
        get => GetValue(CellsProperty);
        set => SetValue(CellsProperty, value);
    }

    /// <summary>Font size of the entries.</summary>
    public double CellFontSize
    {
        get => GetValue(CellFontSizeProperty);
        set => SetValue(CellFontSizeProperty, value);
    }

    /// <summary>Draws a bar before this column; -1 for none.</summary>
    public int AugmentAt
    {
        get => GetValue(AugmentAtProperty);
        set => SetValue(AugmentAtProperty, value);
    }

    /// <summary>Thickness of the bracket lines.</summary>
    public double BracketThickness
    {
        get => GetValue(BracketThicknessProperty);
        set => SetValue(BracketThicknessProperty, value);
    }

    /// <summary>Space between columns.</summary>
    public double ColumnGap
    {
        get => GetValue(ColumnGapProperty);
        set => SetValue(ColumnGapProperty, value);
    }

    /// <summary>Space between rows.</summary>
    public double RowGap
    {
        get => GetValue(RowGapProperty);
        set => SetValue(RowGapProperty, value);
    }

    /// <summary>The text shown in a cell (for tests and accessibility).</summary>
    public string CellText(int row, int col) => Cells?[row][col] ?? "";

    private void Rebuild()
    {
        var cells = Cells;
        if (cells is null || cells.Count == 0)
        {
            Child = null;
            return;
        }
        int rows = cells.Count, cols = cells.Max(r => r.Count);
        double t = BracketThickness, size = CellFontSize;
        int bar = AugmentAt > 0 && AugmentAt < cols ? AugmentAt : -1;

        var grid = new Grid
        {
            ColumnSpacing = ColumnGap,
            RowSpacing = RowGap,
            Margin = new Thickness(Math.Max(5, size * 0.4), 3),
            VerticalAlignment = VerticalAlignment.Center,
        };
        for (int c = 0; c < cols + (bar > 0 ? 1 : 0); c++) grid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));
        for (int r = 0; r < rows; r++) grid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
        for (int r = 0; r < rows; r++)
        {
            for (int c = 0; c < cols; c++)
            {
                var text = new TextBlock
                {
                    Text = c < cells[r].Count ? cells[r][c] : "",
                    FontSize = size,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center,
                    TextAlignment = TextAlignment.Center,
                };
                text.Bind(TextBlock.FontFamilyProperty, text.GetResourceObservable("Calcy.Font.Math"));
                Grid.SetRow(text, r);
                Grid.SetColumn(text, bar > 0 && c >= bar ? c + 1 : c);
                grid.Children.Add(text);
            }
        }
        if (bar > 0)
        {
            var line = new Border { Width = Math.Max(2, t - 1), HorizontalAlignment = HorizontalAlignment.Center };
            line.Bind(Border.BackgroundProperty, line.GetResourceObservable("Calcy.Ink"));
            Grid.SetColumn(line, bar);
            Grid.SetRowSpan(line, rows);
            grid.Children.Add(line);
        }

        var left = Bracket(new Thickness(t, t, 0, t), size);
        var right = Bracket(new Thickness(0, t, t, t), size);
        var outer = new DockPanel();
        DockPanel.SetDock(left, Dock.Left);
        DockPanel.SetDock(right, Dock.Right);
        outer.Children.Add(left);
        outer.Children.Add(right);
        outer.Children.Add(grid);
        Child = outer;
    }

    private static Border Bracket(Thickness thickness, double size)
    {
        var b = new Border { Width = Math.Max(6, Math.Round(size * 0.45)), BorderThickness = thickness };
        b.Bind(Border.BorderBrushProperty, b.GetResourceObservable("Calcy.Ink"));
        return b;
    }
}
