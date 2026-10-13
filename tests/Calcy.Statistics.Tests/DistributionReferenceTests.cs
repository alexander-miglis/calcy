using Calcy.Statistics;

namespace Calcy.Statistics.Tests;

// Distribution reference values generated with 40 digit mpmath arithmetic.
public class DistributionReferenceTests
{
    [Theory]
    [InlineData(0.0, 0.0, 1.0, 0.3989422804014327)]
    [InlineData(1.5, 0.0, 1.0, 0.12951759566589172)]
    [InlineData(-3.0, 0.0, 1.0, 0.0044318484119380075)]
    [InlineData(10.0, 2.0, 3.0, 0.00379866200793248)]
    [InlineData(30.0, 0.0, 1.0, 1.4736461348785476e-196)]
    [InlineData(100.0, 98.0, 0.5, 0.0002676604515297707)]
    public void NormalPdf(double x, double mu, double sigma, double expected)
    {
        Tol.Rel(expected, CalcDistributions.NormalPdf(x, mu, sigma), 1e-14);
    }

    [Theory]
    [InlineData(-1.96, 1.96, 0.0, 1.0, 0.9500042097035591)]
    [InlineData(-1e+99, 1.5, 0.0, 1.0, 0.9331927987311419)]
    [InlineData(-10.0, -9.0, 0.0, 1.0, 1.1285122074235991e-19)]
    [InlineData(9.0, 10.0, 0.0, 1.0, 1.1285122074235991e-19)]
    [InlineData(-1.0, 1.0, 0.0, 1.0, 0.6826894921370859)]
    [InlineData(0.5, 0.6, 0.0, 1.0, 0.03428442097591331)]
    [InlineData(2.0, 1e+99, 0.0, 1.0, 0.02275013194817921)]
    [InlineData(-30.0, 30.0, 0.0, 1.0, 1.0)]
    [InlineData(60.0, 70.0, 50.0, 5.0, 0.02271846070634609)]
    [InlineData(1.0, 0.0, 0.0, 1.0, -0.3413447460685429)]
    [InlineData(-37.0, double.PositiveInfinity, 0.0, 1.0, 1.0)]
    [InlineData(8.0, 9.0, 0.0, 1.0, 6.21983198586583e-16)]
    [InlineData(-0.1, 0.1, 0.0, 1.0, 0.07965567455405796)]
    [InlineData(5.0, 30.0, 0.0, 1.0, 2.866515718791939e-07)]
    [InlineData(double.NegativeInfinity, -20.0, 0.0, 1.0, 2.7536241186062337e-89)]
    [InlineData(double.NegativeInfinity, -30.0, 0.0, 1.0, 4.906713927148187e-198)]
    public void NormalCdf(double lower, double upper, double mu, double sigma, double expected)
    {
        Tol.Rel(expected, CalcDistributions.NormalCdf(lower, upper, mu, sigma), 1e-13);
    }

    [Theory]
    [InlineData(0.975, 1.9599639845400538)]
    [InlineData(0.5, 0.0)]
    [InlineData(0.025, -1.9599639845400543)]
    [InlineData(1e-10, -6.361340902404057)]
    [InlineData(1e-300, -37.0470962993612)]
    [InlineData(0.9999999, 5.199337582290661)]
    [InlineData(0.3, -0.5244005127080408)]
    [InlineData(0.7, 0.5244005127080407)]
    [InlineData(0.95, 1.6448536269514722)]
    [InlineData(0.99, 2.3263478740408408)]
    [InlineData(0.01, -2.326347874040841)]
    public void InvNormLeft(double p, double expected)
    {
        Tol.Rel(expected, CalcDistributions.InvNorm(p), 1e-14);
    }

    [Theory]
    [InlineData(0.0, 1.0, 0.3183098861837907)]
    [InlineData(1.5, 2.0, 0.11413441178180375)]
    [InlineData(2.0, 10.0, 0.061145766321218174)]
    [InlineData(-3.0, 5.5, 0.01633078118527233)]
    [InlineData(0.1, 1000000.0, 0.3969524462640489)]
    [InlineData(50.0, 3.0, 5.280077606481894e-07)]
    [InlineData(10000000000.0, 4.0, 1.2e-49)]
    [InlineData(2.0, 10000000000.0, 0.05399096652263647)]
    [InlineData(1.0, 0.5, 0.11830465704039261)]
    public void TPdf(double x, double df, double expected)
    {
        Tol.Rel(expected, CalcDistributions.TPdf(x, df), 1e-13);
    }

