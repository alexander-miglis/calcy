"""Turtle graphics, compatible with the standard turtle module (and the TI and Casio calculator turtles).

The turtle starts in the middle of a 320 by 220 canvas facing east; one unit is one pixel and y grows upwards.
Use the module functions (forward(100), left(90)) or Turtle() objects. Colours are names ("red"), "#rrggbb" or
(r, g, b) tuples (0 to 1, or 0 to 255 after colormode(255)). Drawings appear in Code mode's canvas.
"""

import math
import _calcy as _c
import _calcyrt as _rt

_SPEEDS = {"fastest": 0, "fast": 10, "normal": 6, "slow": 3, "slowest": 1}
_KEYS = {"return": "enter", "escape": "esc", "backspace": "del", "delete": "del", "prior": "pageup", "next": "pagedown"}


def _keyname(key):
    k = str(key).strip().lower()
    return _KEYS.get(k, k)


class _Screen:
    """The drawing window shared by all turtles (turtle.Screen())."""

    def __init__(self):
        self._ready = False
        self._width = 320
        self._height = 220
        self._colormode = 1.0
        self._tracer = True
        self._world = None
        self._bg = "white"
        self._turtles = []
        self._keys = {}
        self._timers = []

    def _init(self):
        if not self._ready:
            _c.canvas_init(self._width, self._height)
            self._ready = True

    def _point(self, x, y):
        if self._world is None:
            return self._width / 2 + x, self._height / 2 - y
        llx, lly, urx, ury = self._world
        return (x - llx) / (urx - llx) * self._width, (ury - y) / (ury - lly) * self._height

    def _unit(self):
        if self._world is None:
            return 1.0
        llx, lly, urx, ury = self._world
        return (self._width / abs(urx - llx) + self._height / abs(ury - lly)) / 2

    def _code(self, spec):
        if isinstance(spec, (tuple, list)) and self._colormode == 1.0 and all(isinstance(v, (int, float)) and v <= 1 for v in spec):
            return _c.color(tuple(float(v) for v in spec), 1.0)
        return _c.color(spec, 255.0 if isinstance(spec, (tuple, list)) else 0.0)

    def setup(self, width=None, height=None, startx=None, starty=None):
        """setup(width, height): set the canvas size in pixels (clears the drawing)."""
        if isinstance(width, float) and width <= 1:
            width = None
        if isinstance(height, float) and height <= 1:
            height = None
        if width is not None:
            self._width = max(16, min(2048, int(width)))
        if height is not None:
            self._height = max(16, min(2048, int(height)))
        _c.canvas_init(self._width, self._height)
        self._ready = True
        for t in self._turtles:
            t._sprite()

    def screensize(self, canvwidth=None, canvheight=None, bg=None):
        """screensize(canvwidth=None, canvheight=None, bg=None): set or return the canvas size."""
        if canvwidth is None and canvheight is None and bg is None:
            return (self._width, self._height)
        if canvwidth is not None or canvheight is not None:
            self.setup(canvwidth or self._width, canvheight or self._height)
        if bg is not None:
            self.bgcolor(bg)

    def window_width(self):
        """window_width(): the canvas width in pixels."""
        return self._width

    def window_height(self):
        """window_height(): the canvas height in pixels."""
        return self._height

    def bgcolor(self, *args):
        """bgcolor(color): set the background colour; bgcolor() returns it."""
        if len(args) == 0:
            return self._bg
        spec = args[0] if len(args) == 1 else tuple(args)
        self._init()
        _c.background(self._code(spec))
        self._bg = spec

    def title(self, text):
        """title(text): accepted for compatibility (the canvas has no title bar)."""

    def colormode(self, mode=None):
        """colormode(mode=None): 1.0 for colour values 0 to 1, 255 for 0 to 255; colormode() returns the mode."""
        if mode is None:
            return 255 if self._colormode == 255 else 1.0
        if mode not in (1, 1.0, 255):
            raise ValueError("colormode must be 1.0 or 255")
        self._colormode = 255 if mode == 255 else 1.0

    def tracer(self, n=None, delay=None):
        """tracer(n=None, delay=None): tracer(0) turns animation off so drawings appear at once; tracer(1) turns it back on."""
        if n is None:
            return 1 if self._tracer else 0
        self._tracer = bool(n)

    def update(self):
        """update(): show the drawing (drawings always show at once here)."""
        _c.show()

    def delay(self, delay=None):
        """delay(delay=None): accepted for compatibility."""
        return 0

    def setworldcoordinates(self, llx, lly, urx, ury):
        """setworldcoordinates(llx, lly, urx, ury): use your own coordinates: (llx, lly) is the lower left corner and (urx, ury) the upper right."""
        if urx == llx or ury == lly:
            raise ValueError("the world needs a width and a height")
        self._world = (float(llx), float(lly), float(urx), float(ury))
        for t in self._turtles:
            t._sprite()

    def clear(self):
        """clear(): delete all drawings and put every turtle back home."""
        self._init()
        _c.clear()
        for t in self._turtles:
            t._defaults()
            t._sprite()

    clearscreen = clear

    def reset(self):
        """reset(): put every turtle back home and clear the drawing."""
        self.clear()

    resetscreen = reset

    def turtles(self):
        """turtles(): the list of turtles on the screen."""
        return list(self._turtles)

    def listen(self, xdummy=None, ydummy=None):
        """listen(): accepted for compatibility (keys always reach the program while it runs)."""

    def onkey(self, fun, key):
        """onkey(fun, key): call fun() when key is pressed ("Up", "a", "space"); handlers run inside mainloop() or done()."""
        if fun is None:
            self._keys.pop(_keyname(key), None)
        else:
            self._keys[_keyname(key)] = fun

    onkeypress = onkey

    def onkeyrelease(self, fun, key):
        """onkeyrelease(fun, key): accepted for compatibility (key releases are not reported)."""

    def ontimer(self, fun, t=0):
        """ontimer(fun, t=0): call fun() after t milliseconds, inside mainloop() or done()."""
        import time
        self._timers.append((time.monotonic() + t / 1000.0, fun))

    def onclick(self, fun, btn=1, add=None):
        """onclick(fun): accepted for compatibility (mouse clicks are not reported)."""

    onscreenclick = onclick

    def mainloop(self):
        """mainloop(): run key and timer handlers until the program is stopped; returns at once when there are none."""
        import time
        while self._keys or self._timers:
            k = _c.key()
            if k and k in self._keys:
                self._keys[k]()
            now = time.monotonic()
            due = [item for item in self._timers if item[0] <= now]
            if due:
                self._timers = [item for item in self._timers if item[0] > now]
                for item in due:
                    item[1]()
            _rt.wait(0.01)

    done = mainloop

    def exitonclick(self):
        """exitonclick(): accepted for compatibility."""

    def bye(self):
        """bye(): accepted for compatibility."""


