namespace Calcy.Graphing;

/// <summary>
/// Clips pixel-space polylines to a rectangle (Liang-Barsky), splitting them where they leave it.
/// Keeps drawing exact near asymptotes without sending huge coordinates to a renderer.
/// </summary>
public static class PolylineClipper
{
    /// <summary>Clips a polyline given in pixel coordinates; returns the visible runs.</summary>
    public static List<List<(double X, double Y)>> Clip(IReadOnlyList<(double X, double Y)> points, double x0, double y0, double x1, double y1, bool closed = false)
    {
        var runs = new List<List<(double X, double Y)>>();
        List<(double X, double Y)>? current = null;
        int n = points.Count;
        int segs = closed ? n : n - 1;
        for (int i = 0; i < segs; i++)
        {
            var a = points[i];
            var b = points[(i + 1) % n];
            if (!IsFinite(a) || !IsFinite(b))
            {
                current = null;
                continue;
            }
            if (!ClipSegment(ref a, ref b, x0, y0, x1, y1, out bool aMoved, out bool bMoved))
            {
                current = null;
                continue;
            }
            if (current is null || aMoved)
            {
                current = [a];
                runs.Add(current);
            }
            current.Add(b);
            if (bMoved) current = null;
        }
        return runs;
    }

    private static bool IsFinite((double X, double Y) p) => double.IsFinite(p.X) && double.IsFinite(p.Y);

    /// <summary>Clips the segment a-b to the rectangle; reports whether each end was moved.</summary>
    public static bool ClipSegment(ref (double X, double Y) a, ref (double X, double Y) b, double x0, double y0, double x1, double y1, out bool aMoved, out bool bMoved)
    {
        aMoved = bMoved = false;
        double dx = b.X - a.X, dy = b.Y - a.Y;
        double t0 = 0, t1 = 1;
        if (!Edge(-dx, a.X - x0, ref t0, ref t1) || !Edge(dx, x1 - a.X, ref t0, ref t1) ||
            !Edge(-dy, a.Y - y0, ref t0, ref t1) || !Edge(dy, y1 - a.Y, ref t0, ref t1))
            return false;
        var na = (a.X + t0 * dx, a.Y + t0 * dy);
        var nb = (a.X + t1 * dx, a.Y + t1 * dy);
        aMoved = t0 > 0;
        bMoved = t1 < 1;
        a = na;
        b = nb;
        return true;
    }

    private static bool Edge(double p, double q, ref double t0, ref double t1)
    {
        if (p == 0) return q >= 0;
        double r = q / p;
        if (p < 0)
        {
            if (r > t1) return false;
            if (r > t0) t0 = r;
        }
        else
        {
            if (r < t0) return false;
            if (r < t1) t1 = r;
        }
        return true;
    }
}
