using Calcy.Statistics;

namespace Calcy.Statistics.Tests;

// Reference values generated with 40 digit mpmath arithmetic.
public class SpecialFunctionReferenceTests
{
    [Theory]
    [InlineData(0.001, 999.4237724845955)]
    [InlineData(0.1, 9.51350769866873)]
    [InlineData(0.5, 1.772453850905516)]
    [InlineData(1.5, 0.886226925452758)]
    [InlineData(2.5, 1.329340388179137)]
    [InlineData(3.7, 4.170651783796604)]
    [InlineData(10.3, 716430.6890623764)]
    [InlineData(50.5, 4.29046291235196e+63)]
    [InlineData(120.7, 1.5894968726397612e+198)]
    [InlineData(170.5, 5.56209241456e+305)]
    [InlineData(-0.5, -3.544907701811032)]
    [InlineData(-1.5, 2.363271801207355)]
    [InlineData(-2.7, -0.931082784838964)]
    [InlineData(-10.3, -5.26236323953561e-07)]
    [InlineData(1e-15, 999999999999999.4)]
    [InlineData(25.0, 6.204484017332394e+23)]
    [InlineData(171.0, 7.257415615307999e+306)]
    [InlineData(4.5, 11.631728396567448)]
    public void Gamma(double x, double expected)
    {
        Tol.Rel(expected, SpecialFunctions.Gamma(x), 1e-14);
    }

    [Theory]
    [InlineData(0.001, 6.907178885383853)]
    [InlineData(0.5, 0.5723649429247001)]
    [InlineData(0.81, 0.1425238344299568)]
    [InlineData(0.99, 0.005854806764709781)]
    [InlineData(1.0001, -5.771334222047127e-05)]
    [InlineData(1.19, -0.08242007447712085)]
    [InlineData(1.5, -0.12078223763524522)]
    [InlineData(1.79, -0.0738968509272423)]
    [InlineData(1.9999, -4.227520877215346e-05)]
    [InlineData(2.0001, 4.2281658112919945e-05)]
    [InlineData(2.21, 0.10241899450395861)]
    [InlineData(3.7, 1.428072326665388)]
    [InlineData(10.3, 13.482036786138359)]
    [InlineData(100000.0, 1051287.7089736569)]
    [InlineData(10000000000.0, 220258509288.81058)]
    [InlineData(1e+300, 6.897755278982137e+302)]
    [InlineData(-0.5, 1.2655121234846454)]
    [InlineData(-2.7, -0.07140708531564569)]
    [InlineData(-100.3, -363.7662061826763)]
    public void LogGamma(double x, double expected)
    {
        Tol.Rel(expected, SpecialFunctions.LogGamma(x), 1e-14);
    }

    [Theory]
    [InlineData(1e-10, -10000000000.577215)]
    [InlineData(0.1, -10.423754940411076)]
    [InlineData(0.5, -1.9635100260214235)]
    [InlineData(1.0, -0.5772156649015329)]
    [InlineData(1.3, -0.16919088886679962)]
    [InlineData(1.46, -0.0015805619870834522)]
    [InlineData(1.4616321449683622, -9.241265521729427e-17)]
    [InlineData(1.5, 0.03648997397857652)]
    [InlineData(1.7, 0.2085478748734939)]
    [InlineData(2.0, 0.42278433509846713)]
    [InlineData(3.0, 0.9227843350984671)]
    [InlineData(9.5, 2.1977378764029494)]
    [InlineData(10.0, 2.251752589066721)]
    [InlineData(100.5, 4.605174352581845)]
    [InlineData(1000000.0, 13.815510057964191)]
    [InlineData(-0.5, 0.03648997397857652)]
    [InlineData(-1.3, 2.8825405488661673)]
    [InlineData(-7.9, -7.540007224419479)]
    public void Digamma(double x, double expected)
    {
        Tol.Rel(expected, SpecialFunctions.Digamma(x), 1e-13);
    }

