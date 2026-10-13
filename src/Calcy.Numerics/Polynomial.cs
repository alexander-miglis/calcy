namespace Calcy.Numerics;

/// <summary>
/// Polynomial utilities. Coefficient arrays are in ascending order of powers:
/// c[0] + c[1] x + c[2] x^2 + ... Both real and complex coefficients are supported.
/// </summary>
public static class Polynomial
{
    private const double Eps = NumericsCore.Eps;

    // ------------------------------------------------------------------ evaluation

    /// <summary>Evaluates the polynomial at x (Horner's scheme).</summary>
    public static double Evaluate(double[] c, double x)
    {
        ArgumentNullException.ThrowIfNull(c);
        double r = 0;
        for (int i = c.Length - 1; i >= 0; i--) r = r * x + c[i];
        return r;
    }

    /// <summary>Evaluates a real polynomial at a complex point.</summary>
    public static Complex Evaluate(double[] c, Complex z)
    {
        ArgumentNullException.ThrowIfNull(c);
        Complex r = Complex.Zero;
        for (int i = c.Length - 1; i >= 0; i--) r = r * z + c[i];
        return r;
    }

    /// <summary>Evaluates a complex polynomial at a complex point.</summary>
    public static Complex Evaluate(Complex[] c, Complex z)
    {
        ArgumentNullException.ThrowIfNull(c);
        Complex r = Complex.Zero;
        for (int i = c.Length - 1; i >= 0; i--) r = r * z + c[i];
        return r;
    }

    /// <summary>Degree after ignoring exact zero leading coefficients (-1 for the zero polynomial).</summary>
    public static int Degree(double[] c)
    {
        int d = c.Length - 1;
        while (d >= 0 && c[d] == 0) d--;
        return d;
    }

    /// <summary>Degree after ignoring exact zero leading coefficients (-1 for the zero polynomial).</summary>
    public static int Degree(Complex[] c)
    {
        int d = c.Length - 1;
        while (d >= 0 && c[d].IsZero) d--;
        return d;
    }

    /// <summary>Removes exact zero leading coefficients (keeps at least one coefficient).</summary>
    public static double[] Trim(double[] c)
    {
        int d = Math.Max(Degree(c), 0);
        var r = new double[d + 1];
        Array.Copy(c, r, Math.Min(c.Length, d + 1));
        return r;
    }

    private static Complex[] Trim(Complex[] c)
    {
        int d = Math.Max(Degree(c), 0);
        var r = new Complex[d + 1];
        Array.Copy(c, r, Math.Min(c.Length, d + 1));
        return r;
    }

    // ------------------------------------------------------------------ algebra

    /// <summary>Derivative.</summary>
    public static double[] Derivative(double[] c)
    {
        if (c.Length <= 1) return [0];
        var d = new double[c.Length - 1];
        for (int i = 1; i < c.Length; i++) d[i - 1] = i * c[i];
        return d;
    }

    /// <summary>Derivative.</summary>
    public static Complex[] Derivative(Complex[] c)
    {
        if (c.Length <= 1) return [Complex.Zero];
        var d = new Complex[c.Length - 1];
        for (int i = 1; i < c.Length; i++) d[i - 1] = i * c[i];
        return d;
    }

    /// <summary>Antiderivative with constant term <paramref name="constant"/>.</summary>
    public static double[] Integral(double[] c, double constant = 0)
    {
        var r = new double[c.Length + 1];
        r[0] = constant;
        for (int i = 0; i < c.Length; i++) r[i + 1] = c[i] / (i + 1);
        return r;
    }

    /// <summary>Sum.</summary>
    public static double[] Add(double[] a, double[] b)
    {
        var r = new double[Math.Max(a.Length, b.Length)];
        for (int i = 0; i < a.Length; i++) r[i] += a[i];
        for (int i = 0; i < b.Length; i++) r[i] += b[i];
        return r;
    }

    /// <summary>Difference.</summary>
    public static double[] Subtract(double[] a, double[] b)
    {
        var r = new double[Math.Max(a.Length, b.Length)];
        for (int i = 0; i < a.Length; i++) r[i] += a[i];
        for (int i = 0; i < b.Length; i++) r[i] -= b[i];
        return r;
    }

