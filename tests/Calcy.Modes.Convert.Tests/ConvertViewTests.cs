using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using Avalonia.VisualTree;
using Calcy.UI.Controls;
using Calcy.UI.Services;
using Calcy.UI.TestKit;

using Calcy.Units;

namespace Calcy.Modes.Convert.Tests;

/// <summary>The Convert screen driven headlessly: typing, clicking, keyboard, cards, and screenshots in five schemes.</summary>
public class ConvertViewTests
{
    public static TheoryData<string> Schemes => new() { "nes", "pico", "gameboy", "synth", "paper" };

    private static T Named<T>(Control root, string name) where T : Control =>
        root.GetLogicalDescendants().OfType<T>().FirstOrDefault(c => c.Name == name)
        ?? root.GetVisualDescendants().OfType<T>().First(c => c.Name == name);

    private static void Type(Window w, TextBox box, string text)
    {
        box.Focus();
        box.SelectAll();
        w.KeyTextInput(text);
        Headless.Pump();
    }

    private static void Press(Window w, Key key)
    {
        w.KeyPress(key, RawInputModifiers.None, PhysicalKey.None, null);
        w.KeyRelease(key, RawInputModifiers.None, PhysicalKey.None, null);
        Headless.Pump();
    }

    private static void Click(Button b)
    {
        b.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Headless.Pump();
    }

    private static (AppServices Services, ConvertView View, Window Window) Open(string scheme = "nes")
    {
        var services = Headless.CreateServices(scheme);
        var view = (ConvertView)new ConvertMode().CreateView(services);
        var window = Headless.Show(view);
        return (services, view, window);
    }

    private static string Result(Control view) => Named<TextBlock>(view, "Result").Text ?? "";

    private static string Formula(Control view) => Named<TextBlock>(view, "Formula").Text ?? "";

    [Fact]
    public void ModeDescribesItself()
    {
        var mode = new ConvertMode();
        Assert.Equal(("convert", "Convert", "Units and dates", "Ctrl+9", "Key", "convert"),
            (mode.Id, mode.Title, mode.Description, mode.Shortcut, mode.AccentToken, mode.Icon));
    }

    [Fact]
    public void ConvertsLiveWhileTyping() => Headless.Run(() =>
    {
        var (_, view, w) = Open();
        Assert.Equal("length", view.CurrentCategoryId);
        Assert.Equal("3.106855961 mi", Result(view));
        Assert.Equal("1 km = 0.6213711922 mi", Formula(view));

        var value = Named<TextBox>(view, "FromValue");
        Type(w, value, "12");
        Assert.Equal("7.456454307 mi", Result(view));

        Type(w, value, "3 ft 4 in to cm");
        Assert.Equal("101.6 cm", Result(view));

        Type(w, value, "100 °C in °F");
        Assert.Equal("temperature", view.CurrentCategoryId);
        Assert.Equal("212 °F", Result(view));
        Assert.Equal("°F = °C × 9/5 + 32", Formula(view));

        Type(w, value, "60 mph to m/s");
        Assert.Equal("speed", view.CurrentCategoryId);
        Assert.Equal("26.8224 m/s", Result(view));

        Type(w, value, "170 cm to ft in");
        Assert.Equal("5 ft 6.929133858 in", Result(view));

        Type(w, value, "2^10");
        Assert.Equal("33.59580052 ft", Result(view));

        Type(w, value, "5 km to kg");
        Assert.Equal("?", Result(view));
        Assert.Contains("cannot be converted", Formula(view));
        Assert.Contains("error", value.Classes);
        w.Close();
    });