    [Theory]
    [InlineData(-2.0, 2.0, 10.0, 0.9266119652292596)]
    [InlineData(-1e+99, 2.228138852, 10.0, 0.9750000000005817)]
    [InlineData(1.5, 1e+99, 3.0, 0.11529193262241152)]
    [InlineData(-1e+99, -50.0, 3.0, 8.808576020635988e-06)]
    [InlineData(-1e+99, -100000.0, 2.5, 2.274751923175622e-13)]
    [InlineData(0.1, 0.2, 1000000.0, 0.039431861850907135)]
    [InlineData(-1.0, 1.0, 1.0, 0.5)]
    [InlineData(2.0, 3.0, 1.5, 0.045320288050568903)]
    [InlineData(-1e+99, 0.5, 30.0, 0.6896384975574363)]
    [InlineData(-1e+99, 1.96, 100000000.0, 0.975002103465336)]
    [InlineData(3.0, 4.0, 100.0, 0.001643195849514344)]
    [InlineData(-1e+99, -8.0, 7.0, 4.5574605854376814e-05)]
    [InlineData(-1e+99, 1.96, 10000.0, 0.9749882398840835)]
    [InlineData(-1e+99, -4.0, 1000000.0, 3.167351700502405e-05)]
    [InlineData(0.5, 1e+99, 100000.0, 0.308538088827209)]
    public void TCdf(double lower, double upper, double df, double expected)
    {
        Tol.Rel(expected, CalcDistributions.TCdf(lower, upper, df), 1e-13);
    }

    [Theory]
    [InlineData(0.975, 10.0, 2.2281388519862744)]
    [InlineData(0.95, 5.0, 2.0150483733330233)]
    [InlineData(0.995, 2.5, 7.163728138948783)]
    [InlineData(0.025, 30.0, -2.042272456301238)]
    [InlineData(0.9, 1.0, 3.077683537175254)]
    [InlineData(0.6, 3.0, 0.27667066233268983)]
    [InlineData(0.5000001, 20.0, 2.538144595402267e-07)]
    [InlineData(1e-10, 4.0, -416.1751403296041)]
    [InlineData(0.999999, 100000.0, 4.753704716385476)]
    [InlineData(0.75, 2.0, 0.816496580927726)]
    public void InvT(double p, double df, double expected)
    {
        Tol.Rel(expected, CalcDistributions.InvT(p, df), 1e-13);
    }

    [Theory]
    [InlineData(1.0, 1.0, 0.24197072451914334)]
    [InlineData(3.84, 1.0, 0.02984688748306056)]
    [InlineData(0.5, 3.0, 0.2196956447338612)]
    [InlineData(10.0, 10.0, 0.08773368488392536)]
    [InlineData(100.0, 80.0, 0.008599852478731192)]
    [InlineData(1e-05, 0.5, 1304.245222524182)]
    [InlineData(50.0, 2.0, 6.9439719324820104e-12)]
    [InlineData(2.0, 2.0, 0.18393972058572117)]
    public void Chi2Pdf(double x, double df, double expected)
    {
        Tol.Rel(expected, CalcDistributions.Chi2Pdf(x, df), 1e-14);
    }

    [Theory]
    [InlineData(0.0, 3.841458821, 1.0, 0.950000000009121)]
    [InlineData(5.0, 1e+99, 3.0, 0.17179714429673312)]
    [InlineData(0.0, 18.307, 10.0, 0.9499994109086018)]
    [InlineData(100.0, 1e+99, 50.0, 3.454931382984864e-05)]
    [InlineData(1.0, 2.0, 0.5, 0.08559246379821366)]
    [InlineData(300.0, 1e+99, 10.0, 1.5546747543803181e-58)]
    [InlineData(0.0, 1e-06, 4.0, 1.2499995833334113e-13)]
    [InlineData(70.0, 90.0, 80.0, 0.5718086251420893)]
    public void Chi2Cdf(double lower, double upper, double df, double expected)
    {
        Tol.Rel(expected, CalcDistributions.Chi2Cdf(lower, upper, df), 1e-13);
    }

