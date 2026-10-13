"""Calcy's calculator from Python: exact arithmetic, algebra, calculus, the session's variables and graphs.

Results follow one rule: exact integers come back as int, other exact rationals as Fraction, decimals as float,
complex numbers as complex, true and false as bool, text as str, lists as list, points as tuple, matrices as lists
of rows, and anything symbolic (sqrt(2), pi, x^2 + 1, equations) as a calc.Expr, which is a str holding the
calculator input text that prints the way the calculator shows it. Pass numeric=True (or use calc.n) to get floats
instead. Calculator errors raise ValueError (also available as calc.CalcError).
"""

import _calcy as _c
from fractions import Fraction

CalcError = ValueError

# "from calc import *" leaves Python's own eval and set alone; use calc.eval and calc.set for those.
__all__ = ["CalcError", "Expr", "Fraction", "n", "exact", "latex", "get", "delete", "variables", "solve", "nsolve",
           "diff", "integrate", "limit", "factor", "expand", "simplify", "show", "function", "graph"]


def _text(value):
    """Calculator input text for a Python value."""
    if isinstance(value, Expr):
        return "(" + value.text + ")"
    if isinstance(value, str):
        return value
    if isinstance(value, bool):
        return "true" if value else "false"
    if isinstance(value, Fraction):
        return value._calc_text()
    if isinstance(value, int):
        return str(value)
    if isinstance(value, float):
        r = repr(value)
        if r == "inf":
            return "infinity"
        if r == "-inf":
            return "-infinity"
        return "(" + r.replace("e", "*10^") + ")" if "e" in r else r
    if isinstance(value, complex):
        return "(" + _text(value.real) + "+" + _text(value.imag) + "i)"
    if isinstance(value, (list, tuple)):
        if len(value) > 0 and all(isinstance(r, (list, tuple)) for r in value):
            return "[" + ", ".join("[" + ", ".join(_text(x) for x in r) + "]" for r in value) + "]"
        return "{" + ", ".join(_text(x) for x in value) + "}"
    raise TypeError("a " + type(value).__name__ + " has no calculator value")


def _encode(value):
    """A value the host can store: numbers stay numbers, exact and symbolic values become calculator text."""
    if isinstance(value, (Expr, Fraction)):
        return _text(value)
    if isinstance(value, (list, tuple)):
        return [_encode(x) for x in value]
    return value