_screen = _Screen()


def Screen():
    """Screen(): the shared screen object (bgcolor, setup, tracer, onkey, mainloop and so on)."""
    return _screen


class Turtle:
    """Turtle(): a new turtle in the middle of the canvas, facing east, pen down, drawing in black."""

    def __init__(self, shape="classic", undobuffersize=None, visible=True):
        _screen._init()
        self._visible = bool(visible)
        self._defaults()
        _screen._turtles.append(self)
        self._sprite()

    def _defaults(self):
        self._x = 0.0
        self._y = 0.0
        self._heading = 0.0
        self._down = True
        self._width = 1
        self._pencolor = "black"
        self._fillcolor = "black"
        self._pencode = _screen._code("black")
        self._fillcode = self._pencode
        self._speed = 6
        self._filling = False
        self._fill_points = []
        self._fill_lines = []
        self._full = 360.0
        self._stamps = 0

    # ------------------------------------------------------------ helpers

    def _sprite(self):
        px, py = _screen._point(self._x, self._y)
        _c.sprite(px, py, self._heading, self._pencode, self._visible)

    def _pause(self, distance):
        if self._speed != 0 and _screen._tracer and _c.animate():
            _rt.wait(min(0.25, 0.002 + abs(distance) / (self._speed * 240.0)))

    def _line_to(self, x, y):
        x0, y0 = self._x, self._y
        self._x = float(x)
        self._y = float(y)
        if self._down:
            a = _screen._point(x0, y0)
            b = _screen._point(self._x, self._y)
            w = max(1, self._width * _screen._unit())
            _c.line(a[0], a[1], b[0], b[1], self._pencode, w)
            if self._filling:
                self._fill_lines.append((a, b, self._pencode, w))
        if self._filling:
            self._fill_points.append((self._x, self._y))

    def _move(self, x, y):
        d = math.hypot(x - self._x, y - self._y)
        self._line_to(x, y)
        self._sprite()
        self._pause(d)

    def _rad(self, angle):
        return angle * 2 * math.pi / self._full

    def _color(self, args):
        if len(args) == 1:
            return args[0]
        if len(args) == 3:
            return tuple(args)
        raise TypeError("expected a colour name, '#rrggbb' or r, g, b")

    # ------------------------------------------------------------ moving

    def forward(self, distance):
        """forward(distance): move forward, drawing a line when the pen is down."""
        a = self._rad(self._heading)
        self._move(self._x + distance * math.cos(a), self._y + distance * math.sin(a))

    fd = forward

    def back(self, distance):
        """back(distance): move backward without turning."""
        self.forward(-distance)

    bk = back
    backward = back

    def left(self, angle):
        """left(angle): turn left (counterclockwise) by angle degrees."""
        self._heading = (self._heading + angle) % self._full
        self._sprite()

    lt = left

    def right(self, angle):
        """right(angle): turn right (clockwise) by angle degrees."""
        self._heading = (self._heading - angle) % self._full
        self._sprite()

    rt = right

    def goto(self, x, y=None):
        """goto(x, y): move to a point, drawing when the pen is down; goto((x, y)) works too."""
        if y is None:
            x, y = x
        self._move(float(x), float(y))

    setpos = goto
    setposition = goto

    def teleport(self, x=None, y=None):
        """teleport(x, y): jump to a point without drawing."""
        down = self._down
        self._down = False
        self._move(self._x if x is None else x, self._y if y is None else y)
        self._down = down

    def setx(self, x):
        """setx(x): move to a new x, keeping y."""
        self._move(float(x), self._y)

    def sety(self, y):
        """sety(y): move to a new y, keeping x."""
        self._move(self._x, float(y))

    def setheading(self, to_angle):
        """setheading(angle): face a direction: 0 east, 90 north, 180 west, 270 south."""
        self._heading = float(to_angle) % self._full
        self._sprite()

    seth = setheading

    def home(self):
        """home(): go back to (0, 0) facing east."""
        self._move(0.0, 0.0)
        self._heading = 0.0
        self._sprite()

    def circle(self, radius, extent=None, steps=None):
        """circle(radius, extent=None, steps=None): draw a circle (or an arc of extent degrees) with its centre radius units to the left."""
        if extent is None:
            extent = self._full
        frac = abs(extent) / self._full
        if steps is None:
            steps = 1 + int(min(11 + abs(radius * _screen._unit()) / 6.0, 59.0) * frac)
        steps = max(1, int(steps))
        w = 1.0 * extent / steps
        w2 = 0.5 * w
        length = 2.0 * radius * math.sin(self._rad(w2))
        if radius < 0:
            length, w, w2 = -length, -w, -w2
        self._heading = (self._heading + w2) % self._full
        for _ in range(steps):
            a = self._rad(self._heading)
            self._line_to(self._x + length * math.cos(a), self._y + length * math.sin(a))
            self._heading = (self._heading + w) % self._full
        self._heading = (self._heading - w2) % self._full
        self._sprite()
        self._pause(abs(length) * steps)

    def dot(self, size=None, *color):
        """dot(size=None, color=None): draw a filled dot of diameter size at the turtle."""
        if size is not None and not isinstance(size, (int, float)):
            color = (size,) + color
            size = None
        if size is None:
            size = max(self._width + 4, self._width * 2)
        code = _screen._code(self._color(color)) if color else self._pencode
        px, py = _screen._point(self._x, self._y)
        _c.circle(px, py, size * _screen._unit() / 2.0, code, True)

    def stamp(self):
        """stamp(): print the turtle's arrow shape on the canvas; returns a stamp id."""
        a = self._rad(self._heading)
        pts = []
        for dx, dy in ((10, 0), (-6, 6), (-3, 0), (-6, -6)):
            x = self._x + dx * math.cos(a) - dy * math.sin(a)
            y = self._y + dx * math.sin(a) + dy * math.cos(a)
            px, py = _screen._point(x, y)
            pts.append(px)
            pts.append(py)
        _c.polygon(pts, self._fillcode, True)
        self._stamps += 1
        return self._stamps

    def clearstamp(self, stampid):
        """clearstamp(stampid): accepted for compatibility (stamps become part of the drawing)."""

    def clearstamps(self, n=None):
        """clearstamps(n=None): accepted for compatibility."""

    def write(self, arg, move=False, align="left", font=("Arial", 8, "normal")):
        """write(text, move=False, align="left", font=("Arial", 8, "normal")): write text at the turtle; align is "left", "center" or "right"."""
        text = str(arg)
        size = 8
        if isinstance(font, (tuple, list)) and len(font) > 1 and isinstance(font[1], (int, float)):
            size = font[1]
        scale = 1 if size < 14 else (2 if size < 24 else 3)
        width = _c.text_width(text, scale)
        px, py = _screen._point(self._x, self._y)
        if align == "center":
            px -= width / 2
        elif align == "right":
            px -= width
        _c.text(px, py - 7 * scale, text, self._pencode, scale)
        if move:
            self.teleport(self._x + width / _screen._unit(), self._y)

    # ------------------------------------------------------------ state

    def position(self):
        """position(): the turtle's (x, y)."""
        return (round(self._x, 10), round(self._y, 10))

    pos = position

    def xcor(self):
        """xcor(): the turtle's x."""
        return round(self._x, 10)

    def ycor(self):
        """ycor(): the turtle's y."""
        return round(self._y, 10)

    def heading(self):
        """heading(): the direction the turtle faces in degrees."""
        return round(self._heading, 10)

    def towards(self, x, y=None):
        """towards(x, y): the heading that points at (x, y)."""
        if y is None:
            x, y = x
        a = math.degrees(math.atan2(y - self._y, x - self._x)) % 360.0
        return round(a * self._full / 360.0, 10)

    def distance(self, x, y=None):
        """distance(x, y): the distance from the turtle to (x, y)."""
        if y is None:
            if isinstance(x, Turtle):
                x, y = x._x, x._y
            else:
                x, y = x
        return math.hypot(x - self._x, y - self._y)

    def degrees(self, fullcircle=360.0):
        """degrees(fullcircle=360): measure angles in degrees (or in units with fullcircle per turn)."""
        self._heading = self._heading * fullcircle / self._full
        self._full = float(fullcircle)

    def radians(self):
        """radians(): measure angles in radians."""
        self.degrees(2 * math.pi)

    def speed(self, speed=None):
        """speed(speed=None): 1 (slowest) to 10 (fast), or 0 for no animation; also "slow", "normal", "fast", "fastest"."""
        if speed is None:
            return self._speed
        if isinstance(speed, str):
            speed = _SPEEDS.get(speed, 6)
        speed = int(round(speed))
        self._speed = speed if 0 <= speed <= 10 else 0

    # ------------------------------------------------------------ pen

    def pendown(self):
        """pendown(): draw when moving."""
        self._down = True

    pd = pendown
    down = pendown

    def penup(self):
        """penup(): move without drawing."""
        self._down = False

    pu = penup
    up = penup

    def isdown(self):
        """isdown(): True when the pen is down."""
        return self._down

    def pensize(self, width=None):
        """pensize(width=None): set the line width in pixels; pensize() returns it."""
        if width is None:
            return self._width
        self._width = max(1, width)

    width = pensize

    def pencolor(self, *args):
        """pencolor(color): set the line colour; pencolor() returns it."""
        if not args:
            return self._pencolor
        spec = self._color(args)
        self._pencode = _screen._code(spec)
        self._pencolor = spec
        _c.set_color(self._pencode)
        self._sprite()

    def fillcolor(self, *args):
        """fillcolor(color): set the fill colour used by begin_fill and end_fill; fillcolor() returns it."""
        if not args:
            return self._fillcolor
        spec = self._color(args)
        self._fillcode = _screen._code(spec)
        self._fillcolor = spec

    def color(self, *args):
        """color(pen, fill=None): set the pen and fill colours (one colour sets both); color() returns both."""
        if not args:
            return (self._pencolor, self._fillcolor)
        if len(args) == 2:
            self.pencolor(args[0])
            self.fillcolor(args[1])
        else:
            spec = self._color(args)
            self.pencolor(spec)
            self.fillcolor(spec)

    def filling(self):
        """filling(): True between begin_fill() and end_fill()."""
        return self._filling

    def begin_fill(self):
        """begin_fill(): start a shape to fill; draw it, then call end_fill()."""
        self._filling = True
        self._fill_points = [(self._x, self._y)]
        self._fill_lines = []

    def end_fill(self):
        """end_fill(): fill the shape drawn since begin_fill() with the fill colour."""
        if self._filling and len(self._fill_points) > 2:
            pts = []
            for x, y in self._fill_points:
                px, py = _screen._point(x, y)
                pts.append(px)
                pts.append(py)
            _c.polygon(pts, self._fillcode, True)
            for a, b, code, w in self._fill_lines:
                _c.line(a[0], a[1], b[0], b[1], code, w)
        self._filling = False
        self._fill_points = []
        self._fill_lines = []

    def showturtle(self):
        """showturtle(): show the turtle cursor."""
        self._visible = True
        self._sprite()

    st = showturtle

    def hideturtle(self):
        """hideturtle(): hide the turtle cursor (drawing is a little faster too)."""
        self._visible = False
        self._sprite()

    ht = hideturtle

    def isvisible(self):
        """isvisible(): True when the turtle cursor is shown."""
        return self._visible

    def shape(self, name=None):
        """shape(name=None): accepted for compatibility; the turtle is always an arrow."""
        return "classic"

    def shapesize(self, stretch_wid=None, stretch_len=None, outline=None):
        """shapesize(): accepted for compatibility."""
        return (1, 1, 1)

    turtlesize = shapesize

    def clear(self):
        """clear(): delete the drawing (all of it) without moving the turtle."""
        _c.clear()

    def reset(self):
        """reset(): delete the drawing and put the turtle back home with default settings."""
        _c.clear()
        self._defaults()
        self._sprite()

    def getscreen(self):
        """getscreen(): the screen the turtle draws on."""
        return _screen

    def done(self):
        """done(): run key and timer handlers until stopped (returns at once when there are none)."""
        _screen.mainloop()

    def show(self):
        """show(): show the drawing (for the TI turtle)."""
        _c.show()


