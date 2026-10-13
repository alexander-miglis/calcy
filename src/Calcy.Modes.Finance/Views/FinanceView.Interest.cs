using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;
using Calcy.Modes.Finance.Logic;
using Calcy.UI.Controls;

namespace Calcy.Modes.Finance.Views;

/// <summary>The Interest tab: nominal and effective rates, then simple and compound interest on a sum.</summary>
public sealed partial class FinanceView
{
    private List<PixelToggle> _direction = null!;
    private TextBox _convRate = null!;
    private TextBox _convM = null!;
    private TextBlock _convFrom = null!;
    private TextBlock _convResult = null!;
    private TextBlock _convErrors = null!;
    private TextBox _principal = null!;
    private TextBox _annualRate = null!;
    private TextBox _days = null!;
    private List<PixelToggle> _basis = null!;
    private TextBox _years = null!;
    private TextBox _yearM = null!;
    private TextBlock _simpleResult = null!;
    private TextBlock _compoundResult = null!;
    private TextBlock _sumErrors = null!;
    private Answer<RateConversion>? _conversion;
    private Answer<SimpleInterestAnswer>? _simple;
    private Answer<CompoundInterestAnswer>? _compound;

    /// <summary>The converted rate on screen (for tests).</summary>
    internal string ConversionText => _convResult.Text ?? "";

