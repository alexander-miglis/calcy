using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Calcy.Core;
using Calcy.Modes.Statistics.Logic;
using Calcy.UI.Controls;
using Calcy.UI.Math;

namespace Calcy.Modes.Statistics.Views;

/// <summary>What the buttons of a statistics card ask the screen to do.</summary>
public interface IStatCardHost
{
    /// <summary>Copies text to the clipboard and reports it in the status bar.</summary>
    void Copy(string text, string what);

    /// <summary>Sends a calculator expression to Advanced mode.</summary>
    void Use(string text);

    /// <summary>Reopens the analysis a card came from, with its plot.</summary>
    void ShowPlot(StatRecipe recipe);

    /// <summary>The session (for typesetting).</summary>
    CalcSession Session { get; }
}

/// <summary>
/// One result card (design decision D5): the numbered header strip, the headline (typeset when it is an equation),
/// the values as "label = value" lines, a plain-language note and the COPY, COPY LATEX, USE and SHOW PLOT buttons.
/// </summary>
public sealed class StatCardView : Card
{
    /// <summary>Builds the card.</summary>
    public StatCardView(StatCard model, IStatCardHost host)
    {
        Model = model;
        Title = $"#{model.Number} {model.Title}";
        // The header strip has room for about 24 pixel-font characters; a long title leaves out the time so they never overlap.
        Meta = Title.Length + model.Time.Length <= 24 ? model.Time : null;
        IsError = model.IsError;
        Padding = new Thickness(10, 8);
        AutomationProperties.SetName(this, $"Card {model.Number}: {model.Title.ToLowerInvariant()}");

        var body = new StackPanel { Spacing = 4 };
        if (model.Headline is { } headline)
        {
            if (model.MathHeadline)
            {
                var math = new MathView
                {
                    Text = headline,
                    FontSize = 17,
                    Session = host.Session,
                    PreserveStructure = true,
                    OverflowMode = MathOverflowMode.Scale,
                    HorizontalAlignment = HorizontalAlignment.Left,
                    Margin = new Thickness(0, 0, 0, 2),
                };
                body.Children.Add(math);
            }
            else
            {
                var t = Ui.Math(headline, 14, bold: true);
                t.TextWrapping = TextWrapping.Wrap;
                body.Children.Add(t);
            }
        }
        foreach (var line in model.Lines)
        {
            var t = Ui.Math($"{line.Label} = {line.Value}", 14);
            t.TextWrapping = TextWrapping.Wrap;
            body.Children.Add(t);
        }
        if (model.Note is { Length: > 0 } note)
        {
            var n = Ui.Body(note, 12, muted: !model.IsError);
            n.Margin = new Thickness(0, 2, 0, 0);
            if (model.IsError) n.Token(TextBlock.ForegroundProperty, "Calcy.Err");
            body.Children.Add(n);
        }
        var actions = new WrapPanel { ItemSpacing = 6, LineSpacing = 6, Margin = new Thickness(0, 6, 0, 0) };
        if (!model.IsError)
        {
            actions.Children.Add(Ui.Small("COPY", "Copy this card as text", () => host.Copy(model.CopyText, "the card")));
            if (model.Latex is { } latex) actions.Children.Add(Ui.Small("COPY LATEX", "Copy as LaTeX", () => host.Copy(latex, "LaTeX")));
            if (model.UseText is { } use) actions.Children.Add(Ui.Small("USE", "Send " + use + " to Advanced", () => host.Use(use), "head"));
            if (model.Recipe is { } recipe) actions.Children.Add(Ui.Small("SHOW PLOT", "Open this analysis again with its plot", () => host.ShowPlot(recipe), "sel"));
        }
        else actions.Children.Add(Ui.Small("COPY", "Copy the message", () => host.Copy(model.CopyText, "the message")));
        foreach (var b in actions.Children.OfType<PixelButton>()) b.Padding = new Thickness(6, 3);
        body.Children.Add(actions);
        Content = body;
    }

    /// <summary>Uses the Calcy card look.</summary>
    protected override Type StyleKeyOverride => typeof(Card);

    /// <summary>The card's data.</summary>
    public StatCard Model { get; }
}
