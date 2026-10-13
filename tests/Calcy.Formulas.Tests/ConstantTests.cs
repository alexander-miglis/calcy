namespace Calcy.Formulas.Tests;

/// <summary>Checks constant values against their definitions and against each other.</summary>
public class ConstantTests
{
    private static double V(string id) => TestData.Library.GetConstant(id)!.Value;

    private static void Close(double expected, double actual, double relative = 1e-9) =>
        Assert.True(Math.Abs(expected - actual) <= relative * Math.Abs(expected), $"Expected {expected:R}, got {actual:R}.");

    [Theory]
    [InlineData("speed-of-light", 299792458)]
    [InlineData("planck-constant", 6.62607015e-34)]
    [InlineData("elementary-charge", 1.602176634e-19)]
    [InlineData("boltzmann-constant", 1.380649e-23)]
    [InlineData("avogadro-constant", 6.02214076e23)]
    [InlineData("caesium-hyperfine-frequency", 9192631770)]
    [InlineData("luminous-efficacy", 683)]
    [InlineData("standard-gravity", 9.80665)]
    [InlineData("standard-atmosphere", 101325)]
    [InlineData("astronomical-unit", 149597870700)]
    public void Defining_constants_have_their_exact_values(string id, double value)
    {
        var c = TestData.Library.GetConstant(id)!;
        Assert.Equal(value, c.Value);
        Assert.True(c.Exact);
    }

    [Theory]
    [InlineData("gravitational-constant", 6.67430e-11)]
    [InlineData("electron-mass", 9.1093837015e-31)]
    [InlineData("proton-mass", 1.67262192369e-27)]
    [InlineData("neutron-mass", 1.67492749804e-27)]
    [InlineData("atomic-mass-constant", 1.66053906660e-27)]
    [InlineData("vacuum-permittivity", 8.8541878128e-12)]
    [InlineData("vacuum-permeability", 1.25663706212e-6)]
    [InlineData("fine-structure-constant", 7.2973525693e-3)]
    [InlineData("rydberg-constant", 10973731.568160)]
    [InlineData("bohr-radius", 5.29177210903e-11)]
    public void Measured_constants_match_codata_2018(string id, double value)
    {
        var c = TestData.Library.GetConstant(id)!;
        Assert.Equal(value, c.Value);
        Assert.False(c.Exact);
        Assert.NotNull(c.Uncertainty);
    }

    [Fact] public void Hbar_is_h_over_two_pi() => Close(V("planck-constant") / (2 * Math.PI), V("reduced-planck-constant"));
    [Fact] public void Gas_constant_is_avogadro_times_boltzmann() => Close(V("avogadro-constant") * V("boltzmann-constant"), V("molar-gas-constant"), 1e-14);
    [Fact] public void Faraday_is_avogadro_times_charge() => Close(V("avogadro-constant") * V("elementary-charge"), V("faraday-constant"), 1e-12);
    [Fact] public void Coulomb_constant_is_one_over_four_pi_epsilon0() => Close(1 / (4 * Math.PI * V("vacuum-permittivity")), V("coulomb-constant"));
    [Fact] public void Mu0_epsilon0_c_squared_is_one() => Close(1, V("vacuum-permeability") * V("vacuum-permittivity") * Math.Pow(V("speed-of-light"), 2), 1e-9);
    [Fact] public void Flux_quantum_is_h_over_2e() => Close(V("planck-constant") / (2 * V("elementary-charge")), V("magnetic-flux-quantum"));
    [Fact] public void Josephson_is_2e_over_h() => Close(2 * V("elementary-charge") / V("planck-constant"), V("josephson-constant"));
    [Fact] public void Von_klitzing_is_h_over_e_squared() => Close(V("planck-constant") / Math.Pow(V("elementary-charge"), 2), V("von-klitzing-constant"));
    [Fact] public void Impedance_of_vacuum_is_mu0_c() => Close(V("vacuum-permeability") * V("speed-of-light"), V("impedance-of-free-space"));
    [Fact] public void Hartree_is_2hcR() => Close(2 * V("planck-constant") * V("speed-of-light") * V("rydberg-constant"), V("hartree-energy"));
    [Fact] public void Compton_wavelength_is_h_over_mc() => Close(V("planck-constant") / (V("electron-mass") * V("speed-of-light")), V("compton-wavelength"));
    [Fact] public void Bohr_radius_from_hbar_m_c_alpha() => Close(V("reduced-planck-constant") / (V("electron-mass") * V("speed-of-light") * V("fine-structure-constant")), V("bohr-radius"));
    [Fact] public void Rydberg_from_alpha_m_c_h() => Close(Math.Pow(V("fine-structure-constant"), 2) * V("electron-mass") * V("speed-of-light") / (2 * V("planck-constant")), V("rydberg-constant"));
    [Fact] public void Classical_electron_radius_is_alpha_squared_a0() => Close(Math.Pow(V("fine-structure-constant"), 2) * V("bohr-radius"), V("classical-electron-radius"));
    [Fact] public void Bohr_magneton_is_e_hbar_over_2me() => Close(V("elementary-charge") * V("reduced-planck-constant") / (2 * V("electron-mass")), V("bohr-magneton"));
    [Fact] public void Nuclear_magneton_is_e_hbar_over_2mp() => Close(V("elementary-charge") * V("reduced-planck-constant") / (2 * V("proton-mass")), V("nuclear-magneton"));