    [Theory]
    [InlineData(1e-20, 1.1283791670955125e-20)]
    [InlineData(0.01, 0.011283415555849618)]
    [InlineData(0.3, 0.3286267594591274)]
    [InlineData(0.49, 0.511668261188523)]
    [InlineData(0.5, 0.5204998778130465)]
    [InlineData(0.51, 0.5292436198411704)]
    [InlineData(1.0, 0.8427007929497149)]
    [InlineData(2.0, 0.9953222650189527)]
    [InlineData(3.5, 0.9999992569016276)]
    [InlineData(3.99, 0.9999999832607886)]
    [InlineData(4.01, 0.9999999858030606)]
    [InlineData(5.0, 0.9999999999984626)]
    [InlineData(-0.7, -0.6778011938374184)]
    [InlineData(-2.5, -0.999593047982555)]
    public void Erf(double x, double expected)
    {
        Tol.Rel(expected, SpecialFunctions.Erf(x), 1e-15);
    }

    [Theory]
    [InlineData(-5.0, 1.9999999999984626)]
    [InlineData(-1.0, 1.8427007929497148)]
    [InlineData(-0.4, 1.4283923550466684)]
    [InlineData(0.0, 1.0)]
    [InlineData(0.3, 0.6713732405408726)]
    [InlineData(0.5, 0.4795001221869535)]
    [InlineData(0.7, 0.32219880616258156)]
    [InlineData(1.0, 0.15729920705028513)]
    [InlineData(2.0, 0.004677734981047266)]
    [InlineData(3.0, 2.209049699858544e-05)]
    [InlineData(3.99, 1.6739211364520814e-08)]
    [InlineData(4.01, 1.4196939421371052e-08)]
    [InlineData(5.0, 1.537459794428035e-12)]
    [InlineData(8.0, 1.1224297172982926e-29)]
    [InlineData(10.0, 2.088487583762545e-45)]
    [InlineData(15.0, 7.212994172451207e-100)]
    [InlineData(20.0, 5.395865611607901e-176)]
    [InlineData(26.0, 5.663192408856143e-296)]
    public void Erfc(double x, double expected)
    {
        Tol.Rel(expected, SpecialFunctions.Erfc(x), 1e-15);
    }

    [Theory]
    [InlineData(-3.0, 16205.988853999586)]
    [InlineData(-0.6, 2.2988541117340935)]
    [InlineData(0.0, 1.0)]
    [InlineData(0.4, 0.6707877852947615)]
    [InlineData(0.6, 0.567804717386587)]
    [InlineData(1.0, 0.427583576155807)]
    [InlineData(3.0, 0.17900115118138996)]
    [InlineData(4.0, 0.13699945762506138)]
    [InlineData(7.0, 0.07980005432915294)]
    [InlineData(30.0, 0.01879588886141675)]
    [InlineData(100000.0, 5.6418958351954685e-06)]
    [InlineData(10000000000.0, 5.641895835477563e-11)]
    public void Erfcx(double x, double expected)
    {
        Tol.Rel(expected, SpecialFunctions.Erfcx(x), 1e-15);
    }

    [Theory]
    [InlineData(1e-300, 8.86226925452758e-301)]
    [InlineData(1e-10, 8.862269254527581e-11)]
    [InlineData(0.1, 0.08885599049425769)]
    [InlineData(0.4, 0.37080715859355795)]
    [InlineData(0.5, 0.4769362762044699)]
    [InlineData(0.6, 0.5951160814499948)]
    [InlineData(0.9, 1.1630871536766743)]
    [InlineData(0.99, 1.8213863677184494)]
    [InlineData(0.999999, 3.458910737275499)]
    [InlineData(-0.3, -0.2724627147267543)]
    [InlineData(-0.99999999, -4.052237243268763)]
    public void ErfInv(double y, double expected)
    {
        Tol.Rel(expected, SpecialFunctions.ErfInv(y), 1e-15);
    }

