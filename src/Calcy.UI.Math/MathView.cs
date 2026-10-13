using Avalonia;
using Avalonia.Automation;
using Avalonia.Automation.Peers;
using Avalonia.Controls;
using Avalonia.Media;
using Calcy.Core;
using Calcy.Core.Expressions;
using Calcy.Core.Printing;
using Calcy.UI.Math.Layout;
using Calcy.UI.Math.Model;
using Calcy.UI.Math.Rendering;

namespace Calcy.UI.Math;

/// <summary>What <see cref="MathView"/> does when the maths is wider than the space it gets.</summary>
public enum MathOverflowMode
{
    /// <summary>Break long sums at + and − (and at =) onto more lines, then scale down if a line is still too wide.</summary>
    Auto,

    /// <summary>Scale the whole expression down to fit (never wrap).</summary>
    Scale,
}

/// <summary>
/// Displays an expression in 2D textbook form (fractions, roots, powers, integrals...). Set <see cref="Expression"/>
/// to a Calcy.Core expression, or <see cref="Text"/> to linear Calcy input which is parsed.
/// </summary>
public class MathView : Control
{
    /// <summary>The expression to show (takes precedence over <see cref="Text"/>).</summary>
    public static readonly StyledProperty<Expr?> ExpressionProperty =
        AvaloniaProperty.Register<MathView, Expr?>(nameof(Expression));

    /// <summary>Linear Calcy input to parse and show when <see cref="Expression"/> is null.</summary>
    public static readonly StyledProperty<string?> TextProperty =
        AvaloniaProperty.Register<MathView, string?>(nameof(Text));

    /// <summary>Optional session used to parse <see cref="Text"/> (known functions and variables) and format numbers.</summary>
    public static readonly StyledProperty<CalcSession?> SessionProperty =
        AvaloniaProperty.Register<MathView, CalcSession?>(nameof(Session));

    /// <summary>Font size of the top-level text.</summary>
    public static readonly StyledProperty<double> FontSizeProperty =
        AvaloniaProperty.Register<MathView, double>(nameof(FontSize), 20.0, validate: v => v > 0 && double.IsFinite(v));

    /// <summary>Text colour override; null uses the theme's "Calcy.Ink".</summary>
    public static readonly StyledProperty<IBrush?> ForegroundProperty =
        AvaloniaProperty.Register<MathView, IBrush?>(nameof(Foreground));

    /// <summary>Horizontal alignment of the maths inside the control.</summary>
    public static readonly StyledProperty<TextAlignment> TextAlignmentProperty =
        AvaloniaProperty.Register<MathView, TextAlignment>(nameof(TextAlignment), TextAlignment.Left);

    /// <summary>What to do when the maths is too wide.</summary>
    public static readonly StyledProperty<MathOverflowMode> OverflowModeProperty =
        AvaloniaProperty.Register<MathView, MathOverflowMode>(nameof(OverflowMode), MathOverflowMode.Auto);

    /// <summary>Show the input as typed (term order, explicit multiplication) instead of the canonical result form.</summary>
    /// <summary>Keep the order of terms as written (E = mc², v = u + at) while using display conventions; ignored with PreserveStructure.</summary>
    public static readonly StyledProperty<bool> KeepTermOrderProperty =
        AvaloniaProperty.Register<MathView, bool>(nameof(KeepTermOrder));

    public static readonly StyledProperty<bool> PreserveStructureProperty =
        AvaloniaProperty.Register<MathView, bool>(nameof(PreserveStructure));

    /// <summary>Number formatting; null uses the session settings or the display defaults.</summary>
    public static readonly StyledProperty<PrintOptions?> PrintOptionsProperty =
        AvaloniaProperty.Register<MathView, PrintOptions?>(nameof(PrintOptions));

    /// <summary>Space around the maths.</summary>
    public static readonly StyledProperty<Thickness> PaddingProperty =
        AvaloniaProperty.Register<MathView, Thickness>(nameof(Padding));

    /// <summary>Draw single-letter variables in italics.</summary>
    public static readonly StyledProperty<bool> ItalicVariablesProperty =
        AvaloniaProperty.Register<MathView, bool>(nameof(ItalicVariables));

    /// <summary>The parse error of <see cref="Text"/>, or null.</summary>
    public static readonly DirectProperty<MathView, string?> ErrorMessageProperty =
        AvaloniaProperty.RegisterDirect<MathView, string?>(nameof(ErrorMessage), o => o.ErrorMessage);

