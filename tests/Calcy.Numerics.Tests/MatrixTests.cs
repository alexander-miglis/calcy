namespace Calcy.Numerics.Tests;

public class MatrixTests
{
    private static Matrix M(params double[][] rows) => Matrix.FromRows(rows);

    private static Matrix RandomMatrix(int rows, int cols, int seed)
    {
        var rng = new Random(seed);
        var m = new Matrix(rows, cols);
        for (int i = 0; i < rows; i++)
            for (int j = 0; j < cols; j++) m[i, j] = rng.NextDouble() * 2 - 1;
        return m;
    }

    [Fact]
    public void BasicOperations()
    {
        var a = M([1, 2], [3, 4]);
        var b = M([5, 6], [7, 8]);
        Assert.Equal(M([19, 22], [43, 50]), a * b);
        Assert.Equal(M([6, 8], [10, 12]), a + b);
        Assert.Equal(M([-4, -4], [-4, -4]), a - b);
        Assert.Equal(M([1, 3], [2, 4]), a.Transpose());
        Assert.Equal(M([2, 4], [6, 8]), 2 * a);
        Assert.Equal(a, a * Matrix.Identity(2));
        Assert.Equal(new double[] { 5, 11 }, a.Multiply([1, 2]));
        Assert.Equal(5, a.Trace());
        Assert.Equal("[[1, 2], [3, 4]]", a.ToString());
    }

    [Fact]
    public void DimensionMismatchThrows()
    {
        Assert.Throws<ArgumentException>(() => M([1, 2]) * M([1, 2]));
        Assert.Throws<ArgumentException>(() => M([1, 2]) + M([1], [2]));
        Assert.Throws<ArgumentException>(() => M([1, 2]).Determinant());
    }

