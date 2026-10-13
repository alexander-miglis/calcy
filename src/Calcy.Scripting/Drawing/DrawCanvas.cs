namespace Calcy.Scripting.Drawing;

/// <summary>A copy of the visible canvas picture.</summary>
/// <param name="Width">Width in canvas pixels.</param>
/// <param name="Height">Height in canvas pixels.</param>
/// <param name="Pixels">Row-major packed <see cref="DrawColor"/> values.</param>
/// <param name="Version">The canvas version this frame shows.</param>
/// <param name="Sprite">The turtle cursor, when it is visible.</param>
/// <param name="HasDrawing">False while nothing has been drawn since the canvas was reset.</param>
public sealed record CanvasFrame(int Width, int Height, uint[] Pixels, int Version, DrawSprite? Sprite, bool HasDrawing)
{
    /// <summary>The colour of a pixel.</summary>
    public DrawColor this[int x, int y] => DrawColor.FromPacked(Pixels[y * Width + x]);

    /// <summary>
    /// The picture as opaque 0xAARRGGBB pixels (row-major), for saving an image. Scheme tokens use <paramref name="palette"/>
    /// when it has them, otherwise their reference colours (<see cref="DrawColor.ToRgb"/>).
    /// </summary>
    public uint[] ToArgb(IReadOnlyDictionary<DrawToken, (byte R, byte G, byte B)>? palette = null)
    {
        var result = new uint[Pixels.Length];
        for (int i = 0; i < Pixels.Length; i++)
        {
            var c = DrawColor.FromPacked(Pixels[i]);
            var (r, g, b) = c.IsToken && palette is not null && palette.TryGetValue(c.Token, out var p) ? p : c.ToRgb();
            result[i] = 0xFF000000u | ((uint)r << 16) | ((uint)g << 8) | b;
        }
        return result;
    }
}

/// <summary>
/// The drawing surface of a script host: a fixed grid of pixels that drawing commands are rasterised into, plus the
/// list of commands since the last clear. With double buffering on, drawing goes to a back buffer and only
/// <see cref="Show"/> makes it visible, which gives flicker free animation. Safe to use from several threads: the
/// interpreter draws while the screen copies frames.
/// </summary>
public sealed class DrawCanvas
{
    /// <summary>Default width in pixels.</summary>
    public const int DefaultWidth = 320;

    /// <summary>Default height in pixels.</summary>
    public const int DefaultHeight = 220;

    /// <summary>Largest allowed width or height.</summary>
    public const int MaxSide = 2048;

    private readonly object _gate = new();
    private readonly List<DrawCommand> _log = [];
    private uint[] _back = [];
    private uint[] _front = [];
    private Rasterizer _raster = null!;
    private Rasterizer? _frontRaster;
    private bool _buffered;
    private bool _used;
    private bool _truncated;
    private bool _pending;
    private int _version;
    private DrawSprite? _sprite;
    private DrawColor _background = DrawColor.Background;

    /// <summary>Creates a canvas of the default size.</summary>
    public DrawCanvas(int maxLoggedCommands = 100_000)
    {
        MaxLoggedCommands = Math.Max(0, maxLoggedCommands);
        Reset();
    }

    /// <summary>How many commands are kept in <see cref="Commands"/> before the log is truncated (the picture is still drawn).</summary>
    public int MaxLoggedCommands { get; set; }

    /// <summary>Width in pixels.</summary>
    public int Width { get; private set; }

    /// <summary>Height in pixels.</summary>
    public int Height { get; private set; }

    /// <summary>Increases whenever the visible picture or the turtle cursor changes.</summary>
    public int Version
    {
        get { lock (_gate) return _version; }
    }

    /// <summary>True when drawing goes to a back buffer until <see cref="Show"/>.</summary>
    public bool IsBuffered
    {
        get { lock (_gate) return _buffered; }
    }

    /// <summary>True once anything was drawn since the last <see cref="Reset"/>.</summary>
    public bool HasDrawing
    {
        get { lock (_gate) return _used; }
    }

    /// <summary>The background colour that clearing fills with.</summary>
    public DrawColor BackgroundColor
    {
        get { lock (_gate) return _background; }
    }

    /// <summary>The turtle cursor, or null when hidden.</summary>
    public DrawSprite? Sprite
    {
        get { lock (_gate) return _sprite; }
    }

    /// <summary>True when more commands were drawn since the last clear than <see cref="MaxLoggedCommands"/>.</summary>
    public bool IsLogTruncated
    {
        get { lock (_gate) return _truncated; }
    }

    /// <summary>A copy of the commands drawn since the last clear (the first one is that clear, if any).</summary>
    public IReadOnlyList<DrawCommand> Commands
    {
        get { lock (_gate) return [.. _log]; }
    }