    [Theory]
    [InlineData(0.95, 1.0, 3.8414588206941245)]
    [InlineData(0.95, 10.0, 18.307038053275143)]
    [InlineData(0.05, 3.0, 0.3518463177492714)]
    [InlineData(0.5, 0.5, 0.08734760470574682)]
    [InlineData(0.999, 4.0, 18.46682695290317)]
    public void InvChi2Left(double p, double df, double expected)
    {
        Tol.Rel(expected, CalcDistributions.InvChi2(p, df), 1e-13);
    }

    [Theory]
    [InlineData(0.05, 1.0, 3.841458820694126)]
    [InlineData(1e-12, 5.0, 65.23863621336784)]
    [InlineData(0.01, 20.0, 37.56623478662505)]
    public void InvChi2Right(double q, double df, double expected)
    {
        Tol.Rel(expected, CalcDistributions.InvChi2(q, df, Tail.Right), 1e-13);
    }

    [Theory]
    [InlineData(1.0, 2.0, 3.0, 0.27885480092693404)]
    [InlineData(0.5, 5.0, 10.0, 0.6876070027706234)]
    [InlineData(2.0, 1.0, 1.0, 0.07502635967975883)]
    [InlineData(3.0, 10.0, 20.0, 0.02720626900992)]
    [InlineData(0.1, 0.5, 4.0, 1.0161052685058172)]
    [InlineData(5.0, 100.0, 200.0, 2.5972718333078285e-21)]
    public void FPdf(double x, double d1, double d2, double expected)
    {
        Tol.Rel(expected, CalcDistributions.FPdf(x, d1, d2), 1e-13);
    }

    [Theory]
    [InlineData(0.0, 3.5, 2.0, 3.0, 0.8356832327484501)]
    [InlineData(2.0, 1e+99, 5.0, 10.0, 0.16419495089973882)]
    [InlineData(0.5, 1.5, 10.0, 20.0, 0.6592139116509872)]
    [InlineData(10.0, 1e+99, 4.0, 50.0, 5.027723214087906e-06)]
    [InlineData(0.0, 1.0, 1.0, 1.0, 0.5)]
    [InlineData(100.0, 1e+99, 3.0, 3.0, 0.0016675259263060872)]
    public void FCdf(double lower, double upper, double d1, double d2, double expected)
    {
        Tol.Rel(expected, CalcDistributions.FCdf(lower, upper, d1, d2), 1e-13);
    }

    [Theory]
    [InlineData(0.95, 2.0, 3.0, 9.552094495921153)]
    [InlineData(0.05, 5.0, 10.0, 0.21119042878234492)]
    [InlineData(0.5, 1.0, 1.0, 1.0)]
    [InlineData(0.99, 10.0, 20.0, 3.3681863891887422)]
    public void InvF(double p, double d1, double d2, double expected)
    {
        Tol.Rel(expected, CalcDistributions.InvF(p, d1, d2), 1e-13);
    }

    [Theory]
    [InlineData(10.0, 0.5, 5.0, 0.24609375)]
    [InlineData(20.0, 0.3, 4.0, 0.13042097437387065)]
    [InlineData(100.0, 0.01, 0.0, 0.3660323412732295)]
    [InlineData(100.0, 0.99, 100.0, 0.3660323412732292)]
    [InlineData(1000.0, 0.5, 500.0, 0.0252250181783608)]
    [InlineData(1000000.0, 0.3, 300000.0, 0.0008705631546366808)]
    [InlineData(50.0, 0.2, 49.0, 2.251799813685254e-33)]
    [InlineData(15.0, 0.75, 9.0, 0.0917477672919631)]
    public void BinomPdf(double n, double p, double k, double expected)
    {
        Tol.Rel(expected, CalcDistributions.BinomPdf(n, p, k), 1e-13);
    }

    [Theory]
    [InlineData(10.0, 0.5, 5.0, 0.623046875)]
    [InlineData(20.0, 0.3, 4.0, 0.2375077788776017)]
    [InlineData(100.0, 0.01, 0.0, 0.3660323412732295)]
    [InlineData(1000.0, 0.5, 450.0, 0.0008652680424881588)]
    [InlineData(50.0, 0.2, 3.0, 0.005656361012155304)]
    [InlineData(15.0, 0.75, 9.0, 0.148368077352643)]
    [InlineData(30.0, 0.6, 29.0, 0.9999997789260803)]
    [InlineData(1000000.0, 3e-06, 2.0, 0.4231897450635999)]
    [InlineData(100000.0, 4e-05, 3.0, 0.43346621294575116)]
    [InlineData(2000.0, 0.001, 1.0, 0.40587044671381534)]
    public void BinomCdf(double n, double p, double k, double expected)
    {
        Tol.Rel(expected, CalcDistributions.BinomCdf(n, p, k), 1e-13);
    }

