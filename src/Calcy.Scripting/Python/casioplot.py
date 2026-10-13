"""Pixel drawing like the Casio fx-CG50 casioplot module, on a 384 by 192 pixel screen with (0, 0) at the top left.

Drawing goes to a buffer: call show_screen() to display it (it is also shown when the program ends).
Colours are (r, g, b) tuples with values 0 to 255.
"""

import _calcy as _c

_W = 384
_H = 192
_SIZES = {"small": 1, "medium": 2, "large": 3}


class _State:
    def __init__(self):
        self.ready = False


_s = _State()


def _init():
    if not _s.ready:
        _c.canvas_init(_W, _H, True)
        _s.ready = True


def _code(color):
    return _c.color(color, 255.0)


def set_pixel(x, y, color=(0, 0, 0)):
    """set_pixel(x, y, color=(0, 0, 0)): colour one pixel."""
    _init()
    _c.pixel(int(x), int(y), _code(color))


def get_pixel(x, y):
    """get_pixel(x, y): the (r, g, b) colour of a pixel, or None outside the screen."""
    _init()
    return _c.get_pixel(int(x), int(y))


def clear_screen():
    """clear_screen(): make the whole screen white."""
    _init()
    _c.clear()


def show_screen():
    """show_screen(): display what has been drawn."""
    _init()
    _c.show()


def draw_string(x, y, text, color=(0, 0, 0), size="medium"):
    """draw_string(x, y, text, color=(0, 0, 0), size="medium"): text with its top left corner at (x, y); size "small", "medium" or "large"."""
    _init()
    _c.text(int(x), int(y), str(text), _code(color), _SIZES.get(size, 2))
