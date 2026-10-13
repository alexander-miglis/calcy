using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Calcy.Modes.Notebook.Model;
using Calcy.UI.Controls;

namespace Calcy.Modes.Notebook.Views;

/// <summary>A text cell: rendered Markdown in the document flow; double-click (or EDIT, or Enter) to edit the source.</summary>
internal sealed class TextCellView : CellView
{
    private readonly TextCell _cell;
    private readonly MarkdownView _preview;
    private readonly TextBox _editor;
    private readonly StackPanel _editPanel;
    private readonly TextBlock _empty;
    private readonly PixelButton _toggle;
    private bool _editing;

    public TextCellView(NotebookView owner, TextCell cell) : base(owner, cell)
    {
        _cell = cell;
        _preview = new MarkdownView(owner.Session, owner.OpenLink);
        _empty = Ui.Body("Empty text cell: double-click to write. Markdown works: # heading, **bold**, - list, $maths$.", 15, "Calcy.Muted");
        _editor = new TextBox
        {
            AcceptsReturn = true,
            TextWrapping = TextWrapping.Wrap,
            FontSize = 16,
            MinHeight = 90,
            Padding = new Thickness(10, 8),
            VerticalContentAlignment = VerticalAlignment.Top,
            Text = cell.Text,
        };
        _editor.Res(TextBox.FontFamilyProperty, "Calcy.Font.Body");
        Avalonia.Automation.AutomationProperties.SetName(_editor, "Text cell source");
        _editor.TextChanged += (_, _) => _cell.Text = _editor.Text ?? "";
        _editor.AddHandler(KeyDownEvent, OnEditorKey, RoutingStrategies.Tunnel);
        _editor.LostFocus += (_, _) => Dispatcher.UIThread.Post(() =>
        {
            if (_editing && !_editor.IsKeyboardFocusWithin && !IsKeyboardFocusWithin) StopEditing(focusCell: false);
        }, DispatcherPriority.Background);
        var hint = Ui.Body("# heading  **bold**  *italic*  - list  > note  [link](https://...)  $maths$  $=live value$   Esc: done", 12, "Calcy.Muted");
        _editPanel = new StackPanel { Spacing = 4, Children = { _editor, hint } };

        _toggle = Ui.Button("EDIT", "Edit the text (double-click or Enter)");
        _toggle.Click += (_, _) =>
        {
            if (_editing) StopEditing();
            else StartEditing();
        };
        AddAction(_toggle);

        var host = new StackPanel();
        host.Children.Add(_preview);
        host.Children.Add(_empty);
        host.Children.Add(_editPanel);
        Body.Content = host;
        DoubleTapped += (_, e) =>
        {
            if (_editing) return;
            e.Handled = true;
            StartEditing();
        };
        cell.InputChanged += OnInput;
        ShowMode();
        Render();
    }

    protected override string KindTitle => "Text cell";

    protected override bool CanCollapse => false;

    /// <summary>True while the source is edited.</summary>
    public bool IsEditing => _editing;

    /// <summary>The rendered Markdown.</summary>
    public MarkdownView Preview => _preview;

    /// <summary>The source editor.</summary>
    public TextBox SourceEditor => _editor;

    private void OnInput(object? sender, EventArgs e)
    {
        if (!_editing && _editor.Text != _cell.Text) _editor.Text = _cell.Text;
        Render();
    }

    /// <summary>Shows the source editor.</summary>
    public void StartEditing()
    {
        if (_editing) return;
        _editing = true;
        _editor.Text = _cell.Text;
        ShowMode();
        Owner.Select(this, focus: false);
        Dispatcher.UIThread.Post(() =>
        {
            _editor.Focus();
            _editor.CaretIndex = (_editor.Text ?? "").Length;
        }, DispatcherPriority.Input);
    }

    /// <summary>Shows the rendered text again (and keeps the keyboard on the cell when asked).</summary>
    public void StopEditing(bool focusCell = true)
    {
        if (!_editing) return;
        _editing = false;
        _cell.Text = _editor.Text ?? "";
        ShowMode();
        Render();
        if (focusCell) Focus();
    }

    private void ShowMode()
    {
        _editPanel.IsVisible = _editing;
        _preview.IsVisible = !_editing;
        _empty.IsVisible = !_editing && string.IsNullOrWhiteSpace(_cell.Text);
        _toggle.Content = _editing ? "DONE" : "EDIT";
    }

    private void Render()
    {
        if (_editing) return;
        _preview.Render(_cell.Markdown, _cell.LiveValues);
        _empty.IsVisible = string.IsNullOrWhiteSpace(_cell.Text);
    }

    private void OnEditorKey(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            e.Handled = true;
            StopEditing();
        }
    }

    public override bool FocusInput(bool atEnd = false)
    {
        if (_editing)
        {
            _editor.Focus();
            return true;
        }
        Focus();
        return true;
    }

    public override bool Run()
    {
        StopEditing();
        return true;
    }

    public override void Refresh()
    {
        base.Refresh();
        Render();
    }

    public override void Detach()
    {
        base.Detach();
        _cell.InputChanged -= OnInput;
    }
}