class Expr(str):
    """Expr(text): a symbolic calculator result. It is a str holding the calculator input text ("2*sqrt(2)"), so it joins with strings (f + " = 0" builds an equation) and works anywhere text does, but it prints like the calculator (2√2, x², π) and supports + - * / ** with numbers and other expressions. text is the input form, pretty the display form, latex the LaTeX, and float() gives a decimal."""

    def __new__(cls, text, pretty=None):
        if pretty is None:
            value = eval(text)
            if isinstance(value, Expr):
                return value
            text, pretty = _text(value), str(value)
        self = str.__new__(cls, text)
        self.pretty = pretty
        return self

    @property
    def text(self):
        """text: the calculator input text."""
        return str.__str__(self)

    def _calc_text(self):
        return self.text

    @property
    def latex(self):
        """latex: the LaTeX of the expression."""
        return _c.calc_latex(self.text)

    def n(self):
        """n(): the decimal value as a float (or complex)."""
        return _c.calc_number(self.text)

    def subs(self, **values):
        """subs(name=value, ...): the expression with some names replaced, for example f.subs(x=2)."""
        return _c.calc_subs(self.text, {k: _encode(v) for k, v in values.items()})

    def __call__(self, *args):
        if len(args) == 1:
            return self.subs(x=args[0])
        raise TypeError("call an expression with one value for x, or use subs(name=value)")

    def __str__(self):
        return self.pretty

    def __repr__(self):
        return self.pretty

    def __format__(self, spec):
        return format(self.pretty, spec)

    def __hash__(self):
        return str.__hash__(self)

    def __eq__(self, other):
        if isinstance(other, str):
            return str.__eq__(self, other) or other == self.pretty
        if isinstance(other, (int, float, Fraction)):
            try:
                return _c.calc_number(self.text) == float(other)
            except (TypeError, ValueError):
                return False
        return NotImplemented

    def __ne__(self, other):
        r = self.__eq__(other)
        if r is NotImplemented:
            return r
        return not r

    def __lt__(self, other):
        return float(self) < float(other)

    def __le__(self, other):
        return float(self) <= float(other)

    def __gt__(self, other):
        return float(self) > float(other)

    def __ge__(self, other):
        return float(self) >= float(other)

    def __float__(self):
        v = _c.calc_number(self.text)
        if isinstance(v, complex):
            raise TypeError(self.pretty + " is not a real number")
        return v

    def __complex__(self):
        return complex(_c.calc_number(self.text))

    def __int__(self):
        return int(float(self))

    def __round__(self, ndigits=None):
        return round(float(self), ndigits)

    def __abs__(self):
        return eval("abs(" + self.text + ")")

    def __neg__(self):
        return eval("-(" + self.text + ")")

    def __pos__(self):
        return self

    def __add__(self, other):
        if isinstance(other, str) and not isinstance(other, Expr):
            return self.text + other
        return eval(_text(self) + "+" + _text(other))

    def __radd__(self, other):
        if isinstance(other, str) and not isinstance(other, Expr):
            return other + self.text
        return eval(_text(other) + "+" + _text(self))

    def __sub__(self, other):
        return eval(_text(self) + "-" + _text(other))

    def __rsub__(self, other):
        return eval(_text(other) + "-" + _text(self))

    def __mul__(self, other):
        return eval(_text(self) + "*" + _text(other))

    def __rmul__(self, other):
        return eval(_text(other) + "*" + _text(self))

    def __truediv__(self, other):
        return eval(_text(self) + "/" + _text(other))

    def __rtruediv__(self, other):
        return eval(_text(other) + "/" + _text(self))

    def __pow__(self, other):
        return eval(_text(self) + "^" + _text(other))

    def __rpow__(self, other):
        return eval(_text(other) + "^" + _text(self))


def eval(text, numeric=False):
    """eval(text, numeric=False): evaluate calculator input such as "1/3 + 1/6", "sqrt(72)" or "a = 5" and return the result as a Python value (see the module help for the rule). numeric=True gives floats."""
    return _c.calc_eval(_text(text), numeric)


def n(text):
    """n(text): evaluate calculator input and return decimals (floats) instead of exact values."""
    return _c.calc_eval(_text(text), True)


def exact(text, plain=False):
    """exact(text, plain=False): the exact result as text the way the calculator shows it ("2√2"); plain=True gives input text ("2sqrt(2)")."""
    return _c.calc_exact(_text(text), plain)


def latex(text):
    """latex(text): the exact result as LaTeX, for example "\\frac{1}{2}"."""
    return _c.calc_latex(_text(text))


def get(name, default=None):
    """get(name, default=None): the value of a calculator variable, a callable for a calculator function, or default."""
    found = _c.calc_get(name)
    if found is None:
        return default
    if found[0] == "value":
        return found[1]

    def call(*args):
        return _c.calc_eval(name + "(" + ", ".join(_text(a) for a in args) + ")", False)

    call.__name__ = name
    return call


def set(name, value):
    """set(name, value): store a value in a calculator variable, so every mode and expression can use it."""
    _c.calc_set(name, _encode(value))


def delete(name):
    """delete(name): remove a calculator variable or function; returns True when it existed."""
    return _c.calc_delete(name)


def variables():
    """variables(): a dict of every calculator variable and its value."""
    return _c.calc_variables()


def solve(equation, variable="x"):
    """solve(equation, variable="x"): the solutions as a list, for example solve("x^2 = 2") gives [-√2, √2]. For a system pass lists: solve(["x + y = 3", "x - y = 1"], ["x", "y"]) gives [{'x': 2, 'y': 1}]."""
    return _c.calc_solve(_text(equation), _text(variable))


def nsolve(equation, variable="x", guess=None):
    """nsolve(equation, variable="x", guess=None): a numeric solution as a float, near guess when given."""
    args = _text(equation) + ", " + _text(variable)
    if guess is not None:
        args += ", " + _text(guess)
    r = _c.calc_eval("nsolve(" + args + ")", True)
    if isinstance(r, list) and len(r) == 1:
        return r[0]
    return r


