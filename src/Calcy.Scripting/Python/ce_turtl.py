"""The TI-84 Plus CE turtle module: from ce_turtl import turtle, then turtle.forward(50), turtle.pencolor(255, 0, 0) and so on.

It is the same turtle as the turtle module; colours given as three numbers use 0 to 255.
"""

import turtle as _turtle
from turtle import Turtle, Screen

_turtle.colormode(255)

turtle = _turtle._getpen()
"""turtle: the turtle object of the TI-84 Plus CE module."""
