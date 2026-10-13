using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Calcy.UI.Controls;
using Calcy.UI.Services;

namespace Calcy.App;

/// <summary>Help: a searchable catalog of every function with its signature, description and an example to try.</summary>
internal sealed class HelpView : UserControl
{
    private readonly AppServices _s;
    private readonly StackPanel _list = new() { Spacing = 10 };
    private readonly TextBox _search = new() { PlaceholderText = "Search functions (sin, solve, mean, ...)", MinWidth = 420 };

    public HelpView(AppServices services, Action back, string? fromMode = null)
    {
        _s = services;
        _search.TextChanged += (_, _) => Fill();
        var backButton = new PixelButton { Content = "BACK", Classes = { "primary" } };
        backButton.Click += (_, _) => back();
        var report = new PixelButton { Content = "REPORT A BUG", Classes = { "sel" } };
        ToolTip.SetTip(report, "Opens a bug report form on GitHub, filled in with the version, your system and the mode. You review it before anything is sent.");
        report.Click += async (_, _) =>
        {
            bool opened = await BugReport.OpenAsync(this, fromMode);
            _s.ShowStatus(opened ? "Opened the bug report form in your browser" : "Could not open a browser; the form's link is on the clipboard");
        };
        var details = new PixelButton { Content = "COPY DETAILS" };
        ToolTip.SetTip(details, "Copies the version, system and mode, for a report sent another way.");
        details.Click += async (_, _) =>
        {
            await BugReport.CopyDetailsAsync(this, fromMode);
            _s.ShowStatus("Copied the version and system details");
        };
        var header = new WrapPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(16, 16, 4, 8), Children = { _search, backButton, report, details } };
        foreach (var child in header.Children) child.Margin = new Thickness(0, 0, 12, 8);
        var body = new DockPanel();
        DockPanel.SetDock(header, Dock.Top);
        body.Children.Add(header);
        body.Children.Add(new ScrollViewer { Content = _list, Padding = new Thickness(16, 0, 16, 16) });
        Content = new PixelPanel { Header = "Function catalog", Margin = new Thickness(24), Content = body };
        Fill();
    }

    private void Fill()
    {
        _list.Children.Clear();
        var q = (_search.Text ?? "").Trim();
        var groups = _s.Session.Functions.All
            .Where(f => !f.Hidden)
            .Where(f => q.Length == 0 || f.Name.Contains(q, StringComparison.OrdinalIgnoreCase) || f.Aliases.Any(a => a.Contains(q, StringComparison.OrdinalIgnoreCase))
                        || f.Description.Contains(q, StringComparison.OrdinalIgnoreCase) || f.Category.Contains(q, StringComparison.OrdinalIgnoreCase))
            .GroupBy(f => f.Category)
            .OrderBy(g => g.Key);
        foreach (var g in groups)
        {
            _list.Children.Add(new TextBlock { Text = g.Key.ToUpperInvariant(), Classes = { "label" }, Margin = new Thickness(0, 8, 0, 0) });
            foreach (var f in g.OrderBy(f => f.Name))
            {
                var tryIt = new PixelButton { Content = "TRY", Classes = { "small", "sel" }, IsVisible = f.Example.Length > 0 };
                var example = f.Example;
                tryIt.Click += (_, _) => _s.Navigate("advanced", new InputRequest(example, Evaluate: true));
                var texts = new StackPanel
                {
                    Spacing = 2,
                    Children =
                    {
                        new TextBlock { Text = string.IsNullOrEmpty(f.Signature) ? f.Name : f.Signature, FontSize = 16, Classes = { "math" }, FontWeight = Avalonia.Media.FontWeight.Bold },
                        new TextBlock { Text = f.Description, Classes = { "body" } },
                        new TextBlock { Text = f.Aliases.Length > 0 ? "also: " + string.Join(", ", f.Aliases) : "", Classes = { "muted" }, FontSize = 12, IsVisible = f.Aliases.Length > 0 },
                    },
                };
                var row = new DockPanel();
                DockPanel.SetDock(tryIt, Dock.Right);
                row.Children.Add(tryIt);
                row.Children.Add(texts);
                _list.Children.Add(new Card { Title = f.Name, Meta = example.Length > 0 ? "e.g. " + example : "", Content = row });
            }
        }
    }
}
