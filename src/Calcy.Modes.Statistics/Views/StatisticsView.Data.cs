using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Layout;
using Avalonia.Platform.Storage;
using Calcy.Modes.Statistics.Logic;
using Calcy.UI.Controls;
using Calcy.UI.Services;

namespace Calcy.Modes.Statistics.Views;

public sealed partial class StatisticsView
{
    private const string DefaultHint = "Edit a cell: every stat and plot updates.";

    private ListGrid _grid = null!;
    private TextBox _formula = null!;
    private TextBlock _dataHint = null!;
    private StackPanel _renameRow = null!;
    private TextBox _renameName = null!;
    private TextBox _renameCaption = null!;
    private TextBlock _renameError = null!;
    private string? _renaming;

    private PixelPanel BuildDataPanel()
    {
        _grid = new ListGrid();
        _grid.CellEdited += OnCellEdited;
        _grid.TablePasted += (c, r, text) => PasteTable(c, r, text);
        _grid.HeaderDoubleClicked += c => OpenRename(c);
        _grid.HeaderMenuRequested += OpenColumnMenu;
        _grid.ActiveCellChanged += (c, _) => OnActiveColumn(c);

        var paste = Ui.Small("PASTE", "Paste a table from a spreadsheet at the active cell", PasteFromClipboard, "paper");
        var import = Ui.Small("IMPORT CSV", "Read a CSV or TSV file into new lists", ImportCsv, "paper");
        var header = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, Children = { paste, import } };

        var toolbar = new WrapPanel
        {
            ItemSpacing = 6,
            LineSpacing = 6,
            Children =
            {
                Ui.Small("+ LIST", "Add a list", AddList, "card"),
                Ui.Small("SORT ↑", "Sort the rows by the active list, smallest first (pairs stay together)", () => Sort(false), "card"),
                Ui.Small("SORT ↓", "Sort the rows by the active list, largest first (pairs stay together)", () => Sort(true), "card"),
                Ui.Small("RENAME", "Rename the active list and give it a caption", () => OpenRename(_grid.Active.Column), "card"),
                Ui.Small("CLEAR", "Empty the active list", ClearActive, "paper"),
                Ui.Small("DELETE", "Delete the active list", DeleteActive, "paper"),
            },
        };