    /// <summary>The scale applied to fit the available width (1 when it fits).</summary>
    public static readonly DirectProperty<MathView, double> ActualScaleProperty =
        AvaloniaProperty.RegisterDirect<MathView, double>(nameof(ActualScale), o => o.ActualScale);

    private MathRow? _model;
    private MathLayout? _layout;
    private MathPalette? _palette;
    private AvaloniaMathFont? _font;
    private string? _error;
    private double _scale = 1;
    private string _linear = "";

    static MathView()
    {
        AffectsMeasure<MathView>(ExpressionProperty, TextProperty, SessionProperty, FontSizeProperty, OverflowModeProperty,
            PreserveStructureProperty, KeepTermOrderProperty, PrintOptionsProperty, PaddingProperty, ItalicVariablesProperty);
        AffectsRender<MathView>(ForegroundProperty, TextAlignmentProperty);
    }

    /// <summary>Creates the view.</summary>
    public MathView()
    {
        ClipToBounds = false;
        ResourcesChanged += (_, _) => OnThemeChanged();
        ActualThemeVariantChanged += (_, _) => OnThemeChanged();
    }

    /// <inheritdoc cref="ExpressionProperty"/>
    public Expr? Expression { get => GetValue(ExpressionProperty); set => SetValue(ExpressionProperty, value); }

    /// <inheritdoc cref="TextProperty"/>
    public string? Text { get => GetValue(TextProperty); set => SetValue(TextProperty, value); }

    /// <inheritdoc cref="SessionProperty"/>
    public CalcSession? Session { get => GetValue(SessionProperty); set => SetValue(SessionProperty, value); }

    /// <inheritdoc cref="FontSizeProperty"/>
    public double FontSize { get => GetValue(FontSizeProperty); set => SetValue(FontSizeProperty, value); }

    /// <inheritdoc cref="ForegroundProperty"/>
    public IBrush? Foreground { get => GetValue(ForegroundProperty); set => SetValue(ForegroundProperty, value); }

    /// <inheritdoc cref="TextAlignmentProperty"/>
    public TextAlignment TextAlignment { get => GetValue(TextAlignmentProperty); set => SetValue(TextAlignmentProperty, value); }

    /// <inheritdoc cref="OverflowModeProperty"/>
    public MathOverflowMode OverflowMode { get => GetValue(OverflowModeProperty); set => SetValue(OverflowModeProperty, value); }

    /// <inheritdoc cref="PreserveStructureProperty"/>
    public bool PreserveStructure { get => GetValue(PreserveStructureProperty); set => SetValue(PreserveStructureProperty, value); }

    /// <inheritdoc cref="KeepTermOrderProperty"/>
    public bool KeepTermOrder { get => GetValue(KeepTermOrderProperty); set => SetValue(KeepTermOrderProperty, value); }

    /// <inheritdoc cref="PrintOptionsProperty"/>
    public PrintOptions? PrintOptions { get => GetValue(PrintOptionsProperty); set => SetValue(PrintOptionsProperty, value); }

    /// <inheritdoc cref="PaddingProperty"/>
    public Thickness Padding { get => GetValue(PaddingProperty); set => SetValue(PaddingProperty, value); }

    /// <inheritdoc cref="ItalicVariablesProperty"/>
    public bool ItalicVariables { get => GetValue(ItalicVariablesProperty); set => SetValue(ItalicVariablesProperty, value); }

    /// <inheritdoc cref="ErrorMessageProperty"/>
    public string? ErrorMessage { get => _error; private set => SetAndRaise(ErrorMessageProperty, ref _error, value); }

    /// <inheritdoc cref="ActualScaleProperty"/>
    public double ActualScale { get => _scale; private set => SetAndRaise(ActualScaleProperty, ref _scale, value); }

    /// <summary>The current layout (after measure), for tests and tools.</summary>
    public MathLayout? CurrentLayout => _layout;

    /// <summary>The shown maths as linear text (also exposed to screen readers).</summary>
    public string LinearText => _linear;

