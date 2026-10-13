using Calcy.Graphing;

namespace Calcy.Modes.Notebook.Model;

/// <summary>Everything the Notebook screen saves (stored with the preferences under the mode id "notebook").</summary>
public sealed class NotebookStoreData
{
    /// <summary>Format version of the saved data.</summary>
    public int Version { get; set; } = 1;

    /// <summary>Id of the notebook that was open.</summary>
    public string? Current { get; set; }

    /// <summary>The notebooks in list order.</summary>
    public List<NotebookData> Notebooks { get; set; } = [];
}

/// <summary>A saved notebook.</summary>
public sealed class NotebookData
{
    /// <summary>Stable id.</summary>
    public string Id { get; set; } = "";

    /// <summary>The name shown in the notebook list.</summary>
    public string Name { get; set; } = "";

    /// <summary>When it was last changed (UTC).</summary>
    public DateTime Modified { get; set; }

    /// <summary>The cells in order.</summary>
    public List<CellData> Cells { get; set; } = [];
}

/// <summary>A saved cell. Only the fields of its kind are set.</summary>
public sealed class CellData
{
    /// <summary>Stable id.</summary>
    public string Id { get; set; } = "";

    /// <summary>The kind of cell.</summary>
    public CellKind Kind { get; set; }

    /// <summary>Optional caption.</summary>
    public string? Caption { get; set; }

    /// <summary>True when the output is folded away.</summary>
    public bool Collapsed { get; set; }

    /// <summary>Text cells: the Markdown source.</summary>
    public string? Text { get; set; }

    /// <summary>Maths cells: the lines.</summary>
    public List<MathLineData>? Lines { get; set; }

    /// <summary>Table cells: the named data rows.</summary>
    public List<TableRowData>? Rows { get; set; }

    /// <summary>Table cells: the number of value columns.</summary>
    public int? Columns { get; set; }

    /// <summary>Table cells: summary lines under the table.</summary>
    public List<MathLineData>? Summary { get; set; }

    /// <summary>Graph cells: expressions, sliders and window.</summary>
    public GraphState? Graph { get; set; }

    /// <summary>Graph cells: height of the graph in pixels.</summary>
    public double? GraphHeight { get; set; }

    /// <summary>Code cells: the Python source.</summary>
    public string? Code { get; set; }

    /// <summary>Code cells: the console text of the last run, when it is short.</summary>
    public string? Output { get; set; }

    /// <summary>Code cells: true when the last run failed.</summary>
    public bool OutputFailed { get; set; }
}

/// <summary>A saved maths line.</summary>
public sealed class MathLineData
{
    /// <summary>The line as linear calculator input.</summary>
    public string Text { get; set; } = "";

    /// <summary>True when a slider is shown for a number definition.</summary>
    public bool Slider { get; set; }

    /// <summary>Slider minimum.</summary>
    public double? Min { get; set; }

    /// <summary>Slider maximum.</summary>
    public double? Max { get; set; }

    /// <summary>Slider step.</summary>
    public double? Step { get; set; }

    /// <summary>Show the decimal first (true), the exact form first (false) or follow the settings (null).</summary>
    public bool? DecimalFirst { get; set; }
}

/// <summary>A saved table row.</summary>
public sealed class TableRowData
{
    /// <summary>The variable name of the row.</summary>
    public string Name { get; set; } = "";

    /// <summary>The values as typed.</summary>
    public List<string> Values { get; set; } = [];
}