    private Control BuildInterestPage()
    {
        var page = new StackPanel();
        var i = _state.Interest;

        // Nominal and effective rates, as a sentence (design).
        var rates = new StackPanel { Spacing = 12 };
        rates.Children.Add(Title("Nominal and effective rates"));
        var (dirGroup, dirItems) = Ui.Segmented("Conversion direction", [("Nominal to effective", "Convert a nominal rate to the effective rate"), ("Effective to nominal", "Convert an effective rate to the nominal rate")], 40);
        _direction = dirItems;
        dirGroup.HorizontalAlignment = HorizontalAlignment.Left;
        for (int k = 0; k < dirItems.Count; k++)
        {
            int index = k;
            dirItems[k].Click += (_, _) =>
            {
                Ui.Check(_direction, index);
                _state.Interest.Direction = index == 0 ? RateDirection.NominalToEffective : RateDirection.EffectiveToNominal;
                RecomputeInterest();
                SaveSoon();
            };
        }
        Ui.Check(_direction, i.Direction == RateDirection.NominalToEffective ? 0 : 1);
        rates.Children.Add(dirGroup);

        _convRate = Ui.Input("Rate to convert, in percent", "The rate in percent", 110);
        _convM = Ui.Input("Compounding periods per year", "How often interest is added each year: 12 monthly, 365 daily, continuous", 120);
        _convRate.Text = i.Rate;
        _convM.Text = i.Compoundings;
        _convFrom = Words("% nominal, compounded");
        _convResult = Ui.Text("?", "mathline");
        _convResult.FontWeight = Avalonia.Media.FontWeight.Bold;
        AutomationProperties.SetLiveSetting(_convResult, AutomationLiveSetting.Polite);
        var resultBox = new Border { Child = _convResult, VerticalAlignment = VerticalAlignment.Center };
        resultBox.Classes.Add("box");
        resultBox.Classes.Add("sel");
        var sentence = new WrapPanel { Orientation = Orientation.Horizontal };
        foreach (var c in new Control[] { Gap(_convRate), _convFrom, Gap(_convM), Words("times a year"), Words("="), resultBox })
        {
            c.Margin = new Thickness(c.Margin.Left, 0, Math.Max(c.Margin.Right, 10), 8);
            c.VerticalAlignment = VerticalAlignment.Center;
            sentence.Children.Add(c);
        }
        rates.Children.Add(sentence);
        _convErrors = Ui.Text("", "error");
        _convErrors.IsVisible = false;
        rates.Children.Add(_convErrors);
        var rateHint = Ui.Text("Type continuous (or inf) for continuous compounding. Banks quote the nominal rate (APR); the effective rate (APY) is what a year really earns.", "hint");
        var rateFoot = new DockPanel();
        var rateCard = AddCardButton("rate conversion", AddRateCard);
        DockPanel.SetDock(rateCard, Dock.Right);
        rateCard.VerticalAlignment = VerticalAlignment.Top;
        rateHint.Margin = new Thickness(0, 0, 16, 0);
        rateFoot.Children.Add(rateCard);
        rateFoot.Children.Add(rateHint);
        rates.Children.Add(rateFoot);
        Field(_convRate, AddRateCard, FinanceTab.Interest);
        Field(_convM, AddRateCard, FinanceTab.Interest);
        Ui.OnText(_convRate, () => OnInterestEdited());
        Ui.OnText(_convM, () => OnInterestEdited());
        page.Children.Add(Section(rates));

        // Interest on a sum: simple and compound.
        var sums = new StackPanel { Spacing = 12 };
        sums.Children.Add(Title("Interest on a sum", "Simple interest is paid on the principal only; compound interest is also paid on earlier interest."));
        _principal = Ui.Input("Principal", "The amount", 150);
        _annualRate = Ui.Input("Rate per year in percent", "The yearly rate in percent", 100);
        _principal.Text = i.Principal;
        _annualRate.Text = i.AnnualRate;
        var top = new WrapPanel { Orientation = Orientation.Horizontal };
        var p1 = Ui.Labelled("Principal", _principal);
        p1.Margin = new Thickness(0, 0, 14, 0);
        top.Children.Add(p1);
        top.Children.Add(Ui.Labelled("Rate % a year", _annualRate));
        sums.Children.Add(top);

        _days = Ui.Input("Days", "Number of days", 90);
        _days.Text = i.Days;
        var (basis, basisItems) = Ui.Segmented("Days in a year", [("365", "365 day year"), ("360", "360 day year (banker's year)")], 44);
        _basis = basisItems;
        basis.Width = 150;
        for (int k = 0; k < basisItems.Count; k++)
        {
            int index = k;
            basisItems[k].Click += (_, _) =>
            {
                Ui.Check(_basis, index);
                _state.Interest.DayBasis = index == 0 ? 365 : 360;
                RecomputeInterest();
                SaveSoon();
            };
        }
        Ui.Check(_basis, i.DayBasis == 360 ? 1 : 0);
        _simpleResult = Ui.Text("?", "mathline");
        _simpleResult.FontWeight = Avalonia.Media.FontWeight.Bold;
        sums.Children.Add(SumRow("Simple", [Words("for"), Gap(_days), Words("days,"), basis, Words("day year")], _simpleResult, AddSimpleCard, "simple interest"));

        _years = Ui.Input("Years", "Number of years (fractions are fine)", 90);
        _yearM = Ui.Input("Compounding periods per year", "How often interest is added each year: 1, 4, 12, 365 or continuous", 120);
        _years.Text = i.Years;
        _yearM.Text = i.YearCompoundings;
        _compoundResult = Ui.Text("?", "mathline");
        _compoundResult.FontWeight = Avalonia.Media.FontWeight.Bold;
        sums.Children.Add(SumRow("Compound", [Words("for"), Gap(_years), Words("years, compounded"), Gap(_yearM), Words("times a year")], _compoundResult, AddCompoundCard, "compound interest"));
        _sumErrors = Ui.Text("", "error");
        _sumErrors.IsVisible = false;
        sums.Children.Add(_sumErrors);

        foreach (var b in new[] { _principal, _annualRate, _days })
        {
            Field(b, b == _days ? AddSimpleCard : AddCompoundCard, FinanceTab.Interest);
            Ui.OnText(b, () => OnInterestEdited());
        }
        foreach (var b in new[] { _years, _yearM })
        {
            Field(b, AddCompoundCard, FinanceTab.Interest);
            Ui.OnText(b, () => OnInterestEdited());
        }
        page.Children.Add(sums);
        return Page(page);
    }

    private static Control Gap(Control c)
    {
        c.Margin = new Thickness(0, 0, 10, 0);
        return c;
    }

    private static Border Section(Control content)
    {
        var b = new Border { Child = content };
        b.Classes.Add("section");
        return b;
    }

