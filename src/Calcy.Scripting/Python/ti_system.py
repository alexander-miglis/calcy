"""TI style system helpers: keys, waiting, the console and the calculator's variables.

get_key() returns the name of the last key pressed ("" when none): letters and digits as typed, and "up", "down",
"left", "right", "enter", "space", "tab", "del" and so on. Esc stops the program. store_value and recall_value use
the same variables as the rest of Calcy.
"""

import time as _time
import _calcy as _c
import _calcyrt as _rt
import calc as _calc


def get_key(wait=False):
    """get_key(wait=False): the name of the next key pressed, or "" when none; get_key(True) waits for one."""
    if wait:
        return _rt.wait_key()
    return _c.key()


def wait_key():
    """wait_key(): wait for a key press and return its name."""
    return _rt.wait_key()


def escape():
    """escape(): True when Delete (the calculator's clear key) was pressed since the last check."""
    return _c.key() in ("del", "clear")


def is_pressed(key):
    """is_pressed(key): True while the key is held down."""
    return _c.key_down(key)


def disp_clr():
    """disp_clr(): clear the console."""
    _c.console_clear()


def clear_history():
    """clear_history(): clear the console."""
    _c.console_clear()


def disp_at(row, text, align="left"):
    """disp_at(row, text, align="left"): show text in the console (the row and alignment are accepted for compatibility)."""
    print(str(text))


def disp_cursor(on=0):
    """disp_cursor(on): accepted for compatibility."""


def disp_wait():
    """disp_wait(): wait until a key is pressed."""
    _rt.wait_key()


def sleep(seconds):
    """sleep(seconds): pause the program."""
    _rt.wait(seconds)


def get_time_ms():
    """get_time_ms(): a millisecond clock for timing."""
    return int(_time.monotonic() * 1000)


def store_value(name, value):
    """store_value(name, value): store a number in a calculator variable."""
    _calc.set(name, value)


def recall_value(name):
    """recall_value(name): read a calculator variable (None when it does not exist)."""
    return _calc.get(name)


def store_list(name, values):
    """store_list(name, values): store a list in a calculator variable."""
    _calc.set(name, list(values))


def recall_list(name):
    """recall_list(name): read a list from a calculator variable."""
    v = _calc.get(name)
    if v is None:
        return None
    return list(v) if isinstance(v, (list, tuple)) else [v]


def eval_function(name, value):
    """eval_function(name, value): evaluate a calculator function, for example eval_function("f", 2) is f(2)."""
    return _calc.eval(name + "(" + _calc._text(value) + ")")