    [Fact]
    public void SwapGridAndEnter() => Headless.Run(() =>
    {
        var (services, view, w) = Open();
        var value = Named<TextBox>(view, "FromValue");
        Type(w, value, "5");
        Click(Named<PixelButton>(view, "Swap"));
        Assert.Equal("8.04672 km", Result(view));
        Assert.Equal("1 mi = 1.609344 km", Formula(view));

        // Click the "yd" cell of the every-unit grid.
        var grid = Named<Avalonia.Controls.Primitives.UniformGrid>(view, "EveryUnit");
        Assert.Equal(15, grid.Children.OfType<Button>().Count());
        var yd = grid.Children.OfType<Button>().First(b => b.GetLogicalDescendants().OfType<TextBlock>().Any(t => t.Text == "yard"));
        Click(yd);
        Assert.Equal("8800 yd", Result(view));
        Assert.Equal("yd  yard", Named<ComboBox>(view, "ToUnit").SelectedItem);
        Assert.Contains("sel", yd.Classes);

        // Enter saves a card; the card list and the saved preferences both have it.
        value.Focus();
        Press(w, Key.Enter);
        Assert.Single(view.Cards);
        Assert.Equal("5 mi = 8800 yd", view.Cards[0].Text);
        Assert.Equal("Length", view.Cards[0].Category);
        var saved = services.Prefs.GetModeState<ConvertState>("convert");
        Assert.NotNull(saved);
        Assert.Single(saved.Cards);

        // A new view on the same preferences restores the cards and the category.
        var again = (ConvertView)new ConvertMode().CreateView(services);
        Assert.Single(again.Cards);
        w.Close();
    });

    [Fact]
    public void PayloadFillsConvertsAndSaves() => Headless.Run(() =>
    {
        var (_, view, w) = Open();
        view.Receive(new InputRequest("5 km to mi"));
        Headless.Pump();
        Assert.Equal("5", Named<TextBox>(view, "FromValue").Text);
        Assert.Equal("3.106855961 mi", Result(view));
        Assert.Empty(view.Cards);

        view.Receive(new InputRequest("1 GiB to GB", Evaluate: true));
        Headless.Pump();
        Assert.Equal("data", view.CurrentCategoryId);
        Assert.Equal("1 GiB = 1.073741824 GB", view.Cards[0].Text);

        view.Receive(new InputRequest("2026-10-13 + 100 days", Evaluate: true));
        Headless.Pump();
        Assert.Equal(ConvertView.DatesId, view.CurrentCategoryId);
        Assert.Equal("2026-10-13 + 100 days = 2027-01-21 (Thursday)", view.Cards[0].Text);

        // Using a card again puts its conversion back on screen.
        var use = view.GetVisualDescendants().OfType<PixelButton>().Last(b => (b.Content as string) == "USE");
        Click(use);
        Assert.Equal("data", view.CurrentCategoryId);
        Assert.Equal("1.073741824 GB", Result(view));
        w.Close();
    });

    [Fact]
    public void SearchPicksAUnit() => Headless.Run(() =>
    {
        var (_, view, w) = Open();
        var search = Named<TextBox>(view, "UnitSearch");
        Type(w, search, "psi");
        Press(w, Key.Enter);
        Assert.Equal("pressure", view.CurrentCategoryId);
        Assert.StartsWith("psi", (string?)Named<ComboBox>(view, "FromUnit").SelectedItem);
        Assert.Equal("", search.Text);

        // A unit of the same category becomes the target.
        Type(w, search, "kilopascal");
        Press(w, Key.Enter);
        Assert.Equal("34.47378647 kPa", Result(view));
        w.Close();
    });

    [Fact]
    public void ElectricKeepsDimensionsApart() => Headless.Run(() =>
    {
        var (_, view, w) = Open();
        view.ShowCategory("electric");
        Headless.Pump();
        Assert.Equal("18 C", Result(view));
        var to = Named<ComboBox>(view, "ToUnit");
        Assert.All(to.Items.Cast<string>(), item => Assert.DoesNotContain("volt", item));
        var from = Named<ComboBox>(view, "FromUnit");
        from.SelectedIndex = from.Items.Cast<string>().ToList().IndexOf("kV  kilovolt");
        Headless.Pump();
        Assert.Contains(to.Items.Cast<string>(), item => item.StartsWith("V  volt"));
        Assert.DoesNotContain(to.Items.Cast<string>(), item => item.Contains("coulomb"));
        w.Close();
    });

