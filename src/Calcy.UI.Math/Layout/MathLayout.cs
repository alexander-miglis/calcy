using Avalonia;
using Calcy.UI.Math.Model;

namespace Calcy.UI.Math.Layout;

/// <summary>The result of laying out a model: the box tree plus the geometry of every row for carets and hit testing.</summary>
public sealed class MathLayout : IMathCaretGeometry
{
    private readonly Dictionary<MathRow, MathRowBox> _rows;

    internal MathLayout(MathRowBox root, Dictionary<MathRow, MathRowBox> rows, MathBox? content = null)
    {
        RootRow = root;
        _rows = rows;
        Content = content ?? root;
    }

    /// <summary>The box of the root row (the first line when wrapped).</summary>
    public MathRowBox RootRow { get; }

    /// <summary>The box to draw (the root row, or the stacked lines when wrapped).</summary>
    public MathBox Content { get; }

    /// <summary>Total width.</summary>
    public double Width => Content.Width;

    /// <summary>Height above the first baseline.</summary>
    public double Ascent => Content.Ascent;

    /// <summary>Depth below the first baseline.</summary>
    public double Descent => Content.Descent;

    /// <summary>Total height.</summary>
    public double Height => Content.Height;

    /// <summary>The laid-out rows by model row.</summary>
    public IReadOnlyDictionary<MathRow, MathRowBox> Rows => _rows;

    /// <summary>Positions every box with the content's baseline origin at <paramref name="origin"/>.</summary>
    public void Arrange(Point origin) => Place(Content, origin);

    private static void Place(MathBox box, Point origin)
    {
        box.Origin = origin;
        foreach (var c in box.Children) Place(c, new Point(origin.X + c.OffsetX, origin.Y + c.OffsetY));
    }

    /// <summary>The laid-out box of a row, or null when the row is not part of this layout.</summary>
    public MathRowBox? Find(MathRow row) => _rows.GetValueOrDefault(row);

    /// <summary>Every box in drawing order.</summary>
    public IEnumerable<MathBox> AllBoxes()
    {
        var stack = new Stack<MathBox>();
        stack.Push(Content);
        while (stack.Count > 0)
        {
            var b = stack.Pop();
            yield return b;
            for (int i = b.Children.Count - 1; i >= 0; i--) stack.Push(b.Children[i]);
        }
    }

    /// <summary>The caret position nearest to a point: the deepest row under it, then the nearest gap.</summary>
    public MathCaret HitTest(Point p)
    {
        MathRowBox? best = null;
        int bestDepth = -1;
        double bestArea = double.MaxValue;
        foreach (var rb in _rows.Values)
        {
            var r = rb.Bounds.Inflate(1);
            if (!r.Contains(p)) continue;
            int depth = rb.Row.Depth;
            double area = r.Width * r.Height;
            if (depth > bestDepth || (depth == bestDepth && area < bestArea))
            {
                best = rb;
                bestDepth = depth;
                bestArea = area;
            }
        }
        if (best is null)
        {
            double bestDist = double.MaxValue;
            foreach (var rb in _rows.Values)
            {
                var r = rb.Bounds;
                double dx = p.X < r.Left ? r.Left - p.X : p.X > r.Right ? p.X - r.Right : 0;
                double dy = p.Y < r.Top ? r.Top - p.Y : p.Y > r.Bottom ? p.Y - r.Bottom : 0;
                double d = dx * dx + dy * dy - rb.Row.Depth * 0.01;
                if (d < bestDist)
                {
                    bestDist = d;
                    best = rb;
                }
            }
        }
        if (best is null) return new MathCaret(RootRow.Row, 0);
        return new MathCaret(best.Row, NearestIndex(best.Row, p.X));
    }

    /// <summary>The block caret rectangle: the height of the row's text, as wide as the character it sits on.</summary>
    public Rect GetCaretRect(MathCaret caret)
    {
        if (Find(caret.Row) is not { } rb) return default;
        double f = rb.FontSize;
        var m = rb.Metrics;
        int idx = System.Math.Clamp(caret.Index, 0, rb.CaretX.Length - 1);
        double x = rb.Origin.X + rb.CaretX[idx];
        // Top level: a character cell at the end; inside a slot a narrower block so it does not cover what follows.
        double w = caret.Row.Owner is null ? 0.55 * f : 0.32 * f;
        if (caret.Row.IsEmpty && rb.Children.Count > 0) w = System.Math.Min(rb.Children[0].Width, 0.6 * f);
        else if (idx < rb.ItemBoxes.Length && rb.ItemBoxes[idx] is MathGlyphBox g) w = System.Math.Max(0.3 * f, g.Width);
        double top = rb.Origin.Y - (m.CapHeight + 0.14 * f);
        double bottom = rb.Origin.Y + 0.2 * f;
        return new Rect(x, top, w, bottom - top);
    }

    /// <summary>The highlight rectangle of a selected range of a row.</summary>
    public Rect GetRangeRect(MathRow row, int start, int end)
    {
        if (Find(row) is not { } rb) return default;
        double x0 = rb.Origin.X + rb.CaretX[System.Math.Clamp(start, 0, rb.CaretX.Length - 1)];
        double x1 = rb.Origin.X + rb.CaretX[System.Math.Clamp(end, 0, rb.CaretX.Length - 1)];
        var b = rb.Bounds;
        return new Rect(x0, b.Top, System.Math.Max(0, x1 - x0), b.Height);
    }

    /// <inheritdoc />
    public bool TryGetRowBounds(MathRow row, out Rect bounds)
    {
        if (Find(row) is { } rb)
        {
            bounds = rb.Bounds;
            return true;
        }
        bounds = default;
        return false;
    }

    /// <inheritdoc />
    public double GetCaretX(MathRow row, int index)
    {
        if (Find(row) is not { } rb) return 0;
        return rb.Origin.X + rb.CaretX[System.Math.Clamp(index, 0, rb.CaretX.Length - 1)];
    }

    /// <inheritdoc />
    public int NearestIndex(MathRow row, double x)
    {
        if (Find(row) is not { } rb) return 0;
        double local = x - rb.Origin.X;
        int best = 0;
        double bestD = double.MaxValue;
        for (int i = 0; i < rb.CaretX.Length; i++)
        {
            double d = System.Math.Abs(rb.CaretX[i] - local);
            if (d < bestD)
            {
                bestD = d;
                best = i;
            }
        }
        return best;
    }
}