    /// <summary>Product.</summary>
    public static double[] Multiply(double[] a, double[] b)
    {
        if (a.Length == 0 || b.Length == 0) return [0];
        var r = new double[a.Length + b.Length - 1];
        for (int i = 0; i < a.Length; i++)
            for (int j = 0; j < b.Length; j++) r[i + j] += a[i] * b[j];
        return r;
    }

    /// <summary>Product.</summary>
    public static Complex[] Multiply(Complex[] a, Complex[] b)
    {
        if (a.Length == 0 || b.Length == 0) return [Complex.Zero];
        var r = new Complex[a.Length + b.Length - 1];
        for (int i = 0; i < a.Length; i++)
            for (int j = 0; j < b.Length; j++) r[i + j] += a[i] * b[j];
        return r;
    }

    /// <summary>Polynomial long division a = q b + r with deg r &lt; deg b.</summary>
    /// <exception cref="DivideByZeroException">b is the zero polynomial.</exception>
    public static (double[] Quotient, double[] Remainder) DivideWithRemainder(double[] a, double[] b)
    {
        int db = Degree(b);
        if (db < 0) throw new DivideByZeroException("Division by the zero polynomial.");
        int da = Degree(a);
        if (da < db) return ([0], Trim(a));
        var rem = (double[])Trim(a).Clone();
        var q = new double[da - db + 1];
        double lead = b[db];
        for (int k = da - db; k >= 0; k--)
        {
            double coef = rem[k + db] / lead;
            q[k] = coef;
            for (int j = 0; j <= db; j++) rem[k + j] -= coef * b[j];
            rem[k + db] = 0;
        }
        var r = new double[Math.Max(db, 1)];
        Array.Copy(rem, r, Math.Min(r.Length, rem.Length));
        return (q, Trim(r));
    }

    /// <summary>Polynomial long division for complex coefficients.</summary>
    public static (Complex[] Quotient, Complex[] Remainder) DivideWithRemainder(Complex[] a, Complex[] b)
    {
        int db = Degree(b);
        if (db < 0) throw new DivideByZeroException("Division by the zero polynomial.");
        int da = Degree(a);
        if (da < db) return ([Complex.Zero], Trim(a));
        var rem = (Complex[])Trim(a).Clone();
        var q = new Complex[da - db + 1];
        Complex lead = b[db];
        for (int k = da - db; k >= 0; k--)
        {
            Complex coef = rem[k + db] / lead;
            q[k] = coef;
            for (int j = 0; j <= db; j++) rem[k + j] -= coef * b[j];
            rem[k + db] = Complex.Zero;
        }
        var r = new Complex[Math.Max(db, 1)];
        Array.Copy(rem, r, Math.Min(r.Length, rem.Length));
        return (q, Trim(r));
    }

    /// <summary>Monic polynomial with the given real roots.</summary>
    public static double[] FromRoots(params double[] roots)
    {
        double[] p = [1];
        foreach (double r in roots) p = Multiply(p, [-r, 1]);
        return p;
    }

    /// <summary>Monic polynomial with the given complex roots.</summary>
    public static Complex[] FromRoots(params Complex[] roots)
    {
        Complex[] p = [Complex.One];
        foreach (Complex r in roots) p = Multiply(p, [-r, Complex.One]);
        return p;
    }

    // ------------------------------------------------------------------ roots

    /// <summary>
    /// All complex roots of a real polynomial (with multiplicity), by the Aberth-Ehrlich
    /// simultaneous iteration started from Newton-polygon radii, followed by Newton polishing,
    /// cluster (multiple root) refinement and symmetric cleaning of tiny imaginary parts.
    /// Roots are sorted by real part, then imaginary part. Leading zero coefficients are ignored.
    /// </summary>
    /// <exception cref="ArgumentException">The polynomial is identically zero or has non-finite coefficients.</exception>
    public static Complex[] AllRoots(double[] coefficients, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(coefficients);
        foreach (double c in coefficients)
            if (!double.IsFinite(c)) throw new ArgumentException("Polynomial coefficients must be finite.");
        var cc = coefficients.Select(c => new Complex(c, 0)).ToArray();
        return RootsCore(cc, true, cancellationToken);
    }

