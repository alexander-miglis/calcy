using System.Text;
using Calcy.Core;
using Calcy.Graphing;
using Calcy.Scripting;
using Calcy.Scripting.Drawing;

namespace Calcy.Modes.Notebook.Model;

/// <summary>What a piece of code cell output is.</summary>
public enum CodeOutputKind
{
    /// <summary>print() output.</summary>
    Text,

    /// <summary>sys.stderr output.</summary>
    ErrorText,

    /// <summary>A typeset calculator result (calc.show, or the value of the last expression).</summary>
    Math,

    /// <summary>A prompt of input() with its answer (or waiting for one).</summary>
    Input,

    /// <summary>A drawing from turtle, ti_draw, ti_plotlib, casioplot or calcydraw.</summary>
    Drawing,

    /// <summary>Graphs requested with calc.graph.add.</summary>
    Graph,

    /// <summary>The traceback of an error, or why the run stopped.</summary>
    Failure,
}

/// <summary>One piece of a code cell's output. Text items grow while the code runs.</summary>
public sealed class CodeOutputItem
{
    private readonly StringBuilder _text = new();
    private readonly object _gate;
    private readonly CodeOutput _owner;
    private TaskCompletionSource<string?>? _answer;

    internal CodeOutputItem(CodeOutput owner, object gate, CodeOutputKind kind, string text = "")
    {
        _owner = owner;
        _gate = gate;
        Kind = kind;
        _text.Append(text);
    }

    /// <summary>The kind of output.</summary>
    public CodeOutputKind Kind { get; }

    /// <summary>The text (console text, calculator text for maths, the traceback for failures).</summary>
    public string Text
    {
        get { lock (_gate) return _text.ToString(); }
    }

    /// <summary>Label shown before a typeset result.</summary>
    public string? Label { get; internal init; }

    /// <summary>The prompt of an input request.</summary>
    public string? Prompt { get; internal init; }

    /// <summary>The answer typed for an input request (null while waiting or after the run stopped).</summary>
    public string? Answer { get; private set; }

    /// <summary>True while an input request waits for an answer.</summary>
    public bool IsWaiting
    {
        get { lock (_gate) return _answer is { Task.IsCompleted: false }; }
    }

    /// <summary>The picture of a drawing.</summary>
    public CanvasFrame? Frame { get; internal init; }

    /// <summary>The requested graphs.</summary>
    public IReadOnlyList<GraphRequest> Graphs { get; internal init; } = [];

    /// <summary>The window set with calc.graph.window, or null to fit.</summary>
    public Viewport? Window { get; internal init; }

    /// <summary>A session for drawing the requested graphs (the notebook's functions, a copy of its variables).</summary>
    public CalcSession? GraphSession { get; internal init; }

    internal void Append(string text)
    {
        lock (_gate) _text.Append(text);
    }

    internal bool EndsWith(string suffix)
    {
        lock (_gate)
        {
            if (_text.Length < suffix.Length) return false;
            return _text.ToString(_text.Length - suffix.Length, suffix.Length) == suffix;
        }
    }

    internal void TrimEnd(int count)
    {
        lock (_gate) _text.Length = Math.Max(0, _text.Length - count);
    }

    internal int Length
    {
        get { lock (_gate) return _text.Length; }
    }

    internal Task<string?> BeginWaiting()
    {
        lock (_gate)
        {
            _answer = new TaskCompletionSource<string?>(TaskCreationOptions.RunContinuationsAsynchronously);
            return _answer.Task;
        }
    }

    /// <summary>Answers an input request (from the screen); null ends the input with EOF.</summary>
    public void Respond(string? answer)
    {
        TaskCompletionSource<string?>? tcs;
        lock (_gate)
        {
            tcs = _answer;
            if (tcs is null || tcs.Task.IsCompleted) return;
            Answer = answer;
        }
        tcs.TrySetResult(answer);
        _owner.Touch();
    }
}

