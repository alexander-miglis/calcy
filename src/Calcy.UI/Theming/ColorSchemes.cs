using Avalonia.Media;

namespace Calcy.UI.Theming;

/// <summary>One 8-bit inspired colour scheme. Every colour in the app comes from these tokens.</summary>
public sealed record ColorScheme(string Id, string Name, IReadOnlyDictionary<string, Color> Colors)
{
    public Color this[string token] => Colors[token];

    /// <summary>True when the panels are dark, so standard controls should use the dark theme variant.</summary>
    public bool IsDark
    {
        get
        {
            var p = Colors["Paper"];
            double l = 0.2126 * Lin(p.R) + 0.7152 * Lin(p.G) + 0.0722 * Lin(p.B);
            return l < 0.3;
        }
    }

    private static double Lin(byte c)
    {
        double s = c / 255.0;
        return s <= 0.04045 ? s / 12.92 : Math.Pow((s + 0.055) / 1.055, 2.4);
    }
}

/// <summary>The built-in colour schemes (design decision D13).</summary>
public static class ColorSchemes
{
    /// <summary>Token names, in the order used by the packed scheme strings below.</summary>
    public static readonly string[] Tokens =
    [
        "Ground", "Ground2", "OnGround", "Bar", "BarInk", "BarAccent", "Paper", "Card", "Ink", "Muted", "Line", "Head", "Sel",
        "Primary", "Key", "Mem", "Ok", "Err", "Off", "OffInk", "Lcd", "LcdInk", "LcdShade", "Grid", "G1", "G2", "G3", "G4", "G5",
    ];

    private static readonly (string Id, string Name, string Packed)[] Packed =
    [
        ("nes", "NES Classic", "#1C1C1C #262626 #FCFCFC #000000 #FCFCFC #F83800 #FCFCFC #FFFFFF #000000 #595959 #000000 #3CBCFC #F8B800 #F83800 #BCBCBC #F878F8 #58D854 #D82800 #000000 #FCFCFC #BCBCBC #000000 #A4A4A4 #E4E4E4 #0058F8 #D82800 #00A800 #6844FC #AC7C00"),
        ("pico", "Pico Night", "#1D2B53 #22325F #FFF1E8 #000000 #FFF1E8 #29ADFF #FFF1E8 #FFFFFF #1D2B53 #5F574F #000000 #29ADFF #FFEC27 #FFA300 #C2C3C7 #FF77A8 #00E436 #FF004D #1D2B53 #FFF1E8 #9BBC0F #0F380F #8BAC0F #E5D7CC #1E5AC8 #FF004D #008751 #7E2553 #AB5236"),
        ("gameboy", "Game Boy", "#0F380F #133F13 #9BBC0F #0F380F #9BBC0F #8BAC0F #9BBC0F #CADC9F #0F380F #1B431B #0F380F #8BAC0F #E0F8D0 #CADC9F #A8C46A #C8E0A0 #B4D27A #0F380F #0F380F #9BBC0F #9BBC0F #0F380F #8BAC0F #B5C97A #0F380F #306230 #5A7D1A #1E4D1E #6E5A12"),
        ("c64", "C64 Blue", "#352879 #3D2F88 #C9C3F5 #20165C #C9C3F5 #6C5EB5 #C9C3F5 #E6E2FF #20165C #463B8E #20165C #9A8EE0 #EEEE77 #DD8855 #ADADAD #E0A3D8 #AAFF66 #883932 #352879 #FFFFFF #6C5EB5 #FFFFFF #5A4CA6 #D6D1F7 #40318D #883932 #3E7A33 #8B3F96 #9C6A20"),
        ("amber", "Amber CRT", "#0A0700 #140E00 #FFB000 #000000 #FFB000 #FFB000 #1A1200 #241A02 #FFB000 #C98A00 #A06E00 #3D2A00 #644500 #573C00 #2E2100 #4A2B10 #2F3A00 #FF5A1F #0A0700 #FFB000 #1A1200 #FFB000 #120D00 #3A2A00 #FFB000 #FF6A00 #FFE08A #D98E04 #FFF4CC"),
        ("paper", "Paper", "#E9E4D6 #E1DBCB #2B2B2B #2B2B2B #F7F4EC #E4572E #F7F4EC #FFFFFF #2B2B2B #6B645A #2B2B2B #A8D5BA #FFD166 #F4A259 #D9D4C7 #F7A6C4 #9BE38E #D7263D #2B2B2B #F7F4EC #C7D1B1 #2B2B2B #B9C3A2 #ECE6D9 #2D6CDF #D7263D #2A9D5F #7B4FB8 #B5651D"),
        ("mono", "High contrast", "#000000 #141414 #FFFFFF #000000 #FFFFFF #FFFFFF #FFFFFF #FFFFFF #000000 #3A3A3A #000000 #D9D9D9 #FFF000 #BFBFBF #EDEDED #CFCFCF #E3E3E3 #000000 #000000 #FFFFFF #E6E6E6 #000000 #D0D0D0 #E6E6E6 #000000 #4D4D4D #000000 #6E6E6E #262626"),
        ("synth", "Synthwave", "#1B0B33 #241044 #FFE3F5 #0E0420 #FFE3F5 #FF4FD8 #2A1452 #351A66 #FFE3F5 #C9A8E8 #0E0420 #6B2FB5 #9D1789 #B8360B #3D2470 #8E1A72 #136B4F #FF4F6D #0E0420 #FFE3F5 #2A1452 #4FD8FF #22103F #3A2266 #4FD8FF #FF4FD8 #7CFF6B #FFD24F #FF8A4F"),
    ];

    public static readonly IReadOnlyList<ColorScheme> All = [.. Packed.Select(Unpack)];

    public const string DefaultId = "nes";

    public static ColorScheme Get(string? id) => All.FirstOrDefault(s => s.Id == id) ?? All[0];

    private static ColorScheme Unpack((string Id, string Name, string Packed) p)
    {
        var values = p.Packed.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (values.Length != Tokens.Length) throw new InvalidOperationException($"Scheme {p.Id} has {values.Length} colours, expected {Tokens.Length}");
        var dict = new Dictionary<string, Color>();
        for (int i = 0; i < Tokens.Length; i++) dict[Tokens[i]] = Color.Parse(values[i]);
        return new ColorScheme(p.Id, p.Name, dict);
    }
}