    [Theory]
    [InlineData(3.0, 2.0, 0.22404180765538775, 0.42319008112684353)]
    [InlineData(2.0, 0.0, 0.1353352832366127, 0.1353352832366127)]
    [InlineData(10.0, 10.0, 0.1251100357211333, 0.5830397501929855)]
    [InlineData(0.5, 4.0, 0.001579506926334983, 0.9998278843700441)]
    [InlineData(1000.0, 1000.0, 0.012614611348721499, 0.508409367168506)]
    [InlineData(1000000.0, 1001000.0, 0.0002418901012017414, 0.8414656709634282)]
    [InlineData(50.0, 100.0, 1.63031935214773e-10, 0.9999999998430255)]
    [InlineData(7.5, 3.0, 0.03888874477601955, 0.059145459832683954)]
    public void Poisson(double mu, double k, double pdf, double cdf)
    {
        Tol.Rel(pdf, CalcDistributions.PoissonPdf(mu, k), 1e-13);
        Tol.Rel(cdf, CalcDistributions.PoissonCdf(mu, k), 1e-13);
    }

    [Theory]
    [InlineData(3.0, 15.0, 1.2408017083996458e-07)]
    [InlineData(100.0, 200.0, 4.626179470195773e-19)]
    [InlineData(0.1, 5.0, 1.2748986922297919e-09)]
    public void PoissonUpperTail(double mu, double k, double expected)
    {
        Tol.Rel(expected, new PoissonDistribution(mu).Survival(k), 1e-13);
    }

    [Theory]
    [InlineData(2.0, 3.0, 4.0, 0.11715428360698968, 0.3849400110633042)]
    [InlineData(0.5, 1.0, 0.01, 5.585758033944685, 0.1124629160182849)]
    [InlineData(100.0, 0.1, 9.0, 0.2591202825015754, 0.15822098918643004)]
    [InlineData(1.0, 2.0, 5.0, 0.0410424993119494, 0.9179150013761012)]
    public void GammaDistribution(double shape, double scale, double x, double pdf, double cdf)
    {
        var d = new GammaDistribution(shape, scale);
        Tol.Rel(pdf, d.Pdf(x), 1e-13);
        Tol.Rel(cdf, d.Cdf(x), 1e-13);
        Tol.Rel(x, d.InverseCdf(cdf), 1e-12);
    }

    [Theory]
    [InlineData(2.0, 3.0, 0.4, 1.728, 0.5248)]
    [InlineData(0.5, 0.5, 0.1, 1.0610329539459689, 0.20483276469913345)]
    [InlineData(10.0, 20.0, 0.3, 4.4940222559365415, 0.3640040810719442)]
    [InlineData(1.0, 1.0, 0.7, 1.0, 0.7)]
    [InlineData(100.0, 50.0, 0.7, 7.418926554447597, 0.8054481662277535)]
    public void BetaDistribution(double alpha, double beta, double x, double pdf, double cdf)
    {
        var d = new BetaDistribution(alpha, beta);
        Tol.Rel(pdf, d.Pdf(x), 1e-13);
        Tol.Rel(cdf, d.Cdf(x), 1e-13);
        Tol.Rel(x, d.InverseCdf(cdf), 1e-12);
    }

