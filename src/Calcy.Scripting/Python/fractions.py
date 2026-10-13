"""Exact rational numbers, like the standard fractions module.

Fraction(3, 4), Fraction("3/4"), Fraction("0.75") and Fraction(0.75) are all three quarters. Arithmetic with
int and Fraction stays exact; mixing in a float gives a float. The calc module returns exact non-integer results
as Fractions.
"""

import math


def _parse(text):
    t = text.strip().replace("_", "")
    if "/" in t:
        a, b = t.split("/", 1)
        return int(a.strip()), int(b.strip())
    m = t.lower()
    exp = 0
    if "e" in m:
        m, e = m.split("e", 1)
        exp = int(e)
    sign = 1
    if m.startswith("-"):
        sign = -1
        m = m[1:]
    elif m.startswith("+"):
        m = m[1:]
    if "." in m:
        whole, frac = m.split(".", 1)
    else:
        whole, frac = m, ""
    digits = whole + frac
    if digits == "" or not digits.isdigit():
        raise ValueError("Invalid literal for Fraction: " + repr(text))
    n = int(digits)
    d = 10 ** len(frac)
    if exp > 0:
        n = n * 10 ** exp
    elif exp < 0:
        d = d * 10 ** (-exp)
    return sign * n, d


def _parts(value):
    if isinstance(value, Fraction):
        return value._n, value._d
    if isinstance(value, int):
        return int(value), 1
    if isinstance(value, float):
        if value != value or value in (float("inf"), float("-inf")):
            raise ValueError("cannot convert " + repr(value) + " to Fraction")
        return value.as_integer_ratio()
    if isinstance(value, str):
        return _parse(value)
    if hasattr(value, "_calc_text"):
        return _parse(value._calc_text())
    raise TypeError("argument should be a string or a number, not " + type(value).__name__)


