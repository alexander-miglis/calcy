using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;
using Calcy.Modes.Finance.Logic;
using Calcy.Statistics;
using Calcy.UI.Controls;

namespace Calcy.Modes.Finance.Views;

/// <summary>The Bonds tab: price from yield or yield from price, with accrued interest.</summary>
public sealed partial class FinanceView
{
    private static readonly int[] Frequencies = [1, 2, 4, 12];
    private TextBox _settleBox = null!;
    private TextBox _maturityBox = null!;
    private TextBox _couponBox = null!;
    private TextBox _redemptionBox = null!;
    private TextBox _yieldBox = null!;
    private TextBox _priceBox = null!;
    private List<PixelToggle> _frequency = null!;
    private List<PixelToggle> _dayCountItems = null!;
    private TextBlock _bondErrors = null!;
    private Border _bondBox = null!;
    private PixelText _bondLabel = null!;
    private TextBlock _bondValue = null!;
    private TextBlock _bondText = null!;
    private TextBlock _accrued = null!;
    private TextBlock _dirty = null!;
    private bool _bondUpdating;

    /// <summary>The current bond answer (for tests).</summary>
    internal BondAnswer? BondAnswer { get; private set; }

    private Control BuildBondsPage()
    {
        var b = _state.Bonds;
        var left = new StackPanel { Spacing = 8 };
        left.Children.Add(Title("Bond price and yield", "Prices are per 100 of face value."));
        _settleBox = Ui.Input("Settlement date", "When you buy the bond, like 2026-10-13");
        _maturityBox = Ui.Input("Maturity date", "When the bond is repaid, like 2036-06-15");
        _settleBox.Classes.Add("left");
        _maturityBox.Classes.Add("left");
        _couponBox = Ui.Input("Coupon rate in percent a year", "The yearly coupon in percent of the face value");
        _redemptionBox = Ui.Input("Redemption value per 100", "What is repaid per 100 of face value at maturity (usually 100)");
        _yieldBox = Ui.Input("Yield to maturity in percent", "The yearly yield to maturity in percent; leave blank to work it out from the price");
        _priceBox = Ui.Input("Clean price per 100", "The price per 100 without accrued interest; leave blank to work it out from the yield");
        (_settleBox.Text, _maturityBox.Text, _couponBox.Text, _redemptionBox.Text, _yieldBox.Text, _priceBox.Text) =
            (b.Settlement, b.Maturity, b.Coupon, b.Redemption, b.Yield, b.Price);
        foreach (var box in new[] { _settleBox, _maturityBox, _couponBox, _redemptionBox, _yieldBox, _priceBox })
        {
            Field(box, () => SolveBond(null), FinanceTab.Bonds);
            Ui.OnText(box, () => OnBondEdited(box));
        }

        left.Children.Add(Row("Settles", "when you buy", _settleBox, null));
        left.Children.Add(Row("Matures", "repaid on", _maturityBox, null));
        left.Children.Add(Row("Coupon", "% a year", _couponBox, null));
        left.Children.Add(Row("Repays", "per 100", _redemptionBox, null));

        var (freq, freqItems) = Ui.Segmented("Coupons per year", Frequencies.Select(f => (f.ToString(System.Globalization.CultureInfo.InvariantCulture), $"{f} coupons a year")), 40);
        _frequency = freqItems;
        for (int k = 0; k < freqItems.Count; k++)
        {
            int index = k;
            freqItems[k].Click += (_, _) =>
            {
                Ui.Check(_frequency, index);
                _state.Bonds.Frequency = Frequencies[index];
                RecomputeBond();
                SaveSoon();
            };
        }
        Ui.Check(_frequency, Math.Max(0, Array.IndexOf(Frequencies, b.Frequency)));
        left.Children.Add(Row("Coupons", "per year", freq, null));

        var (days, dayItems) = Ui.Segmented("Day count", [("Actual", "Actual days"), ("30/360", "30/360 (US)")], 40);
        _dayCountItems = dayItems;
        for (int k = 0; k < dayItems.Count; k++)
        {
            int index = k;
            dayItems[k].Click += (_, _) =>
            {
                Ui.Check(_dayCountItems, index);
                _state.Bonds.DayCount = index == 0 ? DayCountConvention.Actual : DayCountConvention.Thirty360US;
                RecomputeBond();
                SaveSoon();
            };
        }
        Ui.Check(_dayCountItems, b.DayCount == DayCountConvention.Actual ? 0 : 1);
        left.Children.Add(Row("Days", "count", days, null));

        var solveYield = Ui.Button("Solve", "Solve for the yield", "Work out the yield from the price", "primary");
        var solvePrice = Ui.Button("Solve", "Solve for the price", "Work out the price from the yield", "primary");
        foreach (var s in new[] { solveYield, solvePrice })
        {
            s.MinHeight = 44;
            s.HorizontalAlignment = HorizontalAlignment.Stretch;
        }
        solveYield.Click += (_, _) => SolveBond(BondTarget.Yield);
        solvePrice.Click += (_, _) => SolveBond(BondTarget.Price);
        left.Children.Add(Row("Yield", "% to maturity", _yieldBox, solveYield));
        left.Children.Add(Row("Price", "clean, per 100", _priceBox, solvePrice));
        _bondErrors = Ui.Text("", "error");
        _bondErrors.IsVisible = false;
        left.Children.Add(_bondErrors);
        left.Children.Add(Ui.Text("Leave the yield or the price blank to work it out. The clean price leaves out the interest earned since the last coupon; the buyer pays that on top.", "hint"));

        _bondLabel = Ui.Pixel("Solved", 2);
        _bondValue = Ui.Text("?", "big");
        _bondText = Ui.Text("", "body");
        AutomationProperties.SetLiveSetting(_bondValue, AutomationLiveSetting.Polite);
        _bondBox = new Border { Child = new StackPanel { Spacing = 6, Children = { _bondLabel, _bondValue, _bondText } } };
        _bondBox.Classes.Add("solved");
        var (accTile, acc, _) = Ui.Tile("Accrued interest");
        var (dirtyTile, dirty, _) = Ui.Tile("Price with accrued");
        (_accrued, _dirty) = (acc, dirty);
        var tiles = new Grid { ColumnDefinitions = new ColumnDefinitions("*,10,*") };
        Grid.SetColumn(dirtyTile, 2);
        tiles.Children.Add(accTile);
        tiles.Children.Add(dirtyTile);
        var right = new StackPanel { Spacing = 12, Children = { _bondBox, tiles } };

        FlowColumns.SetMinColumnWidth(left, 380);
        FlowColumns.SetMinColumnWidth(right, 280);
        left.VerticalAlignment = VerticalAlignment.Top;
        right.VerticalAlignment = VerticalAlignment.Top;
        return Page(new FlowColumns { Gap = 24, Children = { left, right } });
    }