        _renameName = Ui.Field("", "List name", 70);
        _renameCaption = Ui.Field("", "List caption", 92);
        _renameCaption.PlaceholderText = "caption";
        _renameError = Ui.Body("", 12);
        _renameError.Token(TextBlock.ForegroundProperty, "Calcy.Err");
        _renameError.IsVisible = false;
        foreach (var box in new[] { _renameName, _renameCaption })
            box.KeyDown += (_, e) =>
            {
                if (e.Key == Key.Enter) { e.Handled = true; CommitRename(); }
                else if (e.Key == Key.Escape) { e.Handled = true; CloseRename(); }
            };
        _renameRow = new StackPanel
        {
            Spacing = 6,
            IsVisible = false,
            Children =
            {
                new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    Spacing = 6,
                    Children =
                    {
                        _renameName, _renameCaption,
                        Ui.Small("OK", "Rename the list", CommitRename, "ok"),
                        Ui.Small("×", "Cancel renaming", CloseRename, "paper"),
                    },
                },
                _renameError,
            },
        };

        _formula = new TextBox
        {
            PlaceholderText = "L3 = L2 / L1  (column formula)",
            FontSize = 14,
            MinHeight = 34,
            BorderThickness = new Thickness(0),
            Background = Avalonia.Media.Brushes.Transparent,
            Padding = new Thickness(8, 4),
            VerticalContentAlignment = VerticalAlignment.Center,
        };
        _formula.Token(TextBox.FontFamilyProperty, "Calcy.Font.Math");
        _formula.Classes.Add("formula");
        AutomationProperties.SetName(_formula, "Column formula");
        ToolTip.SetTip(_formula, "Type a formula for a whole list and press Enter: L3 = L1^2, L4 = seq(k, k, 1, 20) or L5 = L2 - mean(L2). It stays live.");
        _formula.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter) { e.Handled = true; ApplyFormula(); }
        };
        _formula.TextChanged += (_, _) => ScheduleSave();
        var formulaBox = new DashedBorder { Child = _formula };

        _dataHint = Ui.Body(DefaultHint, 13, muted: true);

        var footer = new StackPanel
        {
            Spacing = 8,
            Margin = new Thickness(12, 10, 12, 12),
            Children = { toolbar, _renameRow, formulaBox, _dataHint },
        };
        var content = new DockPanel();
        DockPanel.SetDock(footer, Dock.Bottom);
        content.Children.Add(footer);
        content.Children.Add(_grid);
        var panel = new PixelPanel { Header = "Data", HeaderContent = header, Content = content };
        AutomationProperties.SetName(panel, "Data");
        return panel;
    }

    /// <summary>Redraws the list editor from the table.</summary>
    private void RefreshGrid()
    {
        var cols = _table.Columns.Select(c =>
        {
            var values = c.Values(_session);
            string? tip = c.FormulaError is { } err ? $"{c.Name} = {c.Formula}: {err}" : c.Formula is { } f ? $"{c.Name} = {f} (live: recomputed when its lists change)" : null;
            return new GridColumn(c.Name, c.Header, c.Cells, [.. values.Select(v => v.Kind)], c.Formula is not null, tip, c.FormulaError is not null);
        }).ToList();
        _grid.Load(cols);
    }

    private void Hint(string text, bool error = false)
    {
        _dataHint.Text = text;
        if (error) _dataHint.Token(TextBlock.ForegroundProperty, "Calcy.Err");
        else _dataHint.Token(TextBlock.ForegroundProperty, "Calcy.Muted");
    }

    private void DataChanged(bool listsChanged = false)
    {
        RefreshGrid();
        if (listsChanged) RefreshListPickers();
        ScheduleAnalysis();
        ScheduleSave();
    }

    private void OnCellEdited(string column, int row, string text)
    {
        bool wasFormula = _table.Find(column)?.Formula is not null;
        if (!_table.SetCell(column, row, text)) return;
        if (wasFormula) Hint($"{column} is typed data now; its formula was removed");
        else if (_dataHint.Text != DefaultHint) Hint(DefaultHint);
        DataChanged();
    }

    private void OnActiveColumn(int index)
    {
        if (index < 0 || index >= _table.Columns.Count) return;
        var c = _table.Columns[index];
        if (c.Formula is { } f && !_formula.IsFocused) _formula.Text = $"{c.Name} = {f}";
        if (c.FormulaError is { } err) Hint($"{c.Name}: {err}", error: true);
    }

    private string ActiveList => _grid.ActiveName ?? _table.Columns.FirstOrDefault()?.Name ?? "L1";

    private void AddList()
    {
        if (_table.Columns.Count >= StatTable.MaxColumns)
        {
            _s.ShowStatus($"The editor holds at most {StatTable.MaxColumns} lists");
            return;
        }
        var c = _table.AddColumn();
        DataChanged(listsChanged: true);
        _grid.Activate(_table.IndexOf(c.Name), 0);
        _s.ShowStatus($"Added {c.Name}");
    }

    private void Sort(bool descending)
    {
        var name = ActiveList;
        if (_table.Find(name) is not { IsEmpty: false }) return;
        _table.SortRows(name, descending);
        DataChanged();
        _s.ShowStatus($"Sorted the rows by {name}, {(descending ? "largest" : "smallest")} first");
    }

    private void ClearActive()
    {
        var name = ActiveList;
        if (_table.Find(name) is null) return;
        _table.Clear(name);
        DataChanged();
        _s.ShowStatus($"Cleared {name}");
    }

    private void DeleteActive()
    {
        var name = ActiveList;
        if (_table.Columns.Count <= 1)
        {
            ClearActive();
            return;
        }
        int index = _table.IndexOf(name);
        _table.RemoveColumn(name);
        DataChanged(listsChanged: true);
        _grid.Activate(Math.Min(index, _table.Columns.Count - 1), _grid.Active.Row);
        _s.ShowStatus($"Deleted {name}");
    }

    private void OpenRename(int index)
    {
        if (index < 0 || index >= _table.Columns.Count) return;
        var c = _table.Columns[index];
        _renaming = c.Name;
        _renameName.Text = c.Name;
        _renameCaption.Text = c.Caption;
        _renameError.IsVisible = false;
        _renameRow.IsVisible = true;
        _renameName.Focus();
        _renameName.SelectAll();
    }

    private void CommitRename()
    {
        if (_renaming is null) return;
        var newName = (_renameName.Text ?? "").Trim();
        var error = _table.Rename(_renaming, newName, _renameCaption.Text ?? "");
        if (error is not null)
        {
            _renameError.Text = error;
            _renameError.IsVisible = true;
            return;
        }
        RenameInSettings(_renaming, newName);
        _s.ShowStatus(_renaming == newName ? $"Caption of {newName} set" : $"Renamed {_renaming} to {newName}");
        CloseRename();
        DataChanged(listsChanged: true);
    }

    /// <summary>Renames a list (for tests and keyboard users). Returns an error or null.</summary>
    public string? RenameList(string oldName, string newName, string? caption = null)
    {
        var error = _table.Rename(oldName, newName, caption);
        if (error is null)
        {
            RenameInSettings(oldName, newName);
            DataChanged(listsChanged: true);
        }
        return error;
    }

    private void RenameInSettings(string oldName, string newName)
    {
        if (oldName == newName) return;
        string R(string v) => v == oldName ? newName : v;
        string? Rn(string? v) => v == oldName ? newName : v;
        var s = _state;
        s.OneVar.List = R(s.OneVar.List);
        s.OneVar.Freq = Rn(s.OneVar.Freq);
        s.TwoVar.X = R(s.TwoVar.X);
        s.TwoVar.Y = R(s.TwoVar.Y);
        s.TwoVar.Freq = Rn(s.TwoVar.Freq);
        s.Regression.X = R(s.Regression.X);
        s.Regression.Y = R(s.Regression.Y);
        s.Regression.Freq = Rn(s.Regression.Freq);
        s.Plot.List = R(s.Plot.List);
        s.Plot.List2 = Rn(s.Plot.List2);
        s.Plot.Freq = Rn(s.Plot.Freq);
        foreach (var key in s.Test.Fields.Keys.ToList())
            s.Test.Fields[key] = string.Join(",", s.Test.Fields[key].Split(',').Select(v => v.Trim() == oldName ? newName : v));
    }

    private void CloseRename()
    {
        _renaming = null;
        _renameRow.IsVisible = false;
        _grid.FocusEditor();
    }

    private void OpenColumnMenu(int index, Control target)
    {
        if (index < 0 || index >= _table.Columns.Count) return;
        var c = _table.Columns[index];
        var menu = new MenuFlyout();
        void Item(string text, Action action)
        {
            var item = new MenuItem { Header = text };
            item.Click += (_, _) => action();
            menu.Items.Add(item);
        }
        Item("Rename…", () => OpenRename(index));
        Item("Sort rows, smallest first", () => Sort(false));
        Item("Sort rows, largest first", () => Sort(true));
        if (c.Formula is not null)
            Item("Keep the values, drop the formula", () => { _table.Unlink(c.Name); DataChanged(); });
        Item("Insert a row above the active cell", () => { _table.InsertRow(_grid.Active.Row); DataChanged(); });
        Item("Delete the row of the active cell", () => { _table.DeleteRow(_grid.Active.Row); DataChanged(); });
        Item("Clear " + c.Name, ClearActive);
        Item("Delete " + c.Name, DeleteActive);
        menu.ShowAt(target);
    }

    /// <summary>Applies the text of the formula box to a list (L3 = L2 / L1); without "name =" it goes to the active list.</summary>
    public void ApplyFormula()
    {
        var text = (_formula.Text ?? "").Trim();
        if (text.Length == 0) return;
        var before = _table.Columns.Count;
        var error = _table.ApplyFormula(text, ActiveList);
        string target = ColumnFormula.TryParse(text, out var t, out _) ? t : ActiveList;
        DataChanged(listsChanged: _table.Columns.Count != before);
        if (error is not null)
        {
            Hint(error, error: true);
            return;
        }
        int index = _table.IndexOf(target);
        if (index >= 0) _grid.Activate(index, 0, focus: false);
        var c = _table.Find(target)!;
        Hint($"{target} = {c.Formula}: recomputed whenever its lists change. Type in {target} to turn it back into data.");
        _s.ShowStatus($"{target} has {c.Length} values");
    }

    /// <summary>Pastes text (a spreadsheet block, CSV, a Calcy list) with its top-left cell at the active cell.</summary>
    public void PasteText(string text) => PasteTable(_grid.Active.Column, _grid.Active.Row, text);

    private void PasteTable(int column, int row, string text)
    {
        var table = TablePaste.Parse(text);
        if (table is null || table.ColumnCount == 0)
        {
            _s.ShowStatus("Nothing to paste");
            return;
        }
        int before = _table.Columns.Count;
        var filled = _table.Paste(table, column, row);
        DataChanged(listsChanged: true);
        _s.ShowStatus(filled.Count == 0 ? "There is no room for more lists" :
            $"Pasted {table.RowCount} row{(table.RowCount == 1 ? "" : "s")} into {string.Join(", ", filled)}" + (_table.Columns.Count > before ? " (new lists added)" : ""));
    }

    private async void PasteFromClipboard()
    {
        var clipboard = TopLevel.GetTopLevel(this)?.Clipboard;
        if (clipboard is null) return;
        string? text;
        try
        {
            text = await clipboard.TryGetTextAsync();
        }
        catch (Exception ex) when (ex is InvalidOperationException or TimeoutException or System.Runtime.InteropServices.COMException)
        {
            _s.ShowStatus("The clipboard could not be read");
            return;
        }
        if (string.IsNullOrWhiteSpace(text))
        {
            _s.ShowStatus("The clipboard has no text to paste");
            return;
        }
        PasteText(text);
    }

    private async void ImportCsv()
    {
        var storage = TopLevel.GetTopLevel(this)?.StorageProvider;
        if (storage is null || !storage.CanOpen) return;
        IReadOnlyList<IStorageFile> files;
        try
        {
            files = await storage.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = "Import a CSV file into new lists",
                AllowMultiple = false,
                FileTypeFilter = [new FilePickerFileType("CSV and TSV") { Patterns = ["*.csv", "*.tsv", "*.txt"] }, FilePickerFileTypes.All],
            });
        }
        catch (Exception ex) when (ex is InvalidOperationException or IOException or UnauthorizedAccessException)
        {
            _s.ShowStatus("The file dialog could not be opened");
            return;
        }
        if (files.Count == 0) return;
        try
        {
            await using var stream = await files[0].OpenReadAsync();
            if (stream.CanSeek && stream.Length > 20_000_000)
            {
                _s.ShowStatus("The file is larger than 20 MB");
                return;
            }
            using var reader = new StreamReader(stream);
            var text = await reader.ReadToEndAsync();
            int start = FirstFreeColumn();
            PasteTable(start, 0, text);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _s.ShowStatus("The file could not be read: " + ex.Message);
        }
    }

    /// <summary>The first empty list, or the index a new list would get.</summary>
    private int FirstFreeColumn()
    {
        for (int i = 0; i < _table.Columns.Count; i++)
            if (_table.Columns[i].IsEmpty && _table.Columns[i].Formula is null) return i;
        return _table.Columns.Count;
    }

    private void ImportData(DataRequest d)
    {
        var names = new List<string>();
        foreach (var (values, j) in d.Columns.Select((c, j) => (c, j)))
        {
            if (_table.Columns.Count >= StatTable.MaxColumns && FirstFreeColumn() >= _table.Columns.Count) break;
            int index = FirstFreeColumn();
            var column = index < _table.Columns.Count ? _table.Columns[index] : _table.AddColumn();
            _table.SetNumbers(column.Name, values);
            column.Caption = d.ColumnNames is { } cn && j < cn.Count ? cn[j] : d.Columns.Count == 1 ? d.Name : "";
            names.Add(column.Name);
        }
        if (names.Count == 0)
        {
            _s.ShowStatus("There is no room for more lists");
            return;
        }
        DataChanged(listsChanged: true);
        if (names.Count == 1)
        {
            _state.OneVar.List = names[0];
            ShowTab(StatTab.OneVar);
        }
        else
        {
            _state.Regression.X = names[0];
            _state.Regression.Y = names[1];
            _state.TwoVar.X = names[0];
            _state.TwoVar.Y = names[1];
            ShowTab(StatTab.Regression);
        }
        RefreshListPickers();
        _s.ShowStatus($"Added {d.Name} to {string.Join(", ", names)}");
    }

    private void SyncFromSession()
    {
        var changed = _table.PullFromSession();
        if (changed.Count == 0) return;
        DataChanged(listsChanged: true);
    }

    /// <summary>The number of rows in the list editor.</summary>
    public int Rows => _table.RowCount;

    /// <summary>Sets one cell as if it was typed (adding the list when it does not exist yet).</summary>
    public void EditCell(string list, int row, string text)
    {
        bool added = false;
        if (_table.Find(list) is null)
        {
            _table.AddColumn(list);
            added = true;
        }
        if (!_table.SetCell(list, row, text) && !added) return;
        DataChanged(listsChanged: added);
    }

    /// <summary>Sets every cell of a list (adding the list when it does not exist yet) and its caption.</summary>
    public void EditList(string list, IEnumerable<string> cells, string? caption = null)
    {
        bool added = _table.Find(list) is null;
        var column = _table.Find(list) ?? _table.AddColumn(list);
        _table.SetCells(list, cells);
        if (caption is not null) column.Caption = caption;
        DataChanged(listsChanged: added);
    }
}
