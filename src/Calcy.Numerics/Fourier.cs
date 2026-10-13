namespace Calcy.Numerics;

/// <summary>Window functions for spectral analysis.</summary>
public enum WindowType
{
    /// <summary>All ones.</summary>
    Rectangular,

    /// <summary>Hann window.</summary>
    Hann,

    /// <summary>Hamming window.</summary>
    Hamming,

    /// <summary>Blackman window.</summary>
    Blackman,
}

/// <summary>
/// Discrete Fourier transforms of any length: iterative radix-2 FFT for powers of two and
/// Bluestein's chirp-z algorithm otherwise. Convention: X[k] = sum_n x[n] exp(-2 pi i k n / N),
/// and the inverse includes the 1/N factor.
/// </summary>
public static class Fourier
{
    /// <summary>Forward DFT.</summary>
    public static Complex[] Forward(Complex[] x)
    {
        ArgumentNullException.ThrowIfNull(x);
        int n = x.Length;
        if (n <= 1) return (Complex[])x.Clone();
        var a = (Complex[])x.Clone();
        if ((n & (n - 1)) == 0) Radix2(a, false);
        else a = Bluestein(a);
        return a;
    }

    /// <summary>Inverse DFT (with the 1/N factor).</summary>
    public static Complex[] Inverse(Complex[] spectrum)
    {
        ArgumentNullException.ThrowIfNull(spectrum);
        int n = spectrum.Length;
        if (n == 0) return [];
        var c = spectrum.Select(Complex.Conjugate).ToArray();
        var y = Forward(c);
        for (int i = 0; i < n; i++) y[i] = Complex.Conjugate(y[i]) / n;
        return y;
    }

    /// <summary>Forward DFT of real data; returns the full spectrum of length N (Hermitian symmetric).</summary>
    public static Complex[] ForwardReal(double[] x)
    {
        ArgumentNullException.ThrowIfNull(x);
        int n = x.Length;
        if (n < 4 || n % 2 != 0) return Forward(x.Select(v => new Complex(v, 0)).ToArray());
        // Pack pairs of real samples into one complex sequence of half length.
        int h = n / 2;
        var z = new Complex[h];
        for (int m = 0; m < h; m++) z[m] = new Complex(x[2 * m], x[2 * m + 1]);
        var Z = Forward(z);
        var X = new Complex[n];
        for (int k = 0; k < h; k++)
        {
            Complex zk = Z[k], zc = Complex.Conjugate(Z[(h - k) % h]);
            Complex even = 0.5 * (zk + zc);
            Complex odd = new Complex(0, -0.5) * (zk - zc);
            Complex w = Twiddle(k, n);
            X[k] = even + w * odd;
            X[k + h] = even - w * odd;
        }
        return X;
    }

    /// <summary>Real part of the inverse DFT (use for spectra of real signals).</summary>
    public static double[] InverseReal(Complex[] spectrum) => Inverse(spectrum).Select(z => z.Re).ToArray();

    /// <summary>Magnitudes |X[k]|.</summary>
    public static double[] Magnitudes(Complex[] spectrum) => spectrum.Select(Complex.Abs).ToArray();

    /// <summary>Linear convolution of two real sequences (length a + b - 1).</summary>
    public static double[] Convolve(double[] a, double[] b)
    {
        ArgumentNullException.ThrowIfNull(a);
        ArgumentNullException.ThrowIfNull(b);
        if (a.Length == 0 || b.Length == 0) return [];
        int n = a.Length + b.Length - 1;
        if ((long)a.Length * b.Length <= 4096 || Math.Min(a.Length, b.Length) <= 16)
        {
            var r = new double[n];
            for (int i = 0; i < a.Length; i++)
                for (int j = 0; j < b.Length; j++) r[i + j] += a[i] * b[j];
            return r;
        }
        var ca = Convolve(a.Select(v => new Complex(v, 0)).ToArray(), b.Select(v => new Complex(v, 0)).ToArray());
        return ca.Select(z => z.Re).ToArray();
    }

