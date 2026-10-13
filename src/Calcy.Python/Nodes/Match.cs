using Calcy.Python.Runtime;

namespace Calcy.Python.Nodes;

internal sealed class MatchCase
{
    public Pat Pattern = null!;
    public Expr? Guard;
    public Block Body = null!;
}

internal sealed class MatchStmt : Stmt
{
    public readonly Expr Subject;
    public readonly MatchCase[] Cases;

    public MatchStmt(Expr subject, MatchCase[] cases)
    {
        Subject = subject;
        Cases = cases;
        HasYield = cases.Any(c => c.Body.HasYield);
    }

    private Block? Select(Frame f)
    {
        var v = Subject.Eval(f);
        foreach (var c in Cases)
        {
            if (!c.Pattern.Match(f, v)) continue;
            if (c.Guard != null && !Ops.Truthy(c.Guard.Eval(f))) continue;
            return c.Body;
        }
        return null;
    }

    public override Flow Exec(Frame f) => Select(f)?.Exec(f) ?? Flow.Normal;

    public override IEnumerable<PyObject> ExecGen(Frame f)
    {
        var b = Select(f);
        if (b == null)
        {
            f.GenFlow = Flow.Normal;
            return Array.Empty<PyObject>();
        }
        return Run(b, f);
    }
}

internal abstract class Pat
{
    public abstract bool Match(Frame f, PyObject v);
}

internal sealed class ValuePat : Pat
{
    private readonly Expr _value;
    public ValuePat(Expr value) { _value = value; }
    public override bool Match(Frame f, PyObject v) => Ops.EqBool(v, _value.Eval(f));
}

internal sealed class SingletonPat : Pat
{
    private readonly PyObject _value;
    public SingletonPat(PyObject value) { _value = value; }
    public override bool Match(Frame f, PyObject v) => ReferenceEquals(v, _value);
}

internal sealed class CapturePat : Pat
{
    private readonly Pat? _inner;
    private readonly Target? _target;

    public CapturePat(Pat? inner, Target? target)
    {
        _inner = inner;
        _target = target;
    }

    public override bool Match(Frame f, PyObject v)
    {
        if (_inner != null && !_inner.Match(f, v)) return false;
        _target?.Assign(f, v);
        return true;
    }
}

internal sealed class OrPat : Pat
{
    private readonly Pat[] _alts;
    public OrPat(Pat[] alts) { _alts = alts; }

    public override bool Match(Frame f, PyObject v)
    {
        foreach (var p in _alts)
            if (p.Match(f, v)) return true;
        return false;
    }
}

internal sealed class SeqPat : Pat
{
    private readonly Pat[] _items;
    private readonly int _star;

    public SeqPat(Pat[] items, int star)
    {
        _items = items;
        _star = star;
    }

    private static bool IsSequence(PyObject v) =>
        v is PyList or PyTuple or PyRange || v.Type.Name == "deque" && v.Type.Module == "collections";

    public override bool Match(Frame f, PyObject v)
    {
        if (!IsSequence(v)) return false;
        var items = Ops.ToArray(v);
        int n = _items.Length;
        if (_star < 0)
        {
            if (items.Length != n) return false;
            for (int i = 0; i < n; i++)
                if (!_items[i].Match(f, items[i])) return false;
            return true;
        }
        if (items.Length < n - 1) return false;
        for (int i = 0; i < _star; i++)
            if (!_items[i].Match(f, items[i])) return false;
        int after = n - _star - 1;
        int starLen = items.Length - (n - 1);
        for (int i = 0; i < after; i++)
            if (!_items[_star + 1 + i].Match(f, items[_star + starLen + i])) return false;
        var starItems = new PyObject[starLen];
        Array.Copy(items, _star, starItems, 0, starLen);
        return _items[_star].Match(f, PyList.Wrap(starItems));
    }
}

internal sealed class MappingPat : Pat
{
    private readonly Expr[] _keys;
    private readonly Pat[] _pats;
    private readonly Target? _rest;

    public MappingPat(Expr[] keys, Pat[] pats, Target? rest)
    {
        _keys = keys;
        _pats = pats;
        _rest = rest;
    }

    public override bool Match(Frame f, PyObject v)
    {
        if (v is not PyDict d) return false;
        var used = new List<PyObject>();
        for (int i = 0; i < _keys.Length; i++)
        {
            var k = _keys[i].Eval(f);
            var val = d.GetOrNull(k);
            if (val == null) return false;
            if (!_pats[i].Match(f, val)) return false;
            used.Add(k);
        }
        if (_rest != null)
        {
            var rest = new PyDict();
            foreach (var (k, val) in d.Pairs())
                if (!used.Any(u => Ops.EqBool(u, k))) rest.SetItem(k, val);
            _rest.Assign(f, rest);
        }
        return true;
    }
}

internal sealed class ClassPat : Pat
{
    private readonly Expr _cls;
    private readonly Pat[] _pos;
    private readonly string[] _kwNames;
    private readonly Pat[] _kwPats;

    public ClassPat(Expr cls, Pat[] pos, string[] kwNames, Pat[] kwPats)
    {
        _cls = cls;
        _pos = pos;
        _kwNames = kwNames;
        _kwPats = kwPats;
    }

    private static bool SelfMatching(PyType t) =>
        t == PyTypes.Int || t == PyTypes.Bool || t == PyTypes.Float || t == PyTypes.Str || t == PyTypes.List || t == PyTypes.Tuple ||
        t == PyTypes.Dict || t == PyTypes.Set || t == PyTypes.FrozenSet || t == PyTypes.Complex;

    public override bool Match(Frame f, PyObject v)
    {
        var c = _cls.Eval(f);
        if (c is not PyType t) throw PyErr.TypeError("called match pattern must be a class");
        if (!v.Type.IsSubtypeOf(t)) return false;
        if (_pos.Length > 0)
        {
            if (SelfMatching(t))
            {
                if (_pos.Length > 1) throw PyErr.TypeError($"{t.Name}() accepts 1 positional sub-pattern ({_pos.Length} given)");
                if (!_pos[0].Match(f, v)) return false;
            }
            else
            {
                var ma = t.Lookup("__match_args__");
                if (ma is not PyTuple names) throw PyErr.TypeError($"{t.Name}() accepts 0 positional sub-patterns ({_pos.Length} given)");
                if (_pos.Length > names.Count)
                    throw PyErr.TypeError($"{t.Name}() accepts {names.Count} positional sub-pattern{(names.Count == 1 ? "" : "s")} ({_pos.Length} given)");
                for (int i = 0; i < _pos.Length; i++)
                {
                    var attr = Ops.TryGetAttr(v, Arg.Str(names.Items[i], "__match_args__ elements"));
                    if (attr == null || !_pos[i].Match(f, attr)) return false;
                }
            }
        }
        for (int i = 0; i < _kwNames.Length; i++)
        {
            PyObject? attr;
            try
            {
                attr = Ops.TryGetAttr(v, _kwNames[i]);
            }
            catch (PythonException e) when (e.Value.Type.IsSubtypeOf(Exc.AttributeError))
            {
                attr = null;
            }
            if (attr == null || !_kwPats[i].Match(f, attr)) return false;
        }
        return true;
    }
}