    [Fact]
    public void DatesTools() => Headless.Run(() =>
    {
        var (_, view, w) = Open();
        view.ShowCategory(ConvertView.DatesId);
        Headless.Pump();
        Type(w, Named<TextBox>(view, "DateFrom"), "2026-10-13");
        Type(w, Named<TextBox>(view, "DateTo"), "2026-12-25");
        Assert.Equal("73", Named<TextBlock>(view, "Days").Text);
        Assert.Equal("10 w 3 d", Named<TextBlock>(view, "WeeksDays").Text);
        Assert.Equal("53", Named<TextBlock>(view, "Weekdays").Text);
        Assert.Equal("0 y 2 mo 12 d", Named<TextBlock>(view, "YearsMonthsDays").Text);
        Assert.Equal("2027-01-21 (Thursday)", Named<TextBlock>(view, "AddResult").Text);
        Assert.Equal("2:15:30", Named<TextBlock>(view, "DurationResult").Text);

        // Up moves the date by a day; reversed dates give negative spans.
        var to = Named<TextBox>(view, "DateTo");
        to.Focus();
        Press(w, Key.Up);
        Assert.Equal("2026-12-26", to.Text);
        Type(w, to, "2026-08-01");
        Assert.Equal("-73", Named<TextBlock>(view, "Days").Text);
        Assert.Equal("-10 w 3 d", Named<TextBlock>(view, "WeeksDays").Text);

        Type(w, Named<TextBox>(view, "AddAmount"), "1");
        Named<ComboBox>(view, "AddStep").SelectedIndex = 2;
        Headless.Pump();
        Assert.Equal("2026-11-13 (Friday)", Named<TextBlock>(view, "AddResult").Text);
        Press(w, Key.Enter);
        Assert.Equal("2026-10-13 + 1 month = 2026-11-13 (Friday)", view.Cards[0].Text);
        w.Close();
    });

    [Theory]
    [MemberData(nameof(Schemes))]
    public void LengthScreenshot(string scheme) => Headless.Run(() =>
    {
        var (_, view, w) = Open(scheme);
        SeedCards(view);
        view.ShowCategory("length");
        Type(w, Named<TextBox>(view, "FromValue"), "5");
        Assert.Equal("3.106855961 mi", Result(view));
        Assert.True(File.Exists(Headless.Save(w, $"convert-length-{scheme}.png")));
        w.Close();
    });

    [Theory]
    [MemberData(nameof(Schemes))]
    public void TemperatureScreenshot(string scheme) => Headless.Run(() =>
    {
        var (_, view, w) = Open(scheme);
        SeedCards(view);
        view.ShowCategory("temperature");
        Type(w, Named<TextBox>(view, "FromValue"), "100");
        Assert.Equal("212 °F", Result(view));
        Assert.True(File.Exists(Headless.Save(w, $"convert-temperature-{scheme}.png")));
        w.Close();
    });

    [Theory]
    [MemberData(nameof(Schemes))]
    public void DatesScreenshot(string scheme) => Headless.Run(() =>
    {
        var (_, view, w) = Open(scheme);
        SeedCards(view);
        view.Receive(new InputRequest("2026-10-13 to 2026-12-25", Evaluate: true));
        Headless.Pump();
        Assert.Equal("73", Named<TextBlock>(view, "Days").Text);
        Assert.True(File.Exists(Headless.Save(w, $"convert-dates-{scheme}.png")));
        w.Close();
    });

    [Fact]
    public void SearchAndFuelScreenshots() => Headless.Run(() =>
    {
        var (_, view, w) = Open("nes");
        view.Receive(new InputRequest("10 L/100km to mpg"));
        Type(w, Named<TextBox>(view, "UnitSearch"), "gal");
        Assert.Equal("fuel-economy", view.CurrentCategoryId);
        Assert.Equal("23.52145833 mpg", Result(view));
        Assert.True(File.Exists(Headless.Save(w, "convert-search-nes.png")));
        Type(w, Named<TextBox>(view, "FromValue"), "abc");
        Assert.True(File.Exists(Headless.Save(w, "convert-error-nes.png")));
        w.Close();
    });

    [Fact]
    public void NarrowWindowScreenshot() => Headless.Run(() =>
    {
        var services = Headless.CreateServices("nes");
        var view = (ConvertView)new ConvertMode().CreateView(services);
        var w = Headless.Show(view, 900, 600);
        SeedCards(view);
        view.ShowCategory("data");
        Headless.Pump();
        Assert.Equal("107.3741824 GB", Result(view));
        Assert.True(File.Exists(Headless.Save(w, "convert-narrow-nes.png")));
        w.Close();
    });

    private static void SeedCards(ConvertView view)
    {
        view.Receive(new InputRequest("100 km/h to mph", Evaluate: true));
        view.Receive(new InputRequest("1 GiB to GB", Evaluate: true));
        view.Receive(new InputRequest("100 °C to °F", Evaluate: true));
        Headless.Pump();
    }
}