class Fraction:
    """Fraction(numerator=0, denominator=1): an exact rational number. Also accepts one float, decimal string or "a/b" string."""

    __slots__ = ("_n", "_d")

    def __init__(self, numerator=0, denominator=None):
        if denominator is None:
            n, d = _parts(numerator)
        elif isinstance(numerator, int) and isinstance(denominator, int):
            n, d = numerator, denominator
        else:
            a, b = _parts(numerator)
            c, e = _parts(denominator)
            n, d = a * e, b * c
        if d == 0:
            raise ZeroDivisionError("Fraction(%s, 0)" % n)
        if d < 0:
            n, d = -n, -d
        g = math.gcd(n, d)
        if g > 1:
            n //= g
            d //= g
        self._n = n
        self._d = d

    @property
    def numerator(self):
        """numerator: the numerator in lowest terms (carries the sign)."""
        return self._n

    @property
    def denominator(self):
        """denominator: the positive denominator in lowest terms."""
        return self._d

    def as_integer_ratio(self):
        """as_integer_ratio(): the pair (numerator, denominator)."""
        return (self._n, self._d)

    def is_integer(self):
        """is_integer(): True when the denominator is 1."""
        return self._d == 1

    def limit_denominator(self, max_denominator=1000000):
        """limit_denominator(max_denominator=1000000): the closest Fraction with a denominator at most max_denominator."""
        if max_denominator < 1:
            raise ValueError("max_denominator should be at least 1")
        if self._d <= max_denominator:
            return Fraction(self._n, self._d)
        p0, q0, p1, q1 = 0, 1, 1, 0
        n, d = self._n, self._d
        while True:
            a = n // d
            q2 = q0 + a * q1
            if q2 > max_denominator:
                break
            p0, q0, p1, q1 = p1, q1, p0 + a * p1, q2
            n, d = d, n - a * d
        k = (max_denominator - q0) // q1
        bound1 = Fraction(p0 + k * p1, q0 + k * q1)
        bound2 = Fraction(p1, q1)
        if abs(bound2 - self) <= abs(bound1 - self):
            return bound2
        return bound1

    @classmethod
    def from_float(cls, value):
        """from_float(value): the exact value of a float as a Fraction."""
        return cls(value)

    def _calc_text(self):
        if self._d == 1:
            return str(self._n)
        return "(" + str(self._n) + "/" + str(self._d) + ")"

    def __repr__(self):
        if self._d == 1:
            return str(self._n)
        return str(self._n) + "/" + str(self._d)

    def __str__(self):
        return self.__repr__()

    def __format__(self, spec):
        if spec == "":
            return self.__repr__()
        return format(float(self), spec)

    def __float__(self):
        return self._n / self._d

    def __int__(self):
        if self._n < 0:
            return -((-self._n) // self._d)
        return self._n // self._d

    def __trunc__(self):
        return self.__int__()

    def __floor__(self):
        return self._n // self._d

    def __ceil__(self):
        return -((-self._n) // self._d)

    def __round__(self, ndigits=None):
        if ndigits is None:
            q, r = divmod(self._n, self._d)
            if 2 * r < self._d:
                return q
            if 2 * r > self._d:
                return q + 1
            return q if q % 2 == 0 else q + 1
        shift = 10 ** abs(ndigits)
        if ndigits > 0:
            return Fraction(round(self * shift), shift)
        return Fraction(round(self / shift) * shift)

    def __bool__(self):
        return self._n != 0

    def __hash__(self):
        if self._d == 1:
            return hash(self._n)
        return hash((self._n, self._d))

    def __neg__(self):
        return Fraction(-self._n, self._d)

    def __pos__(self):
        return self

    def __abs__(self):
        return Fraction(abs(self._n), self._d)

    def __add__(self, other):
        if isinstance(other, Fraction):
            return Fraction(self._n * other._d + other._n * self._d, self._d * other._d)
        if isinstance(other, int):
            return Fraction(self._n + other * self._d, self._d)
        if isinstance(other, (float, complex)):
            return float(self) + other
        return NotImplemented

    def __radd__(self, other):
        if isinstance(other, int):
            return Fraction(self._n + other * self._d, self._d)
        if isinstance(other, (float, complex)):
            return other + float(self)
        return NotImplemented

    def __sub__(self, other):
        if isinstance(other, Fraction):
            return Fraction(self._n * other._d - other._n * self._d, self._d * other._d)
        if isinstance(other, int):
            return Fraction(self._n - other * self._d, self._d)
        if isinstance(other, (float, complex)):
            return float(self) - other
        return NotImplemented

    def __rsub__(self, other):
        if isinstance(other, int):
            return Fraction(other * self._d - self._n, self._d)
        if isinstance(other, (float, complex)):
            return other - float(self)
        return NotImplemented

    def __mul__(self, other):
        if isinstance(other, Fraction):
            return Fraction(self._n * other._n, self._d * other._d)
        if isinstance(other, int):
            return Fraction(self._n * other, self._d)
        if isinstance(other, (float, complex)):
            return float(self) * other
        return NotImplemented

    def __rmul__(self, other):
        if isinstance(other, int):
            return Fraction(self._n * other, self._d)
        if isinstance(other, (float, complex)):
            return other * float(self)
        return NotImplemented

    def __truediv__(self, other):
        if isinstance(other, Fraction):
            return Fraction(self._n * other._d, self._d * other._n)
        if isinstance(other, int):
            return Fraction(self._n, self._d * other)
        if isinstance(other, (float, complex)):
            return float(self) / other
        return NotImplemented

    def __rtruediv__(self, other):
        if isinstance(other, int):
            return Fraction(other * self._d, self._n)
        if isinstance(other, (float, complex)):
            return other / float(self)
        return NotImplemented

    def __floordiv__(self, other):
        q = self / other
        if isinstance(q, Fraction):
            return q.__floor__()
        return q // 1

    def __rfloordiv__(self, other):
        q = other / self
        if isinstance(q, Fraction):
            return q.__floor__()
        return q // 1

    def __mod__(self, other):
        return self - other * (self // other)

    def __rmod__(self, other):
        return other - self * (other // self)

    def __pow__(self, other):
        if isinstance(other, Fraction) and other._d == 1:
            other = other._n
        if isinstance(other, int):
            if other >= 0:
                return Fraction(self._n ** other, self._d ** other)
            if self._n == 0:
                raise ZeroDivisionError("Fraction(%s, 0)" % (self._d ** -other))
            return Fraction(self._d ** -other, self._n ** -other)
        return float(self) ** float(other) if not isinstance(other, complex) else float(self) ** other

    def __rpow__(self, other):
        if self._d == 1:
            return other ** self._n
        return other ** float(self)

    def _cmp(self, other):
        if isinstance(other, Fraction):
            return self._n * other._d - other._n * self._d
        if isinstance(other, int):
            return self._n - other * self._d
        if isinstance(other, float):
            f = float(self)
            return -1 if f < other else (1 if f > other else 0)
        return None

    def __eq__(self, other):
        if isinstance(other, (Fraction, int)):
            return self._cmp(other) == 0
        if isinstance(other, float):
            return float(self) == other
        if isinstance(other, complex):
            return other.imag == 0 and float(self) == other.real
        return NotImplemented

    def __ne__(self, other):
        r = self.__eq__(other)
        if r is NotImplemented:
            return r
        return not r

    def __lt__(self, other):
        c = self._cmp(other)
        return NotImplemented if c is None else c < 0

    def __le__(self, other):
        c = self._cmp(other)
        return NotImplemented if c is None else c <= 0

    def __gt__(self, other):
        c = self._cmp(other)
        return NotImplemented if c is None else c > 0

    def __ge__(self, other):
        c = self._cmp(other)
        return NotImplemented if c is None else c >= 0
