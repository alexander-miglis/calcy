# Calcy: Feature Specification

Draft 0.2, 13 October 2026.

## Design decisions (agreed 13 October 2026)

These override anything else in this document where they conflict.

| # | Topic | Decision |
|---|---|---|
| D1 | Shell | Modes are picked from a **dropdown**. Every mode has its **own fixed layout** (no dockable panels). |
| D2 | Visual style | **8-bit inspired** look. |
| D3 | Screens | **Every mode has its own screen.** |
| D4 | Typing maths | **Input helpers** put you in the right place: templates (fraction, root, power, integral, matrix...) insert slots you click into and type in. A **symbol map** lets you pick any math symbol. |
| D5 | Results | **Every calculation gets its own card** in a scrollable list, so you can go back and see (and reuse) earlier work. |
| D6 | Decimal results | A setting. **Default: Casio style**, so `0.1 + 0.2` shows `3/10` (with the decimal available). |
| D7 | Implicit multiplication | `ax` means `a·x`. `1/2x` is a setting; default is the Casio reading `1/(2x)`, and the input preview always draws the fraction so it is unambiguous. |
| D8 | `log` | Calculator convention: `log` is base 10, `ln` is natural log, `log(x, b)` is base b. |
| D9 | Definitions | `a = 5` defines a variable, `f(x) = x^2` defines a function. |
| D10 | Name | **Calcy**. |
| D11 | Code mode | A **sandboxed Python subset interpreter written in C#** (no CPython, no file, network or OS access). |
| D14 | Formula collection | A **Formulas** mode with a documented, searchable library (maths, physics, chemistry, finance, statistics): explanation, variables with units, when to use, assumptions, worked example; solve for any variable, send to calculator or graph. |
| D15 | Platforms | Windows, macOS and Linux from one codebase. No OS-specific APIs. |
| D12 | Implementation | **C# native**: no third-party math libraries; all maths implemented in the project (only the .NET runtime's System.Math and BigInteger are used). |
| D13 | Colour schemes | The user picks a **colour scheme** from a Colours button in the top bar (and in Settings); it applies to every screen. Eight 8-bit inspired schemes: NES Classic (default), Pico Night, Game Boy, C64 Blue, Amber CRT, Paper, High contrast, Synthwave. All colours are scheme tokens (29 per scheme), never hard-coded. |

Target: a cross-platform desktop app (Windows, macOS, Linux) built with Avalonia and .NET, that covers everything the leading TI and Casio graphing calculators do, then goes much further: a real CAS, real Python, a reactive document model, GPU graphing, and an MCP interface so that AI agents and scripts can use and drive it as a precise math engine.

---

## 0. How to read this document

Every feature has an ID (for example `G2-014`) so it can become a backlog item directly.

**Origin** says where the idea comes from:

| Code | Meaning |
|---|---|
| `TI84` | TI-84 Plus CE / CE-T Python Edition |
| `NSP` | TI-Nspire CX II / CX II CAS |
| `CG` | Casio fx-CG50 / fx-CG100 (and fx-9750GIII / fx-9860GIII) |
| `CP` | Casio ClassPad II (fx-CP400) / ClassPad.net |
| `WIN` | Windows Calculator (reference for Simple, Programmer and converter modes only) |
| `NEW` | Not on either platform. This is where we go "way better". |
| `+` suffix | Parity feature that we substantially improve (for example `TI84+`) |

**P** is priority: `P0` = MVP, `P1` = version 1.0, `P2` = later.

---

## 1. Vision and design principles

**One line:** everything a TI-Nspire CX II CAS and a Casio fx-CG100 / ClassPad can do, on a big screen, at desktop speed, with real Python, a real CAS, and an MCP interface for AI agents.