    /// <summary>Clears everything and sets a new size (clamped to 1 to <see cref="MaxSide"/>); buffering is switched off.</summary>
    public void Reset(int width = DefaultWidth, int height = DefaultHeight)
    {
        lock (_gate)
        {
            Width = Math.Clamp(width, 1, MaxSide);
            Height = Math.Clamp(height, 1, MaxSide);
            _background = DrawColor.Background;
            _back = new uint[Width * Height];
            Array.Fill(_back, _background.Value);
            _front = _back;
            _raster = new Rasterizer(_back, Width, Height);
            _frontRaster = null;
            _buffered = false;
            _used = false;
            _truncated = false;
            _pending = false;
            _sprite = null;
            _log.Clear();
            _version++;
        }
    }

    /// <summary>Draws a command (into the back buffer when double buffered).</summary>
    public void Execute(DrawCommand command)
    {
        ArgumentNullException.ThrowIfNull(command);
        lock (_gate)
        {
            _used = true;
            switch (command.Kind)
            {
                case DrawKind.Show:
                    ShowCore();
                    return;
                case DrawKind.Background:
                {
                    uint old = _background.Value;
                    _background = command.Color.IsToken && command.Color.Token == DrawToken.Background ? DrawColor.Background : command.Color;
                    _raster.Replace(old, _background.Value);
                    _frontRaster?.Replace(old, _background.Value);
                    Log(command);
                    _version++;
                    return;
                }
                case DrawKind.Clear:
                {
                    if (command.Color.IsToken && command.Color.Token == DrawToken.Background) command = command with { Color = _background };
                    _log.Clear();
                    _truncated = false;
                    break;
                }
            }
            Log(command);
            _raster.Execute(command);
            if (_buffered) _pending = true;
            else _version++;
        }
    }

    private void Log(DrawCommand command)
    {
        if (_truncated) return;
        if (_log.Count >= MaxLoggedCommands)
        {
            _truncated = true;
            return;
        }
        _log.Add(command);
    }

    /// <summary>Makes the back buffer visible (does nothing visible when not double buffered).</summary>
    public void Show()
    {
        lock (_gate)
        {
            _used = true;
            ShowCore();
        }
    }

    private void ShowCore()
    {
        Log(new DrawCommand { Kind = DrawKind.Show });
        if (_buffered) Array.Copy(_back, _front, _back.Length);
        _pending = false;
        _version++;
    }

    /// <summary>Shows the back buffer when something was drawn into it since the last <see cref="Show"/>.</summary>
    public void ShowPending()
    {
        lock (_gate)
        {
            if (_buffered && _pending) ShowCore();
        }
    }

    /// <summary>Switches double buffering on or off. Switching it off shows the back buffer.</summary>
    public void SetBuffered(bool buffered)
    {
        lock (_gate)
        {
            if (buffered == _buffered) return;
            _buffered = buffered;
            if (buffered)
            {
                _front = (uint[])_back.Clone();
                _frontRaster = new Rasterizer(_front, Width, Height);
            }
            else
            {
                _front = _back;
                _frontRaster = null;
                _version++;
            }
        }
    }

    /// <summary>Shows or hides the turtle cursor.</summary>
    public void SetSprite(DrawSprite? sprite)
    {
        lock (_gate)
        {
            if (Equals(_sprite, sprite)) return;
            _sprite = sprite;
            _version++;
        }
    }

    /// <summary>The colour of a pixel in the drawing buffer, or null outside the canvas.</summary>
    public DrawColor? GetPixel(int x, int y)
    {
        lock (_gate)
        {
            if ((uint)x >= (uint)Width || (uint)y >= (uint)Height) return null;
            return DrawColor.FromPacked(_back[y * Width + x]);
        }
    }

    /// <summary>Copies the visible picture.</summary>
    public CanvasFrame Capture()
    {
        lock (_gate)
        {
            return new CanvasFrame(Width, Height, (uint[])_front.Clone(), _version, _sprite, _used);
        }
    }

    /// <summary>
    /// Copies the visible picture into <paramref name="buffer"/> (reallocated when the size changed) if it changed since
    /// <paramref name="knownVersion"/>, which is then updated. Returns false when nothing changed.
    /// </summary>
    public bool TryCopyFrame(ref uint[]? buffer, ref int knownVersion, out int width, out int height, out DrawSprite? sprite, out bool hasDrawing)
    {
        lock (_gate)
        {
            width = Width;
            height = Height;
            sprite = _sprite;
            hasDrawing = _used;
            if (knownVersion == _version && buffer is not null && buffer.Length == _front.Length) return false;
            if (buffer is null || buffer.Length != _front.Length) buffer = new uint[_front.Length];
            Array.Copy(_front, buffer, _front.Length);
            knownVersion = _version;
            return true;
        }
    }
}