    /// <summary>All complex roots of a complex polynomial (with multiplicity); see <see cref="AllRoots(double[], CancellationToken)"/>.</summary>
    public static Complex[] AllRoots(Complex[] coefficients, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(coefficients);
        foreach (var c in coefficients)
            if (!c.IsFinite) throw new ArgumentException("Polynomial coefficients must be finite.");
        bool real = coefficients.All(c => c.Im == 0);
        return RootsCore(coefficients, real, cancellationToken);
    }

    /// <summary>The real roots (imaginary part cleaned to zero) of a real polynomial, ascending, with multiplicity.</summary>
    public static double[] RealRoots(double[] coefficients, CancellationToken cancellationToken = default) =>
        AllRoots(coefficients, cancellationToken).Where(z => z.Im == 0).Select(z => z.Re).OrderBy(x => x).ToArray();

    private static Complex[] RootsCore(Complex[] coefficients, bool realCoefficients, CancellationToken ct)
    {
        int deg = Degree(coefficients);
        if (deg < 0) throw new ArgumentException("The zero polynomial has infinitely many roots.");
        var roots = new List<Complex>();
        // Exact zero roots from trailing zero coefficients.
        int low = 0;
        while (low < deg && coefficients[low].IsZero) { roots.Add(Complex.Zero); low++; }
        var c = new Complex[deg - low + 1];
        Array.Copy(coefficients, low, c, 0, c.Length);
        int n = c.Length - 1;
        if (n == 1) roots.Add(-c[0] / c[1]);
        else if (n == 2) roots.AddRange(Quadratic(c, realCoefficients));
        else if (n > 2) roots.AddRange(Aberth(c, realCoefficients, ct));
        var result = roots.ToArray();
        Array.Sort(result, (p, q) =>
        {
            int cmp = p.Re.CompareTo(q.Re);
            return cmp != 0 ? cmp : p.Im.CompareTo(q.Im);
        });
        return result;
    }

    private static Complex[] Quadratic(Complex[] c, bool real)
    {
        if (real)
        {
            double a = c[2].Re, b = c[1].Re, cc = c[0].Re;
            double disc = Discriminant(a, b, cc);
            if (disc >= 0)
            {
                double sq = Math.Sqrt(disc);
                double q = -0.5 * (b + (b >= 0 ? sq : -sq));
                if (q == 0) return [Complex.Zero, Complex.Zero];
                double r1 = q / a, r2 = cc / q;
                return [r1, r2];
            }
            double re = -b / (2 * a), im = Math.Sqrt(-disc) / (2 * Math.Abs(a));
            return [new Complex(re, -im), new Complex(re, im)];
        }
        Complex A = c[2], B = c[1], C = c[0];
        Complex d = Complex.Sqrt(B * B - 4 * A * C);
        // Choose the sign that avoids cancellation.
        Complex qq = (B.Re * d.Re + B.Im * d.Im) >= 0 ? -0.5 * (B + d) : -0.5 * (B - d);
        if (qq.IsZero) return [Complex.Zero, Complex.Zero];
        return [qq / A, C / qq];
    }

    /// <summary>b^2 - 4ac with Kahan's FMA trick to avoid cancellation.</summary>
    private static double Discriminant(double a, double b, double c)
    {
        double p = b * b, q = 4 * a * c;
        double d = p - q;
        if (3 * Math.Abs(d) >= p + q) return d;
        double dp = Math.FusedMultiplyAdd(b, b, -p);
        double dq = Math.FusedMultiplyAdd(4 * a, c, -q);
        return d + (dp - dq);
    }

