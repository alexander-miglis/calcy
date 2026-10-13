"""Plotting like the TI-84 Plus CE ti_plotlib module: window, grid, axes, labels, plot, scatter, line and show_plot.

The plot area is 320 by 210 pixels. Typical use: import ti_plotlib as plt, plt.cls(), plt.window(xmin, xmax, ymin, ymax),
plt.axes("on"), plt.plot(xs, ys, "o"), plt.show_plot(). Axes are drawn by window() unless axes("off") was called.
"""

import math
import _calcy as _c
import _calcyrt as _rt

_W = 320
_H = 210
_PENS = {"thin": 1, "medium": 2, "thick": 3}


class _State:
    def __init__(self):
        self.ready = False
        self.xmin = -10.0
        self.xmax = 10.0
        self.ymin = -6.67
        self.ymax = 6.67
        self.code = _c.color("accent1")
        self.pen = 1
        self.style = "solid"
        self.axes_mode = None
        self.title = ""


_s = _State()

xmin = _s.xmin
xmax = _s.xmax
ymin = _s.ymin
ymax = _s.ymax


def _init():
    if not _s.ready:
        _c.canvas_init(_W, _H, True)
        _s.ready = True


def _px(x):
    return (x - _s.xmin) / (_s.xmax - _s.xmin) * (_W - 1)


def _py(y):
    return (_s.ymax - y) / (_s.ymax - _s.ymin) * (_H - 1)


def _segment(x1, y1, x2, y2, code, width, style):
    if style == "solid":
        _c.line(x1, y1, x2, y2, code, width)
        return
    on, off = (2, 3) if style in ("dot", "dotted", "point") else (6, 4)
    length = math.hypot(x2 - x1, y2 - y1)
    if length == 0:
        return
    t = 0.0
    while t < length:
        e = min(length, t + on)
        _c.line(x1 + (x2 - x1) * t / length, y1 + (y2 - y1) * t / length, x1 + (x2 - x1) * e / length, y1 + (y2 - y1) * e / length, code, width)
        t = e + off


def _mark(px, py, mark, code):
    if mark == "o":
        _c.circle(px, py, 2, code, True)
    elif mark == "+":
        _c.line(px - 3, py, px + 3, py, code, 1)
        _c.line(px, py - 3, px, py + 3, code, 1)
    elif mark == "x":
        _c.line(px - 2, py - 2, px + 2, py + 2, code, 1)
        _c.line(px - 2, py + 2, px + 2, py - 2, code, 1)
    else:
        _c.rect(px - 1, py - 1, 2, 2, code, True)


def _seq(v):
    if isinstance(v, (list, tuple)):
        return [float(x) for x in v]
    return [float(v)]


def cls():
    """cls(): clear the plot area."""
    _init()
    _c.clear()
    _s.axes_mode = _s.axes_mode if _s.axes_mode in ("off",) else None


def window(xmin_, xmax_, ymin_, ymax_):
    """window(xmin, xmax, ymin, ymax): set the plotting window (and draw the axes unless axes("off") was called)."""
    global xmin, xmax, ymin, ymax
    if xmax_ <= xmin_ or ymax_ <= ymin_:
        raise ValueError("window() needs xmin < xmax and ymin < ymax")
    _init()
    _s.xmin, _s.xmax, _s.ymin, _s.ymax = float(xmin_), float(xmax_), float(ymin_), float(ymax_)
    xmin, xmax, ymin, ymax = _s.xmin, _s.xmax, _s.ymin, _s.ymax
    if _s.axes_mode is None:
        _draw_axes()


def auto_window(xlist, ylist):
    """auto_window(xlist, ylist): choose a window that shows all the data with a margin."""
    xs, ys = _seq(xlist), _seq(ylist)
    if not xs or not ys:
        raise ValueError("auto_window() needs data")
    x0, x1, y0, y1 = min(xs), max(xs), min(ys), max(ys)
    dx = (x1 - x0) * 0.1 or 1.0
    dy = (y1 - y0) * 0.1 or 1.0
    window(x0 - dx, x1 + dx, y0 - dy, y1 + dy)