def diff(expr, variable="x", order=1):
    """diff(expr, variable="x", order=1): the derivative, for example diff("x^3 - 3x") gives 3x² - 3."""
    args = _text(expr) + ", " + _text(variable)
    if order != 1:
        args += ", " + _text(order)
    return _c.calc_eval("diff(" + args + ")", False)


def integrate(expr, variable="x", lower=None, upper=None):
    """integrate(expr, variable="x", lower=None, upper=None): the antiderivative, or the definite integral between lower and upper."""
    args = _text(expr) + ", " + _text(variable)
    if lower is not None or upper is not None:
        if lower is None or upper is None:
            raise TypeError("integrate() needs both lower and upper for a definite integral")
        args += ", " + _text(lower) + ", " + _text(upper)
    return _c.calc_eval("integrate(" + args + ")", False)


def limit(expr, variable, point, side=None):
    """limit(expr, variable, point, side=None): the limit as variable approaches point; side "+" or "-" for one sided limits."""
    args = _text(expr) + ", " + _text(variable) + ", " + _text(point)
    if side is not None:
        args += ', "' + side + '"'
    return _c.calc_eval("limit(" + args + ")", False)


def factor(expr):
    """factor(expr): factorise a polynomial or an integer, for example factor("x^2 - 1") gives (x - 1)·(x + 1)."""
    return _c.calc_eval("factor(" + _text(expr) + ")", False)


def expand(expr):
    """expand(expr): multiply out products and powers, for example expand("(x + 1)^3")."""
    return _c.calc_eval("expand(" + _text(expr) + ")", False)


def simplify(expr):
    """simplify(expr): a simpler equivalent form."""
    return _c.calc_eval("simplify(" + _text(expr) + ")", False)


def show(value, label=None):
    """show(value, label=None): print a value as typeset maths in the console (fractions, roots and powers drawn properly)."""
    if isinstance(value, str):
        text = str.__str__(value)
    else:
        text = _text(value)
    _c.calc_show(text, label)


def function(fn=None, name=None):
    """function(fn=None, name=None): decorator that makes a Python function a calculator function, usable in calculator input and graphs while the program's session is open. Write @calc.function above def f(x): ..."""

    def register(f):
        fname = name if name is not None else f.__name__
        if fname == "<lambda>":
            raise ValueError("give a lambda a name: calc.function(fn, name='f')")

        def bridge(*args):
            result = f(*args)
            if result is None:
                raise TypeError(fname + "() returned None; return a number")
            return _encode(result)

        _c.calc_register(fname, bridge, f.__doc__, f)
        return f

    if fn is None:
        return register
    if isinstance(fn, str):
        name = fn
        return register
    return register(fn)


class _Graph:
    """The graph panel: graph.add("y = sin(x)") shows a native, interactive graph that can be sent to Graphing mode."""

    def add(self, text, color=None):
        """add(text, color=None): add a graph such as "y = x^2", "x^2 + y^2 = 9", "(cos(t), sin(t))" or "y < x"; color 0 to 4 picks a graph colour. Returns its index."""
        return _c.graph_add(_text(text), color)

    def remove(self, index):
        """remove(index): remove the graph with that index."""
        return _c.graph_remove(index)

    def clear(self):
        """clear(): remove every graph."""
        _c.graph_clear()

    def window(self, xmin=None, xmax=None, ymin=None, ymax=None):
        """window(xmin, xmax, ymin, ymax): set the visible range; window() goes back to fitting the graphs."""
        if xmin is None:
            _c.graph_window(None)
        else:
            _c.graph_window(float(xmin), float(xmax), float(ymin), float(ymax))

    def plot(self, f, xmin=-10, xmax=10, points=200):
        """plot(f, xmin=-10, xmax=10, points=200): plot a Python function of x as a graph (it becomes a calculator function named after it)."""
        fname = getattr(f, "__name__", "f")
        if not fname.isidentifier() or fname == "<lambda>":
            fname = "pyfunc"
        function(f, name=fname)
        return self.add("y = " + fname + "(x)")


graph = _Graph()

_c.bind_types(Fraction, Expr)