    [Theory]
    [InlineData(1e-300, 26.209469960516124)]
    [InlineData(1e-100, 15.065574702592645)]
    [InlineData(1e-20, 6.601580622355143)]
    [InlineData(1e-05, 3.123413274340875)]
    [InlineData(0.01, 1.8213863677184496)]
    [InlineData(0.1, 1.163087153676674)]
    [InlineData(0.5, 0.4769362762044699)]
    [InlineData(0.7, 0.2724627147267544)]
    [InlineData(1.3, -0.2724627147267544)]
    [InlineData(1.9, -1.1630871536766738)]
    [InlineData(1.99999, -3.1234132743398733)]
    public void ErfcInv(double z, double expected)
    {
        Tol.Rel(expected, SpecialFunctions.ErfcInv(z), 1e-15);
    }

    [Theory]
    [InlineData(0.5, 0.5, 3.141592653589793, 1.1447298858494002)]
    [InlineData(1.0, 2.0, 0.5, -0.6931471805599453)]
    [InlineData(2.5, 3.5, 0.03681553890925539, -3.301835269962053)]
    [InlineData(10.0, 20.0, 4.992508740634678e-09, -19.115327299887046)]
    [InlineData(100.0, 0.5, 0.177467079428307, -1.7289701552775227)]
    [InlineData(1e-05, 3.0, 99998.5000174998, 11.512910465032729)]
    [InlineData(0.001, 0.002, 1499.995075998483, 7.313217104417236)]
    [InlineData(50.0, 50.0, 3.964661208567336e-31, -70.00271747688562)]
    [InlineData(10000000000.0, 2.0, 9.999999999e-21, -46.051701859980916)]
    public void BetaAndLogBeta(double a, double b, double beta, double logBeta)
    {
        Tol.Rel(beta, SpecialFunctions.Beta(a, b), 1e-14);
        Tol.Rel(logBeta, SpecialFunctions.LogBeta(a, b), 1e-14);
    }

    [Theory]
    [InlineData(0.5, 0.1, 0.345279153981423, 0.654720846018577)]
    [InlineData(0.5, 2.0, 0.9544997361036416, 0.04550026389635842)]
    [InlineData(1.0, 1.0, 0.6321205588285577, 0.36787944117144233)]
    [InlineData(3.0, 0.5, 0.014387677966970687, 0.9856123220330293)]
    [InlineData(3.0, 10.0, 0.9972306042844884, 0.002769395715511576)]
    [InlineData(10.0, 10.0, 0.5420702855281478, 0.4579297144718522)]
    [InlineData(10.0, 3.0, 0.0011024881301154798, 0.9988975118698845)]
    [InlineData(10.0, 30.0, 0.9999928782491372, 7.121750862815577e-06)]
    [InlineData(100.0, 80.0, 0.017108313035133115, 0.9828916869648668)]
    [InlineData(100.0, 120.0, 0.9721362601094793, 0.027863739890520663)]
    [InlineData(0.001, 0.5, 0.9994399333435292, 0.0005600666564707498)]
    [InlineData(1e-08, 0.3, 0.9999999909432334, 9.056766532959496e-09)]
    [InlineData(0.1, 1.5, 0.9886559833621947, 0.01134401663780527)]
    [InlineData(0.3, 0.9, 0.9022526480296695, 0.09774735197033045)]
    [InlineData(1000.0, 1100.0, 0.99894067674607, 0.0010593232539299773)]
    [InlineData(100000.0, 100300.0, 0.8286363112512076, 0.17136368874879235)]
    [InlineData(0.9, 0.95, 0.6576968396167732, 0.34230316038322683)]
    [InlineData(2.5, 1e-10, 3.009011112039771e-26, 1.0)]
    [InlineData(50.0, 200.0, 1.0, 1.6927979958857087e-37)]
    public void RegularizedGamma(double a, double x, double p, double q)
    {
        Tol.Rel(p, SpecialFunctions.RegularizedGammaP(a, x), 5e-14);
        Tol.Rel(q, SpecialFunctions.RegularizedGammaQ(a, x), 5e-14);
    }

