"""Drawing like the TI-Nspire CX II ti_draw module, on a 318 by 212 pixel screen with (0, 0) at the top left.

set_color(r, g, b), set_pen("thin", "solid"), draw_line, draw_rect, fill_rect, draw_circle, fill_circle, draw_arc,
draw_poly, fill_poly, draw_text and clear. use_buffer() and paint_buffer() give flicker free animation;
set_window(xmin, xmax, ymin, ymax) switches to your own coordinates with y upwards.
"""

import math
import _calcy as _c
import _calcyrt as _rt

_W = 318
_H = 212
_PENS = {"thin": 1, "medium": 2, "thick": 3}


class _State:
    def __init__(self):
        self.ready = False
        self.code = _c.color("black")
        self.pen = 1
        self.style = "solid"
        self.window = None


_s = _State()


def _init():
    if not _s.ready:
        _c.canvas_init(_W, _H)
        _s.ready = True


def _x(x):
    if _s.window is None:
        return float(x)
    x0, x1, y0, y1 = _s.window
    return (x - x0) / (x1 - x0) * (_W - 1)


def _y(y):
    if _s.window is None:
        return float(y)
    x0, x1, y0, y1 = _s.window
    return (y1 - y) / (y1 - y0) * (_H - 1)


def _sx(w):
    if _s.window is None:
        return float(w)
    x0, x1, y0, y1 = _s.window
    return w / (x1 - x0) * (_W - 1)


def _sy(h):
    if _s.window is None:
        return float(h)
    x0, x1, y0, y1 = _s.window
    return h / (y1 - y0) * (_H - 1)


def _line(x1, y1, x2, y2):
    if _s.style == "solid":
        _c.line(x1, y1, x2, y2, _s.code, _s.pen)
        return
    on, off = (2, 3) if _s.style in ("dotted", "dot") else (6, 4)
    length = math.hypot(x2 - x1, y2 - y1)
    t = 0.0
    while t < length:
        e = min(length, t + on)
        _c.line(x1 + (x2 - x1) * t / length, y1 + (y2 - y1) * t / length, x1 + (x2 - x1) * e / length, y1 + (y2 - y1) * e / length, _s.code, _s.pen)
        t = e + off


def _rect_corner(x, y, w, h):
    if _s.window is None:
        return float(x), float(y), float(w), float(h)
    return _x(x), _y(y) - _sy(h), _sx(w), _sy(h)


def clear():
    """clear(): clear the screen."""
    _init()
    _c.clear()


def clear_rect(x, y, width, height):
    """clear_rect(x, y, width, height): clear a rectangle."""
    _init()
    a, b, w, h = _rect_corner(x, y, width, height)
    _c.rect(a, b, w, h, _c.color("paper"), True)


def set_color(r=0, g=0, b=0):
    """set_color(r, g, b): set the drawing colour (0 to 255 each); set_color((r, g, b)) and colour names work too."""
    _init()
    spec = r if isinstance(r, (str, tuple, list)) else (int(r), int(g), int(b))
    _s.code = _c.color(spec, 255.0 if not isinstance(spec, str) else 0.0)
    _c.set_color(_s.code)


def set_pen(thickness="thin", style="solid"):
    """set_pen(thickness="thin", style="solid"): thickness "thin", "medium" or "thick"; style "solid", "dotted" or "dashed"."""
    _s.pen = _PENS.get(thickness, 1) if isinstance(thickness, str) else max(1, int(thickness))
    _s.style = style


def set_window(xmin, xmax, ymin, ymax):
    """set_window(xmin, xmax, ymin, ymax): use your own coordinates (y upwards); set_window(0, 318, 0, 212) style calls work too."""
    if xmax <= xmin or ymax <= ymin:
        raise ValueError("set_window() needs xmin < xmax and ymin < ymax")
    _s.window = (float(xmin), float(xmax), float(ymin), float(ymax))


def get_screen_dim():
    """get_screen_dim(): the screen size (318, 212)."""
    return (_W, _H)


