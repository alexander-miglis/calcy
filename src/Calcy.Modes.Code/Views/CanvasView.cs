using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Threading;
using Calcy.Scripting;
using Calcy.Scripting.Drawing;

namespace Calcy.Modes.Code.Views;

/// <summary>
/// Shows a program's <see cref="DrawCanvas"/>: the pixels are copied into a bitmap whenever the canvas changes (polled at
/// 60 frames a second) and scaled up without smoothing, so drawings stay pixel crisp. Scheme tokens are resolved with the
/// current colour scheme; colours chosen by the program are drawn as they are. While focused it passes keys to the program.
/// </summary>
public sealed class CanvasView : Control
{
    private static readonly string[] PaletteTokens = ["Card", "Ink", "Muted", "Grid", "G1", "G2", "G3", "G4", "G5"];

    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMilliseconds(16) };
    private readonly Color[] _palette = new Color[PaletteTokens.Length];
    private readonly Dictionary<string, IBrush> _brushes = [];
    private DrawCanvas? _canvas;
    private ScriptKeyboard? _keyboard;
    private WriteableBitmap? _bitmap;
    private uint[]? _pixels;
    private int _version = -1;
    private int _width;
    private int _height;
    private DrawSprite? _sprite;
    private bool _hasDrawing;
    private bool _paletteDirty = true;

    /// <summary>Creates the view.</summary>
    public CanvasView()
    {
        Focusable = true;
        ClipToBounds = true;
        RenderOptions.SetBitmapInterpolationMode(this, Avalonia.Media.Imaging.BitmapInterpolationMode.None);
        _timer.Tick += (_, _) => Refresh();
        for (int i = 0; i < PaletteTokens.Length; i++)
        {
            int index = i;
            this.GetResourceObservable($"Calcy.{PaletteTokens[i]}.Color").Subscribe(new Observer(v =>
            {
                if (v is Color c) _palette[index] = c;
                _paletteDirty = true;
                InvalidateVisual();
            }));
        }
        foreach (var token in new[] { "Card", "Ink", "Muted", "Line", "Paper" })
        {
            var t = token;
            this.GetResourceObservable($"Calcy.{t}").Subscribe(new Observer(v =>
            {
                if (v is IBrush b) _brushes[t] = b;
                InvalidateVisual();
            }));
        }
        this.GetResourceObservable("Calcy.Font.Label").Subscribe(new Observer(v =>
        {
            if (v is FontFamily f) _labelFont = f;
            InvalidateVisual();
        }));
    }

    private FontFamily _labelFont = FontFamily.Default;

    /// <summary>The canvas to show.</summary>
    public DrawCanvas? Canvas
    {
        get => _canvas;
        set
        {
            _canvas = value;
            _version = -1;
            Refresh();
        }
    }

    /// <summary>Receives key presses while the view has focus (null to ignore keys).</summary>
    public ScriptKeyboard? Keyboard
    {
        get => _keyboard;
        set => _keyboard = value;
    }

    /// <summary>The message shown while nothing has been drawn.</summary>
    public string EmptyText { get; set; } = "Drawings from turtle and ti_plotlib appear here";

    /// <summary>True when the canvas shows a drawing.</summary>
    public bool HasDrawing => _hasDrawing;

    /// <summary>Logical size of the picture in canvas pixels.</summary>
    public PixelSize PictureSize => new(Math.Max(1, _width), Math.Max(1, _height));

    /// <inheritdoc />
    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        _timer.Start();
    }

    /// <inheritdoc />
    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        _timer.Stop();
    }

    /// <summary>Copies the canvas picture if it changed since the last frame.</summary>
    public void Refresh()
    {
        var canvas = _canvas;
        if (canvas is null) return;
        if (!canvas.TryCopyFrame(ref _pixels, ref _version, out var w, out var h, out var sprite, out var has) && !_paletteDirty)
        {
            if (!Equals(sprite, _sprite) || has != _hasDrawing)
            {
                _sprite = sprite;
                _hasDrawing = has;
                InvalidateVisual();
            }
            return;
        }
        bool resized = w != _width || h != _height;
        _width = w;
        _height = h;
        _sprite = sprite;
        _hasDrawing = has;
        UpdateBitmap();
        if (resized) InvalidateMeasure();
        InvalidateVisual();
    }

    private void UpdateBitmap()
    {
        if (_pixels is null || _width <= 0 || _height <= 0) return;
        if (_bitmap is null || _bitmap.PixelSize.Width != _width || _bitmap.PixelSize.Height != _height)
        {
            _bitmap?.Dispose();
            _bitmap = new WriteableBitmap(new PixelSize(_width, _height), new Vector(96, 96), PixelFormat.Bgra8888, AlphaFormat.Premul);
        }
        _paletteDirty = false;
        var palette = new int[_palette.Length];
        for (int i = 0; i < palette.Length; i++)
        {
            var c = _palette[i];
            palette[i] = unchecked((int)(0xFF000000u | ((uint)c.R << 16) | ((uint)c.G << 8) | c.B));
        }
        using var fb = _bitmap.Lock();
        var src = _pixels;
        var row = new int[_width];
        for (int y = 0; y < _height; y++)
        {
            int o = y * _width;
            for (int x = 0; x < _width; x++)
            {
                uint v = src[o + x];
                row[x] = (v & 0xFF000000u) == 0 ? palette[Math.Min(v, (uint)palette.Length - 1)] : unchecked((int)v);
            }
            System.Runtime.InteropServices.Marshal.Copy(row, 0, fb.Address + y * fb.RowBytes, _width);
        }
    }

    /// <summary>The picture rectangle inside the control: scaled to fit, by whole pixels when it is large enough.</summary>
    public Rect PictureRect()
    {
        var b = Bounds.Size;
        double w = Math.Max(1, _width), h = Math.Max(1, _height);
        double scale = Math.Min(b.Width / w, b.Height / h);
        if (scale >= 1) scale = Math.Floor(scale * 4) / 4;
        if (scale <= 0) scale = 1;
        double pw = w * scale, ph = h * scale;
        return new Rect(Math.Round((b.Width - pw) / 2), Math.Round((b.Height - ph) / 2), pw, ph);
    }

    /// <inheritdoc />
    protected override Size MeasureOverride(Size availableSize)
    {
        double w = Math.Max(1, _width == 0 ? DrawCanvas.DefaultWidth : _width);
        double h = Math.Max(1, _height == 0 ? DrawCanvas.DefaultHeight : _height);
        double width = double.IsInfinity(availableSize.Width) ? w : availableSize.Width;
        double height = width * h / w;
        if (!double.IsInfinity(availableSize.Height)) height = Math.Min(height, availableSize.Height);
        return new Size(width, height);
    }

    /// <inheritdoc />
    public override void Render(DrawingContext context)
    {
        var bounds = new Rect(Bounds.Size);
        context.FillRectangle(Brush("Card"), bounds);
        if (!_hasDrawing || _bitmap is null)
        {
            var text = new FormattedText(EmptyText, System.Globalization.CultureInfo.CurrentUICulture, FlowDirection.LeftToRight,
                new Typeface(_labelFont), 12, Brush("Muted"))
            {
                MaxTextWidth = Math.Max(40, bounds.Width - 24),
                TextAlignment = TextAlignment.Center,
            };
            context.DrawText(text, new Point(12, (bounds.Height - text.Height) / 2));
            return;
        }
        if (_paletteDirty) UpdateBitmap();
        var r = PictureRect();
        context.DrawImage(_bitmap, new Rect(0, 0, _width, _height), r);
        if (_sprite is { } s) DrawTurtle(context, s, r);
    }

    private void DrawTurtle(DrawingContext ctx, DrawSprite s, Rect r)
    {
        double scale = r.Width / Math.Max(1, _width);
        double cx = r.X + (s.X + 0.5) * scale, cy = r.Y + (s.Y + 0.5) * scale;
        double a = s.Heading * Math.PI / 180, size = Math.Clamp(7 * scale, 7, 14);
        Point P(double dx, double dy) => new(cx + dx * Math.Cos(a) + dy * Math.Sin(a), cy - dx * Math.Sin(a) + dy * Math.Cos(a));
        var geometry = new StreamGeometry();
        using (var g = geometry.Open())
        {
            g.BeginFigure(P(size * 1.3, 0), true);
            g.LineTo(P(-size * 0.8, size * 0.8));
            g.LineTo(P(-size * 0.35, 0));
            g.LineTo(P(-size * 0.8, -size * 0.8));
            g.EndFigure(true);
        }
        ctx.DrawGeometry(new SolidColorBrush(Resolve(s.Color)), new Pen(Brush("Card"), 1.5), geometry);
    }

    private Color Resolve(DrawColor c)
    {
        if (c.IsToken) return _palette[Math.Min((int)c.Token, _palette.Length - 1)];
        var (r, g, b) = c.ToRgb();
        return Color.FromRgb(r, g, b);
    }

    private IBrush Brush(string token) => _brushes.TryGetValue(token, out var b) ? b : Brushes.Gray;

    /// <inheritdoc />
    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        Focus();
    }

    /// <inheritdoc />
    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (_keyboard is null || e.Handled || e.Key == Key.Escape) return;
        var name = KeyName(e);
        if (name.Length == 0) return;
        _keyboard.Press(name);
        e.Handled = true;
    }

    /// <inheritdoc />
    protected override void OnKeyUp(KeyEventArgs e)
    {
        base.OnKeyUp(e);
        if (_keyboard is null) return;
        var name = KeyName(e);
        if (name.Length > 0) _keyboard.Release(name);
    }

    /// <summary>The program's name for a key event: the typed symbol for printable keys, otherwise the key name.</summary>
    public static string KeyName(KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Up: return "up";
            case Key.Down: return "down";
            case Key.Left: return "left";
            case Key.Right: return "right";
            case Key.Enter: return "enter";
            case Key.Space: return "space";
            case Key.Tab: return "tab";
            case Key.Back or Key.Delete: return "del";
            case Key.Home: return "home";
            case Key.End: return "end";
            case Key.PageUp: return "pageup";
            case Key.PageDown: return "pagedown";
            case Key.LeftShift or Key.RightShift or Key.LeftCtrl or Key.RightCtrl or Key.LeftAlt or Key.RightAlt or Key.LWin or Key.RWin:
                return "";
        }
        if (!string.IsNullOrEmpty(e.KeySymbol) && e.KeySymbol.Length == 1 && !char.IsControl(e.KeySymbol[0]))
            return ScriptKeyboard.Normalize(e.KeySymbol);
        return ScriptKeyboard.Normalize(e.Key.ToString());
    }

    private sealed class Observer(Action<object?> next) : IObserver<object?>
    {
        public void OnCompleted() { }
        public void OnError(Exception error) { }
        public void OnNext(object? value) => next(value);
    }
}
