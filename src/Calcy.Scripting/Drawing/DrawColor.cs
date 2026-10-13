using System.Globalization;

namespace Calcy.Scripting.Drawing;

/// <summary>Colour scheme roles a drawing can use instead of a fixed RGB value; the screen resolves them with the current scheme.</summary>
public enum DrawToken : byte
{
    /// <summary>The canvas background (the scheme's card colour).</summary>
    Background = 0,

    /// <summary>The default drawing colour (the scheme's ink).</summary>
    Ink = 1,

    /// <summary>Secondary text and marks.</summary>
    Muted = 2,

    /// <summary>Grid lines.</summary>
    Grid = 3,

    /// <summary>First graph colour (G1).</summary>
    Accent1 = 4,

    /// <summary>Second graph colour (G2).</summary>
    Accent2 = 5,

    /// <summary>Third graph colour (G3).</summary>
    Accent3 = 6,

    /// <summary>Fourth graph colour (G4).</summary>
    Accent4 = 7,

    /// <summary>Fifth graph colour (G5).</summary>
    Accent5 = 8,
}

/// <summary>
/// A colour in a drawing: either an explicit RGB value chosen by the program or a <see cref="DrawToken"/> that follows the
/// colour scheme. Packed in <see cref="Value"/> as 0xFFRRGGBB for RGB and 0x000000tt for tokens.
/// </summary>
public readonly record struct DrawColor
{
    private DrawColor(uint value) => Value = value;

    /// <summary>The packed value (also the pixel value stored in canvas frames).</summary>
    public uint Value { get; }

    /// <summary>True for scheme tokens, false for explicit RGB colours.</summary>
    public bool IsToken => (Value & 0xFF000000u) == 0;

    /// <summary>The token (only meaningful when <see cref="IsToken"/>).</summary>
    public DrawToken Token => (DrawToken)(Value & 0xFF);

    /// <summary>The canvas background token.</summary>
    public static DrawColor Background => new((uint)DrawToken.Background);

    /// <summary>The default ink token.</summary>
    public static DrawColor Ink => new((uint)DrawToken.Ink);

    /// <summary>Creates a scheme token colour.</summary>
    public static DrawColor FromToken(DrawToken token) => new((uint)token);

    /// <summary>Creates an explicit RGB colour.</summary>
    public static DrawColor FromRgb(byte r, byte g, byte b) => new(0xFF000000u | ((uint)r << 16) | ((uint)g << 8) | b);

    /// <summary>Recreates a colour from <see cref="Value"/>.</summary>
    public static DrawColor FromPacked(uint value)
    {
        if ((value & 0xFF000000u) == 0) return new(Math.Min(value & 0xFF, (uint)DrawToken.Accent5));
        return new(0xFF000000u | (value & 0xFFFFFF));
    }

    /// <summary>
    /// The RGB value: the colour itself, or for tokens a fixed reference value (white background, black ink and the
    /// NES graph colours) so that programs reading pixels back get stable answers whatever the scheme.
    /// </summary>
    public (byte R, byte G, byte B) ToRgb()
    {
        if (!IsToken) return ((byte)(Value >> 16), (byte)(Value >> 8), (byte)Value);
        return Token switch
        {
            DrawToken.Background => (255, 255, 255),
            DrawToken.Ink => (0, 0, 0),
            DrawToken.Muted => (128, 128, 128),
            DrawToken.Grid => (200, 200, 200),
            DrawToken.Accent1 => (0, 88, 248),
            DrawToken.Accent2 => (216, 40, 0),
            DrawToken.Accent3 => (0, 168, 0),
            DrawToken.Accent4 => (104, 68, 252),
            _ => (172, 124, 0),
        };
    }

    /// <inheritdoc />
    public override string ToString()
    {
        if (IsToken) return Token.ToString();
        var (r, g, b) = ToRgb();
        return string.Create(CultureInfo.InvariantCulture, $"#{r:X2}{g:X2}{b:X2}");
    }

    /// <summary>
    /// Reads a colour name ("red", "darkgreen", the single letters b g r c m y k w), a hex string ("#f80", "#ff8800")
    /// or a scheme role ("ink", "paper", "background", "muted", "grid", "accent1" to "accent5").
    /// </summary>
    public static bool TryParse(string? text, out DrawColor color)
    {
        color = Ink;
        if (string.IsNullOrWhiteSpace(text)) return false;
        var t = text.Trim().ToLowerInvariant().Replace(" ", "").Replace("_", "");
        if (t.StartsWith('#'))
        {
            var hex = t[1..];
            if (hex.Length == 3) hex = string.Concat(hex.Select(c => new string(c, 2)));
            if (hex.Length == 6 && uint.TryParse(hex, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var v))
            {
                color = FromRgb((byte)(v >> 16), (byte)(v >> 8), (byte)v);
                return true;
            }
            return false;
        }
        switch (t)
        {
            case "ink": color = Ink; return true;
            case "paper" or "background" or "bg": color = Background; return true;
            case "muted": color = FromToken(DrawToken.Muted); return true;
            case "grid": color = FromToken(DrawToken.Grid); return true;
            case "accent" or "accent1": color = FromToken(DrawToken.Accent1); return true;
            case "accent2": color = FromToken(DrawToken.Accent2); return true;
            case "accent3": color = FromToken(DrawToken.Accent3); return true;
            case "accent4": color = FromToken(DrawToken.Accent4); return true;
            case "accent5": color = FromToken(DrawToken.Accent5); return true;
        }
        if (Names.TryGetValue(t, out var rgb))
        {
            color = FromRgb((byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb);
            return true;
        }
        return false;
    }

    /// <summary>The colour names <see cref="TryParse"/> understands (CSS names plus the matplotlib single letters).</summary>
    public static IReadOnlyCollection<string> KnownNames => Names.Keys;

    private static readonly Dictionary<string, uint> Names = new(StringComparer.Ordinal)
    {
        ["black"] = 0x000000, ["white"] = 0xFFFFFF, ["red"] = 0xFF0000, ["green"] = 0x008000, ["blue"] = 0x0000FF,
        ["yellow"] = 0xFFFF00, ["cyan"] = 0x00FFFF, ["aqua"] = 0x00FFFF, ["magenta"] = 0xFF00FF, ["fuchsia"] = 0xFF00FF,
        ["orange"] = 0xFFA500, ["purple"] = 0x800080, ["pink"] = 0xFFC0CB, ["brown"] = 0xA52A2A, ["gray"] = 0x808080,
        ["grey"] = 0x808080, ["lightgray"] = 0xD3D3D3, ["lightgrey"] = 0xD3D3D3, ["darkgray"] = 0xA9A9A9,
        ["darkgrey"] = 0xA9A9A9, ["silver"] = 0xC0C0C0, ["navy"] = 0x000080, ["teal"] = 0x008080, ["lime"] = 0x00FF00,
        ["maroon"] = 0x800000, ["olive"] = 0x808000, ["gold"] = 0xFFD700, ["violet"] = 0xEE82EE, ["indigo"] = 0x4B0082,
        ["turquoise"] = 0x40E0D0, ["beige"] = 0xF5F5DC, ["coral"] = 0xFF7F50, ["salmon"] = 0xFA8072, ["khaki"] = 0xF0E68C,
        ["crimson"] = 0xDC143C, ["tomato"] = 0xFF6347, ["chocolate"] = 0xD2691E, ["tan"] = 0xD2B48C, ["orchid"] = 0xDA70D6,
        ["plum"] = 0xDDA0DD, ["lavender"] = 0xE6E6FA, ["darkred"] = 0x8B0000, ["darkgreen"] = 0x006400,
        ["darkblue"] = 0x00008B, ["darkorange"] = 0xFF8C00, ["darkviolet"] = 0x9400D3, ["darkcyan"] = 0x008B8B,
        ["lightblue"] = 0xADD8E6, ["lightgreen"] = 0x90EE90, ["lightyellow"] = 0xFFFFE0, ["lightpink"] = 0xFFB6C1,
        ["skyblue"] = 0x87CEEB, ["steelblue"] = 0x4682B4, ["royalblue"] = 0x4169E1, ["forestgreen"] = 0x228B22,
        ["seagreen"] = 0x2E8B57, ["springgreen"] = 0x00FF7F, ["limegreen"] = 0x32CD32, ["yellowgreen"] = 0x9ACD32,
        ["hotpink"] = 0xFF69B4, ["deeppink"] = 0xFF1493, ["firebrick"] = 0xB22222, ["sienna"] = 0xA0522D,
        ["goldenrod"] = 0xDAA520, ["slategray"] = 0x708090, ["slategrey"] = 0x708090, ["midnightblue"] = 0x191970,
        ["b"] = 0x0000FF, ["g"] = 0x008000, ["r"] = 0xFF0000, ["c"] = 0x00BFBF, ["m"] = 0xBF00BF, ["y"] = 0xBFBF00,
        ["k"] = 0x000000, ["w"] = 0xFFFFFF,
    };
}