/// <summary>The output of a code cell: a thread-safe, append-only list while the code runs (the interpreter adds, the screen reads).</summary>
public sealed class CodeOutput
{
    /// <summary>Console characters kept per run; the rest is dropped with a note.</summary>
    public const int MaxChars = 200_000;

    private readonly object _gate = new();
    private readonly List<CodeOutputItem> _items = [];
    private int _version;
    private int _chars;
    private bool _truncated;

    /// <summary>Increases with every change.</summary>
    public int Version
    {
        get { lock (_gate) return _version; }
    }

    /// <summary>The items so far.</summary>
    public IReadOnlyList<CodeOutputItem> Items
    {
        get { lock (_gate) return [.. _items]; }
    }

    /// <summary>True when there is nothing to show.</summary>
    public bool IsEmpty
    {
        get { lock (_gate) return _items.Count == 0; }
    }

    internal void Clear()
    {
        lock (_gate)
        {
            _items.Clear();
            _chars = 0;
            _truncated = false;
            _version++;
        }
    }

    internal void AppendText(CodeOutputKind kind, string text)
    {
        if (text.Length == 0) return;
        lock (_gate)
        {
            if (_truncated) return;
            if (_chars + text.Length > MaxChars)
            {
                text = text[..Math.Max(0, MaxChars - _chars)] + "\n[more output was dropped]\n";
                _truncated = true;
            }
            _chars += text.Length;
            if (_items.Count > 0 && _items[^1].Kind == kind && kind is CodeOutputKind.Text or CodeOutputKind.ErrorText) _items[^1].Append(text);
            else _items.Add(new CodeOutputItem(this, _gate, kind, text));
            _version++;
        }
    }

    internal CodeOutputItem Add(CodeOutputKind kind, string text = "", string? label = null, CanvasFrame? frame = null,
        IReadOnlyList<GraphRequest>? graphs = null, Viewport? window = null, CalcSession? graphSession = null, string? prompt = null)
    {
        lock (_gate)
        {
            var item = new CodeOutputItem(this, _gate, kind, text)
            {
                Label = label,
                Prompt = prompt,
                Frame = frame,
                Graphs = graphs ?? [],
                Window = window,
                GraphSession = graphSession,
            };
            _items.Add(item);
            _version++;
            return item;
        }
    }

    /// <summary>Removes text from the end of the last text item when it ends with it (a prompt that moves into an input row, an echo).</summary>
    internal bool RemoveTrailing(string suffix)
    {
        if (suffix.Length == 0) return false;
        lock (_gate)
        {
            if (_items.Count == 0) return false;
            var last = _items[^1];
            if (last.Kind is not (CodeOutputKind.Text or CodeOutputKind.ErrorText) || !last.EndsWith(suffix)) return false;
            last.TrimEnd(suffix.Length);
            if (last.Length == 0) _items.RemoveAt(_items.Count - 1);
            _version++;
            return true;
        }
    }

    internal void Touch()
    {
        lock (_gate) _version++;
    }

    /// <summary>The output as plain text (for Markdown export and saving).</summary>
    public string PlainText()
    {
        var sb = new StringBuilder();
        foreach (var item in Items)
        {
            switch (item.Kind)
            {
                case CodeOutputKind.Text or CodeOutputKind.ErrorText or CodeOutputKind.Failure:
                    sb.Append(item.Text);
                    if (sb.Length > 0 && sb[^1] != '\n') sb.Append('\n');
                    break;
                case CodeOutputKind.Math:
                    sb.Append(item.Label is null ? "" : item.Label + " ").Append(item.Text).Append('\n');
                    break;
                case CodeOutputKind.Input:
                    sb.Append(item.Prompt).Append(item.Answer).Append('\n');
                    break;
                case CodeOutputKind.Drawing:
                    sb.Append("[drawing]\n");
                    break;
                case CodeOutputKind.Graph:
                    foreach (var g in item.Graphs) sb.Append("[graph] ").Append(g.Text).Append('\n');
                    break;
            }
        }
        return sb.ToString().TrimEnd('\n');
    }
}
