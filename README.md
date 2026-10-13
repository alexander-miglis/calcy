# Calcy

An 8-bit styled graphing calculator for Windows, macOS and Linux, written in C# with Avalonia.
Every piece of maths (exact arithmetic, algebra, calculus, statistics, numerics, graphing and a
sandboxed Python interpreter) is implemented in this repository, with no third-party maths libraries.

The full feature specification and the agreed design decisions are in [docs/FEATURES.md](docs/FEATURES.md);
the screen designs are in [docs/design](docs/design).

## Installing

Download a package from the [releases](https://github.com/alexander-miglis/calcy/releases). Every package contains
the app (`Calcy`) and the terminal calculator and MCP server (`calcy-cli`); no .NET installation is needed.

| System | Package | How |
|---|---|---|
| Windows 10/11 (x64, Arm64) | `Calcy-<version>-win-x64.zip` | Unzip and run `Calcy.exe` |
| macOS 12+ (Apple silicon, Intel) | `Calcy-<version>-osx-arm64.dmg` | Open the dmg and drag Calcy to Applications. The app is not notarized yet: the first time, right-click it and choose Open |
| Debian 12+, Ubuntu 22.04+, Mint | `calcy_<version>_amd64.deb` | `sudo apt install ./calcy_<version>_amd64.deb` |
| Fedora, RHEL 9/10, AlmaLinux, Rocky, CentOS Stream | `calcy-<version>.x86_64.rpm` | `sudo dnf install ./calcy-<version>.x86_64.rpm` |
| Any other Linux (glibc) | `calcy-<version>-linux-x64.tar.gz` | Unpack anywhere and run `Calcy` |

Arm64 Linux packages are built too (`arm64.deb`, `aarch64.rpm`, `linux-arm64.tar.gz`).
## Requirements for building

- .NET 10 SDK
- Windows 10 or later, macOS 12 or later, or a Linux desktop with X11 (Wayland through XWayland)

## Build and run

```bash
dotnet build Calcy.slnx
```

```bash
dotnet run --project src/Calcy.App
```

```bash
dotnet test Calcy.slnx
```

### In VS Code

Open the `calcy` folder and install the recommended C# Dev Kit extension when VS Code offers it. Then press
**F5**, or the green play button in Run and Debug with **Calcy** selected, to build and start the app. Press
**Shift+F5**, or the red stop square in the debug toolbar, to close it. The same dropdown also has
**calcy-cli (terminal calculator)** and **calcy-cli (MCP server)**. Ctrl+Shift+B builds, and the
"test: everything" task runs all tests.

## Modes

Pick a mode from the dropdown in the top bar or with its shortcut.

| Mode | Shortcut | What it is for |
|---|---|---|
| Simple | Ctrl+1 | Everyday sums with memory keys and a tape |
| Advanced | Ctrl+2 | Scientific and algebra, every result on its own card |
| Graphing | Ctrl+3 | Plot, trace, roots, areas, sliders |
| Programmer | Ctrl+4 | Hex, binary, bit operations, word sizes |
| Statistics | Ctrl+5 | Data, regressions, tests, distributions |
| Matrix | Ctrl+6 | Edit matrices, determinants, inverses, eigenvalues |
| Solver | Ctrl+7 | Polynomials, systems, any equation |
| Finance | Ctrl+8 | Loans, savings, cash flows |
| Convert | Ctrl+9 | Units and dates |
| Formulas | Ctrl+0 | The documented formula collection |
| Notebook | Ctrl+Shift+N | Text, maths and graphs in one document |
| Code | Ctrl+E | Sandboxed Python |

## Typing maths

- `1/3 + 1/6` gives `1/2`; decimals are exact too, so `0.1 + 0.2` gives `3/10` (Casio style).
- Implied multiplication: `2x`, `3(x + 1)`. By default it binds tighter than division, so `1/2x` is `1/(2x)`;
  the TI and Desmos reading (`x/2`) is a setting.
- `log` is base 10, `ln` is natural.
- Definitions: `a = 5`, `f(x) = x^2 - 3x`, then `f(a)`.
- Lists `{1, 2, 3}`, matrices `[[1, 2], [3, 4]]`, piecewise `{x < 0: -x, x}`.

## Command line

`calcy-cli` is the same engine in the terminal.

```bash
dotnet run --project src/Calcy.Cli -- eval "1/3 + 1/6" "sqrt(72)" "solve(x^2 = 2, x)"
```

```bash
dotnet run --project src/Calcy.Cli
```

The second command starts an interactive session; type `:help` for commands. Options include `--deg`,
`--ti`, `--decimal`, `--latex`, `--json` and `--digits N`; run `calcy-cli --help` for the list. Piped input
is evaluated line by line, so `calcy-cli < homework.txt` works.

## MCP server (for AI agents)

`calcy-cli mcp` runs a [Model Context Protocol](https://modelcontextprotocol.io) server on stdin and stdout,
so assistants such as Claude can use Calcy as an exact maths engine instead of doing arithmetic themselves.

Tools:
- maths: `evaluate`, `solve`, `simplify`, `expand`, `factor`, `differentiate`, `integrate`, `limit`, `series`,
  `matrix`, `linear_solve`
- statistics: `describe_data`, `distribution`, `regress`, `hypothesis_test`, `confidence_interval`
- finance: `finance_tvm`, `cash_flow`
- formulas: `search_formulas`, `get_formula`, `solve_formula`
- graphs: `plot_2d` (PNG image, SVG and the roots, extrema and intersections)
- units and dates: `unit_convert`, `date_calc`
- session: `define`, `get_variables`, `clear_variables`, `settings`, `list_functions`, `function_help`
- code (only with `--allow-code`): `run_code` runs sandboxed Python with the `calc` module and returns output, errors and drawings

Each maths tool returns the exact answer, a decimal value, plain text
that can be passed back in, and LaTeX. Tools that need a module that is not in a build are not offered.

Resources: `calcy://guide/syntax`, `calcy://reference/functions`, `calcy://reference/functions/{name}`,
`calcy://formulas`, `calcy://formulas/{id}`, `calcy://session/variables` and `calcy://session/settings`. Prompts: `check_solution`, `explain`,
`practice_problems` and `fit_model`.

Print the client configuration for your machine:

```bash
dotnet run --project src/Calcy.Cli -- mcp-config
```

For Claude Code, after building:

```bash
claude mcp add calcy -- dotnet /path/to/calcy/src/Calcy.Cli/bin/Debug/net10.0/calcy-cli.dll mcp
```

Safety options: `--allow-code` offers `run_code` (off by default; the Python sandbox has no file, network or OS access), `--read-only` refuses every tool that changes variables or settings, `--disable a,b` hides
tools, `--timeout SECONDS` limits each call (default 30), and `--audit-log FILE` appends one JSON line per
call. No tool touches files or the network.

## Project layout

| Project | Contents |
|---|---|
| `Calcy.Core` | Exact numbers, expressions, parser, evaluator, printers (text and LaTeX), compiler for fast plotting |
| `Calcy.Cas` | Computer algebra: polynomials, factoring, solving, calculus |
| `Calcy.Numerics` | Root finding, integration, ODEs, optimisation, numeric linear algebra |
| `Calcy.Statistics` | Descriptive statistics, distributions, regressions, tests |
| `Calcy.Graphing` | Plotting engine: sampling, implicit curves, points of interest, SVG export |
| `Calcy.Python` | Sandboxed Python 3 subset interpreter (no file, network or OS access) |
| `Calcy.Formulas` | The formula collection: 418 documented formulas and 56 constants |
| `Calcy.Units` | Unit catalogue, conversions and date arithmetic |
| `Calcy.Engine` | Composes the modules into one calculator session |
| `Calcy.Mcp` | MCP server (protocol, tools, resources, prompts), no UI dependency |
| `Calcy.Cli` | `calcy-cli`: REPL, one-shot evaluation, MCP stdio server |
| `Calcy.UI` | Theme, colour schemes, pixel controls, app services |
| `Calcy.UI.Math`, `Calcy.UI.Graph` | Maths rendering and editing controls, graph canvas |
| `Calcy.Modes.*` | One project per mode screen |
| `Calcy.App` | The desktop application |
| `Calcy.UI.TestKit` | Headless rendering helpers for UI tests and screenshots |

## Screenshots without a display

The app can render any mode headlessly, which the tests use to check every colour scheme:

```bash
dotnet run --project src/Calcy.App -- --screenshot advanced advanced.png --scheme nes --input "1/3 + 1/6"
```

## Versions and releases

The version lives in one place: `<VersionPrefix>` in `Directory.Build.props` (the next release, for example `0.1.0`).
Every build stamps it into the programs: `calcy --version`, `calcy-cli --version`, the MCP server and Settings > About
all show it, together with the commit for CI builds.

Releases are built by the **Release** workflow (`.github/workflows/release.yml`), which only runs when started by hand:
GitHub > Actions > Release > Run workflow.

- `channel: prerelease` builds `0.1.0-pre.<run number>` from any commit, as often as you like.
- `channel: release` builds exactly `0.1.0` and refuses to run when the tag `v0.1.0` already exists. Bump
  `VersionPrefix` after each release.
- `version` overrides `VersionPrefix` for one run.
- `create_release` makes a draft GitHub release with every package and a `SHA256SUMS.txt`; publish it after a look.

The workflow runs all tests, builds self-contained packages for Windows (x64, Arm64), macOS (Apple silicon, Intel) and
Linux (x64, Arm64: tar.gz, deb, rpm), smoke-tests them on Windows and macOS, and installs the Linux packages on Debian
12 and 13, Ubuntu 22.04 and 24.04, Fedora, AlmaLinux 9 and 10, Rocky Linux 9, CentOS Stream 10 and Red Hat UBI 9 and 10,
where it runs `calcy-cli` and renders a screen with the app. To build a package locally:

```bash
pwsh build/package.ps1 -Rid linux-x64 -Version 0.1.0-dev
```

## Reporting bugs

In the app, open Help (`?` or F1) or Settings > About and press **REPORT A BUG**: it opens a GitHub issue form with
the version, your system and the mode already filled in, and nothing is sent until you submit it. **COPY DETAILS** copies
the same information for a report sent another way. On the command line, `calcy-cli bug` prints the link. Ideas are
welcome as feature requests on the [issues](https://github.com/alexander-miglis/calcy/issues) page.

## License

Calcy is released under the [MIT License](LICENSE). Copyright (c) 2026 Alexander Miglis.