    /// <summary>p(z) and p'(z) with a running rounding error bound for p(z).</summary>
    private static (Complex p, Complex dp, double bound) Horner(Complex[] c, Complex z)
    {
        int n = c.Length - 1;
        Complex p = c[n], dp = Complex.Zero;
        double az = Complex.Abs(z);
        double s = Complex.Abs(c[n]);
        for (int i = n - 1; i >= 0; i--)
        {
            dp = dp * z + p;
            p = p * z + c[i];
            s = s * az + Complex.Abs(c[i]);
        }
        return (p, dp, s);
    }

    /// <summary>Newton correction p(z)/p'(z), computed via the reversed polynomial when |z| &gt; 1 to avoid overflow.</summary>
    private static (Complex correction, bool converged) NewtonCorrection(Complex[] c, Complex[] rev, Complex z)
    {
        int n = c.Length - 1;
        if (Complex.Abs(z) <= 1)
        {
            var (p, dp, s) = Horner(c, z);
            bool conv = Complex.Abs(p) <= 4 * n * Eps * s;
            if (dp.IsZero) return (p.IsZero ? Complex.Zero : new Complex(Complex.Abs(z) * 1e-8 + 1e-300, 0), conv);
            return (p / dp, conv);
        }
        Complex w = Complex.One / z;
        var (q, dq, sq) = Horner(rev, w);
        bool convR = Complex.Abs(q) <= 4 * n * Eps * sq;
        // p/p' = z / (n - w q'(w)/q(w))
        if (q.IsZero) return (Complex.Zero, true);
        Complex denom = n - w * dq / q;
        if (denom.IsZero) return (new Complex(Complex.Abs(z) * 1e-8, 0), convR);
        return (z / denom, convR);
    }

    private static Complex[] Aberth(Complex[] c, bool real, CancellationToken ct)
    {
        int n = c.Length - 1;
        var rev = new Complex[n + 1];
        for (int i = 0; i <= n; i++) rev[i] = c[n - i];
        Complex[] z = InitialPoints(c);
        var done = new bool[n];
        // Iterations spent with |p(z)| already below the a priori rounding bound. That bound is a
        // worst case and can be met far from the root for ill-conditioned polynomials
        // (Wilkinson), so a few more iterations are taken before the root is frozen.
        var belowBound = new int[n];
        for (int iter = 0; iter < 2000; iter++)
        {
            ct.ThrowIfCancellationRequested();
            bool all = true;
            for (int i = 0; i < n; i++)
            {
                if (done[i]) continue;
                var (N, conv) = NewtonCorrection(c, rev, z[i]);
                if (conv && ++belowBound[i] > 10) { done[i] = true; continue; }
                if (N.IsZero) { done[i] = true; continue; }
                all = false;
                Complex sum = Complex.Zero;
                for (int j = 0; j < n; j++)
                {
                    if (j == i) continue;
                    Complex d = z[i] - z[j];
                    if (d.IsZero) d = new Complex(Eps * (1 + Complex.Abs(z[i])), 0);
                    sum += Complex.One / d;
                }
                Complex w = N / (Complex.One - N * sum);
                if (!w.IsFinite) w = N;
                z[i] -= w;
                if (Complex.Abs(w) <= Eps * Complex.Abs(z[i])) done[i] = true;
            }
            if (all) break;
        }

        // Newton polishing on the original polynomial (accept only if the residual drops).
        for (int i = 0; i < n; i++)
        {
            for (int k = 0; k < 3; k++)
            {
                var (p, dp, _) = Horner(c, z[i]);
                if (dp.IsZero || p.IsZero) break;
                Complex zn = z[i] - p / dp;
                var (pn, _, _) = Horner(c, zn);
                if (!zn.IsFinite || !(Complex.Abs(pn) < Complex.Abs(p))) break;
                z[i] = zn;
            }
        }

        RefineClusters(c, z, real);
        if (real) CleanConjugates(c, z);
        return z;
    }