def _draw_axes(mode="on"):
    code = _c.color("ink")
    if mode == "window":
        _c.rect(0, 0, _W, _H, code, False)
        return
    if _s.ymin <= 0 <= _s.ymax:
        _c.line(0, _py(0), _W - 1, _py(0), code, 1)
    if _s.xmin <= 0 <= _s.xmax:
        _c.line(_px(0), 0, _px(0), _H - 1, code, 1)
    if mode == "axes":
        return
    sx = _tick(_s.xmax - _s.xmin)
    sy = _tick(_s.ymax - _s.ymin)
    if _s.ymin <= 0 <= _s.ymax:
        k = math.ceil(_s.xmin / sx)
        while k * sx <= _s.xmax:
            if k != 0:
                _c.line(_px(k * sx), _py(0) - 2, _px(k * sx), _py(0) + 2, code, 1)
            k += 1
    if _s.xmin <= 0 <= _s.xmax:
        k = math.ceil(_s.ymin / sy)
        while k * sy <= _s.ymax:
            if k != 0:
                _c.line(_px(0) - 2, _py(k * sy), _px(0) + 2, _py(k * sy), code, 1)
            k += 1


def _tick(span):
    raw = span / 10.0
    p = 10 ** math.floor(math.log10(raw)) if raw > 0 else 1
    for m in (1, 2, 5, 10):
        if raw <= m * p:
            return m * p
    return 10 * p


def axes(mode="axes", color=None):
    """axes(mode="axes"): draw the axes: "on" (with ticks), "axes" (plain), "window" (a frame) or "off"."""
    _init()
    _s.axes_mode = mode
    if mode != "off":
        _draw_axes("on" if mode in ("on", True) else mode)


def grid(xscl=1, yscl=1, style="dot", color=None):
    """grid(xscl=1, yscl=1, style="dot"): draw grid lines every xscl and yscl units; style "dot", "dash", "solid" or "point"."""
    _init()
    code = _c.color(color) if color is not None else _c.color("grid")
    if xscl <= 0 or yscl <= 0:
        raise ValueError("grid() spacing must be positive")
    if (_s.xmax - _s.xmin) / xscl > 200 or (_s.ymax - _s.ymin) / yscl > 200:
        return
    k = math.ceil(_s.xmin / xscl)
    while k * xscl <= _s.xmax:
        x = _px(k * xscl)
        if style == "point":
            j = math.ceil(_s.ymin / yscl)
            while j * yscl <= _s.ymax:
                _c.pixel(x, _py(j * yscl), code)
                j += 1
        else:
            _segment(x, 0, x, _H - 1, code, 1, style)
        k += 1
    if style != "point":
        k = math.ceil(_s.ymin / yscl)
        while k * yscl <= _s.ymax:
            y = _py(k * yscl)
            _segment(0, y, _W - 1, y, code, 1, style)
            k += 1
    if _s.axes_mode in (None, "on", "axes"):
        _draw_axes(_s.axes_mode or "on")


def color(r=0, g=0, b=0):
    """color(r, g, b): set the drawing colour (0 to 255 each); a colour name also works."""
    _init()
    spec = r if isinstance(r, (str, tuple, list)) else (int(r), int(g), int(b))
    _s.code = _c.color(spec, 255.0 if not isinstance(spec, str) else 0.0)
    _c.set_color(_s.code)


def pen(size="thin", style="solid"):
    """pen(size="thin", style="solid"): line width "thin", "medium" or "thick" and style "solid", "dot" or "dash"."""
    _s.pen = _PENS.get(size, 1) if isinstance(size, str) else max(1, int(size))
    _s.style = style