    /// <summary>
    /// A sentence-like block: a pixel label with the inputs and words on the first line, then "=" with the answer and an
    /// ADD CARD button on the second.
    /// </summary>
    private static Control SumRow(string label, Control[] parts, TextBlock result, Action add, string what)
    {
        var line = new WrapPanel { Orientation = Orientation.Horizontal };
        var name = Ui.Pixel(label, 2);
        name.Width = 120;
        line.Children.Add(name);
        foreach (var p in parts) line.Children.Add(p);
        foreach (var c in line.Children)
        {
            c.VerticalAlignment = VerticalAlignment.Center;
            c.Margin = new Thickness(0, 0, Math.Max(c.Margin.Right, 10), 8);
        }
        var box = new Border { Child = result, VerticalAlignment = VerticalAlignment.Center };
        box.Classes.Add("box");
        box.Classes.Add("sel");
        var equals = Words("=");
        equals.Margin = new Thickness(120, 0, 10, 0);
        var button = AddCardButton(what, add);
        button.Margin = new Thickness(16, 0, 0, 0);
        var answer = new DockPanel();
        DockPanel.SetDock(equals, Dock.Left);
        DockPanel.SetDock(button, Dock.Right);
        box.HorizontalAlignment = HorizontalAlignment.Left;
        answer.Children.Add(equals);
        answer.Children.Add(button);
        answer.Children.Add(box);
        return new StackPanel { Spacing = 2, Children = { line, answer } };
    }

    private void OnInterestEdited()
    {
        if (_loading || _yearM is null) return;
        var i = _state.Interest;
        i.Rate = _convRate.Text ?? "";
        i.Compoundings = _convM.Text ?? "";
        i.Principal = _principal.Text ?? "";
        i.AnnualRate = _annualRate.Text ?? "";
        i.Days = _days.Text ?? "";
        i.Years = _years.Text ?? "";
        i.YearCompoundings = _yearM.Text ?? "";
        RecomputeInterest();
        SaveSoon();
    }

    private void RecomputeInterest()
    {
        if (_loading || _compoundResult is null) return;
        var i = _state.Interest;
        bool toEffective = i.Direction == RateDirection.NominalToEffective;
        _convFrom.Text = toEffective ? "% nominal, compounded" : "% effective, compounded";
        _conversion = InterestTools.Convert(i, _reader);
        Ui.SetError(_convRate, _conversion.FieldErrors.GetValueOrDefault("rate"), "The rate in percent");
        Ui.SetError(_convM, _conversion.FieldErrors.GetValueOrDefault("compoundings"), "How often interest is added each year");
        _convResult.Text = _conversion.Value is { } c ? $"{Money.Percent(c.Output, 4)} {(toEffective ? "effective" : "nominal")}" : "?";
        Ui.Show(_convErrors, _conversion.FieldErrors.Count > 0 ? string.Join(" ", _conversion.FieldErrors.Values) : _conversion.Problem);

        _simple = InterestTools.Simple(i, _reader);
        _compound = InterestTools.Compound(i, _reader);
        var errors = new Dictionary<string, string>();
        foreach (var (k, v) in _simple.FieldErrors) errors[k] = v;
        foreach (var (k, v) in _compound.FieldErrors) errors[k] = v;
        Ui.SetError(_principal, errors.GetValueOrDefault("principal"), "The amount");
        Ui.SetError(_annualRate, errors.GetValueOrDefault("annualRate"), "The yearly rate in percent");
        Ui.SetError(_days, errors.GetValueOrDefault("days"), "Number of days");
        Ui.SetError(_years, errors.GetValueOrDefault("years"), "Number of years");
        Ui.SetError(_yearM, errors.GetValueOrDefault("yearCompoundings"), "How often interest is added each year");
        _simpleResult.Text = _simple.Value is { } s ? $"interest {Money.Format(s.Interest)}, total {Money.Format(s.Total)}" : "?";
        _compoundResult.Text = _compound.Value is { } cp ? $"value {Money.Format(cp.FutureValue)}, interest {Money.Format(cp.Interest)}" : "?";
        Ui.Show(_sumErrors, errors.Count > 0 ? string.Join(" ", errors.Values.Distinct()) : _compound.Problem ?? _simple.Problem);
    }

    private void AddRateCard()
    {
        if (_conversion?.Value is { } c) AddCard(FinanceCards.Rate(c));
        else _services.ShowStatus(_convErrors.Text ?? "Fix the rate first");
    }

    private void AddSimpleCard()
    {
        if (_simple?.Value is { } s) AddCard(FinanceCards.SimpleInterest(s));
        else _services.ShowStatus(_sumErrors.Text ?? "Fix the fields first");
    }

    private void AddCompoundCard()
    {
        if (_compound?.Value is { } c) AddCard(FinanceCards.CompoundInterest(c));
        else _services.ShowStatus(_sumErrors.Text ?? "Fix the fields first");
    }
}
