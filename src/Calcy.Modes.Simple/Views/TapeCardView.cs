using System.Globalization;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Calcy.UI.Controls;

namespace Calcy.Modes.Simple;

/// <summary>
/// One tape line as a result card: number and time in the header, the calculation and its result,
/// and Use and Edit buttons. Edit swaps the body for a small text box with the line's numbers.
/// </summary>
internal sealed class TapeCardView
{
    private readonly Func<TapeLine, string, string?> _commit;
    private readonly Action<TapeCardView, bool> _editingChanged;
    private readonly TextBlock _expression;
    private readonly TextBlock _result;
    private readonly StackPanel _body;
    private readonly StackPanel _actions;
    private readonly PixelButton _use;
    private readonly PixelButton _edit;
    private readonly TextBox _editor;
    private readonly TextBlock _editHint;
    private readonly StackPanel _editBody;
    private readonly StackPanel _editActions;

    /// <param name="line">The tape line shown.</param>
    /// <param name="use">Called by the Use button.</param>
    /// <param name="commit">Applies an edit; returns an error message, or null when the edit was accepted.</param>
    /// <param name="editingChanged">Called when editing starts (true) or ends (false).</param>
    public TapeCardView(TapeLine line, Action<TapeLine> use, Func<TapeLine, string, string?> commit, Action<TapeCardView, bool> editingChanged)
    {
        Line = line;
        _commit = commit;
        _editingChanged = editingChanged;

        _expression = Ui.Text("", 15, "Calcy.Font.Math", "Calcy.Muted");
        _expression.TextWrapping = TextWrapping.Wrap;
        _result = Ui.Text("", 24, "Calcy.Font.Math");
        _result.FontWeight = FontWeight.Bold;
        _result.TextWrapping = TextWrapping.Wrap;
        _body = new StackPanel { Spacing = 2, VerticalAlignment = VerticalAlignment.Center, Children = { _expression, _result } };

        string n = line.Number.ToString(CultureInfo.InvariantCulture);
        _use = Ui.SmallButton("Use", $"use result of line {n}", "Put this result on the display", "sel");
        _use.Click += (_, _) => use(Line);
        _edit = Ui.SmallButton("Edit", $"edit line {n}", "Change this line's numbers; the lines below it recalculate");
        _edit.Click += (_, _) => BeginEdit();
        _actions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, Children = { _use, _edit } };

        _editor = new TextBox { FontSize = 15, Padding = new Thickness(8, 4), HorizontalAlignment = HorizontalAlignment.Stretch };
        AutomationProperties.SetName(_editor, $"numbers of line {n}");
        _editor.KeyDown += OnEditorKeyDown;
        _editHint = Ui.Text("", 12, "Calcy.Font.Body", "Calcy.Muted");
        _editHint.TextWrapping = TextWrapping.Wrap;
        _editBody = new StackPanel { Spacing = 6, Margin = new Thickness(0, 0, 0, 4) };
        var save = Ui.SmallButton("Save", $"save line {n}", "Recalculate with the new numbers (Enter)", "ok");
        save.Click += (_, _) => Commit();
        var cancel = Ui.SmallButton("Cancel", $"cancel editing line {n}", "Keep the line as it was (Esc)", "paper");
        cancel.Click += (_, _) => EndEdit();
        _editActions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, HorizontalAlignment = HorizontalAlignment.Right, Children = { save, cancel } };
        // The box gets the full card width and the buttons go underneath, so a narrow tape never squeezes or clips the box.
        _editBody.Children.Add(_editor);
        _editBody.Children.Add(_editHint);
        _editBody.Children.Add(_editActions);

        Card = new Card { Content = _body, Actions = _actions, Padding = new Thickness(12, 8), ClipToBounds = false, Tag = this };
        AutomationProperties.SetName(Card, $"tape line {n}");
        Refresh();
    }

    public TapeLine Line { get; }

    public Card Card { get; }

    public bool IsEditing { get; private set; }

    /// <summary>The edit box (for tests).</summary>
    public TextBox Editor => _editor;

    /// <summary>The Use button (for tests).</summary>
    public PixelButton UseButton => _use;

    /// <summary>The Edit button (for tests).</summary>
    public PixelButton EditButton => _edit;

    /// <summary>Shows the line's current calculation and result.</summary>
    public void Refresh()
    {
        string n = Line.Number.ToString(CultureInfo.InvariantCulture);
        Card.Title = Line.IsEdited ? $"#{n} · EDITED" : $"#{n}";
        Card.Meta = Line.Time.ToString("HH:mm", CultureInfo.InvariantCulture);
        Card.IsError = Line.IsError;
        _expression.Text = Line.Expression;
        _result.Text = "= " + Line.ResultText;
        _use.IsEnabled = !Line.IsError;
        var sources = Line.Sources.Select(s => "#" + s.Number.ToString(CultureInfo.InvariantCulture)).ToList();
        ToolTip.SetTip(_expression, sources.Count switch
        {
            0 => null,
            1 => $"Uses the result of {sources[0]}",
            _ => $"Uses the results of {string.Join(", ", sources[..^1])} and {sources[^1]}",
        });
        AutomationProperties.SetHelpText(Card, $"{Line.Expression} equals {Line.ResultText}");
    }

    /// <summary>Switches the card to editing: a text box with the numbers, Save and Cancel.</summary>
    public void BeginEdit()
    {
        if (IsEditing) return;
        IsEditing = true;
        _editor.Text = Line.EditText;
        _editHint.Text = "Change a number, then press Enter. #2 means the result of line #2.";
        _editHint.Token(TextBlock.ForegroundProperty, "Calcy.Muted");
        Card.Content = _editBody;
        Card.Actions = null;
        _editingChanged(this, true);
        _editor.Focus();
        _editor.CaretIndex = _editor.Text?.Length ?? 0;
    }

    /// <summary>Applies the edit. Stays in edit mode and shows the problem when the edit is rejected.</summary>
    public bool Commit()
    {
        if (!IsEditing) return false;
        var error = _commit(Line, _editor.Text ?? "");
        if (error is not null)
        {
            _editHint.Text = error;
            _editHint.Token(TextBlock.ForegroundProperty, "Calcy.Err");
            return false;
        }
        EndEdit();
        return true;
    }

    /// <summary>Leaves edit mode without changing the line.</summary>
    public void EndEdit()
    {
        if (!IsEditing) return;
        IsEditing = false;
        Card.Content = _body;
        Card.Actions = _actions;
        Refresh();
        _editingChanged(this, false);
    }

    private void OnEditorKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key is Key.Enter or Key.Return)
        {
            Commit();
            e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            EndEdit();
            e.Handled = true;
        }
    }
}
