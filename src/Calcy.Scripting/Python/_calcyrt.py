"""Shared helpers of the Calcy modules: waiting for time or keys without using up the program's time limit."""

import _calcy
import time as _time


def wait(seconds):
    """wait(seconds): pause the program; the pause does not count towards the time limit."""
    s = float(seconds)
    if s <= 0:
        return
    if _calcy.pause_waits():
        _calcy.wait(s)
    else:
        _time.sleep(s)


def wait_key():
    """wait_key(): wait for a key press and return its name, for example 'enter', 'up' or 'a'."""
    if _calcy.pause_waits():
        return _calcy.wait_key()
    while True:
        k = _calcy.key()
        if k:
            return k
        _time.sleep(0.02)