def plot(x, y=None, mark="o"):
    """plot(xlist, ylist, mark="o"): a line plot through the points with a mark at each ("o", "+", "x" or "."); plot(x, y, mark) plots one point."""
    _init()
    if y is None:
        raise TypeError("plot() needs x and y")
    xs, ys = _seq(x), _seq(y)
    if len(xs) != len(ys):
        raise ValueError("plot() needs lists of the same length")
    pts = [(_px(a), _py(b)) for a, b in zip(xs, ys)]
    for i in range(1, len(pts)):
        _segment(pts[i - 1][0], pts[i - 1][1], pts[i][0], pts[i][1], _s.code, _s.pen, _s.style)
    for p in pts:
        _mark(p[0], p[1], mark, _s.code)


def scatter(xlist, ylist, mark="o"):
    """scatter(xlist, ylist, mark="o"): a scatter plot: one mark per point, no lines."""
    _init()
    xs, ys = _seq(xlist), _seq(ylist)
    if len(xs) != len(ys):
        raise ValueError("scatter() needs lists of the same length")
    for a, b in zip(xs, ys):
        _mark(_px(a), _py(b), mark, _s.code)


def pt(x, y, mark="o"):
    """pt(x, y, mark="o"): plot one point."""
    scatter([x], [y], mark)


def line(x1, y1, x2, y2, mode=""):
    """line(x1, y1, x2, y2, mode=""): a line segment in window coordinates; mode "v" adds an arrow head (a vector)."""
    _init()
    a, b, c, d = _px(x1), _py(y1), _px(x2), _py(y2)
    _segment(a, b, c, d, _s.code, _s.pen, _s.style)
    if mode == "v":
        ang = math.atan2(d - b, c - a)
        for da in (2.6, -2.6):
            _c.line(c, d, c + 7 * math.cos(ang + da), d + 7 * math.sin(ang + da), _s.code, _s.pen)


def lin_reg(xlist, ylist, disp="center", row=11):
    """lin_reg(xlist, ylist, disp="center", row=11): draw the least squares line and show y = ax + b; returns (a, b)."""
    xs, ys = _seq(xlist), _seq(ylist)
    n = len(xs)
    if n < 2 or n != len(ys):
        raise ValueError("lin_reg() needs two lists of the same length (at least 2 points)")
    mx = sum(xs) / n
    my = sum(ys) / n
    sxx = sum((v - mx) ** 2 for v in xs)
    if sxx == 0:
        raise ValueError("lin_reg() needs at least two different x values")
    a = sum((xs[i] - mx) * (ys[i] - my) for i in range(n)) / sxx
    b = my - a * mx
    line(_s.xmin, a * _s.xmin + b, _s.xmax, a * _s.xmax + b)
    sign = "+" if b >= 0 else "-"
    text_at(row, "y=%.4gx%s%.4g" % (a, sign, abs(b)), disp)
    return (a, b)


def text_at(row, text, align="left"):
    """text_at(row, text, align="left"): write text on row 1 (top) to 12 (bottom); align "left", "center" or "right"."""
    _init()
    row = max(1, min(12, int(row)))
    s = str(text)
    w = _c.text_width(s, 1)
    y = (row - 1) * (_H / 12.0) + 4
    if align == "center":
        x = (_W - w) / 2
    elif align == "right":
        x = _W - w - 4
    else:
        x = 4
    _c.rect(x - 1, y - 1, w + 2, 9, _c.color("paper"), True)
    _c.text(x, y, s, _s.code)


def title(text):
    """title(text): a title centred at the top of the plot."""
    _s.title = str(text)
    text_at(1, text, "center")


def labels(xlabel="", ylabel="", x=12, y=2):
    """labels(xlabel, ylabel, x=12, y=2): axis labels; xlabel at the right end of row x, ylabel at the left of row y."""
    if xlabel:
        text_at(x, xlabel, "right")
    if ylabel:
        text_at(y, ylabel, "left")


def show_plot():
    """show_plot(): show what has been drawn."""
    _init()
    _c.show()


def escape():
    """escape(): True when the clear (Delete) key was pressed since the last check."""
    k = _c.key()
    return k in ("del", "esc", "clear")


def wait(seconds):
    """wait(seconds): pause, for simple animations."""
    _rt.wait(seconds)