1. **Keyboard-first, mouse-rich, pen-capable.** Every action is reachable from the keyboard and from the command palette.
2. **Live and reactive.** Change `a` and every graph, table, cell and result that depends on it updates. No "re-run" button, no stale state.
3. **Exact by default, numeric on demand.** Show `√2/2`; one keystroke gives `0.70710678…`.
4. **One document, many views.** The same variable is visible in the graph, table, spreadsheet, code and notes (Nspire's linked apps idea, made universal).
5. **Everything is addressable.** Every object (expression, graph, cell, dataset) has a stable ID usable from code, from MCP and from links.
6. **Honest numerics.** Results carry precision information. The app warns about cancellation, ill-conditioned matrices and plots that might be missing features.
7. **Local-first and private.** No account, no telemetry by default, fully offline. AI and network features are opt-in.
8. **Familiar on day one.** Optional TI and Casio keypad skins and key-sequence compatibility so students' muscle memory transfers.

---

## 2. Parity snapshot

Summary of the reference devices (details and sources in Appendix A) against this app.

| Area | TI-84 Plus CE Python | TI-Nspire CX II CAS | Casio fx-CG50 / fx-CG100 | Casio ClassPad II | **This app** |
|---|---|---|---|---|---|
| Screen | 320x240, 2.8" | 320x240, 3.2" | about 384x216 color | Touch, stylus | Any window size, HiDPI, multi-monitor |
| Memory | about 149 KB RAM, 3 MB archive | 64 MB RAM, 90+ MB storage | about 61 KB main memory | Not reviewed | Limited only by the computer |
| CAS | No | Yes, can be disabled | No | Yes | Yes, switchable per document and per exam profile |
| 2D graph types | Function, parametric, polar, sequence; inequality and conic apps | Adds implicit, conic, inequality, differential equation | Y=, r=, Param, X=, inequalities, conics, recursion | Adds DE Graph with slope fields and phase planes | All of these plus vector fields, contours, complex plots, guaranteed implicit rendering |
| Graph analysis | value, zero, min, max, intersect, dy/dx, ∫ | Similar, plus CAS commands such as tangentLine and arcLen | G-Solve: root, max, min, y-intercept, intersection, Y-CAL, X-CAL, 4 kinds of ∫ | Adds inflection, distance, volume of revolution | All of these, automatic points of interest, exact values |
| Dynamic graphs | Transformation Graphing app (A to D) | Sliders, animation, grab-and-move | Modify, Dyna Graph | Direct and Dynamic Modify | Auto sliders, drag curves, animation export |
| 3D | No | z(x,y) and parametric surfaces | Templates, Z=, parametric, up to 3 expressions | z=f(x,y) or parametric, 1 at a time | Unlimited surfaces, implicit surfaces, vector fields, GPU |
| Geometry | Cabri Jr app | Geometry app | Geometry app | Geometry with constraints | Dynamic geometry linked both ways to algebra |
| Spreadsheet | CellSheet app, 999x26 | Lists & Spreadsheet | 26x999 | Spreadsheet | Virtualized grid, 1M+ rows, XLSX |
| Statistics | 12 regressions, 10 tests, 7 intervals, ANOVA | Adds MultReg, two-way ANOVA | 12 regressions, tests including two-way ANOVA | 11 regressions, tests | All of these plus nonparametric tests, bootstrap, custom nonlinear fits |
| Programming | TI-Basic; Python (CircuitPython, 50 KB) | TI-Basic, Lua, Python (MicroPython) | Casio Basic (dropped on CG100); MicroPython 1.9.4 | ClassPad Basic with CAS; no Python | Native language, full CPython 3, C# later, Basic import |
| Linking between apps | Minimal | Variables shared within a Problem | Limited | Drag and drop between apps | Every view reactive; cross-document links |
| Notes / documents | No | Notes with math boxes | eActivity (dropped on CG100) | eActivity | Reactive notebooks with code cells |
| Data collection | TI-Innovator Hub, Vernier EasyData | Vernier DataQuest | E-CON4 / E-CON5 | E-CON3 | Later (P2), through serial, USB and BLE |
| Exam mode | Press-to-Test | Press-to-Test, 17 options | Regional modes (IB, UK, NL, Texas, Portugal) | Not reviewed | Restriction profiles on top of any mode |
| Modes | Home screen plus APPS | Apps inside documents | Main menu apps | Apps with drag and drop | 13 built-in modes plus custom modes, all sharing one state |
| Desktop software | TI Connect CE, TI-SmartView CE | Premium Teacher Software | fx-CG Manager PLUS (subscription); CG100 web emulator only | ClassPad Manager (subscription) | It is the desktop app, on every OS |
| AI and MCP | None | None | None | None | MCP server and client, optional assistant |

---

## 3. Workspace, documents and UI (`WS`)

| ID | Feature | Origin | P |
|---|---|---|---|
| WS-001 | **Document model:** a file contains pages; each page is either a free layout of panels or a vertical notebook. Variables are scoped per document, optionally per page group (like Nspire "problems"). | NSP+ | P0 |
| WS-002 | **Dockable panels:** split, tab, float, pop out to a second monitor. Layout presets: "Classic" (expression list + graph + table), "Quad" (Nspire 2x2), "Notebook", "Presentation". | NEW | P0 |
| WS-003 | **Panel types:** Expressions, Graph 2D, Graph 3D, Table, Spreadsheet, Statistics, Geometry, Notes, Code editor, Console/REPL, Variables inspector, History, Units and constants, Catalog/Help, Keypad, Assistant, Connections (MCP). | NEW | P0/P1 |
| WS-004 | **Command palette** (Ctrl/Cmd+K): fuzzy search over commands, functions, settings, variables and open documents. | NEW | P0 |
| WS-005 | **Function catalog:** searchable, with signatures, examples, "insert" and "try it". Replaces TI CATALOG and Casio CATALOG with real documentation. | TI84+ CG+ | P0 |
| WS-006 | **History tape:** every evaluation with input, output and time; click to reuse, edit and re-run, pin, star. `ans`, `ans(3)`, recall last entry (TI 2nd ENTRY). | TI84+ CG+ | P0 |
| WS-007 | **Undo/redo** across all panels as one timeline (no TI/Casio device has real undo); per-document version history with diff and restore. | NEW | P0 undo, P1 history |
| WS-008 | **Autosave, crash recovery and session restore** (open documents, layout, unsaved scratch work). | NEW | P0 |
| WS-009 | **Mode bar:** per-document modes always visible and one click to change: angle (rad/deg/grad), exact/approx/auto, float/fix/sci/eng digits, complex format (real, a+bi, r∠θ), number base, coordinate display. Mismatch hints (for example trig in degree mode with π in the argument). | TI84+ CG+ | P0 |
| WS-010 | **Virtual keypad panel** with TI-84, Nspire, Casio fx-CG and "modern" layouts, clickable and mapped to the physical keyboard. Optional key-sequence compatibility (typing `2nd` then `x²` gives `√`). | NEW | P1 |
| WS-011 | **Themes:** light, dark, high contrast, "classic LCD"; UI scale 75% to 300%; HiDPI everywhere. | NEW | P0 |
| WS-012 | **Presentation mode:** large fonts, pointer highlight, page stepping, hidden chrome, projector mirroring, on-screen **key and command history** so the class can follow every step (TI-SmartView Key Press History and View3, Casio Key-Log, built in). | TI84+ NSP+ CG+ | P1 |
| WS-013 | Multiple documents in tabs, multiple windows, drag objects between documents. | NEW | P1 |
| WS-014 | Templates and starter documents: blank, algebra, calculus, statistics lab, physics lab, finance. | NEW | P1 |
| WS-015 | Search inside documents (expressions, notes, code, data). | NEW | P1 |
| WS-016 | **Context menu on every object** with relevant actions: plot, tabulate, differentiate, solve for, convert, copy as LaTeX, send to spreadsheet. | NEW | P0 |
| WS-017 | **Quick-calc popup** on a global hotkey from the system tray, even when the app is in the background (the desktop take on the Nspire Scratchpad). | NSP+ | P1 |
| WS-018 | **Cross-document links:** reference a variable or dataset from another document, which the Nspire cannot do. | NEW | P2 |
| WS-019 | **Drag and drop between views:** drop an expression on a graph to plot it, an equation on the geometry canvas to draw it, a figure on the CAS to get its equation, a column on a plot to chart it; paste a function from the clipboard to graph it. | CP+ CG+ | P0 |

---

## 4. Calculator modes (`MO`)

Modes are **not separate apps**. Every mode runs on the same engine and the same document, so variables, history and `ans` carry over when you switch: a value worked out in Programmer can be plotted in Graphing a second later. A mode is a bundle of layout (which panels are shown), keypad, visible function set, default settings and window size. This gives progressive disclosure: Simple stays simple, Graphing is a full workstation.

This is the desktop version of the Casio main menu and the TI APPS list, without their silos.

### 4.1 Built-in modes

| Mode | For | What it shows | Reference |
|---|---|---|---|
| **Simple** | Everyday arithmetic | Big display and keys, + − × ÷, %, ±, √, x², 1/x, memory keys, editable running tape. Compact, optional always-on-top window. | `WIN`, desk calculators |
| **Advanced** (scientific) | Students, engineers | Natural display, trig/log/powers, fractions, DMS, complex numbers, constants, `ans` and history, equation and polynomial solvers, small matrices, one-variable stats, exact/decimal toggle. | Casio fx-991 class, TI-30X class, CG Run-Matrix, TI-84 home screen |
| **Programmer** | Developers, embedded, CS students | Hex/dec/oct/bin side by side, word size and signedness, clickable bit grid, bitwise operators, float bit inspector (details in 4.3). | `WIN` Programmer, CG Base-N (its own app on the fx-CG100) |
| **Graphing** | Functions and analysis | Expression list + 2D graph + linked table, analysis tools, sliders (section 7). | TI84 Y=, CG Graph |
| **CAS** | Symbolic math | Symbolic history workspace: solve, factor, calculus, exact results (section 6.3). | NSP Calculator, CP Main |
| **Statistics** | Data and inference | List editor, stat plots, regressions, tests, distributions (section 10). | TI84 STAT, CG Statistics |
| **Matrix** | Linear algebra | Matrix editor, operations, decompositions, transformation visualizer (section 6.6). | TI84 MATRIX, CG Run-Matrix |
| **3D** | Multivariable calculus | 3D graph view with its own expression list (section 8). | NSP 3D, CG 3D Graph |
| **Geometry** | Constructions | Geometry canvas plus linked algebra view (section 9). | NSP Geometry, CG Geometry, Cabri Jr |
| **Finance** | Business, personal finance | TVM solver, amortization, cash flows, interest, depreciation, bonds (section 6.9). | TI Finance, CG Financial |
| **Convert** | Quick conversions | Units, date difference and date add/subtract, time zones; currency only through an MCP data connector. | `WIN` converters, CG CONVERT |
| **Notebook** | Reports, lessons, lab write-ups | Text, math, graph and code cells in one reactive document (section 12). | NSP documents, CG eActivity |
| **Code** | Programming | Editor, console, drawing canvas, variables inspector (section 13). | TI Python, CG Python, CG Program |

Exam is **not a mode**: it is a restriction profile that sits on top of any mode (section 15).

### 4.2 Mode behavior

| ID | Feature | Origin | P |
|---|---|---|---|
| MO-001 | **Mode switcher:** sidebar or top segmented control, Ctrl/Cmd+1 to 9, command palette, CLI flag (`--mode programmer`), and an MCP `set_mode` tool. | NEW | P0 |
| MO-002 | **Shared state:** switching modes keeps variables, history, `ans` and the open document. | NEW | P0 |
| MO-003 | Each mode remembers its own window size and position, layout, keypad and defaults (for example Programmer starts in hex, 32-bit, signed). | NEW | P0 |
| MO-004 | **Progressive disclosure:** using an advanced function in Simple offers "Open in Advanced?" instead of an error. | NEW | P0 |
| MO-005 | **Mode-aware keypads** and keyboard mapping (Programmer gets A to F keys and bit shortcuts; Simple maps the numpad). | WIN+ | P0 |
| MO-006 | Command palette and catalog prioritize the current mode's functions but can always search everything. | NEW | P0 |
| MO-007 | **Compact mini window** for Simple, Advanced and Programmer, optionally always on top. | WIN | P1 |
| MO-008 | **Custom modes:** pick panels, keypad, function palette and defaults; save and share as a file. A teacher can publish a "Year 9 calculator" mode. | NEW | P1 |
| MO-009 | **Mode restrictions:** exam profiles and school policies can limit which modes are available (for example Simple and Advanced only). | TI84+ CG+ | P1 |
| MO-010 | **Mixed-mode documents:** one document can hold pages in different modes (a Graphing page, a Statistics page, a Code page), Nspire style. | NSP+ | P1 |
| MO-011 | MCP clients can read and change the current mode; all engine tools work regardless of the visible mode. | NEW | P1 |

### 4.3 Simple and Programmer mode details

Simple and Programmer are the modes TI and Casio graphing calculators handle worst (or not at all), so they get their own detail list. Programmer mode is the UI for engine features CE-070 and CE-071.

| ID | Feature | Origin | P |
|---|---|---|---|
| MO-020 | Simple: desk-calculator percent semantics (`200 + 10%` = 220, markup and discount), configurable. | WIN | P0 |
| MO-021 | Simple: memory keys (MC, MR, M+, M−, MS), grand total, multiple named memories. | WIN+ | P0 |
| MO-022 | Simple: editable tape; change an earlier line and everything below recalculates (like the fx-CG100 Calculate history); copy or export the tape. | CG+ | P0 |
| MO-023 | Simple: rounding selector (fixed decimals, cut, up, half-up, half-even) and optional tax keys. | NEW | P1 |
| MO-030 | Programmer: hex, dec, oct and bin shown at the same time with digit grouping; click one to make it the input base. | WIN+ CG+ | P0 |
| MO-031 | Programmer: word size 8, 16, 32, 64, 128 or any bit width; signed (two's complement) or unsigned; overflow mode wrap, saturate or flag. | WIN+ | P0 |
| MO-032 | Programmer: clickable bit grid with bit indices; select a range to extract or insert a bit field. | WIN+ | P0 |
| MO-033 | Programmer: and, or, xor, not, nand, nor; logical and arithmetic shifts; rotate and rotate through carry. | WIN CG | P0 |
| MO-034 | Programmer: C-style input (`0xFF & (x << 3)`, `~`, `^`, `0b1010`, `0o17`). | NEW | P0 |
| MO-035 | Programmer: popcount, leading and trailing zeros, bit reverse, byte swap and endianness view, masks, sign extension. | NEW | P1 |
| MO-036 | Programmer: IEEE 754 inspector for half, single, double and quad (sign, exponent, mantissa bits), ULP, next and previous float, NaN and infinity payloads. | NEW | P1 |
| MO-037 | Programmer: characters and encodings (code point, UTF-8 and UTF-16 bytes, ASCII table). | NEW | P1 |
| MO-038 | Programmer: fixed-point Qm.n conversion; checksums (CRC-32, Adler-32). | NEW | P2 |

---

## 5. Math input and display (`IN`)

| ID | Feature | Origin | P |
|---|---|---|---|
| IN-001 | **2D WYSIWYG math editor** (TI MathPrint / Casio Natural Textbook Display): fractions, mixed numbers, roots, exponents, subscripts, absolute value, log base, Σ, Π, ∫, d/dx and nth derivative, limits, matrices and vectors, piecewise, systems of equations, DMS. | TI84+ CG+ | P0 |
| IN-002 | **Linear typing that auto-formats:** `/` makes a fraction, `^` an exponent, `sqrt` gives √, `int` gives ∫, `sum` gives Σ, `pi` gives π, `theta` gives θ, `<=` gives ≤, `_` gives a subscript. | NEW | P0 |
| IN-003 | **Autocomplete** for functions, variables and units, with signature help and inline docs. | NEW | P0 |
| IN-004 | **Paste and import** LaTeX, MathML, AsciiMath, Unicode math and plain text. **Copy as** LaTeX, MathML, plain text, image, Python/SymPy, C#. | NEW | P0 LaTeX/text, P1 rest |
| IN-005 | **Real variable names** (`velocity`, `v_0`, `ΔT`, Greek letters), not just single letters A to Z and θ. | NEW | P0 |
| IN-006 | **Implicit multiplication** with clear, configurable rules (`2x`, `xy`, `sin 2x`) and a faint hint that shows how the input was parsed. | NEW | P0 |
| IN-007 | **Structural editing:** select subexpressions with mouse or Shift+arrows, wrap in a function, drag terms, evaluate only the selection (the ClassPad Interactive menu works on a selection). | CP+ | P1 |
| IN-008 | **Useful errors:** the error points at the exact location, explains it in plain language and suggests a fix (instead of `ERR:SYNTAX` or `Syntax ERROR`). | NEW | P0 |
| IN-009 | **Result formats:** exact, decimal, auto; improper and mixed fractions; scientific, engineering, SI prefixes; fix N and significant figures; repeating decimals with overline; digit grouping; DMS; percent; rectangular/polar complex; bin/oct/dec/hex. One key toggles exact and decimal (Casio S⇔D, TI ►Frac / ►Dec). | TI84+ CG+ | P0 |
| IN-010 | **Result actions:** copy in a format, convert units, show more digits, continued fraction, plot, factor, show steps. | NEW | P1 |
| IN-011 | **Handwriting input** with pen or touch, using OS ink recognition where available (ClassPad stylus heritage). | CP+ | P2 |
| IN-012 | **Clear semantics:** `:=` definition, `=` equation, `==` comparison, each with its own visual style. | NEW | P0 |
| IN-013 | Math speech and braille output for every expression (see `AX`). | NEW | P1 |

---

## 6. Computation engine (`CE`)

### 6.1 Numbers and precision

| ID | Feature | Origin | P |
|---|---|---|---|
| CE-001 | Arbitrary-precision integers and exact rationals by default. | NSP+ CP+ | P0 |
| CE-002 | Configurable-precision big floats (default 34 significant digits, up to thousands), with a fast double-precision path for plotting. | NEW | P0 |
| CE-003 | Exact symbolic constants (π, e, i, φ, ∞) and exact radicals. | NSP CP | P0 basic, P1 full CAS |
| CE-004 | Complex numbers everywhere: a+bi, r∠θ, re^(iθ), conj, arg, abs, Re, Im; branch cuts documented. | TI84 CG | P0 |
| CE-005 | **Uncertainty propagation** (`9.81 ± 0.02`) and interval arithmetic mode for lab work. | NEW | P1 |
| CE-006 | **Units on any value** (`3 m/s * 2 h`), dimensional checking, conversion to SI or a preferred system. | NSP+ CP+ | P1 |
| CE-007 | Precision diagnostics: estimated correct digits, warnings on catastrophic cancellation. | NEW | P2 |

### 6.2 Function library

| ID | Feature | Origin | P |
|---|---|---|---|
| CE-010 | Elementary functions: powers, roots, exp, ln, log, logₐ, all six trig functions and inverses, hyperbolic and inverses, in rad/deg/grad. | TI84 CG | P0 |
| CE-011 | Rounding and parts: abs, sign, floor, ceil, round(x, n), iPart/trunc, fPart, int, mod, rem, min, max, clamp. | TI84 CG | P0 |
| CE-012 | Combinatorics: n!, nPr, nCr, multinomial, double factorial, Stirling, Catalan, partition numbers. | TI84+ CG+ | P0/P1 |
| CE-013 | **Special functions:** Γ, lnΓ, Β, digamma, erf, erfc, erf⁻¹, Bessel J/Y/I/K, Airy, Lambert W, ζ, polylog, elliptic K/E, Fresnel, sinc, Heaviside, Dirac δ (CAS). | NEW | P1 |
| CE-014 | Piecewise definitions, conditions, logical operators, inequalities, sets. | TI84 CG | P0 |
| CE-015 | User-defined functions `f(x) := x^2 + 1`: multi-variable, vector-valued, recursive, default parameters, lambdas. | NSP+ | P0 |
| CE-016 | Lists with broadcasting: seq, cumSum, ΔList, sort, augment, dim, sum, prod, filter, map (TI L1 to L6 and Casio List 1 to 26, without the limits). | TI84+ CG+ | P0 |
| CE-017 | Sequences: explicit and recursive u(n) with u(n-1), u(n-2), initial values, partial sums. | TI84 CG | P0 |

### 6.3 CAS (symbolic)

| ID | Feature | Origin | P |
|---|---|---|---|
| CE-020 | simplify, expand, factor (over ℚ, ℝ, ℂ, ℤ/p and extensions), collect, combine, cancel, partial fractions, common denominator. | NSP CP | P1 |
| CE-021 | **solve** equations, systems and inequalities exactly, with domain and assumptions; complex solving; all solutions with conditions (`x = π/4 + nπ, n ∈ ℤ`); parametric solutions. | NSP CP | P1 |
| CE-022 | Calculus: derivatives (any order, partial, implicit), indefinite, definite and improper integrals, limits (one-sided, at ∞), series (Taylor, Laurent, Puiseux), closed-form sums and products. | NSP CP | P1 |
| CE-023 | Differential equations: deSolve for first and second order ODEs, initial conditions, linear systems, numeric fallback. | NSP CP | P1 |
| CE-024 | Transforms: Laplace and inverse, Fourier, Z-transform (ClassPad has laplace, invLaplace and fourier). | CP+ | P2 |
| CE-025 | Polynomial tools: degree, coefficients, quotient/remainder, gcd/lcm, resultant, discriminant, exact roots where possible, interpolating polynomial. | NSP | P1 |
| CE-026 | Trig and exponential rewriting: tExpand, tCollect, rewrite in terms of sin/cos/exp/log. | NSP CP | P1 |
| CE-027 | Assumptions and substitution: `assume(x > 0)`, `x ∈ ℤ`, the "with" operator (`expr | x = 2`). | NSP CP | P1 |
| CE-028 | **Step-by-step solutions** for standard tasks (linear and quadratic equations, derivatives, standard integrals, row reduction, limits) shown as an expandable trace. | NEW | P2 |
| CE-029 | **Verify:** check whether two expressions, or each line of a student's work, are equivalent; highlight the first wrong step (ClassPad Verify, generalized). | CP+ | P1 |
| CE-030 | CAS on/off per document, and forced off in exam profiles that ban CAS. | TI84 NSP | P0 |

### 6.4 Numerical methods

| ID | Feature | Origin | P |
|---|---|---|---|
| CE-040 | Root finding: bracketing plus Newton/secant with automatic bracketing; find all roots in an interval. | TI84+ CG+ | P0 |
| CE-041 | Derivatives by **automatic differentiation** for user functions (exact to machine precision, unlike nDeriv finite differences). | TI84+ | P0 |
| CE-042 | Numeric integration: adaptive Gauss-Kronrod, tanh-sinh for endpoint singularities, infinite ranges, error estimate shown. | TI84+ CG+ | P0 |
| CE-043 | Optimization: fMin/fMax on an interval, multivariable (Nelder-Mead, BFGS), constrained, linear programming; integer programming later. | TI84+ | P1 |
| CE-044 | ODE solvers: Euler and RK4 (for teaching), adaptive RK45, stiff solvers, systems, events, boundary value problems by shooting (Nspire offers euler and rk23 only). | NSP+ | P1 |
| CE-045 | Interpolation: linear, polynomial, cubic spline, Chebyshev. | NEW | P2 |
| CE-046 | FFT/DFT, convolution, basic filters and windows (ClassPad has FFT). | CP+ | P2 |
| CE-047 | Visual iteration: Newton's method steps, bisection, fixed-point and cobweb animation. | NEW | P1 |

### 6.5 Equation solvers (dedicated UI)

| ID | Feature | Origin | P |
|---|---|---|---|
| CE-050 | **Polynomial solver**, any degree (devices stop at degree 6 or 10): real and complex roots, exact where possible. | CG+ TI84+ | P0 |
| CE-051 | **Simultaneous linear equations**, any number of unknowns: detects no or infinite solutions and gives the parametric solution. | CG+ TI84+ | P0 |
| CE-052 | **Equation Solver:** any equation, pick the unknown, guess and bounds, show left minus right residual. | TI84 CG | P0 |
| CE-053 | Nonlinear system solver with multiple starting points; lists every solution found. | NEW | P1 |
| CE-054 | Inequality solver with interval and number-line output (the fx-CG100 Num Ineq app, extended to any inequality). | CG+ | P1 |

### 6.6 Linear algebra

| ID | Feature | Origin | P |
|---|---|---|---|
| CE-060 | Matrices of any size with any name and symbolic entries (TI: 10 matrices [A] to [J]; Casio: Mat A to Z), spreadsheet-style matrix editor. | TI84+ CG+ | P0 |
| CE-061 | Operations: arithmetic, scalar, power, transpose, conjugate transpose, det, inverse, trace, rank, ref, rref, identity, augment, submatrix, norms, condition number. | TI84 CG | P0 |
| CE-062 | Elementary row operations with history (rowSwap, row+, *row, *row+) for teaching. | TI84 | P0 |
| CE-063 | Decompositions: LU, QR, Cholesky, eigenvalues/eigenvectors (exact for small symbolic matrices), SVD, Jordan form, diagonalization, matrix exponential. | NSP+ | P1 |
| CE-064 | Vectors: dot, cross, norm, unit vector, angle, projection, Gram-Schmidt; 2D/3D vector drawing. | CG NSP | P0/P1 |
| CE-065 | Least squares, null space and column space bases; **linear transformation visualizer** (animate a 2x2 or 3x3 matrix acting on a grid). | NEW | P1 |

### 6.7 Number theory, discrete math, programmer tools

| ID | Feature | Origin | P |
|---|---|---|---|
| CE-070 | Base-N: bin, oct, dec, hex, bitwise and/or/xor/not/shifts, signed/unsigned word sizes 8 to 128 bits. | CG+ | P0 |
| CE-071 | IEEE 754 inspector: sign, exponent and mantissa bits for half, single and double. | NEW | P2 |
| CE-072 | Primes, isPrime, integer factorization, nextPrime, gcd/lcm, extended gcd, modular inverse and power, CRT, Euler φ, divisors. | NSP+ | P1 |
| CE-073 | Continued fractions, Egyptian fractions. | NEW | P2 |
| CE-074 | Logic and sets: truth tables, Boolean simplification, set operations, Venn diagrams. | NEW | P2 |
| CE-075 | Seeded random numbers (rand, randInt, randNorm, randBin, randSamp, random matrices), reproducible across machines. | TI84 CG | P0 |

### 6.8 Units, constants and science data

| ID | Feature | Origin | P |
|---|---|---|---|
| CE-080 | Unit conversion panel and inline units: SI, imperial, US customary, CGS; custom units. | CG+ NSP+ | P1 |
| CE-081 | Physical constants (CODATA) with uncertainties and sources. | CG+ | P1 |
| CE-082 | Periodic table with element data, usable in calculations (`molarMass("H2SO4")`). | CG TI84 | P1 |

### 6.9 Finance

| ID | Feature | Origin | P |
|---|---|---|---|
| CE-090 | TVM solver: N, I%, PV, PMT, FV, P/Y, C/Y, begin/end. | TI84 CG | P1 |
| CE-091 | Amortization schedule as a table and chart; ΣInt, ΣPrn, balance. | TI84 CG | P1 |
| CE-092 | Cash flows: NPV, IRR, MIRR, payback period, uneven flows. | TI84+ CG+ | P1 |
| CE-093 | Simple and compound interest, nominal/effective rate conversion, day counts and date arithmetic (360/365). | TI84 CG | P1 |
| CE-094 | Depreciation (SL, SYD, DB), bonds (price, yield, accrued interest), cost/sell/margin, break-even. | CG | P1 |

---

## 7. 2D graphing (`G2`)

### 7.1 Graph object types

| ID | Feature | Origin | P |
|---|---|---|---|
| G2-001 | Functions y = f(x), with domain restrictions (`{0 < x < 5}`) and piecewise definitions. | TI84 CG | P0 |
| G2-002 | x = f(y). | CG | P0 |
| G2-003 | Parametric (x(t), y(t)) with t range. | TI84 CG | P0 |
| G2-004 | Polar r = f(θ) with θ range. | TI84 CG | P0 |
| G2-005 | **Implicit relations** F(x, y) = G(x, y) of any form, including conics in general form (Nspire supports implicit plots; we add guaranteed-correct rendering, see G2-031). | NSP+ | P0 |
| G2-006 | Inequalities in y, in x and implicit, with shading and union/intersection display (TI Inequality Graphing app, Nspire inequalities, Casio Y>, Y<). | TI84 NSP CG | P0 |
| G2-007 | Sequences: time plot, web/cobweb plot, phase (u vs v) plot. | TI84 CG | P0 |
| G2-008 | Points, lists of points, polylines, polygons, vectors (arrows), segments, labels; point styles. | NEW | P0 |
| G2-009 | Statistical plots on the same axes (scatter, line, histogram, box, normal probability). | TI84 CG | P0 |
| G2-010 | **Slope fields** for dy/dx = f(x, y); click to drop an initial condition and draw the solution curve. | CP NSP | P1 |
| G2-011 | **Phase planes and vector fields** for 2D systems, with nullclines and equilibrium classification. | CP+ | P1 |
| G2-012 | Complex functions: domain coloring, conformal grid mapping. | NEW | P2 |
| G2-013 | Contour plots and heat maps of f(x, y) (the TI-89 had contour plots; the current lineup dropped them). | NEW | P1 |
| G2-014 | Conic sections from standard or general form, with computed focus, vertex, directrix, eccentricity and asymptotes. | CG TI84 | P1 |
| G2-015 | Riemann sum (left, right, midpoint), trapezoid and Simpson visualizations with an n slider. | NEW | P1 |
| G2-016 | Tangent, normal and secant lines as live objects attached to a curve. | CG | P0 |
| G2-017 | Inverse of a function, reflection in y = x. | CG TI84 | P1 |
| G2-018 | Log, semi-log and log-log axes; polar grid; π-based tick labels. | NEW | P1 |
| G2-019 | **Picture Plot / background images:** import a photo, calibrate two reference points, plot points on the image, fit a model to them. | CG TI84 | P1 |
| G2-020 | Annotations: text, LaTeX labels, arrows, shapes, freehand pen (TI DRAW menu, Casio Sketch). | TI84 CG | P0 |
| G2-021 | **Live derivative graphs:** plot f', f'' and an antiderivative of any curve in one click; a tangent that follows the cursor shows the slope (ClassPad Interactive Differential Calculus). | CP+ | P1 |

### 7.2 Rendering quality

| ID | Feature | Origin | P |
|---|---|---|---|
| G2-030 | **Adaptive sampling** with curvature-based refinement; detects poles, jumps and holes so it never draws false vertical lines across asymptotes (the TI-84 only offers a DetectAsym on/off switch). | TI84+ | P0 |
| G2-031 | **Correct implicit plots** using interval-arithmetic subdivision: no missing branches or thin features, plus a "may be incomplete" warning when it cannot guarantee it. | NEW | P0 |
| G2-032 | GPU-accelerated rendering, 60 to 120 fps pan and zoom with hundreds of objects, progressive refinement while moving. | NEW | P0 |
| G2-033 | Deep zoom: switches to arbitrary precision automatically past double-precision limits. | NEW | P2 |
| G2-034 | Antialiasing, stable dash patterns at any zoom, crisp HiDPI export. | NEW | P0 |
| G2-035 | Smart axes and grids (major/minor), equal-aspect lock, custom ranges, units on axes. | NEW | P0 |

### 7.3 Styles and organization

| ID | Feature | Origin | P |
|---|---|---|---|
| G2-040 | Per-object color, width, dash, opacity, marker, fill; TI line styles (thick, dotted, path, animate, shade above/below). | TI84+ CG+ | P0 |
| G2-041 | Color-blind-safe default palette; hatching patterns as well as colors for shaded regions. | NEW | P0 |
| G2-042 | Show/hide, lock, folders/groups, reorder, duplicate. | NEW | P0 |

### 7.4 Parameters and dynamics

| ID | Feature | Origin | P |
|---|---|---|---|
| G2-050 | **Auto sliders:** using an undefined name creates a slider with range, step and play button. | NSP CG TI84 | P0 |
| G2-051 | Animation: play, pause, loop, bounce, speed; record to GIF/MP4/WebM. | NSP+ CG+ | P1 |
| G2-052 | **Direct manipulation:** drag a parameter-defined point, or drag a curve and its coefficients update (Casio Modify, Nspire grab-and-move). | CG NSP | P1 |
| G2-053 | Family view: show all curves across a parameter range (Casio Dyna Graph locus). | CG | P1 |

### 7.5 Navigation

| ID | Feature | Origin | P |
|---|---|---|---|
| G2-060 | Pan, scroll-zoom around the cursor, box zoom, zoom presets (Standard, Trig, Decimal, Integer, Square, Fit/Auto, Round, Stat, Quadrant 1, Previous), named window memories (TI ZoomSto/ZoomRcl, Casio V-Window memory). | TI84 CG | P0 |
| G2-061 | Window settings dialog: x/y min, max, scale; θ and t ranges and steps; resolution. | TI84 CG | P0 |
| G2-062 | **Trace:** snaps to curves, arrow keys move along a curve, up/down switch curves, type a value to jump to x. | TI84 CG | P0 |
| G2-063 | Hover coordinates, crosshair, free cursor in rectangular or polar coordinates. | TI84 CG | P0 |
| G2-064 | Split views: graph + table, graph + expression list, several graphs with linked or independent windows. | TI84+ CG+ | P0 |

### 7.6 Analysis (TI CALC menu, Casio G-Solve)

| ID | Feature | Origin | P |
|---|---|---|---|
| G2-070 | **Points of interest shown automatically:** roots, extrema, intercepts, intersections, inflection points, holes; hover to see exact coordinates. | NEW | P0 |
| G2-071 | Explicit tools: value at x (Y-CAL), x for a given y (X-CAL), zero, minimum, maximum, y-intercept, intersection, dy/dx at a point, inflection point, ∫f(x)dx with shading (over a range, between roots, between intersections, mixed), area between curves, volume of revolution, arc length, distance. No cap on the number of roots in a range (Casio errors above 21). | TI84 CG CP | P0 |
| G2-072 | Results are stored in named variables and the history, as exact values when the CAS can find them. | NEW | P0 |
| G2-073 | Asymptotes (vertical, horizontal, oblique), domain and range estimate, symmetry, period, end behavior. | NEW | P1 |
| G2-074 | **Curve-sketching report:** one click produces domain, intercepts, extrema, monotonicity, concavity, asymptotes and a sign chart (ClassPad Summary Table, extended). | CP+ | P1 |

### 7.7 Tables linked to graphs

| ID | Feature | Origin | P |
|---|---|---|---|
| G2-080 | Function table: start, step, Auto/Ask modes, many functions side by side, editable x column, exact or decimal values, highlight the row's point on the graph. | TI84 CG | P0 |
| G2-081 | Tables for parametric, polar and sequence objects. | TI84 CG | P0 |
| G2-082 | Send a table to the spreadsheet or export as CSV. | NEW | P0 |

---

## 8. 3D graphing (`G3`)

| ID | Feature | Origin | P |
|---|---|---|---|
| G3-001 | Surfaces z = f(x, y) with domain region. | NSP CG | P1 |
| G3-002 | Parametric surfaces and space curves. | NSP | P1 |
| G3-003 | **Implicit surfaces** F(x, y, z) = 0 (dual contouring with interval refinement). | NEW | P1 |
| G3-004 | Primitives: point, line, plane (equation, point-normal, three points), sphere, cylinder, cone, with intersection calculations and classification of line/plane relationships (intersecting, orthogonal, parallel, skew). | CG | P1 |
| G3-005 | Cylindrical and spherical coordinate functions. | NEW | P1 |
| G3-006 | 3D vector fields, gradient fields, 3D scatter plots from data. | NEW | P2 |
| G3-007 | Solids of revolution around any axis, with the volume computed (Casio 3D Graph has Rotate; ClassPad G-Solve computes the volume). | CG+ CP+ | P1 |
| G3-008 | Level curves projected on the floor, draggable cross-section plane (Nspire has a z-trace plane), tangent plane and normal at a point. | NSP+ | P1 |
| G3-009 | Rendering: lighting, mesh/wireframe, color by height, steepness or custom function, transparency, clipping box, unlimited mesh resolution (the Nspire handheld caps it at 21). | NSP+ | P1 |
| G3-010 | Interaction: orbit, pan, zoom, preset views, trace on a surface, sliders and animation. | NSP+ CG+ | P1 |
| G3-011 | Export PNG, rotating GIF/MP4, and glTF/OBJ/STL meshes for 3D printing or CAD. | NEW | P2 |

---

## 9. Geometry (`GE`)

| ID | Feature | Origin | P |
|---|---|---|---|
| GE-001 | Construction tools: point, point on object, intersection, segment, line, ray, vector, circles (several definitions), arc, polygon, regular polygon, conics. | NSP CG CP | P1 |
| GE-002 | Constructions: midpoint, perpendicular, parallel, perpendicular bisector, angle bisector, tangent, locus. | NSP CG CP | P1 |
| GE-003 | Transformations: translation, rotation, reflection, dilation and compositions. | NSP CG | P1 |
| GE-004 | Live measurements (length, angle, area, perimeter, slope, equation) usable as variables. | NSP CG | P1 |
| GE-005 | **Bidirectional link with algebra:** every geometry object is also an expression, and vice versa (ClassPad does this one object at a time by drag and drop). | CP+ | P1 |
| GE-006 | Conjecture tools: check collinear, parallel, perpendicular, concurrent, point on object. | NSP+ | P2 |
| GE-007 | Custom tools (macros) from a construction; construction protocol playback. | NEW | P2 |

---

## 10. Statistics and probability (`ST`)

### 10.1 Data

| ID | Feature | Origin | P |
|---|---|---|---|
| ST-001 | Lists and data tables with named columns, numeric and categorical types, missing values. | TI84+ CG+ NSP | P0 |
| ST-002 | List editor with column formulas (`L3 = L1*L2`). | TI84 CG | P0 |
| ST-003 | Import CSV, TSV, XLSX, JSON, Parquet; paste from Excel or Google Sheets. | NEW | P0 CSV/paste, P1 rest |
| ST-004 | Frequency and weight columns, grouped data. | TI84 CG | P0 |

### 10.2 Descriptive statistics

| ID | Feature | Origin | P |
|---|---|---|---|
| ST-010 | One-variable: n, mean, Σx, Σx², Sx, σx, min, Q1, median, Q3, max, mode, range, IQR, skewness, kurtosis. | TI84+ CG+ | P0 |
| ST-011 | Two-variable: sums, means, deviations, Σxy, r, covariance. | TI84 CG | P0 |
| ST-012 | **Selectable quartile method** (TI, Casio, Excel inclusive/exclusive, Tukey) because devices and textbooks disagree. | NEW | P0 |
| ST-013 | Cross tabs and grouped summaries. | NEW | P1 |

### 10.3 Statistical plots

| ID | Feature | Origin | P |
|---|---|---|---|
| ST-020 | Scatter, xy-line, histogram (bin control), box and modified box plot, dot plot, bar, pie, stem-and-leaf, normal probability / QQ, residual plot, cumulative frequency (ogive), mosaic. | TI84 CG NSP | P0/P1 |
| ST-021 | **Linked brushing:** select points in one view and they highlight in every other view; drag a point and the regression updates live. | NSP+ | P1 |

### 10.4 Regression and fitting

| ID | Feature | Origin | P |
|---|---|---|---|
| ST-030 | Linear (ax+b and a+bx), median-median, quadratic, cubic, quartic, polynomial degree n, logarithmic, exponential (a·bˣ and a·eᵇˣ), power, logistic, sinusoidal, manual fit; multiple linear regression with intervals and tests (Nspire MultReg). | TI84+ CG+ NSP | P0 |
| ST-031 | Coefficients, r, r², adjusted r², standard errors, residuals stored to a list, equation sent to the graph automatically. | TI84 CG | P0 |
| ST-032 | **Custom nonlinear fit** to any model (`a·sin(b·x + c) + d`) by Levenberg-Marquardt, with standard errors and confidence intervals; initial guesses taken from slider positions. | NEW | P1 |
| ST-033 | Model comparison table (AIC/BIC) and side-by-side residual plots. | NEW | P2 |
| ST-034 | Weighted fits and fits with x and y uncertainties (lab data). | NEW | P2 |

### 10.5 Distributions

| ID | Feature | Origin | P |
|---|---|---|---|
| ST-040 | Normal, t, χ², F, binomial, Poisson, geometric, hypergeometric, uniform, exponential: pdf, cdf, inverse cdf. | TI84 CG | P0 |
| ST-041 | Additional: negative binomial, gamma, beta, Weibull, lognormal, Cauchy, triangular, multinomial. | NEW | P1 |
| ST-042 | **Distribution explorer:** shaded area, draggable bounds, parameter sliders. | TI84+ CG+ | P0 |

### 10.6 Inference

| ID | Feature | Origin | P |
|---|---|---|---|
| ST-050 | Tests: 1-sample Z and t, 2-sample Z and t (pooled and unpooled), 1-prop and 2-prop Z, χ² goodness of fit, χ² two-way, 2-sample F, linear regression t, one-way ANOVA, two-way ANOVA. | TI84 CG | P0 |
| ST-051 | Intervals: Z, t, 2-sample, 1-prop, 2-prop, regression slope, prediction intervals. | TI84 CG | P0 |
| ST-052 | Output with statistic, p-value, df, decision at α and a shaded distribution drawing. | TI84 CG | P0 |
| ST-053 | Paired t, nonparametric tests (sign, Wilcoxon, Mann-Whitney, Kruskal-Wallis), Spearman and Kendall correlation, normality tests, Levene. | NEW | P1 |
| ST-054 | **Simulation-based inference:** bootstrap intervals and permutation tests, animated. | NEW | P1 |
| ST-055 | Power and sample-size calculator. | NEW | P2 |

### 10.7 Probability and simulation

| ID | Feature | Origin | P |
|---|---|---|---|
| ST-060 | Simulators: coins, dice (any number of sides and dice), spinners, cards, urns; weighted or biased outcomes; tallies and frequency charts; millions of trials (TI Probability Simulation, fx-CG100 Prob Sim, ClassPad Probability). | TI84+ CG+ CP+ | P1 |
| ST-061 | Probability trees, Venn probabilities, Markov chains with steady state. | NEW | P2 |
| ST-062 | Monte Carlo from any expression that contains random variables. | NEW | P1 |

---

## 11. Spreadsheet (`SS`)

| ID | Feature | Origin | P |
|---|---|---|---|
| SS-001 | Grid with A1 references and named columns; formulas use the full engine (exact and CAS results in cells); fill down/right; relative and absolute refs. | NSP CG CP | P1 |
| SS-002 | Column formulas and generated columns (seq). | NSP | P1 |
| SS-003 | Sort, filter, find/replace, freeze panes, conditional formatting. | NEW | P1 |
| SS-004 | Virtualized grid for over a million rows. | NEW | P1 |
| SS-005 | Charts from a selection; selection linked to graphs and statistics. | NSP+ | P1 |
| SS-006 | Copy, paste, import and export with Excel and CSV, translating formulas, on every OS (fx-CG Manager does this on Windows only). | CG+ | P1 |
| SS-007 | Cells and columns bind to document variables (a column is a list, a cell is a scalar). | NSP | P1 |
| SS-008 | Excel-compatible function names (AVERAGE, STDEV.S, …) for familiarity. | NEW | P2 |

---

## 12. Notes and notebooks (`NB`)

| ID | Feature | Origin | P |
|---|---|---|---|
| NB-001 | Rich-text notes with live math boxes that evaluate (Nspire Notes, Casio eActivity). | NSP CG CP | P1 |
| NB-002 | **Reactive notebook:** text, math, graph, table and code cells; dependencies re-evaluate automatically, so results are never stale or order-dependent. | NEW | P1 |
| NB-003 | Markdown, headings, lists, images, links, callouts, table of contents, collapsible sections. | NEW | P1 |
| NB-004 | Export to PDF, HTML, Markdown, LaTeX and .ipynb; print with page layout. | NEW | P1 |
| NB-005 | Guided activities: locked cells, hints, question blocks with auto-checking (Nspire Question app). | NSP+ | P2 |

---

## 13. Programming and code execution (`PR`)

### 13.1 Languages

| ID | Feature | Origin | P |
|---|---|---|---|
| PR-001 | **Native calculator language:** anything typed in the calculator is valid code, plus `for`, `while`, `if/else`, functions, local scope, strings, lists, matrices, error handling. Math-first and readable; the modern replacement for TI-Basic and Casio Basic. | NEW | P0 |
| PR-002 | **Full CPython 3** (not MicroPython) with numpy, scipy, sympy, matplotlib and pandas in a managed environment; built-in package manager to add more. | TI84+ NSP+ CG+ | P0 |
| PR-003 | **TI-Basic compatibility:** import .8xp and Nspire TI-Basic, run in a compatibility interpreter that emulates the home screen and graph screen (Disp, Input, Prompt, Output, getKey, Menu, Pause, ClrHome, Lbl/Goto, drawing commands); optional translation to native code or Python. | TI84 NSP | P1 |
| PR-004 | **Casio Basic compatibility:** import .g3m/.g1m and text programs, run in a compatibility interpreter (Locate, ?, ◢, Getkey, Menu, PlotOn, F-Line, Text and graph commands). The fx-CG100 dropped Casio Basic and add-ins, so this rescues existing programs. | CG | P1 |
| PR-005 | Nspire Lua script compatibility (large API surface). | NSP | P2 |
| PR-006 | C# scripting (Roslyn) for power users and plugin prototyping. | NEW | P2 |

### 13.2 Calculator API (available from every language)

| ID | Feature | Origin | P |
|---|---|---|---|
| PR-010 | Read and write document variables (`calc.get("a")`, `calc.set("a", 3)`) and subscribe to changes. | NSP+ | P0 |
| PR-011 | Call the engine from code (`calc.eval("solve(x^2=2, x)")`); exact results convert to Python numbers, SymPy objects or NumPy arrays. | NEW | P0 |
| PR-012 | Create and modify graph objects, sliders, tables and notes (`calc.graph.add("y = sin(a x)")`). | NEW | P0 |
| PR-013 | Drawing canvas API: pixels, lines, shapes, text, images, double buffering, 60 fps game loop (a superset of ti_draw and casioplot). | NSP+ CG+ TI84+ | P1 |
| PR-014 | Input: prompts, menus, forms, key and mouse events (getKey equivalent), timers. | TI84 CG | P0 |
| PR-015 | **Compatibility shims** so existing classroom Python runs unchanged: `ti_plotlib`, `ti_draw`, `ti_system`, `ti_image`, `casioplot`, `turtle` and the device `matplotlib.pyplot` subset, rendering into app panels. | TI84 NSP CG | P1 |
| PR-016 | Plots made from code become **native interactive graphs**, not static images. | NEW | P0 |
| PR-017 | Register new functions from code (`@calc.function`), then use them in expressions, graphs, spreadsheets and over MCP. | NEW | P1 |

### 13.3 Editor and tooling

| ID | Feature | Origin | P |
|---|---|---|---|
| PR-020 | Code editor: syntax highlighting, autocomplete and hover docs from a language server, diagnostics, formatting, multi-cursor, find/replace. | NEW | P0 highlighting, P1 LSP |
| PR-021 | Run file, run selection, run cell; REPL console with rich output (typeset math, tables, inline plots). | NEW | P0 |
| PR-022 | **Debugger:** breakpoints, step in/over/out, call stack, watches, variable inspector (Python through the Debug Adapter Protocol, native language built in). | NEW | P1 |
| PR-023 | Program libraries per document and per user, versioned and importable (Nspire public/private libraries). | NSP+ | P1 |
| PR-024 | Share programs as single files; import from a URL with a review prompt showing the code first. | NEW | P1 |
| PR-025 | Example gallery: simulations, games, classic calculator programs. | NEW | P1 |

### 13.4 Execution model and safety

| ID | Feature | Origin | P |
|---|---|---|---|
| PR-030 | Code runs in a **separate worker process**, so a crash or infinite loop never freezes the UI. | NEW | P0 |
| PR-031 | Break (the ON-key equivalent: Esc or Ctrl+Break), timeouts, memory and CPU caps. | TI84+ CG+ | P0 |
| PR-032 | Sandbox permissions: file system (none, document folder, ask), network (off by default), subprocesses (off), hardware (ask). | NEW | P0 |
| PR-033 | Documents from elsewhere open with code disabled until the user trusts them (Office macro model). | NEW | P0 |
| PR-034 | Reproducible mode: fixed seeds and the engine version recorded in the document. | NEW | P1 |
| PR-035 | Jupyter interop: open and save .ipynb; optionally act as a Jupyter kernel. | NEW | P2 |

### 13.5 Hardware and data collection

| ID | Feature | Origin | P |
|---|---|---|---|
| PR-040 | Serial, USB and BLE access from code (with permission) for Arduino, micro:bit and hub-style devices. | TI84+ | P2 |
| PR-041 | Sensor data collection UI (time-based and event-based) with live graph and fitting (Vernier DataQuest, Casio E-CON). | NSP CG | P2 |

---

## 14. MCP and AI (`MC`)

**Why this matters:** language models are unreliable at arithmetic and symbolic manipulation. Exposed over MCP, the app becomes the exact math engine and visual surface for any agent (Claude, IDE assistants, custom agents). As an MCP client, it can also pull data and tools from other systems into the calculator.

### 14.1 MCP server: the app as a tool provider

| ID | Feature | P |
|---|---|---|
| MC-001 | **Transports:** stdio (a headless `calc mcp` process, no UI needed) and Streamable HTTP on localhost (when the GUI runs, agents act on the live workspace). | P0 stdio, P1 HTTP |
| MC-002 | **Headless engine:** the same engine without any Avalonia dependency, so the MCP server also runs in CI, containers and servers. | P0 |
| MC-003 | **Math tools:** `evaluate`, `simplify`, `solve`, `differentiate`, `integrate`, `limit`, `series`, `factor`, `matrix_op`, `linear_solve`, `unit_convert`, `describe_data`, `regress`, `hypothesis_test`, `distribution`, `optimize`, `ode_solve`, `finance_tvm`. Each returns structured output: exact form, decimal, LaTeX, plain text, assumptions and warnings. | P0 core, P1 rest |
| MC-004 | **Visual tools:** `plot_2d`, `plot_3d`, `plot_data` return a PNG image plus SVG and a document reference that opens in the app. | P0 |
| MC-005 | **Workspace tools:** `list_documents`, `open_document`, `get_variables`, `set_variable`, `add_expression`, `remove_object`, `set_window`, `get_table`, `analyze_graph`, `export`, `read_notes`, `append_note`. Agent edits appear live in the app and undo as a single step labeled with the client's name. | P1 |
| MC-006 | `run_code` (Python or native language) in the sandboxed worker with timeouts; off unless the user enables it. | P1 |
| MC-007 | **Resources:** documents (`calc://documents/{id}`), variables, graph snapshots, datasets as CSV, the function reference (`calc://reference/functions/{name}`); subscriptions so clients see updates. | P1 |
| MC-008 | **Prompts:** "Explain this graph", "Check my solution step by step", "Generate practice problems like this one", "Fit a model to this data", "Turn this notebook into a worksheet". | P1 |
| MC-009 | **MCP Apps:** return an interactive graph or calculator widget to hosts that support the MCP Apps extension, so sliders and zoom work inside the AI chat. | P2 |
| MC-010 | **Long-running work** (large ODE runs, big Monte Carlo, slow CAS) uses the MCP Tasks extension with progress and cancellation. | P2 |
| MC-011 | **Elicitation:** tools can ask the user for confirmation or missing input through the client ("Overwrite variable a?"). | P1 |
| MC-012 | Correct tool annotations (read-only, destructive, idempotent) so clients can auto-approve safe tools. | P0 |
| MC-013 | One-click "Add to Claude Desktop / Claude Code / VS Code / Cursor": writes or copies the client configuration. | P1 |

### 14.2 MCP client: the app uses other MCP servers

| ID | Feature | P |
|---|---|---|
| MC-020 | Connect to MCP servers over stdio and HTTP (OAuth for remote servers) from a Connections panel; import standard `mcp.json` configs. | P1 |
| MC-021 | Import data from MCP resources and tools (a database, spreadsheet or API server) into a dataset that can be refreshed. | P1 |
| MC-022 | Call external tools from expressions and code: `mcp("weather", "get_forecast", {city: "Utrecht"})`, with results as lists or matrices. | P1 |
| MC-023 | Browser for tools and resources with schemas, test calls and logs. | P1 |
| MC-024 | Sampling support (a server may ask the app's configured model, with user approval). | P2 |

### 14.3 Optional AI assistant (built on the same tools)

| ID | Feature | P |
|---|---|---|
| MC-030 | Assistant panel: natural language to math ("shade the area between sin x and cos x from 0 to π"). The assistant acts only through the app's own tool surface, so every action is visible, reviewable and undoable. | P1 |
| MC-031 | Provider-agnostic: bring your own API key or run a local model. No default cloud connection. | P1 |
| MC-032 | Explain mode (results, steps, graph features, errors) and tutor mode that gives hints rather than answers, configurable by a teacher. | P1 |
| MC-033 | Code assist in the editor: generate, explain and fix code with a diff preview. | P1 |
| MC-034 | Can be disabled entirely by the user, by policy or by an exam profile, with a clear indicator when active. | P1 |

### 14.4 Security, privacy and governance

| ID | Feature | P |
|---|---|---|
| MC-040 | Per-tool permission levels (allow, ask, deny), per-client and per-document scopes, global read-only mode. | P0 |
| MC-041 | HTTP transport bound to 127.0.0.1 by default, random port, bearer token required, Origin checks against DNS rebinding. | P0 |
| MC-042 | Audit log of every MCP call (client, tool, arguments, result summary, time), viewable and exportable. | P1 |
| MC-043 | Agents only see documents the user has exposed ("share with agents" toggle per document). | P1 |
| MC-044 | Rate limits and resource caps on tool calls. | P1 |
| MC-045 | No telemetry by default; AI features never send document content without an explicit user action. | P0 |

---

## 15. Exam and classroom (`EX`)

| ID | Feature | Origin | P |
|---|---|---|---|
| EX-001 | **Exam profiles** modeled on TI Press-to-Test and Casio Exam Mode: selectively disable CAS, code, MCP, AI, network, stored notes and programs, 3D, implicit plots and specific functions; presets per exam board and region (Casio ships IB, UK, Netherlands, Texas and Portugal modes; Nspire Press-to-Test has 17 restriction options). | TI84 NSP CG | P1 |
| EX-002 | Exam session: hides all stored content, starts a clean sandbox, persistent visual indicator (colored border and banner), session timer. Nothing is deleted (TI-84 Press-to-Test deletes or disables user content) and leaving is instant once the session ends (exiting fx-CG100 exam mode takes 12 to 14 hours or a USB connection). | TI84+ CG+ | P1 |
| EX-003 | Signed exam log (start, end, profile, focus-loss events) exportable for proctors. | NEW | P1 |
| EX-004 | OS lockdown integration where the platform offers it (for example macOS Automatic Assessment Configuration, Windows kiosk / Assigned Access). A desktop app alone cannot guarantee a locked device, and the docs must say so. | NEW | P2 |
| EX-005 | Teacher tools: distribute and collect documents, live view of student screens, quick polls (TI-Navigator style). | NSP+ | P2 |
| EX-006 | School-managed policy file (deployable through MDM) that locks features. | NEW | P2 |

---

## 16. Files, interop and connectivity (`FI`)

| ID | Feature | Origin | P |
|---|---|---|---|
| FI-001 | **Open native format:** documented zip container of JSON plus assets, with a plain-text folder variant that diffs well in git. | NEW | P0 |
| FI-002 | Import TI files: .8xp programs, .8xl lists, .8xm matrices, .8xs strings, graph databases; .tns documents (expressions, lists, programs). | TI84 NSP | P1/P2 |
| FI-003 | Import Casio files: .g3m/.g1m programs, lists and matrices. | CG | P2 |
| FI-004 | Import Desmos graph state, GeoGebra .ggb, Jupyter .ipynb, CSV/XLSX/JSON/Parquet, LaTeX equations. | NEW | P1/P2 |
| FI-005 | Export PDF, PNG, SVG, LaTeX (equations plus TikZ/pgfplots graphs), Markdown, standalone interactive HTML, .ipynb, CSV/XLSX, Python script; .8xp and .g3m programs to transfer to physical calculators. | NEW | P0 images, P1 docs, P2 device files |
| FI-006 | Clipboard: graphs as image or SVG, expressions as LaTeX or MathML, tables as TSV, objects as rich internal data for paste between documents. | NEW | P0 |
| FI-007 | Recent and pinned files, OS file associations, drag and drop files onto the window. | NEW | P0 |
| FI-008 | Optional sync through the user's own cloud folder, with conflict handling. No proprietary cloud. | NEW | P2 |

---

## 17. Accessibility and internationalization (`AX`)

| ID | Feature | Origin | P |
|---|---|---|---|
| AX-001 | Full keyboard operation, including graph navigation (move along a curve, jump between points of interest). | NEW | P0 |
| AX-002 | Screen reader support through Avalonia automation peers; math spoken in natural language with navigation into subexpressions. | NEW | P1 |
| AX-003 | Braille math output (Nemeth, UEB). | NEW | P2 |
| AX-004 | **Audio graphs:** sonify curves (pitch for y) with audio cues for roots, extrema and discontinuities. | NEW | P1 |
| AX-005 | High-contrast themes, scalable UI and fonts, color-blind-safe palettes, line patterns, reduced motion. | NEW | P0 |
| AX-006 | Localized UI and function names; decimal comma, list separator, digit grouping; regional notation (tg vs tan, ln vs log, · vs ×); right-to-left layouts. | NEW | P1 |
| AX-007 | Dyslexia-friendly font option and adjustable spacing. | NEW | P2 |

---

## 18. Customization and extensibility (`CX`)

| ID | Feature | Origin | P |
|---|---|---|---|
| CX-001 | Fully remappable shortcuts with presets (TI-like, Casio-like, Mathematica-like). | NEW | P1 |
| CX-002 | Custom toolbars and quick menus (like Casio OPTN/VARS, but user-defined). | CG+ | P1 |
| CX-003 | Settings export/import as one file. | NEW | P1 |
| CX-004 | **Plugin SDK (.NET):** add functions, panels, importers/exporters, graph object types and MCP tools; plugins declare permissions; untrusted plugins run out of process. | NEW | P2 |
| CX-005 | Python plugins: a package with an entry point registers functions and panels. | NEW | P2 |
| CX-006 | Plugin gallery with signed packages. | NEW | P2 |

---

## 19. Non-functional requirements (`NF`)

| ID | Requirement |
|---|---|
| NF-001 | **Platforms:** Windows 10/11 (x64, arm64), macOS (Apple Silicon and Intel), Linux (x64, arm64; X11 and Wayland). Later: a browser viewer and tablets using Avalonia's browser and mobile targets. |
| NF-002 | **Startup** under 1 second to usable input on a mid-range laptop (Native AOT or ReadyToRun; Python worker started lazily). |
| NF-003 | **Graph interaction** at display refresh rate (60 to 120 fps) with 100 plotted objects, with progressive refinement. |
| NF-004 | **Latency:** simple evaluations under 5 ms; the UI thread never blocks on computation; every computation is cancellable. |
| NF-005 | **Accuracy:** a reference test corpus checked against independent systems (mpmath, MPFR, SymPy) and against TI/Casio outputs where users expect identical answers (for example quartiles and regression coefficients). |
| NF-006 | **Reliability:** periodic autosave, crash-safe writes (write then rename), corruption detection, versioned file format with migrations. |
| NF-007 | **Offline:** everything except external MCP servers and cloud AI works without a network. |
| NF-008 | **Distribution:** signed installers (MSIX/winget, notarized .dmg, AppImage/Flatpak/.deb/.rpm), delta auto-updates, silent enterprise install with a policy file. |
| NF-009 | **Privacy:** no telemetry by default; opt-in crash reports with document content stripped. |
| NF-010 | **Footprint:** base install without Python kept small; the Python environment is an optional component. |

---

## 20. Architecture for Avalonia (`AR`)

```
+---------------------------------------------------------------+
|  Avalonia UI (MVVM)                                            |
|  Dock layout | Math editor | Graph 2D (Skia) | Graph 3D (GL)  |
|  Code editor | Spreadsheet | Notes | Assistant | Connections   |
+---------------------------------------------------------------+
|  App services: commands, undo, settings, permissions, audit    |
+---------------------------------------------------------------+
|  Document model: reactive dependency graph of named objects    |
+-----------+-----------+-------------+------------+------------+
| Math core | CAS       | Plot engine | Stats      | Units      |
| numbers,  | adapter   | sampling,   | Finance    | Constants  |
| numerics  |           | meshing     |            |            |
+-----------+-----------+-------------+------------+------------+
|  Hosts: GUI app | headless CLI | MCP server (stdio / HTTP)     |
|  Workers: Python kernel process | Basic/Lua compat interpreters|
+---------------------------------------------------------------+
```

Key rule: **the engine and document model have no UI dependency.** The GUI, the headless CLI, the MCP server and the test suite all share them from day one.

| Concern | Candidates | Notes |
|---|---|---|
| UI framework | Avalonia 12 | Windows, macOS, Linux; browser and mobile targets available for later |
| MVVM | ReactiveUI or CommunityToolkit.Mvvm | ReactiveUI fits the reactive document model well |
| Docking | Dock (Avalonia) | Panel layout, floating windows |
| Code editor | AvaloniaEdit with TextMate grammars | LSP client on top |
| Math typesetting | Custom box-layout engine on Skia, or CSharpMath | WYSIWYG editing (IN-001, IN-007) most likely needs a custom control |
| 2D graphing | Custom SkiaSharp renderer | Chart libraries (ScottPlot, OxyPlot, LiveCharts) suit data charts, not function graphing with adaptive sampling |
| 3D graphing | OpenGlControlBase with Silk.NET | Vulkan as an option later |
| Numerics | Math.NET Numerics | Linear algebra, distributions, optimization |
| Arbitrary precision | BCL BigInteger plus MPFR/FLINT via P/Invoke | Both LGPL; dynamic linking keeps options open |
| CAS | Giac, SymPy (via the Python worker), AngouriMath | Giac is the strongest (it powers the HP Prime and GeoGebra CAS) but is GPL. SymPy is BSD but slower. AngouriMath is MIT and pure .NET but less complete. **This is the biggest licensing decision.** |
| Python | CSnakes or Python.NET, inside a worker process | Out of process for isolation and break/timeout |
| MCP | Official ModelContextProtocol C# SDK (2.x) | Core, AspNetCore (HTTP), Extensions.Apps, Extensions.Tasks packages |
| Install and update | Velopack | Cross-platform installers and delta updates |

---

## 21. Release plan

**MVP (P0):** Simple, Advanced, Programmer and Graphing modes with the mode switcher and shared state, 2D math editor, exact and big-float numerics, function library, equation/polynomial/linear solvers, matrices, 2D graphing core (functions, parametric, polar, implicit, inequalities, sequences) with correct rendering, sliders, analysis and points of interest, linked tables, lists, descriptive statistics, regressions, distributions and tests, native language and Python in a sandboxed worker, MCP server over stdio with core math and plot tools, native file format, image export.

**Version 1.0 (P1):** the remaining modes plus custom modes and the mini window, CAS, 3D graphing, geometry, spreadsheet, notebooks and notes, finance, units and constants, slope fields and phase planes, custom fitting, debugger, TI-Basic and Casio Basic import, MCP over HTTP with workspace tools, MCP client, optional AI assistant, exam profiles, accessibility (screen reader and audio graphs), localization.

**Later (P2):** step-by-step solutions, transforms, Lua compatibility, hardware and sensors, classroom tools, plugin SDK and gallery, MCP Apps and Tasks, browser/tablet builds, handwriting, device file export.

---

## 22. Pain points to fix, not copy

Limits taken from the vendors' own manuals, unless marked "(community reports)".

| Pain point on the devices | Our answer |
|---|---|
| **Tiny fixed screens:** 320x240 (TI-84, Nspire), 384x216 class (fx-CG), 128x64 monochrome (fx-9750GIII). | Any window size, HiDPI, multiple monitors, dockable panels. |
| **Memory ceilings:** TI-84 has about 149 KB RAM, 999-element lists, 10 matrices up to 20x20, 10 Y= functions. Casio has about 61 KB main memory, eActivity files of about 29 KB, spreadsheets of about 30 KB, 26x999 cells. | No artificial limits; virtualized views for big data (SS-004). |
| **Cut-down, old Python:** TI-84 Python is limited to 100 programs or 50 KB and 100-element lists. Casio runs MicroPython 1.9.4 (2018) with ASCII-only files of at most 300 lines x 255 characters and 8-character file names, rewrites files on open, and the fx-CG50 has no getkey. The plotting modules are vendor approximations of turtle and matplotlib. | Full CPython 3 with numpy, scipy, sympy and matplotlib (PR-002), plus compatibility shims so classroom programs still run (PR-015). |
| **Slow:** Casio's guides warn that integrals and sinusoidal/logistic regression on large data are slow. TI-84 Python runs on a coprocessor; TI-Basic and graph redraws are slow (community reports). | Desktop CPU and GPU, background computation, progressive rendering, everything cancellable (NF-003, NF-004). |
| **Broken backward compatibility:** the fx-CG100 dropped Casio Basic, eActivity, Picture Plot and add-ins. The TI-84 CE removed native code (community reports). | Import and run TI-Basic and Casio Basic programs (PR-003, PR-004); an open, versioned file format (FI-001). |
| **Input friction:** nested menus, alpha keys, function-key tabs, catalog scrolling, single-letter variables; no undo or copy/paste on the TI-84 outside the program editor. | Typing with auto-formatting, autocomplete, command palette, real variable names, full undo (IN-002 to IN-005, WS-004, WS-007). |
| **Siloed apps:** Nspire variables cannot link between documents; TI-84 apps share little state; ClassPad drag and drop works one object at a time. | One reactive document model, drag and drop everywhere, cross-document links (WS-018, WS-019). |
| **Numeric surprises:** Casio warns G-Solve can be inaccurate at tangency and inflection points and errors when more than 21 roots or intersections fall between the bounds. TI warns Nspire Python results may differ from the Calculator app. The TI-84 shows 10 of 14 internal digits. | Robust root isolation, exact values where possible, one engine for every language and view, precision shown on request (CE-001, CE-040, G2-070). |
| **3D limits:** Nspire handheld mesh resolution is capped at 21; fx-CG100 allows 3 expressions, ClassPad 1. | Unlimited surfaces at GPU resolution (G3-009). |
| **Device-only features:** Nspire TI-Basic graphics only run in the handheld view; Lua scripts cannot be edited on the handheld. | One runtime and one editor everywhere. |
| **Proprietary files and transfer:** .8xp, .tns, .g3m, .g3p, .g4p, .vcp; cables; TI Connect CE imports CSV only for columns A to F, 999 rows, no text and no decimal commas. | Open format, any CSV/XLSX/Parquet, importers for the device formats (FI-001 to FI-004). |
| **Paywalled, aging desktop software:** Casio emulators are subscriptions that support only older OS versions, Excel interop is Windows only, the fx-CG50 emulator is being withdrawn during 2026 to 2027, the fx-CG100 has no installed emulator, and the ClassPad Math CAS is behind a paid tier. | A native desktop app on every OS, with CAS included. |
| **Exam mode friction:** TI-84 Press-to-Test deletes or disables user content; leaving fx-CG100 exam mode takes 12 to 14 hours or a USB connection. | Profiles that hide rather than delete, instant exit after the session (EX-001, EX-002). |

---

## 23. Open decisions

1. **CAS engine and license** (Giac GPL vs SymPy vs pure .NET). This decides the app's own license.
2. **Primary audience:** secondary school (exam compatibility first) or university and engineering (power and scripting first). It changes the defaults, not the feature list.
3. **Exam mode ambition:** a convenience profile, or an attempt at official exam approval (which a general-purpose desktop app is unlikely to get).
4. **Open source or commercial**, which interacts with decision 1.

---

## Appendix A. Reference device notes

Taken from the vendors' manuals in October 2026. Items marked "(unverified)" were not confirmed in official documentation.

### TI-84 Plus CE / CE-T Python Edition

- 320x240 color screen; about 149 KB RAM and 3 MB archive; 14-digit internal precision, 10 shown.
- Graph modes: Function (10), Parametric (6), Polar (6), Seq (3, with time, web and phase plots). 15 colors; Thin, Thick, Dot-Thin and Dot-Thick styles confirmed.
- ZOOM: ZBox, Zoom In/Out, ZDecimal, ZSquare, ZStandard, ZTrig, ZInteger, ZoomStat, ZoomFit, ZQuadrant1, ZFrac presets, ZPrevious, ZoomSto, ZoomRcl.
- CALC: value, zero, minimum, maximum, intersect, dy/dx, ∫f(x)dx. DRAW: Line, Horizontal, Vertical, Tangent, DrawF, Shade, DrawInv, Circle, Text, Pen, points and pixels, StorePic/RecallPic, StoreGDB/RecallGDB, background images, ShadeNorm/Shade_t/Shadeχ²/ShadeF.
- Statistics: 12 regressions including Manual-Fit; 10 tests; 7 intervals; ANOVA with up to 20 lists. Finance: TVM, npv, irr, bal, ΣPrn, ΣInt, ►Nom, ►Eff, dbd.
- Apps: Cabri Jr, CellSheet (999x26), Conic Graphing, Inequality Graphing, PlySmlt2 (degree 1 to 10, linear systems), Probability Simulation, Science Tools, Periodic Table, Transformation Graphing (coefficients A to D).
- Python (CircuitPython based): math, random, time, ti_system, ti_plotlib, ti_hub, ti_rover; ti_draw and ti_image as add-ons; 320x210 drawing area.
- Press-to-Test (CE-T): disables apps, programs and pictures, logBASE and Σ; green LED; exit by link or TI Connect CE.

### TI-Nspire CX II CAS

- 320x240, 64 MB RAM, over 90 MB storage. Numeric, Exact or CAS modes; CAS can be disabled.
- Documents hold Problems, Problems hold Pages, up to 4 apps per page; variables are shared within a Problem; separate Scratchpad.
- Apps: Calculator, Graphs, Geometry, Lists & Spreadsheet, Data & Statistics, Notes (math boxes, Chem Box), Vernier DataQuest, Question.
- Graphs: function, parametric, polar, sequence, differential equation, implicit and conic, inequality, 3D (z(x,y) and parametric surfaces, color by height or steepness, z-trace plane). Sliders, animation, grab-and-move.
- CAS families: solve/cSolve/nSolve/zeros/linSolve, factor/expand/comDenom/propFrac/completeSquare/tExpand/tCollect, d/∫/limit/taylor/series/arcLen/impDif/tangentLine/normalLine, deSolve/euler/rk23, LU/QR/eigVl/eigVc, MultReg, ANOVA2way, mirr, amortTbl, units catalog.
- Programming: TI-Basic programs and functions with LibPub/LibPriv libraries, Try/EndTry, draw commands (CX II, 318x212); Python (math, random, cmath, time, ti_plotlib, ti_draw, ti_image, ti_system, ti_hub, ti_rover); Lua (graphics, physics, BLE; edited only on the desktop).
- Press-to-Test: 17 restriction options, plus disabling CAS.
- Teacher software: built-in TI-SmartView, Live Presenter, Question authoring, Lua Script Editor, deSolve wizard, Navigator.

### TI-89 Titanium (legacy features still valued)

- 3D graphing with contour and implicit plots; differential equation mode with slope and direction fields; a full units system with user-defined units; text editor with executable scripts; 32-bit bin/hex bitwise operations; custom toolbars; any two apps side by side.

### Casio fx-CG50

- Apps: Run-Matrix, Statistics, eActivity, Spreadsheet (26x999), Graph, Dyna Graph, Table, Recursion, Conic Graphs, Equation (2 to 6 unknowns, degree 2 to 6, Solve), Program, Financial, E-CON4, Python, Distribution, Geometry, Picture Plot, 3D Graph, CONVERT (v3.70).
- Graph types: Y=, r=, Param, X=, and all inequality forms in y and x.
- G-Solve: ROOT, MAX, MIN, Y-ICEPT, INTSECT, Y-CAL, X-CAL, ∫dx (range, roots, intersections, mixed). Sketch: Tangent, Norm, Inverse, Plot, Line, Circle, Vertical, Horz, PEN, Text.
- 3D Graph templates: line, plane, sphere, cylinder, cone; Z= and parametric; Rotate; cross sections.
- Statistics: 12 regressions; Z, t, prop, χ², F, linear regression t, one-way and two-way ANOVA; 8 distributions with inverse.
- Python: MicroPython 1.9.4 with math, random, casioplot (turtle and matplotlib unverified on this model).
- Exam modes: IB, UK, NL, Texas.

### Casio fx-CG100 / fx-1AU GRAPH (2025)

- New UI: HOME, SETTINGS, CATALOG, TOOLS, VARIABLE and OK keys; tabs in every app; system-wide copy/paste; pinned catalog history.
- Calculate replaces Run-Matrix with recalculating history and prime factorization; Graph&Table merges graph and table; new Num Ineq, Prob Sim, Database (periodic table and constants) and Base-N apps; E-CON5 (v2.10).
- Python: MicroPython 1.9.4 with math, random, matplotlib.pyplot, turtle and casioplot (now with getkey); 384x192 drawing area; editor autocomplete.
- Removed: Casio Basic, eActivity, Picture Plot, add-ins. Storage mounts as a USB drive over USB-C.
- Exam modes: IB, Portugal, UK, Texas; Python stays available.

### Casio ClassPad II (fx-CP400)

- Apps: Main (CAS), eActivity, Statistics, Spreadsheet, Graph & Table, 3D Graph, Geometry, Picture Plot, Interactive Differential Calculus, Conics, Differential Equation Graph, Numeric Solver, Sequence, Financial, Program, E-CON3.
- CAS: solve, dSolve, diff, impDiff, laplace/invLaplace, fourier, FFT, eliminate, rewrite; Verify for step-by-step equivalence; Probability simulator.
- Drag and drop between apps (expression to graph, equation to geometry, figure back to an equation).
- G-Solve adds inflection, distance and volume of revolution. DE Graph: slope fields, phase planes, tap for initial conditions.
- No official Python.

### Casio ClassPad.net / ClassPad Math (browser)

- "Papers" of sticky notes (calculation, graph, geometry, statistics, sequence, financial, text, handwriting, media), slideshow and publish by URL.
- Paid tier adds CAS, advanced statistics and handwriting recognition of formulas. Hosts the fx-CG100 emulator, with key history and a resizable screen.

### Desktop software

- TI Connect CE: file explorer, screen capture, TI-Basic editor, Python file conversion, CSV import wizard, exam mode exit.
- TI-SmartView CE: emulator with Key Press History and View3. TI-Nspire CX Premium Teacher Software: see above.
- fx-CG Manager PLUS (subscription): emulator, Key-Log record and playback, presentation mode, Excel copy/paste with formula translation (Windows only).
- ClassPad Manager (subscription): resizable emulator, key palettes, clipboard exchange, build programs on the PC.

### Sources

TI:
- [TI-84 Plus CE Python guidebooks](https://education.ti.com/en/product-resources/guidebooks/ti-84-plus-ce-python)
- [TI-84 Plus CE-T Reference Guide (PDF)](https://education.ti.com/download/en/ed-tech/41A893A900F54486AE580250A0A9B0BF/680DCC1FC012442499E856C16727CB8C/GRefGuide_84PlusCE-T_EN.pdf)
- [TI-84 Plus CE-T Apps guide (PDF)](https://education.ti.com/download/en/ed-tech/41A893A900F54486AE580250A0A9B0BF/E154BCC567CA40AFABC0F442A21BDD07/AppAll_84PlusCE-T_EN.pdf)
- [TI-84 Plus CE programming guide (PDF)](https://education.ti.com/download/en/ed-tech/41A893A900F54486AE580250A0A9B0BF/0C7E19A782BC442F9C830C714816F98A/TI-ProgGuide_EN.pdf)
- [TI-Nspire CAS Reference Guide (PDF)](https://education.ti.com/download/en/ed-tech/0AF942A4AC0E47ABA20853691FA6712E/21717C07E942465BAB415753181078C7/TI-NspireCAS_Reference_Guide_EN.pdf)
- [TI-Nspire CX II handheld guidebook (PDF)](https://education.ti.com/download/en/ed-tech/4BEBB458C6A64278A3249BA6443E8688/ABC4C792F7BE44ECB55E589F5DF690CF/TI-Nspire_CXII-HH_Guidebook_EN.pdf)
- [TI-Nspire Press-to-Test restrictions](https://education.ti.com/html/eguides/nspire/EG_Nspire/EN/content/m_press-to-test/pt_understanding_press-to-test_restrictions.HTML)
- [TI-Nspire Lua API guide](https://education.ti.com/html/webhelp/EG_TINspireLUA/EN/content/eg_splash_page/splash_luaapiguide.htm)
- [TI-89 Titanium guidebook (PDF)](https://education.ti.com/download/en/ed-tech/FA1DC891957E4700B46A67255850C592/983EA8A4BA2A4AE9B2AF5EEEE922E3C1/TI-89_Guidebook_EN.pdf)

Casio:
- [fx-CG50 software guide v3.70 (PDF)](https://www.casio.com/content/dam/casio/global/support/manuals/calculators/pdf/004-en/f/fx-CG50_Soft_v370_EN.pdf)
- [fx-CG100 guide v2.00 (PDF)](https://casioeducation.com/_files/ugd/3cfc42_a6199bc2722f48b6907ba4bce692e828.pdf)
- [fx-CG100 online manual v2.10](https://support.casio.com/global/en/calc/manual/fx-CG100_1AUGRAPHv210_en/index.html)
- [fx-9860GIII / fx-9750GIII guide v3.50 (PDF)](https://www.casio.com/content/dam/casio/global/support/manuals/calculators/pdf/004-en/f/fx-9860GIII_Soft_v350_EN.pdf)
- [ClassPad II fx-CP400 user guide (PDF)](https://casioau.com/cdn/shop/files/fx-CP400_ClassPad_II__User_Guide.pdf)
- [fx-CG Manager PLUS guide (PDF)](https://www.casio-education.fr/wp-content/uploads/2020/07/fx-cg_manager_plus_subscription_en.pdf)
- [ClassPad.net features](https://classpad.net/intl/features/)
- [Casio UK fx-CG100 FAQ](https://education.casio.co.uk/classwiz-graphic-faq/)

Platform:
- [ModelContextProtocol C# SDK on NuGet](https://www.nuget.org/packages/ModelContextProtocol/2.0.0)
- [Avalonia on NuGet](https://www.nuget.org/profiles/avaloniaui)
- [CSnakes (Python in .NET)](https://github.com/wovika/CSnakes)