    /// <summary>Initial approximations on circles whose radii come from the Newton polygon of |c_k| (Bini).</summary>
    private static Complex[] InitialPoints(Complex[] c)
    {
        int n = c.Length - 1;
        var logs = new double[n + 1];
        for (int k = 0; k <= n; k++)
        {
            double a = Complex.Abs(c[k]);
            logs[k] = a > 0 ? Math.Log(a) : double.NegativeInfinity;
        }
        // Upper convex hull of (k, log|c_k|).
        var hull = new List<int>();
        for (int k = 0; k <= n; k++)
        {
            if (double.IsNegativeInfinity(logs[k])) continue;
            while (hull.Count >= 2)
            {
                int i1 = hull[^2], i2 = hull[^1];
                double cross = (i2 - i1) * (logs[k] - logs[i1]) - (k - i1) * (logs[i2] - logs[i1]);
                if (cross >= 0) hull.RemoveAt(hull.Count - 1);
                else break;
            }
            hull.Add(k);
        }
        var z = new Complex[n];
        int idx = 0;
        const double sigma = 0.7;
        for (int h = 0; h + 1 < hull.Count; h++)
        {
            int k1 = hull[h], k2 = hull[h + 1];
            int m = k2 - k1;
            double r = Math.Exp((logs[k1] - logs[k2]) / m);
            for (int j = 0; j < m && idx < n; j++)
            {
                double theta = 2 * Math.PI / m * j + 2 * Math.PI * h / n + sigma;
                z[idx++] = Complex.FromPolar(r, theta);
            }
        }
        while (idx < n) z[idx] = Complex.FromPolar(1, 2 * Math.PI * idx++ / n + sigma);
        return z;
    }

    /// <summary>Weierstrass-type inclusion radius for each approximation.</summary>
    private static double[] InclusionRadii(Complex[] c, Complex[] z)
    {
        int n = z.Length;
        var rho = new double[n];
        double lead = Complex.Abs(c[n]);
        for (int i = 0; i < n; i++)
        {
            var (p, _, s) = Horner(c, z[i]);
            double bound = Complex.Abs(p) + 4 * n * Eps * s;
            double logProd = Math.Log(lead);
            bool zeroDist = false;
            for (int j = 0; j < n; j++)
            {
                if (j == i) continue;
                double d = Complex.Abs(z[i] - z[j]);
                if (d == 0) { zeroDist = true; break; }
                logProd += Math.Log(d);
            }
            rho[i] = zeroDist ? double.PositiveInfinity : n * Math.Exp(Math.Log(Math.Max(bound, 1e-300)) - logProd);
            if (bound == 0) rho[i] = 0;
        }
        return rho;
    }

    /// <summary>
    /// Groups approximations whose inclusion disks overlap; each group of m &gt; 1 is replaced by
    /// the root of the (m-1)th derivative near the group centroid (an accurate multiple root).
    /// </summary>
    private static void RefineClusters(Complex[] c, Complex[] z, bool real)
    {
        int n = z.Length;
        var rho = InclusionRadii(c, z);
        var parent = new int[n];
        for (int i = 0; i < n; i++) parent[i] = i;
        int Find(int i) { while (parent[i] != i) i = parent[i] = parent[parent[i]]; return i; }
        for (int i = 0; i < n; i++)
            for (int j = i + 1; j < n; j++)
            {
                double d = Complex.Abs(z[i] - z[j]);
                double scale = Math.Max(1.0, Math.Max(Complex.Abs(z[i]), Complex.Abs(z[j])));
                if (d <= rho[i] + rho[j] && d <= 1e-2 * scale) parent[Find(i)] = Find(j);
            }
        var groups = Enumerable.Range(0, n).GroupBy(Find).Where(g => g.Count() > 1);
        foreach (var g in groups)
        {
            int[] members = g.ToArray();
            int m = members.Length;
            Complex centroid = Complex.Zero;
            foreach (int i in members) centroid += z[i];
            centroid /= m;
            // Newton on p^(m-1) from the centroid.
            Complex[] d = c;
            for (int k = 0; k < m - 1; k++) d = Derivative(d);
            Complex[] dd = Derivative(d);
            Complex x = centroid;
            double res = Complex.Abs(Evaluate(d, x));
            for (int k = 0; k < 8; k++)
            {
                Complex dv = Evaluate(dd, x);
                if (dv.IsZero || res == 0) break;
                Complex xn = x - Evaluate(d, x) / dv;
                double rn = Complex.Abs(Evaluate(d, xn));
                if (!xn.IsFinite || !(rn < res)) break;
                x = xn; res = rn;
            }
            // Only accept if x stays within the cluster.
            double spread = members.Max(i => Complex.Abs(z[i] - centroid));
            if (Complex.Abs(x - centroid) > 2 * spread + 1e-12 * Math.Max(1.0, Complex.Abs(centroid))) x = centroid;
            if (real && Math.Abs(x.Im) <= spread + 1e-14 * Math.Max(1.0, Complex.Abs(x)) && MembersSymmetric(z, members)) x = new Complex(x.Re, 0);
            foreach (int i in members) z[i] = x;
        }
    }