    [Fact]
    public void SubMatrixAugmentStack()
    {
        var a = M([1, 2, 3], [4, 5, 6], [7, 8, 9]);
        Assert.Equal(M([5, 6], [8, 9]), a.SubMatrix(1, 1, 2, 2));
        Assert.Equal(M([1, 0], [0, 1]), Matrix.Identity(2));
        Assert.Equal(M([1, 2, 5], [3, 4, 6]), M([1, 2], [3, 4]).Augment(Matrix.ColumnVector(5, 6)));
        Assert.Equal(M([1, 2], [3, 4]), M([1, 2]).Stack(M([3, 4])));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public void Determinants(int which)
    {
        var (a, det) = which switch
        {
            0 => (M([1, 2], [3, 4]), -2.0),
            1 => (M([6, 1, 1], [4, -2, 5], [2, 8, 7]), -306.0),
            _ => (M([2, 0, 0, 0], [0, 3, 0, 0], [0, 0, 4, 0], [1, 2, 3, 5]), 120.0),
        };
        Approx.Equal(det, a.Determinant(), 1e-13);
    }

    [Fact]
    public void SingularMatrix()
    {
        var a = M([1, 2, 3], [4, 5, 6], [7, 8, 9]);
        Assert.Equal(0, a.Determinant());
        Assert.True(new LuDecomposition(a).IsSingular);
        Assert.Throws<InvalidOperationException>(() => a.Inverse());
        Assert.Throws<InvalidOperationException>(() => a.Solve([1, 2, 3]));
    }

    [Fact]
    public void InverseAndSolve()
    {
        var a = M([4, 7], [2, 6]);
        Approx.Equal(M([0.6, -0.7], [-0.2, 0.4]), a.Inverse(), 1e-15);
        var b = M([2, 1, -1], [-3, -1, 2], [-2, 1, 2]);
        Approx.Equal(new[] { 2.0, 3.0, -1.0 }, b.Solve([8, -11, -3]), 1e-14);
        var x = b.Solve(M([8, 1], [-11, 0], [-3, 0]));
        Approx.Equal(new[] { 2.0, 3.0, -1.0 }, x.GetColumn(0), 1e-14);
    }

    [Fact]
    public void LuFactors()
    {
        var a = RandomMatrix(6, 6, 1);
        var lu = new LuDecomposition(a);
        Approx.Equal(lu.P * a, lu.L * lu.U, 1e-14);
    }

    [Fact]
    public void HilbertMatrices()
    {
        var h4 = Matrix.Hilbert(4);
        var inv = M([16, -120, 240, -140], [-120, 1200, -2700, 1680], [240, -2700, 6480, -4200], [-140, 1680, -4200, 2800]);
        Approx.Equal(inv, h4.Inverse(), 1e-8);
        Approx.Equal(476607.25, LinearAlgebra.ConditionNumber(Matrix.Hilbert(5)), 1e-6);
        // Ill-conditioned solve: the residual stays tiny even when the error grows with the condition number.
        var h10 = Matrix.Hilbert(10);
        var ones = Enumerable.Repeat(1.0, 10).ToArray();
        var rhs = h10.Multiply(ones);
        var sol = h10.Solve(rhs);
        var res = h10.Multiply(sol).Zip(rhs, (p, q) => Math.Abs(p - q)).Max();
        Assert.True(res < 1e-14, $"residual {res}");
        Assert.True(sol.Zip(ones, (p, q) => Math.Abs(p - q)).Max() < 1e-2);
        var sol6 = Matrix.Hilbert(6).Solve(Matrix.Hilbert(6).Multiply(ones.Take(6).ToArray()));
        Approx.Equal(ones.Take(6).ToArray(), sol6, 1e-8);
        Assert.Equal(10, h10.Rank());
    }

    [Fact]
    public void QrDecomposition()
    {
        var a = RandomMatrix(6, 4, 2);
        var qr = new QrDecomposition(a);
        var Q = qr.Q;
        Approx.Equal(Matrix.Identity(4), Q.Transpose() * Q, 1e-14);
        Approx.Equal(a, Q * qr.R, 1e-14);
        for (int i = 1; i < 4; i++)
            for (int j = 0; j < i; j++) Assert.Equal(0, qr.R[i, j]);
    }

    [Fact]
    public void LeastSquaresLineFit()
    {
        // Fit y = c0 + c1 x to (0,1), (1,3), (2,5), (3,7.3).
        var a = M([1, 0], [1, 1], [1, 2], [1, 3]);
        var c = LinearAlgebra.LeastSquares(a, [1, 3, 5, 7.3]);
        Approx.Equal(0.94, c[0], 1e-13);
        Approx.Equal(2.09, c[1], 1e-13);
    }

    [Fact]
    public void LeastSquaresUnderdeterminedIsMinimumNorm()
    {
        var x = LinearAlgebra.LeastSquares(M([1, 1]), [2]);
        Approx.Equal(new[] { 1.0, 1.0 }, x, 1e-14);
        var y = LinearAlgebra.LeastSquares(M([1, 2], [2, 4], [3, 6]), [1, 2, 3]);
        Approx.Equal(new[] { 0.2, 0.4 }, y, 1e-13);
    }

    [Fact]
    public void Cholesky()
    {
        var a = M([4, 12, -16], [12, 37, -43], [-16, -43, 98]);
        var ch = new CholeskyDecomposition(a);
        Approx.Equal(M([2, 0, 0], [6, 1, 0], [-8, 5, 3]), ch.L, 1e-14);
        Approx.Equal(36, ch.Determinant, 1e-13);
        Approx.Equal(new[] { 1.0, 2.0, 3.0 }, ch.Solve(a.Multiply([1, 2, 3])), 1e-12);
        Assert.Throws<InvalidOperationException>(() => new CholeskyDecomposition(M([1, 2], [2, 1])));
        Assert.Throws<InvalidOperationException>(() => new CholeskyDecomposition(M([1, 2], [3, 4])));
    }

    [Fact]
    public void Rank()
    {
        Assert.Equal(1, M([1, 2], [2, 4]).Rank());
        Assert.Equal(2, M([1, 2, 3], [4, 5, 6], [7, 8, 9]).Rank());
        Assert.Equal(0, new Matrix(3, 3).Rank());
        Assert.Equal(2, M([1, 0, 0], [0, 1, 0]).Rank());
    }

    [Fact]
    public void ReducedRowEchelonForm()
    {
        var r = M([1, 2, -1, -4], [2, 3, -1, -11], [-2, 0, -3, 22]).Rref();
        Approx.Equal(M([1, 0, 0, -8], [0, 1, 0, 1], [0, 0, 1, -2]), r, 1e-13);
        var s = LinearAlgebra.RrefWithPivots(M([1, 2, 3], [4, 5, 6], [7, 8, 9]));
        Approx.Equal(M([1, 0, -1], [0, 1, 2], [0, 0, 0]), s.Reduced, 1e-14);
        Assert.Equal(new[] { 0, 1 }, s.PivotColumns);
        Assert.Equal(0, s.Reduced[2, 2]);
    }

    [Fact]
    public void NullSpace()
    {
        var a = M([1, 2, 3], [4, 5, 6], [7, 8, 9]);
        var ns = LinearAlgebra.NullSpace(a);
        Assert.Equal(1, ns.Columns);
        var v = ns.GetColumn(0);
        Assert.True(a.Multiply(v).Max(Math.Abs) < 1e-14);
        Approx.Equal(1, LinearAlgebra.VectorNorm(v), 1e-14);
        Approx.Equal(-2, v[1] / v[0], 1e-12);
        var rr = LinearAlgebra.NullSpaceFromRref(a);
        Approx.Equal(new[] { 1.0, -2.0, 1.0 }, rr.GetColumn(0), 1e-14);
        // Wide matrix (more columns than rows).
        var w = LinearAlgebra.NullSpace(M([1, 1, 1], [0, 1, 2]));
        Assert.Equal(1, w.Columns);
        var u = w.GetColumn(0);
        Approx.Equal(-2, u[1] / u[0], 1e-12);
        Assert.Equal(0, LinearAlgebra.NullSpace(Matrix.Identity(3)).Columns);
        Assert.Equal(3, LinearAlgebra.NullSpace(new Matrix(2, 3)).Columns);
    }

    [Fact]
    public void ColumnSpace()
    {
        var cs = LinearAlgebra.ColumnSpace(M([1, 2], [2, 4], [3, 6]));
        Assert.Equal(1, cs.Columns);
        var v = cs.GetColumn(0);
        Approx.Equal(2, v[1] / v[0], 1e-13);
    }

    [Fact]
    public void SymmetricEigenvalues()
    {
        var e = M([2, 1], [1, 2]).Eigenvalues();
        Approx.Equal(new Complex(3, 0), e[0], 1e-15);
        Approx.Equal(new Complex(1, 0), e[1], 1e-15);
        var sym = LinearAlgebra.SymmetricEigen(M([4, 1, 2], [1, 3, 0], [2, 0, 5]));
        var V = sym.Vectors;
        Approx.Equal(Matrix.Identity(3), V.Transpose() * V, 1e-14);
        Approx.Equal(M([4, 1, 2], [1, 3, 0], [2, 0, 5]) * V, V * Matrix.Diagonal(sym.Values), 1e-13);
        Assert.True(sym.Values[0] >= sym.Values[1] && sym.Values[1] >= sym.Values[2]);
    }

    [Fact]
    public void SymmetricEigenOfHilbert()
    {
        var sym = LinearAlgebra.SymmetricEigen(Matrix.Hilbert(6));
        Approx.Equal(1.6188998589243393, sym.Values[0], 1e-13);
        Approx.Equal(1.0827994845655e-7, sym.Values[5], 1e-6);
    }

    [Fact]
    public void ComplexEigenvaluePair()
    {
        var e = M([0, -1], [1, 0]).Eigenvalues();
        Approx.Equal(new Complex(0, 1), e[0], 1e-15);
        Approx.Equal(new Complex(0, -1), e[1], 1e-15);
        var f = M([1, -2, 0], [2, 1, 0], [0, 0, 3]).Eigenvalues();
        Approx.Equal(new Complex(3, 0), f[0], 1e-14);
        Approx.Equal(new Complex(1, 2), f[1], 1e-14);
        Approx.Equal(new Complex(1, -2), f[2], 1e-14);
    }

    [Fact]
    public void GeneralRealEigenvalues()
    {
        var e = M([1, 2], [3, 4]).Eigenvalues();
        Approx.Equal(new Complex((5 + Math.Sqrt(33)) / 2, 0), e[0], 1e-14);
        Approx.Equal(new Complex((5 - Math.Sqrt(33)) / 2, 0), e[1], 1e-14);
        var tri = M([1, 5, 7], [0, 4, 2], [0, 0, 9]).Eigenvalues();
        Approx.Equal(new Complex(9, 0), tri[0]);
        Approx.Equal(new Complex(4, 0), tri[1]);
        Approx.Equal(new Complex(1, 0), tri[2]);
    }

    [Fact]
    public void SimilarityTransformKeepsSpectrum()
    {
        var S = RandomMatrix(5, 5, 9);
        var D = Matrix.Diagonal(5, 4, 3, 2, 1);
        var A = S * D * S.Inverse();
        var e = A.Eigenvalues();
        for (int k = 0; k < 5; k++) Approx.Equal(new Complex(5 - k, 0), e[k], 1e-9);
    }

    [Fact]
    public void LargeRandomMatrixEigenvalues()
    {
        var A = RandomMatrix(30, 30, 4);
        var e = A.Eigenvalues();
        var sum = e.Aggregate(Complex.Zero, (p, q) => p + q);
        Approx.Equal(A.Trace(), sum.Re, 1e-10, 1e-10);
        Assert.True(Math.Abs(sum.Im) < 1e-10);
        // Complex eigenvalues of a real matrix come in conjugate pairs.
        Assert.Equal(0, e.Count(z => z.Im != 0) % 2);
        var eig = LinearAlgebra.Eigen(A);
        for (int k = 0; k < 30; k++)
        {
            var v = eig.Vectors.GetColumn(k);
            var Av = ComplexMatrix.FromReal(A).Multiply(v);
            double res = Av.Zip(v, (p, q) => Complex.Abs(p - eig.Values[k] * q)).Max();
            Assert.True(res < 1e-10, $"residual {res} for eigenvalue {eig.Values[k]}");
        }
    }

    [Fact]
    public void EigenvectorsGeneral()
    {
        var A = M([1, -2, 0], [2, 1, 0], [0, 0, 3]);
        var eig = LinearAlgebra.Eigen(A);
        Assert.True(eig.IsDiagonalizable);
        var CA = ComplexMatrix.FromReal(A);
        for (int k = 0; k < 3; k++)
        {
            var v = eig.Vectors.GetColumn(k);
            var Av = CA.Multiply(v);
            for (int i = 0; i < 3; i++) Approx.Equal(eig.Values[k] * v[i], Av[i], 1e-13);
            Approx.Equal(1, Math.Sqrt(v.Sum(z => Complex.Abs(z) * Complex.Abs(z))), 1e-14);
        }
        // The eigenvector for 3 is real and points along e3.
        var v3 = eig.Vectors.GetColumn(0);
        Approx.Equal(new Complex(1, 0), v3[2], 1e-14);
        Assert.True(v3.All(z => z.Im == 0));
    }

    [Fact]
    public void DefectiveMatrix()
    {
        var eig = LinearAlgebra.Eigen(M([1, 1], [0, 1]));
        Assert.False(eig.IsDiagonalizable);
        Approx.Equal(new Complex(1, 0), eig.Values[0]);
        Approx.Equal(new Complex(1, 0), eig.Values[1]);
    }

    [Fact]
    public void RepeatedEigenvaluesGetIndependentVectors()
    {
        var A = M([2, 0, 0], [0, 2, 0], [1, 0, 3]);
        var eig = LinearAlgebra.Eigen(A);
        Assert.True(eig.IsDiagonalizable);
        var v1 = eig.Vectors.GetColumn(1);
        var v2 = eig.Vectors.GetColumn(2);
        Complex dot = Complex.Zero;
        for (int i = 0; i < 3; i++) dot += Complex.Conjugate(v1[i]) * v2[i];
        Assert.True(Complex.Abs(dot) < 0.99);
    }

    [Fact]
    public void Svd()
    {
        var a = M([3, 2, 2], [2, 3, -2]);
        var svd = new SvdDecomposition(a);
        Approx.Equal(new[] { 5.0, 3.0 }, svd.S, 1e-14);
        Approx.Equal(a, svd.U * Matrix.Diagonal(svd.S) * svd.V.Transpose(), 1e-14);
        var tall = RandomMatrix(7, 4, 3);
        var s2 = new SvdDecomposition(tall);
        Approx.Equal(tall, s2.U * Matrix.Diagonal(s2.S) * s2.V.Transpose(), 1e-14);
        Approx.Equal(Matrix.Identity(4), s2.U.Transpose() * s2.U, 1e-14);
        Approx.Equal(Matrix.Identity(4), s2.V.Transpose() * s2.V, 1e-14);
    }

    [Fact]
    public void SvdRankDeficient()
    {
        var svd = new SvdDecomposition(M([1, 2], [2, 4], [3, 6]));
        Approx.Equal(Math.Sqrt(70), svd.S[0], 1e-14);
        Assert.True(svd.S[1] < 1e-14);
        Assert.Equal(1, svd.Rank());
        Approx.Equal(Matrix.Identity(2), svd.U.Transpose() * svd.U, 1e-14);
    }

    [Fact]
    public void PseudoInverse()
    {
        var a = M([1, 2], [3, 4], [5, 6]);
        var p = LinearAlgebra.PseudoInverse(a);
        Approx.Equal(a, a * p * a, 1e-13);
        Approx.Equal(p, p * a * p, 1e-13);
        Approx.Equal(Matrix.Identity(2), p * a, 1e-13);
    }

    [Fact]
    public void Norms()
    {
        var a = M([1, -2], [3, 4]);
        Assert.Equal(6, a.Norm1());
        Assert.Equal(7, a.NormInf());
        Approx.Equal(Math.Sqrt(30), a.NormFrobenius(), 1e-15);
        Approx.Equal(Math.Sqrt(15 + 5 * Math.Sqrt(5)), a.Norm2(), 1e-14);
        Assert.Equal(double.PositiveInfinity, LinearAlgebra.ConditionNumber(M([1, 2], [2, 4])));
    }

    [Fact]
    public void MatrixExponential()
    {
        Approx.Equal(Matrix.Identity(3), new Matrix(3, 3).Exp(), 0);
        var rot = M([0, 1], [-1, 0]).Exp();
        Approx.Equal(M([Math.Cos(1), Math.Sin(1)], [-Math.Sin(1), Math.Cos(1)]), rot, 1e-15);
        Approx.Equal(Matrix.Diagonal(Math.E, Math.Exp(2)), Matrix.Diagonal(1, 2).Exp(), 1e-14);
        // Moler and Van Loan's example: eigenvalues -1 and -17.
        var A = M([-49, 24], [-64, 31]);
        var V = M([1, 3], [2, 4]);
        var expected = V * Matrix.Diagonal(Math.Exp(-1), Math.Exp(-17)) * V.Inverse();
        Approx.Equal(expected, A.Exp(), 1e-12);
        var big = M([0, 30], [-30, 0]).Exp();
        Approx.Equal(M([Math.Cos(30), Math.Sin(30)], [-Math.Sin(30), Math.Cos(30)]), big, 1e-12);
    }

    [Fact]
    public void MatrixPowers()
    {
        var f = M([1, 1], [1, 0]);
        Assert.Equal(M([89, 55], [55, 34]), f.Power(10));
        Assert.Equal(Matrix.Identity(2), f.Power(0));
        Approx.Equal(f.Inverse(), f.Power(-1), 1e-15);
        Approx.Equal(f.Inverse() * f.Inverse(), f.Power(-2), 1e-14);
    }

    [Fact]
    public void GramSchmidt()
    {
        var a = M([1, 1, 2], [1, 0, 1], [0, 1, 1]);
        var q = LinearAlgebra.GramSchmidt(a);
        Assert.Equal(2, q.Columns); // third column = first + second
        Approx.Equal(Matrix.Identity(2), q.Transpose() * q, 1e-15);
    }

    [Fact]
    public void CharacteristicPolynomial()
    {
        Assert.Equal(new double[] { -2, -5, 1 }, LinearAlgebra.CharacteristicPolynomial(M([1, 2], [3, 4])));
        Assert.Equal(new double[] { -22, 35, -14, 1 }, LinearAlgebra.CharacteristicPolynomial(M([2, 0, 0], [0, 3, 4], [0, 4, 9])));
        var a = M([2, -1, 0, 3], [1, 4, 2, 0], [0, 5, -3, 1], [7, 0, 1, 1]);
        var p = LinearAlgebra.CharacteristicPolynomial(a);
        Approx.Equal(a.Determinant(), p[0], 1e-13);
        Approx.Equal(-a.Trace(), p[3], 1e-15);
        foreach (var lambda in a.Eigenvalues())
            Assert.True(Complex.Abs(Polynomial.Evaluate(p, lambda)) < 1e-9);
    }

    [Fact]
    public void ComplexMatrices()
    {
        var i = new Complex(0, 1);
        var a = new ComplexMatrix(new Complex[,] { { 1, i }, { i, 1 } });
        Approx.Equal(new Complex(2, 0), a.Determinant(), 1e-15);
        var inv = a.Inverse();
        Assert.True((a * inv).ApproximatelyEquals(ComplexMatrix.Identity(2), 1e-15));
        var x = a.Solve([new Complex(1, 1), new Complex(1, 1)]);
        var b = a.Multiply(x);
        Approx.Equal(new Complex(1, 1), b[0], 1e-15);
        Approx.Equal(new Complex(1, 1), b[1], 1e-15);
        var h = a.ConjugateTranspose();
        Approx.Equal(new Complex(0, -1), h[0, 1]);
        Assert.Throws<InvalidOperationException>(() => new ComplexMatrix(new Complex[,] { { 1, i }, { i, -1 } }).Inverse());
    }

    [Fact]
    public void Vectors()
    {
        Assert.Equal(new double[] { 0, 0, 1 }, LinearAlgebra.Cross([1, 0, 0], [0, 1, 0]));
        Assert.Equal(32, LinearAlgebra.Dot([1, 2, 3], [4, 5, 6]));
        Approx.Equal(Math.PI / 2, LinearAlgebra.AngleBetween([1, 0], [0, 3]), 1e-15);
        Approx.Equal(1e-9, LinearAlgebra.AngleBetween([1, 0], [1, 1e-9]), 1e-6);
        Approx.Equal(new[] { 1.0, 0.0 }, LinearAlgebra.Projection([1, 1], [2, 0]), 1e-15);
    }
}