def draw_line(x1, y1, x2, y2):
    """draw_line(x1, y1, x2, y2): a line."""
    _init()
    _line(_x(x1), _y(y1), _x(x2), _y(y2))


def draw_rect(x, y, width, height):
    """draw_rect(x, y, width, height): a rectangle outline with its top left corner at (x, y)."""
    _init()
    a, b, w, h = _rect_corner(x, y, width, height)
    _c.rect(a, b, w, h, _s.code, False, _s.pen)


def fill_rect(x, y, width, height):
    """fill_rect(x, y, width, height): a filled rectangle."""
    _init()
    a, b, w, h = _rect_corner(x, y, width, height)
    _c.rect(a, b, w, h, _s.code, True)


def draw_circle(x, y, radius):
    """draw_circle(x, y, radius): a circle outline centred on (x, y)."""
    _init()
    _c.circle(_x(x), _y(y), _sx(radius), _s.code, False, _s.pen)


def fill_circle(x, y, radius):
    """fill_circle(x, y, radius): a filled circle."""
    _init()
    _c.circle(_x(x), _y(y), _sx(radius), _s.code, True)


def draw_arc(x, y, width, height, start_angle, arc_angle):
    """draw_arc(x, y, width, height, start_angle, arc_angle): an arc of the ellipse in that box, angles in degrees counterclockwise from east."""
    _init()
    a, b, w, h = _rect_corner(x, y, width, height)
    _c.arc(a, b, w, h, start_angle, arc_angle, _s.code, False, _s.pen)


def fill_arc(x, y, width, height, start_angle, arc_angle):
    """fill_arc(x, y, width, height, start_angle, arc_angle): a filled pie slice."""
    _init()
    a, b, w, h = _rect_corner(x, y, width, height)
    _c.arc(a, b, w, h, start_angle, arc_angle, _s.code, True)


def _points(xlist, ylist):
    if len(xlist) != len(ylist):
        raise ValueError("the x and y lists must have the same length")
    pts = []
    for i in range(len(xlist)):
        pts.append(_x(xlist[i]))
        pts.append(_y(ylist[i]))
    return pts


def draw_poly(xlist, ylist):
    """draw_poly(xlist, ylist): a closed polygon outline."""
    _init()
    _c.polygon(_points(xlist, ylist), _s.code, False, _s.pen)


def fill_poly(xlist, ylist):
    """fill_poly(xlist, ylist): a filled polygon."""
    _init()
    _c.polygon(_points(xlist, ylist), _s.code, True)


def plot_xy(x, y, shape=1):
    """plot_xy(x, y, shape=1): a mark at (x, y): 1 dot, 2 small box, 3 cross, 4 plus, 5 circle, 6 filled circle, other numbers a box."""
    _init()
    px, py = _x(x), _y(y)
    if shape == 1:
        _c.pixel(px, py, _s.code)
    elif shape == 3:
        _c.line(px - 2, py - 2, px + 2, py + 2, _s.code, 1)
        _c.line(px - 2, py + 2, px + 2, py - 2, _s.code, 1)
    elif shape == 4:
        _c.line(px - 3, py, px + 3, py, _s.code, 1)
        _c.line(px, py - 3, px, py + 3, _s.code, 1)
    elif shape == 5:
        _c.circle(px, py, 3, _s.code, False)
    elif shape == 6:
        _c.circle(px, py, 3, _s.code, True)
    else:
        _c.rect(px - 1, py - 1, 3, 3, _s.code, True)


def draw_text(x, y, text):
    """draw_text(x, y, text): text with its bottom left corner at (x, y)."""
    _init()
    _c.text(_x(x), _y(y) - 8, str(text), _s.code)


def use_buffer():
    """use_buffer(): draw off screen until paint_buffer(), for flicker free animation."""
    _init()
    _c.canvas_buffered(True)


def paint_buffer():
    """paint_buffer(): show everything drawn since use_buffer() or the last paint_buffer()."""
    _init()
    _c.show()


def show_draw():
    """show_draw(): show the drawing."""
    _init()
    _c.show()


def sleep(seconds):
    """sleep(seconds): pause, for animations."""
    _rt.wait(seconds)
