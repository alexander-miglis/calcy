"""Calcy's own canvas: pixels, lines, shapes and text on a pixel grid with (0, 0) at the top left, double buffering
and a frame loop for animations and games.

The canvas is 320 by 220 pixels; size(w, h) changes it. Colours are names ("red"), "#rrggbb", (r, g, b) with 0 to 255,
or the scheme roles "ink", "paper", "muted", "grid" and "accent1" to "accent5" that follow the colour scheme.
"""

import time as _time
import _calcy as _c
import _calcyrt as _rt


class _State:
    def __init__(self):
        self.ready = False
        self.code = _c.color("ink")
        self.spec = "ink"
        self.start = _time.monotonic()


_s = _State()


def _init():
    if not _s.ready:
        _c.canvas_init(320, 220)
        _s.ready = True


def _code(color):
    if color is None:
        return _s.code
    return _c.color(color, 255.0 if isinstance(color, (tuple, list)) else 0.0)


def size(width=None, height=None):
    """size(width, height): set the canvas size in pixels (clears it); size() returns (width, height)."""
    if width is None:
        _init()
        return _c.canvas_size()
    _c.canvas_init(int(width), int(height if height is not None else width))
    _s.ready = True
    return _c.canvas_size()


def width():
    """width(): the canvas width in pixels."""
    _init()
    return _c.canvas_size()[0]


def height():
    """height(): the canvas height in pixels."""
    _init()
    return _c.canvas_size()[1]


def rgb(r, g, b):
    """rgb(r, g, b): a colour from red, green and blue values 0 to 255."""
    return (int(r), int(g), int(b))


def color(c=None):
    """color(c): set the current drawing colour; color() returns it."""
    if c is None:
        return _s.spec
    _init()
    _s.code = _code(c)
    _s.spec = c
    _c.set_color(_s.code)


def clear(color=None):
    """clear(color=None): fill the canvas with a colour (the background when none is given)."""
    _init()
    if color is None:
        _c.clear()
    else:
        _c.clear(_code(color))


def background(color):
    """background(color): change the background colour, keeping the drawing."""
    _init()
    _c.background(_code(color))


def pixel(x, y, color=None):
    """pixel(x, y, color=None): colour one pixel."""
    _init()
    _c.pixel(x, y, _code(color))


def get_pixel(x, y):
    """get_pixel(x, y): the (r, g, b) colour of a pixel, or None outside the canvas."""
    _init()
    return _c.get_pixel(x, y)


def line(x1, y1, x2, y2, color=None, width=1):
    """line(x1, y1, x2, y2, color=None, width=1): a straight line."""
    _init()
    _c.line(x1, y1, x2, y2, _code(color), width)


def rect(x, y, w, h, color=None, fill=False, width=1):
    """rect(x, y, w, h, color=None, fill=False, width=1): a rectangle with its top left corner at (x, y)."""
    _init()
    _c.rect(x, y, w, h, _code(color), fill, width)


def fill_rect(x, y, w, h, color=None):
    """fill_rect(x, y, w, h, color=None): a filled rectangle."""
    rect(x, y, w, h, color, True)


def circle(x, y, r, color=None, fill=False, width=1):
    """circle(x, y, r, color=None, fill=False, width=1): a circle centred on (x, y)."""
    _init()
    _c.circle(x, y, r, _code(color), fill, width)


def fill_circle(x, y, r, color=None):
    """fill_circle(x, y, r, color=None): a filled circle."""
    circle(x, y, r, color, True)


def ellipse(x, y, w, h, color=None, fill=False, width=1):
    """ellipse(x, y, w, h, color=None, fill=False, width=1): an ellipse in the box with its top left corner at (x, y)."""
    _init()
    _c.arc(x, y, w, h, 0, 360, _code(color), fill, width)


def arc(x, y, w, h, start, extent, color=None, fill=False, width=1):
    """arc(x, y, w, h, start, extent, color=None, fill=False, width=1): part of an ellipse, angles in degrees counterclockwise from east."""
    _init()
    _c.arc(x, y, w, h, start, extent, _code(color), fill, width)


def polygon(points, color=None, fill=False, width=1):
    """polygon(points, color=None, fill=False, width=1): a closed polygon through [(x, y), ...]."""
    _init()
    flat = []
    for p in points:
        flat.append(p[0])
        flat.append(p[1])
    _c.polygon(flat, _code(color), fill, width)


def fill_polygon(points, color=None):
    """fill_polygon(points, color=None): a filled polygon."""
    polygon(points, color, True)


def text(x, y, s, color=None, size=1):
    """text(x, y, s, color=None, size=1): text with its top left corner at (x, y); size 2 or 3 makes it bigger."""
    _init()
    _c.text(x, y, str(s), _code(color), size)


def text_width(s, size=1):
    """text_width(s, size=1): the width of text in pixels."""
    return _c.text_width(str(s), size)


def buffered(on=True):
    """buffered(on=True): draw off screen until show(), for flicker free animation."""
    _init()
    _c.canvas_buffered(bool(on))


def show():
    """show(): show the drawing (needed when buffered)."""
    _init()
    _c.show()


refresh = show


def key():
    """key(): the name of the next key pressed, or "" when none."""
    return _c.key()


def wait_key():
    """wait_key(): wait for a key press and return its name."""
    return _rt.wait_key()


def is_pressed(name):
    """is_pressed(name): True while the key is held down ("left", "right", "up", "down", "space", "a" and so on)."""
    return _c.key_down(name)


def keys():
    """keys(): the keys held down now."""
    return _c.keys_down()


def wait(seconds):
    """wait(seconds): pause; the pause does not count towards the time limit."""
    _rt.wait(seconds)


def time():
    """time(): seconds since the program started drawing."""
    return _time.monotonic() - _s.start


def run(frame, fps=30, seconds=None, frames=None):
    """run(frame, fps=30, seconds=None, frames=None): call frame(n) about fps times a second with buffering on, showing each frame, until frame returns False, the time or frame count runs out, or the program is stopped."""
    _init()
    _c.canvas_buffered(True)
    step = 1.0 / max(1, min(120, fps))
    start = _time.monotonic()
    n = 0
    while True:
        if frames is not None and n >= frames:
            break
        if seconds is not None and _time.monotonic() - start >= seconds:
            break
        t0 = _time.monotonic()
        result = frame(n)
        _c.show()
        n += 1
        if result is False:
            break
        left = step - (_time.monotonic() - t0)
        if left > 0:
            _rt.wait(left)
    return n
