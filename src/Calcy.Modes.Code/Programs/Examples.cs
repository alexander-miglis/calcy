namespace Calcy.Modes.Code.Programs;

/// <summary>A program in the examples gallery.</summary>
/// <param name="FileName">The file name it opens as.</param>
/// <param name="Title">Short title for the gallery.</param>
/// <param name="Description">One line about what it shows.</param>
/// <param name="Source">The program.</param>
/// <param name="Inputs">Answers to input() used when the gallery is tested without a person.</param>
public sealed record ExampleProgram(string FileName, string Title, string Description, string Source, IReadOnlyList<string>? Inputs = null);

/// <summary>Classroom programs for the examples gallery (PR-025).</summary>
public static class Examples
{
    /// <summary>The programs shown in a fresh Code mode (as on the design board).</summary>
    public static IReadOnlyList<string> Starters { get; } = ["primes.py", "star.py", "roots.py"];

    /// <summary>Every example, in gallery order.</summary>
    public static IReadOnlyList<ExampleProgram> All { get; } =
    [
        new("primes.py", "Prime sieve", "The sieve of Eratosthenes finds the primes below 50.", """
            # Primes below 50 with a sieve
            def primes(n):
                is_p = [True] * n
                is_p[0] = is_p[1] = False
                for i in range(2, int(n ** 0.5) + 1):
                    if is_p[i]:
                        for j in range(i * i, n, i):
                            is_p[j] = False
                return [i for i in range(n) if is_p[i]]

            found = primes(50)
            print(len(found), "primes:", found)
            """),
        new("star.py", "Turtle star", "Five lines and five turns draw a star.", """
            import turtle

            t = turtle.Turtle()
            t.color("red")
            t.pensize(3)
            for i in range(5):
                t.forward(150)
                t.right(144)
            print("done")
            """),
        new("roots.py", "Roots and slopes", "Exact roots and the derivative from calc, plotted with ti_plotlib.", """
            from calc import solve, diff
            import ti_plotlib as plt

            f = "x^3 - 3x"
            print("roots:", solve(f + " = 0", "x"))
            print("f'(x) =", diff(f, "x"))

            xs = [i / 10 for i in range(-30, 31)]
            ys = [x ** 3 - 3 * x for x in xs]
            plt.window(-3, 3, -4, 4)
            plt.plot(xs, ys, ".")
            plt.show_plot()
            """),
        new("quadratic.py", "Quadratic solver", "Asks for a, b and c, then gives exact roots and decimals.", """
            # Solve a x^2 + b x + c = 0 exactly
            # Press Enter to keep the value in brackets.
            import calc

            def ask(name, default):
                text = input(name + " [" + str(default) + "] = ")
                return text.strip() or str(default)

            a = ask("a", 1)
            b = ask("b", -3)
            c = ask("c", 2)
            equation = "(" + a + ")x^2 + (" + b + ")x + (" + c + ") = 0"
            print("Discriminant:", calc.eval("(" + b + ")^2 - 4(" + a + ")(" + c + ")"))
            roots = calc.solve(equation, "x")
            if not roots:
                print("No solutions")
            for r in roots:
                print("x =", r, "   about", calc.n(r))
            calc.show("solve(" + equation + ", x)", "Typeset:")
            """, ["", "", ""]),
        new("guess.py", "Guessing game", "The computer picks a number from 1 to 100; you get seven tries.", """
            # Guess the number
            import random

            secret = random.randint(1, 100)
            print("I am thinking of a number from 1 to 100.")
            for attempt in range(1, 8):
                text = input("Guess " + str(attempt) + ": ")
                if not text.strip().isdigit():
                    print("Please type a whole number.")
                    continue
                guess = int(text)
                if guess < secret:
                    print("Too low.")
                elif guess > secret:
                    print("Too high.")
                else:
                    print("Yes! You found it in", attempt, "tries.")
                    break
            else:
                print("Out of tries. It was", secret)
            """, ["50", "25", "75", "", "", "", ""]),
        new("spiral.py", "Turtle spiral", "A square spiral in six colours.", """
            # A colourful square spiral
            import turtle

            t = turtle.Turtle()
            t.speed(10)
            t.pensize(2)
            colours = ["red", "orange", "gold", "green", "blue", "purple"]
            for i in range(72):
                t.pencolor(colours[i % 6])
                t.forward(i * 2.4)
                t.left(91)
            t.hideturtle()
            print("Spiral done with", 72, "lines")
            """),
        new("scatter.py", "Scatter plot", "Height against arm span with the line of best fit (ti_plotlib).", """
            # Height and arm span of a class, with the line of best fit
            import ti_plotlib as plt

            height = [150, 155, 160, 162, 165, 170, 172, 175, 180, 185]
            span = [148, 156, 158, 163, 166, 168, 174, 176, 179, 186]

            plt.cls()
            plt.window(140, 190, 140, 190)
            plt.grid(10, 10, "dot")
            plt.color(216, 40, 0)
            plt.scatter(height, span, "o")
            plt.color(0, 88, 248)
            a, b = plt.lin_reg(height, span, "center", 11)
            plt.title("Arm span against height")
            plt.show_plot()
            print("Best fit: span = %.3f * height + %.2f" % (a, b))
            """),
        new("projectile.py", "Projectile", "A ball thrown at 18 m/s and 50 degrees, animated frame by frame.", """
            # A ball thrown at 18 m/s and 50 degrees
            import math
            import calcydraw as d
            import calc

            v0 = 18.0
            angle = math.radians(50)
            g = 9.81
            vx = v0 * math.cos(angle)
            vy = v0 * math.sin(angle)
            flight = 2 * vy / g
            reach = vx * flight
            print("Time of flight: %.2f s" % flight)
            print("Range: %.2f m" % reach)
            print("Highest point: %.2f m" % (vy ** 2 / (2 * g)))

            W, H = d.size()
            scale = (W - 20) / reach
            trail = []

            def frame(n):
                t = n / 30
                if t > flight:
                    return False
                x = vx * t
                y = vy * t - g * t * t / 2
                px = 10 + x * scale
                py = H - 12 - y * scale
                trail.append((px, py))
                d.clear()
                d.line(0, H - 11, W, H - 11, "muted")
                for i in range(1, len(trail)):
                    d.line(trail[i - 1][0], trail[i - 1][1], trail[i][0], trail[i][1], "accent1", 2)
                d.fill_circle(px, py, 4, "accent2")
                d.text(4, 4, "t = %.1f s" % t)

            d.run(frame, fps=30)
            calc.show("(v^2 sin(2θ))/g", "Range:")
            """),
        new("wave.py", "Python in the calculator", "A Python function becomes a calculator function and a graph.", """
            # Make a Python function a calculator function, then graph it
            import math
            import calc

            @calc.function
            def wave(x):
                "A damped wave."
                return math.exp(-x / 6) * math.sin(2 * x)

            print("wave(1) =", calc.eval("wave(1)"))
            calc.graph.add("y = wave(x)")
            calc.graph.add("y = exp(-x/6)", color=2)
            calc.graph.window(-1, 12, -1.2, 1.2)
            print("wave also works in the other modes while Code mode is open.")
            """),
        new("sierpinski.py", "Pixel fractal", "A Sierpinski triangle drawn pixel by pixel (Casio casioplot).", """
            # Sierpinski triangle, one pixel at a time
            from casioplot import *

            clear_screen()
            for y in range(176):
                for x in range(384):
                    if ((x // 2) & y) == 0:
                        set_pixel(x, y, (40, 90, 200))
            draw_string(4, 182, "Pixels where x & y == 0", (0, 0, 0), "small")
            show_screen()
            """),
        new("house.py", "Shapes", "A house drawn with ti_draw rectangles, a polygon and a circle (TI-Nspire).", """
            # A house drawn with ti_draw shapes
            from ti_draw import *

            clear()
            set_color(135, 206, 235)
            fill_rect(0, 0, 318, 150)
            set_color(60, 160, 60)
            fill_rect(0, 150, 318, 62)
            set_color(200, 60, 40)
            fill_poly([90, 160, 230], [100, 50, 100])
            set_color(240, 220, 170)
            fill_rect(100, 100, 120, 70)
            set_color(110, 70, 30)
            fill_rect(145, 125, 30, 45)
            set_color(255, 210, 0)
            fill_circle(270, 40, 18)
            set_color(0, 0, 0)
            draw_text(8, 205, "Home sweet home")
            """),
        new("paddle.py", "Paddle game", "Keep the ball up with the Left and Right keys (click the canvas first).", """
            # Keep the ball up with the paddle: Left and Right keys
            import calcydraw as d

            W, H = d.size()
            ball = {"x": 40.0, "y": 30.0, "dx": 2.5, "dy": 2.0}
            game = {"paddle": W / 2, "score": 0}

            def frame(n):
                if d.is_pressed("left"):
                    game["paddle"] = max(25, game["paddle"] - 5)
                if d.is_pressed("right"):
                    game["paddle"] = min(W - 25, game["paddle"] + 5)
                ball["x"] += ball["dx"]
                ball["y"] += ball["dy"]
                if ball["x"] < 5 or ball["x"] > W - 5:
                    ball["dx"] = -ball["dx"]
                if ball["y"] < 5:
                    ball["dy"] = -ball["dy"]
                near = abs(ball["x"] - game["paddle"]) < 28
                if ball["y"] > H - 16 and near and ball["dy"] > 0:
                    ball["dy"] = -ball["dy"] * 1.03
                    game["score"] += 1
                if ball["y"] > H:
                    return False
                d.clear()
                d.fill_rect(game["paddle"] - 25, H - 10, 50, 6, "accent1")
                d.fill_circle(ball["x"], ball["y"], 4, "accent2")
                d.text(4, 4, "Score " + str(game["score"]))

            d.run(frame, fps=60)
            print("Game over! Score:", game["score"])
            """),
    ];

    /// <summary>The example with this file name, or null.</summary>
    public static ExampleProgram? Find(string fileName) => All.FirstOrDefault(e => e.FileName == fileName);
}