    [Theory]
    [InlineData(0.5, 0.1, 0.007895387046715613)]
    [InlineData(0.5, 0.9, 1.3527717270477075)]
    [InlineData(1.0, 0.5, 0.6931471805599453)]
    [InlineData(3.0, 1e-10, 0.0008436105624847275)]
    [InlineData(3.0, 0.999999, 19.129168188572923)]
    [InlineData(10.0, 0.3, 8.132928242506392)]
    [InlineData(100.0, 0.01, 78.21598305379582)]
    [InlineData(10000.0, 0.7, 10052.197405331652)]
    [InlineData(2.5, 0.5, 2.1757300955477636)]
    public void InverseRegularizedGammaP(double a, double p, double expected)
    {
        Tol.Rel(expected, SpecialFunctions.InverseRegularizedGammaP(a, p), 1e-13);
        Tol.Rel(p, SpecialFunctions.RegularizedGammaP(a, expected), 1e-13);
    }

    [Theory]
    [InlineData(2.0, 1e-30, 73.38683172302522)]
    [InlineData(0.5, 1e-12, 25.422063955909078)]
    [InlineData(50.0, 1e-08, 100.31595436840833)]
    public void InverseRegularizedGammaQ(double a, double q, double expected)
    {
        Tol.Rel(expected, SpecialFunctions.InverseRegularizedGammaQ(a, q), 1e-13);
    }

    [Theory]
    [InlineData(0.5, 0.5, 0.3, 0.36901011956554536)]
    [InlineData(2.0, 3.0, 0.4, 0.5248)]
    [InlineData(2.0, 3.0, 0.9, 0.9963)]
    [InlineData(10.0, 10.0, 0.5, 0.5)]
    [InlineData(10.0, 10.0, 0.1, 3.929882327128002e-06)]
    [InlineData(100.0, 200.0, 0.3, 0.10884306564490975)]
    [InlineData(0.01, 5.0, 0.2, 0.9978180380770353)]
    [InlineData(5.0, 0.01, 0.999, 0.047204119992922754)]
    [InlineData(10000.0, 10000.0, 0.505, 0.9213529911183359)]
    [InlineData(5.0, 0.5, 1e-05, 2.4609477539721695e-26)]
    [InlineData(0.5, 50.0, 0.001, 0.24763098003462322)]
    [InlineData(300.0, 0.5, 0.995, 0.08300690586767095)]
    [InlineData(1.0, 3.0, 0.2, 0.48800000000000004)]
    [InlineData(3.0, 1.0, 0.2, 0.008000000000000002)]
    [InlineData(100000.0, 3.0, 0.99999, 0.9196958438052767)]
    [InlineData(50000000.0, 0.5, 0.99999996, 0.045500262333466324)]
    public void RegularizedIncompleteBeta(double a, double b, double x, double expected)
    {
        Tol.Rel(expected, SpecialFunctions.RegularizedIncompleteBeta(a, b, x), 2e-14);
    }

    [Theory]
    [InlineData(2.0, 3.0, 0.4, 0.32916650337840786)]
    [InlineData(0.5, 0.5, 0.1, 0.024471741852423217)]
    [InlineData(10.0, 20.0, 0.99, 0.5422163632028141)]
    [InlineData(0.1, 5.0, 0.5, 0.00013014955240919754)]
    [InlineData(50.0, 2.0, 1e-06, 0.718514038715566)]
    [InlineData(3.0, 3.0, 0.5, 0.5)]
    public void InverseRegularizedIncompleteBeta(double a, double b, double p, double expected)
    {
        Tol.Rel(expected, SpecialFunctions.InverseRegularizedIncompleteBeta(a, b, p), 1e-13);
    }

    [Theory]
    [InlineData(-0.36, -0.8060843159708176)]
    [InlineData(-0.3, -0.4894022271802149)]
    [InlineData(-0.2, -0.25917110181907377)]
    [InlineData(-1e-10, -1.0000000001000001e-10)]
    [InlineData(1e-10, 9.999999999e-11)]
    [InlineData(0.1, 0.09127652716086226)]
    [InlineData(0.5, 0.35173371124919584)]
    [InlineData(1.0, 0.5671432904097838)]
    [InlineData(2.0, 0.8526055020137255)]
    [InlineData(2.718281828459045, 1.0)]
    [InlineData(10.0, 1.7455280027406994)]
    [InlineData(100000.0, 9.284571428622108)]
    [InlineData(1e+100, 224.8431064451185)]
    [InlineData(1e+300, 684.2472086297608)]
    public void LambertWPrincipal(double x, double expected)
    {
        Tol.Rel(expected, SpecialFunctions.LambertW(x), 1e-15);
    }