    [Theory]
    [InlineData(50.0, 10.0, 5.0, 2.0, 0.20983971757065453, 0.9517396968037909, 0.0482603031962091)]
    [InlineData(20.0, 7.0, 12.0, 4.0, 0.35758513931888547, 0.608359133126935, 0.39164086687306504)]
    [InlineData(1000.0, 300.0, 100.0, 30.0, 0.09147503923926442, 0.5505633949437034, 0.4494366050562966)]
    [InlineData(100.0, 50.0, 50.0, 25.0, 0.15838466116133043, 0.5791923305806652, 0.4208076694193348)]
    [InlineData(10.0, 10.0, 3.0, 3.0, 1.0, 1.0, 0.0)]
    [InlineData(1000.0, 300.0, 100.0, 15.0, 0.00014267379162766276, 0.00022260205983867096, 0.9997773979401613)]
    [InlineData(1000.0, 300.0, 100.0, 45.0, 0.00032223397712345247, 0.9997299729021503, 0.00027002709784970804)]
    public void Hypergeometric(double N, double K, double n, double k, double pmf, double cdf, double survival)
    {
        var d = new HypergeometricDistribution(N, K, n);
        Tol.Rel(pmf, d.Pmf(k), 1e-13);
        Tol.Rel(cdf, d.Cdf(k), 1e-13);
        Tol.Rel(survival, d.Survival(k), 1e-12);
    }

    [Theory]
    [InlineData(3.0, 0.5, 2.0, 0.1875, 0.5)]
    [InlineData(2.5, 0.3, 10.0, 0.03950099641037318, 0.8621611156029029)]
    [InlineData(1.0, 0.2, 0.0, 0.2, 0.2)]
    [InlineData(10.0, 0.9, 1.0, 0.3486784401, 0.6973568802000001)]
    public void NegativeBinomial(double r, double p, double k, double pmf, double cdf)
    {
        var d = new NegativeBinomialDistribution(r, p);
        Tol.Rel(pmf, d.Pmf(k), 1e-13);
        Tol.Rel(cdf, d.Cdf(k), 1e-13);
    }

    [Theory]
    [InlineData(0.2, 3.0, 0.128, 0.48800000000000004)]
    [InlineData(0.5, 1.0, 0.5, 0.5)]
    [InlineData(0.01, 100.0, 0.003697296376497268, 0.6339676587267705)]
    [InlineData(0.9, 2.0, 0.08999999999999998, 0.99)]
    public void Geometric(double p, double x, double pdf, double cdf)
    {
        Tol.Rel(pdf, CalcDistributions.GeometPdf(p, x), 1e-14);
        Tol.Rel(cdf, CalcDistributions.GeometCdf(p, x), 1e-14);
    }

    [Theory]
    [InlineData(1.5, 2.0, 1.0, 0.372391688219422, 0.2978114986734404, 1.8054905859018673)]
    [InlineData(0.5, 1.0, 0.2, 0.7148791154784528, 0.3605926808381029, 2.0)]
    [InlineData(3.0, 10.0, 12.0, 0.0767401921130983, 0.822360666404865, 8.929795115692492)]
    public void Weibull(double shape, double scale, double x, double pdf, double cdf, double mean)
    {
        var d = new WeibullDistribution(shape, scale);
        Tol.Rel(pdf, d.Pdf(x), 1e-14);
        Tol.Rel(cdf, d.Cdf(x), 1e-14);
        Tol.Rel(mean, d.Mean, 1e-14);
        Tol.Rel(x, d.InverseCdf(cdf), 1e-13);
    }

    [Theory]
    [InlineData(0.0, 1.0, 2.0, 0.1568740192789811, 0.7558914042144173, 1.6487212707001282)]
    [InlineData(0.5, 0.8, 3.0, 0.12563708627134776, 0.7728499431612219, 2.2704998375324057)]
    [InlineData(2.0, 0.25, 7.0, 0.2226932495702425, 0.4143538039476431, 7.623609917712736)]
    public void LogNormal(double mu, double sigma, double x, double pdf, double cdf, double mean)
    {
        var d = new LogNormalDistribution(mu, sigma);
        Tol.Rel(pdf, d.Pdf(x), 1e-14);
        Tol.Rel(cdf, d.Cdf(x), 1e-14);
        Tol.Rel(mean, d.Mean, 1e-14);
        Tol.Rel(x, d.InverseCdf(cdf), 1e-13);
    }

    [Theory]
    [InlineData(0.0, 1.0, 1e-10, -3183098861.837907)]
    [InlineData(2.0, 3.0, 0.75, 5.0)]
    [InlineData(-1.0, 0.5, 0.999, 158.15441949277508)]
    public void CauchyQuantile(double location, double scale, double p, double expected)
    {
        var d = new CauchyDistribution(location, scale);
        Tol.Rel(expected, d.InverseCdf(p), 1e-13);
        Tol.Rel(p, d.Cdf(expected), 1e-13);
    }

}