    /// <summary>Linear convolution of two complex sequences (length a + b - 1).</summary>
    public static Complex[] Convolve(Complex[] a, Complex[] b)
    {
        ArgumentNullException.ThrowIfNull(a);
        ArgumentNullException.ThrowIfNull(b);
        if (a.Length == 0 || b.Length == 0) return [];
        int n = a.Length + b.Length - 1;
        int m = 1;
        while (m < n) m <<= 1;
        var fa = new Complex[m];
        var fb = new Complex[m];
        Array.Copy(a, fa, a.Length);
        Array.Copy(b, fb, b.Length);
        Radix2(fa, false);
        Radix2(fb, false);
        for (int i = 0; i < m; i++) fa[i] *= fb[i];
        Radix2(fa, true);
        var r = new Complex[n];
        for (int i = 0; i < n; i++) r[i] = fa[i] / m;
        return r;
    }

    /// <summary>Symmetric window of length n.</summary>
    public static double[] Window(WindowType type, int n)
    {
        if (n < 1) throw new ArgumentOutOfRangeException(nameof(n), "Window length must be at least 1.");
        var w = new double[n];
        if (n == 1) { w[0] = 1; return w; }
        for (int k = 0; k < n; k++)
        {
            double t = 2 * Math.PI * k / (n - 1);
            w[k] = type switch
            {
                WindowType.Hann => 0.5 - 0.5 * Math.Cos(t),
                WindowType.Hamming => 0.54 - 0.46 * Math.Cos(t),
                WindowType.Blackman => 0.42 - 0.5 * Math.Cos(t) + 0.08 * Math.Cos(2 * t),
                _ => 1,
            };
        }
        return w;
    }

    /// <summary>exp(-2 pi i k / n) computed accurately.</summary>
    private static Complex Twiddle(long k, long n)
    {
        k %= n;
        if (k < 0) k += n;
        double angle = -2 * Math.PI * k / n;
        var (s, c) = Math.SinCos(angle);
        return new Complex(c, s);
    }

    /// <summary>In-place iterative radix-2 FFT (length must be a power of two).</summary>
    private static void Radix2(Complex[] a, bool inverse)
    {
        int n = a.Length;
        if (n <= 1) return;
        for (int i = 1, j = 0; i < n; i++)
        {
            int bit = n >> 1;
            for (; (j & bit) != 0; bit >>= 1) j ^= bit;
            j ^= bit;
            if (i < j) (a[i], a[j]) = (a[j], a[i]);
        }
        // Twiddle table exp(-+2 pi i k / n) for k < n/2.
        var tw = new Complex[n / 2];
        for (int k = 0; k < n / 2; k++)
        {
            Complex w = Twiddle(k, n);
            tw[k] = inverse ? Complex.Conjugate(w) : w;
        }
        for (int len = 2; len <= n; len <<= 1)
        {
            int half = len >> 1, step = n / len;
            for (int i = 0; i < n; i += len)
            {
                for (int k = 0; k < half; k++)
                {
                    Complex w = tw[k * step];
                    Complex u = a[i + k], v = a[i + k + half] * w;
                    a[i + k] = u + v;
                    a[i + k + half] = u - v;
                }
            }
        }
    }

    /// <summary>Bluestein's algorithm: DFT of any length via a power-of-two convolution.</summary>
    private static Complex[] Bluestein(Complex[] x)
    {
        int n = x.Length;
        int m = 1;
        while (m < 2 * n - 1) m <<= 1;
        // Chirp w_k = exp(-i pi k^2 / n), with k^2 reduced modulo 2n for accuracy.
        var chirp = new Complex[n];
        long twoN = 2L * n;
        for (int k = 0; k < n; k++)
        {
            long k2 = (long)k * k % twoN;
            double angle = -Math.PI * k2 / n;
            var (s, c) = Math.SinCos(angle);
            chirp[k] = new Complex(c, s);
        }
        var a = new Complex[m];
        var b = new Complex[m];
        for (int k = 0; k < n; k++) a[k] = x[k] * chirp[k];
        b[0] = Complex.Conjugate(chirp[0]);
        for (int k = 1; k < n; k++)
        {
            Complex c = Complex.Conjugate(chirp[k]);
            b[k] = c;
            b[m - k] = c;
        }
        Radix2(a, false);
        Radix2(b, false);
        for (int i = 0; i < m; i++) a[i] *= b[i];
        Radix2(a, true);
        var r = new Complex[n];
        for (int k = 0; k < n; k++) r[k] = a[k] / m * chirp[k];
        return r;
    }
}
