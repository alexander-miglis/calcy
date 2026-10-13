using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Threading;
using Calcy.Modes.Notebook.Model;
using Calcy.UI.Controls;

namespace Calcy.Modes.Notebook.Views;

/// <summary>A maths cell: lines of structured maths, each with its result; Enter moves on or adds a line.</summary>
internal sealed class MathCellView : CellView
{
    private readonly MathCell _cell;
    private readonly StackPanel _lines = new() { Spacing = 8 };
    private readonly PixelButton _addLine;
    private readonly List<MathLineView> _views = [];

    public MathCellView(NotebookView owner, MathCell cell) : base(owner, cell)
    {
        _cell = cell;
        _addLine = Ui.Button("+ LINE", "Add a line to this cell");
        _addLine.HorizontalAlignment = HorizontalAlignment.Left;
        _addLine.Click += (_, _) => FocusLine(_cell.AddLine());
        _addLine.IsVisible = false;
        var stack = new StackPanel { Spacing = 10, Margin = new Thickness(14, 12), Children = { _lines, _addLine } };
        Body.Content = stack;
        cell.InputChanged += OnInput;
        SyncLines();
        Refresh();
    }

    private void OnInput(object? sender, EventArgs e) => SyncLines();

    protected override string HeaderToken => "Calcy.Sel";

    protected override string KindTitle => "Maths cell";

    /// <summary>The line views in order.</summary>
    public IReadOnlyList<MathLineView> LineViews => _views;

    private void SyncLines()
    {
        if (_views.Select(v => v.Line).SequenceEqual(_cell.Lines)) return;
        foreach (var v in _views.Where(v => !_cell.Lines.Contains(v.Line)).ToList())
        {
            v.Detach();
            _views.Remove(v);
        }
        var map = _views.ToDictionary(v => v.Line);
        _views.Clear();
        foreach (var line in _cell.Lines)
        {
            if (!map.TryGetValue(line, out var view))
            {
                view = new MathLineView(line, Owner.Session, Owner.Settings) { ShowResults = !Cell.Collapsed, IsActive = IsSelected };
                view.Submitted += OnSubmitted;
                view.DeleteRequested += OnDeleteRequested;
                view.MoveRequested += OnMoveRequested;
            }
            _views.Add(view);
        }
        _lines.Children.Clear();
        foreach (var v in _views) _lines.Children.Add(v);
    }

    private void OnSubmitted(object? sender, EventArgs e)
    {
        if (sender is not MathLineView v) return;
        int i = _views.IndexOf(v);
        if (i < _views.Count - 1)
        {
            _views[i + 1].Editor.Focus();
            return;
        }
        if (string.IsNullOrWhiteSpace(v.Line.Text))
        {
            if (_views.Count > 1) _cell.RemoveLine(v.Line);
            Owner.FocusNext(this);
            return;
        }
        FocusLine(_cell.AddLine());
    }

    private void OnDeleteRequested(object? sender, EventArgs e)
    {
        if (sender is not MathLineView v || _views.Count <= 1) return;
        int i = _views.IndexOf(v);
        _cell.RemoveLine(v.Line);
        if (i > 0) FocusLine(_cell.Lines[i - 1], atEnd: true);
    }

    private void OnMoveRequested(object? sender, int dir)
    {
        if (sender is not MathLineView v) return;
        int i = _views.IndexOf(v) + dir;
        if (i >= 0 && i < _views.Count) _views[i].Editor.Focus();
        else if (dir < 0) Owner.FocusPrevious(this);
        else Owner.FocusNext(this);
    }

    private void FocusLine(MathLine line, bool atEnd = false)
    {
        SyncLines();
        Dispatcher.UIThread.Post(() =>
        {
            var v = _views.FirstOrDefault(x => x.Line == line);
            if (v is null) return;
            v.Editor.Focus();
            if (atEnd) v.Editor.EditorState.MoveEnd(false, true);
        }, DispatcherPriority.Input);
    }

    public override bool FocusInput(bool atEnd = false)
    {
        if (_views.Count == 0) return false;
        var v = atEnd ? _views[^1] : _views[0];
        v.Editor.Focus();
        return true;
    }

    public override void Refresh()
    {
        base.Refresh();
        foreach (var v in _views) v.Refresh();
    }

    public override void ReparseInputs()
    {
        foreach (var v in _views) v.Reparse();
    }

    protected override void OnSelectionChanged()
    {
        _addLine.IsVisible = IsSelected;
        foreach (var v in _views) v.IsActive = IsSelected;
    }

    protected override void OnCollapsedChanged()
    {
        foreach (var v in _views) v.ShowResults = !Cell.Collapsed;
    }

    public override void Detach()
    {
        base.Detach();
        _cell.InputChanged -= OnInput;
        foreach (var v in _views) v.Detach();
    }
}