    [Theory]
    [InlineData(-0.36787944117144, -1.0000001122836022)]
    [InlineData(-0.36, -1.2227701339785062)]
    [InlineData(-0.3, -1.7813370234216277)]
    [InlineData(-0.1, -3.577152063957297)]
    [InlineData(-1e-05, -14.163600815810183)]
    [InlineData(-1e-100, -235.72115887568532)]
    [InlineData(-1e-300, -697.3227762954601)]
    public void LambertWMinusOne(double x, double expected)
    {
        Tol.Rel(expected, SpecialFunctions.LambertW(x, -1), 1e-14);
    }

    [Theory]
    [InlineData(-30.5, 149774871.27793476)]
    [InlineData(-11.0, 0.021092796092796094)]
    [InlineData(-3.5, 0.004441011335479432)]
    [InlineData(-1.0, -0.08333333333333333)]
    [InlineData(-0.5, -0.20788622497735457)]
    [InlineData(0.1, -0.6030375198562418)]
    [InlineData(0.5, -1.4603545088095868)]
    [InlineData(0.999, -999.4228571557879)]
    [InlineData(1.001, 1000.5772884760116)]
    [InlineData(1.5, 2.612375348685488)]
    [InlineData(2.0, 1.6449340668482264)]
    [InlineData(3.0, 1.2020569031595942)]
    [InlineData(4.5, 1.0547075107614543)]
    [InlineData(10.0, 1.000994575127818)]
    [InlineData(30.0, 1.0000000009313275)]
    [InlineData(49.0, 1.0000000000000018)]
    [InlineData(60.0, 1.0)]
    public void RiemannZeta(double s, double expected)
    {
        Tol.Rel(expected, SpecialFunctions.RiemannZeta(s), 1e-13);
    }

    [Theory]
    [InlineData(10.0, 3.0, 120.0)]
    [InlineData(50.0, 25.0, 126410606437752.0)]
    [InlineData(100.0, 50.0, 1.008913445455642e+29)]
    [InlineData(1000.0, 500.0, 2.7028824094543655e+299)]
    [InlineData(1000000000000000.0, 3.0, 1.6666666666666617e+44)]
    [InlineData(60.0, 30.0, 1.1826458156486142e+17)]
    [InlineData(5.5, 2.0, 12.375)]
    [InlineData(5.5, 2.5, 14.4375)]
    [InlineData(-3.0, 2.0, 6.0)]
    [InlineData(-3.0, 3.0, -10.0)]
    [InlineData(-2.5, 2.5, 0.0)]
    [InlineData(20.0, 0.0, 1.0)]
    [InlineData(20.0, 20.0, 1.0)]
    [InlineData(7.0, 8.0, 0.0)]
    [InlineData(7.0, -1.0, 0.0)]
    public void Binomial(double n, double k, double expected)
    {
        Tol.Rel(expected, SpecialFunctions.Binomial(n, k), 1e-15);
    }

    [Theory]
    [InlineData(0.0, 1.0)]
    [InlineData(1.0, 1.0)]
    [InlineData(5.0, 120.0)]
    [InlineData(20.0, 2.43290200817664e+18)]
    [InlineData(22.0, 1.1240007277776077e+21)]
    [InlineData(25.0, 1.5511210043330986e+25)]
    [InlineData(100.0, 9.332621544394415e+157)]
    [InlineData(170.0, 7.257415615307999e+306)]
    [InlineData(4.5, 52.34277778455352)]
    [InlineData(0.5, 0.886226925452758)]
    public void Factorial(double n, double expected)
    {
        Tol.Rel(expected, SpecialFunctions.Factorial(n), 1e-15);
    }

}
