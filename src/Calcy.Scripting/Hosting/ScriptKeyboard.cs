namespace Calcy.Scripting;

/// <summary>
/// Keys pressed while a program runs, for get_key style input. The screen feeds key presses and releases from any thread;
/// programs read them from the interpreter thread. Key names are lower case: letters, digits and symbols as typed,
/// plus "up", "down", "left", "right", "enter", "space", "tab", "del", "esc", "home", "end", "pageup" and "pagedown".
/// </summary>
public sealed class ScriptKeyboard
{
    private const int MaxQueued = 64;

    private readonly object _gate = new();
    private readonly Queue<string> _queue = new();
    private readonly HashSet<string> _down = new(StringComparer.Ordinal);
    private readonly SemaphoreSlim _signal = new(0);

    /// <summary>Records a key press: the key is queued for get_key and held down until <see cref="Release"/>.</summary>
    public void Press(string key)
    {
        var name = Normalize(key);
        if (name.Length == 0) return;
        lock (_gate)
        {
            if (_queue.Count >= MaxQueued) _queue.Dequeue();
            _queue.Enqueue(name);
            _down.Add(name);
        }
        _signal.Release();
    }

    /// <summary>Records a key release.</summary>
    public void Release(string key)
    {
        var name = Normalize(key);
        lock (_gate) _down.Remove(name);
    }

    /// <summary>Forgets queued and held keys (done before every run).</summary>
    public void Clear()
    {
        lock (_gate)
        {
            _queue.Clear();
            _down.Clear();
        }
        while (_signal.CurrentCount > 0 && _signal.Wait(0))
        {
        }
    }

    /// <summary>Takes the oldest queued key press, or returns an empty string when there is none.</summary>
    public string Read()
    {
        lock (_gate) return _queue.Count > 0 ? _queue.Dequeue() : "";
    }

    /// <summary>True while the key is held down.</summary>
    public bool IsDown(string key)
    {
        var name = Normalize(key);
        lock (_gate) return _down.Contains(name);
    }

    /// <summary>The keys held down now.</summary>
    public IReadOnlyList<string> Down
    {
        get { lock (_gate) return [.. _down.Order(StringComparer.Ordinal)]; }
    }

    /// <summary>Waits for a key press and returns it; returns an empty string when cancelled or when the timeout passes.</summary>
    public string Wait(CancellationToken cancellationToken, TimeSpan? timeout = null)
    {
        var until = timeout is { } t ? DateTime.UtcNow + t : DateTime.MaxValue;
        while (!cancellationToken.IsCancellationRequested)
        {
            var key = Read();
            if (key.Length > 0) return key;
            var left = until == DateTime.MaxValue ? TimeSpan.FromMilliseconds(250) : until - DateTime.UtcNow;
            if (left <= TimeSpan.Zero) return "";
            try
            {
                _signal.Wait(left < TimeSpan.FromMilliseconds(250) ? left : TimeSpan.FromMilliseconds(250), cancellationToken);
            }
            catch (OperationCanceledException)
            {
                return "";
            }
        }
        return "";
    }

    /// <summary>Turns a key name from any source ("Up", "ArrowUp", "Return", "Escape", "A") into the canonical name.</summary>
    public static string Normalize(string? key)
    {
        if (string.IsNullOrEmpty(key)) return "";
        if (key.Length == 1) return key == " " ? "space" : key.ToLowerInvariant();
        var k = key.Trim().ToLowerInvariant();
        return k switch
        {
            "arrowup" or "uparrow" => "up",
            "arrowdown" or "downarrow" => "down",
            "arrowleft" or "leftarrow" => "left",
            "arrowright" or "rightarrow" => "right",
            "return" or "enter" => "enter",
            "escape" or "esc" => "esc",
            "back" or "backspace" or "delete" or "del" or "clear" => "del",
            "spacebar" or "space" => "space",
            "prior" or "pageup" => "pageup",
            "next" or "pagedown" => "pagedown",
            _ when k.Length == 2 && k[0] == 'd' && char.IsDigit(k[1]) => k[1..],
            _ when k.StartsWith("numpad", StringComparison.Ordinal) && k.Length == 7 && char.IsDigit(k[6]) => k[6..],
            "add" => "+",
            "subtract" => "-",
            "multiply" => "*",
            "divide" => "/",
            "decimal" => ".",
            _ => k,
        };
    }
}