    [Fact]
    public void Stefan_boltzmann_from_k_h_c()
    {
        double k = V("boltzmann-constant"), h = V("planck-constant"), c = V("speed-of-light");
        Close(2 * Math.Pow(Math.PI, 5) * Math.Pow(k, 4) / (15 * Math.Pow(h, 3) * c * c), V("stefan-boltzmann-constant"));
    }

    [Fact]
    public void Wien_constant_from_h_c_k()
    {
        // b = h c / (x k) where x solves x = 5 (1 - e^-x).
        double x = 5;
        for (int i = 0; i < 100; i++) x = 5 * (1 - Math.Exp(-x));
        Close(V("planck-constant") * V("speed-of-light") / (x * V("boltzmann-constant")), V("wien-displacement-constant"));
    }

    [Fact] public void Molar_volume_is_RT_over_p() => Close(V("molar-gas-constant") * 273.15 / V("standard-atmosphere"), V("molar-volume-stp"));
    [Fact] public void Light_year_is_c_times_julian_year() => Close(V("speed-of-light") * 365.25 * 86400, V("light-year"), 1e-14);
    [Fact] public void Parsec_is_648000_over_pi_au() => Close(648000 / Math.PI * V("astronomical-unit"), V("parsec"), 1e-14);
    [Fact] public void Golden_ratio_is_one_plus_root5_over_2() => Close((1 + Math.Sqrt(5)) / 2, V("golden-ratio"), 1e-15);
    [Fact] public void Pi_and_e_match_the_runtime() { Close(Math.PI, V("pi"), 1e-15); Close(Math.E, V("euler-number"), 1e-15); }

    [Fact]
    public void Euler_mascheroni_matches_harmonic_limit()
    {
        // H_n - ln n - 1/(2n) + 1/(12 n^2) - 1/(120 n^4) converges quickly to gamma (summed small terms first).
        int n = 1000;
        double h = 0;
        for (int k = n; k >= 1; k--) h += 1.0 / k;
        double n2 = (double)n * n;
        Close(h - Math.Log(n) - 1.0 / (2 * n) + 1.0 / (12 * n2) - 1.0 / (120 * n2 * n2), V("euler-mascheroni-constant"), 1e-12);
    }

    [Fact]
    public void Apery_constant_matches_zeta_3()
    {
        // Partial sum to N plus the Euler-Maclaurin tail 1/(2N^2) - 1/(2N^3) + 1/(4N^4).
        int n = 1000;
        double s = 0;
        for (int k = n; k >= 1; k--) s += 1.0 / ((double)k * k * k);
        double nn = n;
        Close(s + 1 / (2 * nn * nn) - 1 / (2 * nn * nn * nn) + 1 / (4 * nn * nn * nn * nn), V("apery-constant"), 1e-12);
    }

    [Fact]
    public void Solar_luminosity_is_consistent_with_radius_and_temperature()
    {
        // L = 4 pi R^2 sigma T^4 with the IAU nominal effective temperature 5772 K.
        double l = 4 * Math.PI * Math.Pow(V("sun-radius"), 2) * V("stefan-boltzmann-constant") * Math.Pow(5772, 4);
        Close(V("sun-luminosity"), l, 1e-3);
    }

    [Fact]
    public void Constants_can_be_found_by_id_and_symbol()
    {
        var lib = TestData.Library;
        Assert.Same(lib.GetConstant("speed-of-light"), lib.GetConstant("c"));
        Assert.Same(lib.GetConstant("boltzmann-constant"), lib.GetConstant("k_B"));
        Assert.Null(lib.GetConstant("no-such-constant"));
    }
}