    private void OnBondEdited(TextBox box)
    {
        if (_loading || _bondUpdating || _priceBox is null) return;
        var b = _state.Bonds;
        if ((box == _priceBox && _state.BondSolved == BondTarget.Price) || (box == _yieldBox && _state.BondSolved == BondTarget.Yield))
            _state.BondSolved = null;
        (b.Settlement, b.Maturity, b.Coupon, b.Redemption, b.Yield, b.Price) =
            (_settleBox.Text ?? "", _maturityBox.Text ?? "", _couponBox.Text ?? "", _redemptionBox.Text ?? "", _yieldBox.Text ?? "", _priceBox.Text ?? "");
        RecomputeBond();
        SaveSoon();
    }

    private void RecomputeBond()
    {
        if (_loading || _bondBox is null) return;
        var a = BondWorksheet.Solve(_state.Bonds, _reader, null, _state.BondSolved);
        BondAnswer = a.Value;
        var e = a.FieldErrors;
        Ui.SetError(_settleBox, e.GetValueOrDefault("settlement"), "When you buy the bond");
        Ui.SetError(_maturityBox, e.GetValueOrDefault("maturity"), "When the bond is repaid");
        Ui.SetError(_couponBox, e.GetValueOrDefault("coupon"), "The yearly coupon in percent");
        Ui.SetError(_redemptionBox, e.GetValueOrDefault("redemption"), "Repaid per 100 of face value");
        Ui.SetError(_yieldBox, e.GetValueOrDefault("yield"), "The yield to maturity in percent");
        Ui.SetError(_priceBox, e.GetValueOrDefault("price"), "The clean price per 100");
        Ui.Show(_bondErrors, e.Count > 0 ? string.Join(" ", e.Values) : null);

        var target = BondWorksheet.ChooseTarget(_state.Bonds, null, _state.BondSolved, out _);
        _yieldBox.Classes.Set("solved", target == BondTarget.Yield);
        _priceBox.Classes.Set("solved", target == BondTarget.Price);
        _yieldBox.PlaceholderText = target == BondTarget.Yield && a.Value is { } y ? Money.Number(y.Yield, 4) : "";
        _priceBox.PlaceholderText = target == BondTarget.Price && a.Value is { } p ? BondWorksheet.PriceText(p.CleanPrice) : "";

        _bondBox.Classes.Set("problem", a.Value is null);
        if (a.Value is { } r)
        {
            // Keep a solved field up to date while the other fields change.
            if (_state.BondSolved == r.Solved)
            {
                var box = r.Solved == BondTarget.Price ? _priceBox : _yieldBox;
                var text = Money.Field(r.Solved == BondTarget.Price ? r.CleanPrice : r.Yield);
                if (!box.IsFocused && !string.IsNullOrWhiteSpace(box.Text) && box.Text != text)
                {
                    _bondUpdating = true;
                    try { Ui.SetText(box, text); }
                    finally { _bondUpdating = false; }
                    if (r.Solved == BondTarget.Price) _state.Bonds.Price = text;
                    else _state.Bonds.Yield = text;
                }
            }
            _bondLabel.Text = r.Solved == BondTarget.Price ? "SOLVED: PRICE (PER 100)" : "SOLVED: YIELD (PER YEAR)";
            _bondValue.Text = r.Solved == BondTarget.Price ? BondWorksheet.PriceText(r.CleanPrice) : Money.Percent(r.Yield, 4);
            string left = Periods.Count(r.CouponsRemaining, "coupon");
            _bondText.Text = r.Solved == BondTarget.Price
                ? $"At a yield of {Money.Percent(r.Yield, 4)}. {left} left; the next is paid on {DateCalculator.Show(r.NextCoupon)}."
                : $"At a price of {BondWorksheet.PriceText(r.CleanPrice)}. {left} left; the next is paid on {DateCalculator.Show(r.NextCoupon)}.";
            _accrued.Text = BondWorksheet.PriceText(r.Accrued);
            _dirty.Text = BondWorksheet.PriceText(r.DirtyPrice);
        }
        else
        {
            _bondLabel.Text = "NOT SOLVED YET";
            _bondValue.Text = "?";
            _bondText.Text = a.Problem ?? "";
            _accrued.Text = _dirty.Text = "?";
        }
    }

    private void SolveBond(BondTarget? requested)
    {
        var a = BondWorksheet.Solve(_state.Bonds, _reader, requested, _state.BondSolved);
        if (a.Value is not { } r)
        {
            RecomputeBond();
            _services.ShowStatus(a.Problem ?? "Fix the bond fields first");
            return;
        }
        _state.BondSolved = r.Solved;
        var box = r.Solved == BondTarget.Price ? _priceBox : _yieldBox;
        var text = Money.Field(r.Solved == BondTarget.Price ? r.CleanPrice : r.Yield);
        _bondUpdating = true;
        try { Ui.SetText(box, text); }
        finally { _bondUpdating = false; }
        if (r.Solved == BondTarget.Price) _state.Bonds.Price = text;
        else _state.Bonds.Yield = text;
        RecomputeBond();
        AddCard(FinanceCards.Bond(r));
    }
}