    private static bool MembersSymmetric(Complex[] z, int[] members)
    {
        // A cluster of a real polynomial can only collapse to a real point if it is closed under conjugation.
        double imSum = members.Sum(i => z[i].Im);
        double scale = members.Max(i => Math.Abs(z[i].Im)) + 1e-300;
        return Math.Abs(imSum) <= 1e-6 * scale * members.Length || members.Length % 2 == 1;
    }

    /// <summary>For real coefficients: zero imaginary parts that are within the inclusion radius, and pair conjugates.</summary>
    private static void CleanConjugates(Complex[] c, Complex[] z)
    {
        int n = z.Length;
        var rho = InclusionRadii(c, z);
        for (int i = 0; i < n; i++)
        {
            if (z[i].Im == 0) continue;
            // Exact copies come from the multiple-root refinement, which already decided realness.
            bool duplicated = false;
            for (int j = 0; j < n && !duplicated; j++) duplicated = j != i && z[j] == z[i];
            if (duplicated) continue;
            double tol = Math.Max(rho[i], 8 * Eps * Complex.Abs(z[i]));
            // Look for a conjugate partner.
            int partner = -1;
            double best = double.PositiveInfinity;
            for (int j = 0; j < n; j++)
            {
                if (j == i) continue;
                double d = Complex.Abs(z[j] - Complex.Conjugate(z[i]));
                if (d < best) { best = d; partner = j; }
            }
            bool closeToAxis = Math.Abs(z[i].Im) <= tol;
            if (closeToAxis && (partner < 0 || Math.Abs(z[partner].Im) <= Math.Max(rho[partner], 8 * Eps * Complex.Abs(z[partner])) || best > tol))
            {
                z[i] = new Complex(z[i].Re, 0);
                // Re-polish as a real root.
                double x = z[i].Re;
                double[] rc = c.Select(v => v.Re).ToArray();
                double[] rd = Derivative(rc);
                double px = Evaluate(rc, x);
                for (int k = 0; k < 3; k++)
                {
                    double d = Evaluate(rd, x);
                    if (d == 0 || px == 0) break;
                    double xn = x - px / d;
                    double pn = Evaluate(rc, xn);
                    if (!(Math.Abs(pn) < Math.Abs(px))) break;
                    x = xn; px = pn;
                }
                z[i] = new Complex(x, 0);
            }
        }
        // Symmetrize conjugate pairs.
        var used = new bool[n];
        for (int i = 0; i < n; i++)
        {
            if (used[i] || z[i].Im <= 0) continue;
            int partner = -1;
            double best = double.PositiveInfinity;
            for (int j = 0; j < n; j++)
            {
                if (used[j] || j == i || z[j].Im >= 0) continue;
                double d = Complex.Abs(z[j] - Complex.Conjugate(z[i]));
                if (d < best) { best = d; partner = j; }
            }
            if (partner >= 0 && best <= 1e-6 * Math.Max(1.0, Complex.Abs(z[i])))
            {
                double re = 0.5 * (z[i].Re + z[partner].Re), im = 0.5 * (z[i].Im - z[partner].Im);
                z[i] = new Complex(re, im);
                z[partner] = new Complex(re, -im);
                used[i] = used[partner] = true;
            }
        }
    }
}