Pen = Turtle
RawTurtle = Turtle

_pen = None


def _getpen():
    global _pen
    if _pen is None:
        _pen = Turtle()
    return _pen


def _method(name):
    doc = getattr(Turtle, name).__doc__

    def call(*args, **kwargs):
        return getattr(_getpen(), name)(*args, **kwargs)

    call.__name__ = name
    call.__doc__ = doc
    return call


def _screen_method(name):
    doc = getattr(_Screen, name).__doc__

    def call(*args, **kwargs):
        return getattr(_screen, name)(*args, **kwargs)

    call.__name__ = name
    call.__doc__ = doc
    return call


forward = _method("forward")
fd = _method("fd")
back = _method("back")
bk = _method("bk")
backward = _method("backward")
left = _method("left")
lt = _method("lt")
right = _method("right")
rt = _method("rt")
goto = _method("goto")
setpos = _method("setpos")
setposition = _method("setposition")
teleport = _method("teleport")
setx = _method("setx")
sety = _method("sety")
setheading = _method("setheading")
seth = _method("seth")
home = _method("home")
circle = _method("circle")
dot = _method("dot")
stamp = _method("stamp")
clearstamp = _method("clearstamp")
clearstamps = _method("clearstamps")
write = _method("write")
position = _method("position")
pos = _method("pos")
xcor = _method("xcor")
ycor = _method("ycor")
heading = _method("heading")
towards = _method("towards")
distance = _method("distance")
degrees = _method("degrees")
radians = _method("radians")
speed = _method("speed")
pendown = _method("pendown")
pd = _method("pd")
down = _method("down")
penup = _method("penup")
pu = _method("pu")
up = _method("up")
isdown = _method("isdown")
pensize = _method("pensize")
width = _method("width")
pencolor = _method("pencolor")
fillcolor = _method("fillcolor")
color = _method("color")
filling = _method("filling")
begin_fill = _method("begin_fill")
end_fill = _method("end_fill")
showturtle = _method("showturtle")
st = _method("st")
hideturtle = _method("hideturtle")
ht = _method("ht")
isvisible = _method("isvisible")
shape = _method("shape")
shapesize = _method("shapesize")
turtlesize = _method("turtlesize")
clear = _method("clear")
reset = _method("reset")
getscreen = _method("getscreen")
show = _method("show")

setup = _screen_method("setup")
screensize = _screen_method("screensize")
window_width = _screen_method("window_width")
window_height = _screen_method("window_height")
bgcolor = _screen_method("bgcolor")
title = _screen_method("title")
colormode = _screen_method("colormode")
tracer = _screen_method("tracer")
update = _screen_method("update")
delay = _screen_method("delay")
setworldcoordinates = _screen_method("setworldcoordinates")
clearscreen = _screen_method("clearscreen")
resetscreen = _screen_method("resetscreen")
turtles = _screen_method("turtles")
listen = _screen_method("listen")
onkey = _screen_method("onkey")
onkeypress = _screen_method("onkeypress")
onkeyrelease = _screen_method("onkeyrelease")
ontimer = _screen_method("ontimer")
onclick = _screen_method("onclick")
onscreenclick = _screen_method("onscreenclick")
mainloop = _screen_method("mainloop")
done = _screen_method("done")
exitonclick = _screen_method("exitonclick")
bye = _screen_method("bye")