    /// <inheritdoc />
    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == ExpressionProperty || change.Property == TextProperty || change.Property == SessionProperty
            || change.Property == PreserveStructureProperty || change.Property == KeepTermOrderProperty || change.Property == PrintOptionsProperty)
        {
            _model = null;
        }
    }

    private void OnThemeChanged()
    {
        _palette = null;
        var font = MathPalette.FindFont(this);
        if (!ReferenceEquals(font, _font))
        {
            _font = font;
            InvalidateMeasure();
        }
        InvalidateVisual();
    }

    private MathRow BuildModel()
    {
        string? error = null;
        MathRow row;
        var session = Session;
        var options = PrintOptions ?? (session is null ? Core.Printing.PrintOptions.Display
            : new PrintOptions { Numbers = NumberFormatOptions.From(session.Settings) });
        if (Expression is { } e)
        {
            row = PreserveStructure ? MathInputBuilder.FromExpr(e) : MathDisplayBuilder.FromExpr(e, options, KeepTermOrder);
            _linear = ExprPrinter.Print(e, options);
        }
        else if (!string.IsNullOrWhiteSpace(Text))
        {
            var st = MathParsing.TryParseStatement(Text, session, out var ex);
            if (st is null)
            {
                row = MathInputBuilder.PlainText(Text);
                error = ex?.Message;
            }
            else row = PreserveStructure ? MathInputBuilder.FromStatement(st, session) : MathDisplayBuilder.FromStatement(st, options, KeepTermOrder);
            _linear = Text;
        }
        else
        {
            row = new MathRow();
            _linear = "";
        }
        ErrorMessage = error;
        return row;
    }

    /// <inheritdoc />
    protected override Size MeasureOverride(Size availableSize)
    {
        _model ??= BuildModel();
        _font ??= MathPalette.FindFont(this);
        var pad = Padding;
        var engine = new MathLayoutEngine(_font, new MathLayoutOptions { FontSize = FontSize, ItalicVariables = ItalicVariables, ShowEmptySlots = true });
        var layout = engine.Layout(_model);
        double avail = availableSize.Width - pad.Left - pad.Right;
        double scale = 1;
        if (double.IsFinite(avail) && avail > 0 && layout.Width > avail)
        {
            if (OverflowMode != MathOverflowMode.Scale)
            {
                var lines = MathLineBreaker.BreakLines(_model, layout, avail, FontSize);
                if (lines is not null) layout = engine.LayoutLines(lines, FontSize, 0.35 * FontSize);
            }
            if (layout.Width > avail) scale = avail / layout.Width;
        }
        _layout = layout;
        ActualScale = scale;
        return new Size(layout.Width * scale + pad.Left + pad.Right, layout.Height * scale + pad.Top + pad.Bottom);
    }

    /// <inheritdoc />
    public override void Render(DrawingContext context)
    {
        base.Render(context);
        if (_layout is null || _font is null) return;
        _palette ??= MathPalette.Resolve(this);
        var pad = Padding;
        double contentW = _layout.Width * _scale;
        double avail = Bounds.Width - pad.Left - pad.Right;
        double x = pad.Left + TextAlignment switch
        {
            TextAlignment.Center => System.Math.Max(0, (avail - contentW) / 2),
            TextAlignment.Right or TextAlignment.End => System.Math.Max(0, avail - contentW),
            _ => 0,
        };
        var palette = _palette;
        var ink = Foreground ?? palette.Ink;
        using (context.PushTransform(Matrix.CreateScale(_scale, _scale) * Matrix.CreateTranslation(x, pad.Top)))
        {
            MathRenderer.Draw(context, _layout.Content, _font, role => role == MathColorRole.Ink ? ink : palette.For(role));
        }
    }

    /// <inheritdoc />
    protected override AutomationPeer OnCreateAutomationPeer() => new MathViewAutomationPeer(this);
}

/// <summary>Exposes a <see cref="MathView"/> to screen readers as text with its linear form as the name.</summary>
public sealed class MathViewAutomationPeer(MathView owner) : ControlAutomationPeer(owner)
{
    /// <inheritdoc />
    protected override AutomationControlType GetAutomationControlTypeCore() => AutomationControlType.Text;

    /// <inheritdoc />
    protected override string? GetNameCore()
    {
        var explicitName = AutomationProperties.GetName(Owner);
        if (!string.IsNullOrEmpty(explicitName)) return explicitName;
        var view = (MathView)Owner;
        if (view.Expression is { } e) return ExprPrinter.ToText(e);
        return view.Text ?? "";
    }
}
