using Calcy.Graphing;
using Calcy.Python;
using Calcy.Scripting.Drawing;

namespace Calcy.Scripting;

public sealed partial class ScriptHost
{
    /// <summary>
    /// The private native module "_calcy" behind the Python modules: calculator access, drawing primitives in canvas
    /// pixels, keys and console control. Colours are passed as codes: 0xRRGGBB for RGB and negative numbers for scheme
    /// tokens (-1 background, -2 ink, -3 muted, -4 grid, -5 to -9 accents).
    /// </summary>
    private void BuildNativeModule(ModuleBuilder m)
    {
        m.SetDoc("Internal helpers of the Calcy modules; use calc, turtle, ti_plotlib, ti_draw, casioplot, calcydraw and ti_system.");

        // Calculator.
        Add(m, "calc_eval", a => _calc.Eval(a.GetString(0), a.Count > 1 && a.GetBool(1)));
        Add(m, "calc_exact", a => _calc.Exact(a.GetString(0), a.Count > 1 && a.GetBool(1)));
        Add(m, "calc_latex", a => _calc.Latex(a.GetString(0)));
        Add(m, "calc_number", a => _calc.Number(a.GetString(0)));
        Add(m, "calc_get", a => _calc.Get(a.GetString(0)));
        Add(m, "calc_set", a =>
        {
            _calc.Set(a.GetString(0), a.Get(1));
            return null;
        });
        Add(m, "calc_delete", a => _calc.Delete(a.GetString(0)));
        Add(m, "calc_variables", _ => _calc.Variables());
        Add(m, "calc_solve", a => _calc.Solve(a.GetString(0), a.GetString(1)));
        Add(m, "calc_subs", a => _calc.Subs(a.GetString(0), a.Get(1) as Dictionary<object, object?>));
        Add(m, "calc_register", a =>
        {
            var raw = a.Count > 3 && a.GetOptional(3, null) is not null ? a.GetCallable(3) : null;
            _calc.Register(a.GetString(0), a.GetCallable(1), a.Count > 2 ? a.GetOptional(2, null) as string : null, raw);
            return null;
        });
        Add(m, "calc_show", a =>
        {
            Emit(ScriptOutputKind.Math, a.GetString(0), a.Count > 1 ? a.GetOptional(1, null) as string : null);
            return null;
        });
        Add(m, "bind_types", a =>
        {
            _calc.Bind(a.GetCallable(0), a.GetCallable(1));
            return null;
        });

        // Graphs.
        Add(m, "graph_add", a => AddGraph(a.GetString(0), a.Count > 1 ? a.GetOptional(1, null) : null));
        Add(m, "graph_remove", a => Graphs.RemoveAt((int)a.GetInt64(0)));
        Add(m, "graph_clear", _ =>
        {
            Graphs.Clear();
            return null;
        });
        Add(m, "graph_window", a =>
        {
            if (a.Count == 0 || a.GetOptional(0, null) is null)
            {
                Graphs.SetWindow(null);
                return null;
            }
            var v = new Viewport(a.GetDouble(0), a.GetDouble(1), a.GetDouble(2), a.GetDouble(3));
            if (!v.IsValid) throw new PythonRaiseException("ValueError", "window() needs xmin < xmax and ymin < ymax");
            Graphs.SetWindow(v);
            return null;
        });

        // Canvas.
        Add(m, "canvas_init", a =>
        {
            int w = (int)Math.Clamp(a.GetInt64(0), 1, DrawCanvas.MaxSide), h = (int)Math.Clamp(a.GetInt64(1), 1, DrawCanvas.MaxSide);
            Canvas.Reset(w, h);
            if (a.Count > 2 && a.GetBool(2)) Canvas.SetBuffered(true);
            return (Canvas.Width, Canvas.Height);
        });
        Add(m, "canvas_size", _ => (Canvas.Width, Canvas.Height));
        Add(m, "canvas_buffered", a =>
        {
            if (a.Count > 0) Canvas.SetBuffered(a.GetBool(0));
            return Canvas.IsBuffered;
        });
        Add(m, "color", a => ColorCode(a.Count > 0 ? a[0] : null, a.Count > 1 ? a.GetDouble(1) : 0));
        Add(m, "color_rgb", a =>
        {
            var (r, g, b) = FromCode(a.GetInt64(0), mapBlackWhite: false).ToRgb();
            return ((long)r, (long)g, (long)b);
        });
        Add(m, "set_color", a => Draw(new DrawCommand { Kind = DrawKind.Color, Color = Col(a, 0) }));
        Add(m, "clear", a => Draw(new DrawCommand { Kind = DrawKind.Clear, Color = a.Count > 0 ? Col(a, 0) : DrawColor.Background }));
        Add(m, "background", a => Draw(new DrawCommand { Kind = DrawKind.Background, Color = Col(a, 0) }));
        Add(m, "pixel", a => Draw(new DrawCommand { Kind = DrawKind.Pixel, X = a.GetDouble(0), Y = a.GetDouble(1), Color = Col(a, 2) }));
        Add(m, "get_pixel", a =>
        {
            var c = Canvas.GetPixel((int)Math.Floor(a.GetDouble(0)), (int)Math.Floor(a.GetDouble(1)));
            if (c is not { } color) return null;
            var (r, g, b) = color.ToRgb();
            return ((long)r, (long)g, (long)b);
        });
        Add(m, "line", a => Draw(new DrawCommand
        {
            Kind = DrawKind.Line, X = a.GetDouble(0), Y = a.GetDouble(1), X2 = a.GetDouble(2), Y2 = a.GetDouble(3),
            Color = Col(a, 4), Width = a.GetDouble(5, 1),
        }));
        Add(m, "rect", a => Draw(new DrawCommand
        {
            Kind = DrawKind.Rect, X = a.GetDouble(0), Y = a.GetDouble(1), X2 = a.GetDouble(2), Y2 = a.GetDouble(3),
            Color = Col(a, 4), Fill = a.Count > 5 && a.GetBool(5), Width = a.GetDouble(6, 1),
        }));
        Add(m, "circle", a => Draw(new DrawCommand
        {
            Kind = DrawKind.Circle, X = a.GetDouble(0), Y = a.GetDouble(1), Size = a.GetDouble(2),
            Color = Col(a, 3), Fill = a.Count > 4 && a.GetBool(4), Width = a.GetDouble(5, 1),
        }));
        Add(m, "arc", a => Draw(new DrawCommand
        {
            Kind = DrawKind.Arc, X = a.GetDouble(0), Y = a.GetDouble(1), X2 = a.GetDouble(2), Y2 = a.GetDouble(3),
            Start = a.GetDouble(4), Extent = a.GetDouble(5), Color = Col(a, 6), Fill = a.Count > 7 && a.GetBool(7), Width = a.GetDouble(8, 1),
        }));
        Add(m, "polygon", a =>
        {
            var pts = a.GetDoubleList(0);
            if (pts.Count % 2 != 0) throw new PythonRaiseException("ValueError", "polygon points must come in x, y pairs");
            return Draw(new DrawCommand
            {
                Kind = DrawKind.Polygon, Points = pts.ToArray(), Color = Col(a, 1), Fill = a.Count > 2 && a.GetBool(2), Width = a.GetDouble(3, 1),
            });
        });
        Add(m, "text", a => Draw(new DrawCommand
        {
            Kind = DrawKind.Text, X = a.GetDouble(0), Y = a.GetDouble(1), Text = a.GetString(2), Color = Col(a, 3), Size = a.GetDouble(4, 1),
        }));
        Add(m, "text_width", a => (long)CanvasFont.MeasureWidth(a.GetString(0), (int)Math.Max(1, Math.Round(a.GetDouble(1, 1)))));
        Add(m, "show", _ =>
        {
            Canvas.Show();
            return null;
        });
        Add(m, "sprite", a =>
        {
            if (a.Count == 0 || !a.GetBool(4))
            {
                Canvas.SetSprite(null);
                return null;
            }
            Canvas.SetSprite(new DrawSprite(a.GetDouble(0), a.GetDouble(1), a.GetDouble(2), Col(a, 3)));
            return null;
        });

        // Keys, waiting and the console.
        Add(m, "key", _ => Keyboard.Read());
        Add(m, "key_down", a => Keyboard.IsDown(a.GetString(0)));
        Add(m, "keys_down", _ => Keyboard.Down.ToList());
        Add(m, "pause_waits", _ => _options.PauseTimeoutWhileWaiting);
        Add(m, "wait", a =>
        {
            // Waiting for the next frame or a turtle step does not use up the time limit; Stop still ends it at once.
            double seconds = a.GetDouble(0);
            if (!(seconds > 0)) return null;
            var duration = TimeSpan.FromSeconds(Math.Min(seconds, 3600));
            a.WaitOutsideTimeLimit(ct => ct.WaitHandle.WaitOne(duration));
            return null;
        });
        Add(m, "wait_key", a => a.WaitOutsideTimeLimit(ct => Keyboard.Wait(ct)));
        Add(m, "animate", _ => _options.AnimateTurtle);
        Add(m, "console_clear", _ =>
        {
            Emit(ScriptOutputKind.Clear, "");
            return null;
        });
    }

    /// <summary>Adds a native function that marks the thread as running inside this host (so calculator callbacks call Python directly).</summary>
    private void Add(ModuleBuilder m, string name, Func<PythonArgs, object?> body) =>
        m.AddFunction(name, (PythonArgs a) =>
        {
            var previous = t_active;
            t_active = this;
            try
            {
                return body(a);
            }
            finally
            {
                t_active = previous;
            }
        });

    private object? Draw(DrawCommand command)
    {
        Canvas.Execute(command);
        return null;
    }

    private DrawColor Col(PythonArgs a, int index) => a.Count > index && a[index] is var o && PyConvert.ToClr(o) is long code ? FromCode(code, true) : DrawColor.Ink;

    private int AddGraph(string text, object? color)
    {
        if (string.IsNullOrWhiteSpace(text)) throw new PythonRaiseException("ValueError", "graph.add() needs the text of a graph, for example 'y = x^2'");
        var item = GraphParser.Parse(text, Session, defineFunctions: false);
        if (item.Kind == GraphKind.Error) throw new PythonRaiseException("ValueError", $"graph.add(): {item.Error} in '{text}'");
        if (item.Kind == GraphKind.Empty) throw new PythonRaiseException("ValueError", "graph.add() needs the text of a graph, for example 'y = x^2'");
        int? index = color switch
        {
            long l => (int)(((l % 5) + 5) % 5),
            null => null,
            _ => throw new PythonRaiseException("TypeError", "graph.add() color must be a number 0 to 4"),
        };
        return Graphs.Add(new GraphRequest(text.Trim(), index));
    }

    /// <summary>Turns a colour given by a program (name, "#rrggbb", (r, g, b), 0xRRGGBB or a colour code) into a colour code.</summary>
    private long ColorCode(PyObject? spec, double scale)
    {
        var value = spec is null ? null : PyConvert.ToClr(spec);
        switch (value)
        {
            case null:
                return Code(DrawColor.Ink);
            case string s:
                if (DrawColor.TryParse(s, out var named)) return Code(named);
                throw new PythonRaiseException("ValueError", $"unknown colour '{s}' (use a name such as 'red', '#ff8800' or (r, g, b))");
            case bool:
                throw new PythonRaiseException("TypeError", "a colour must be a name, '#rrggbb' or (r, g, b)");
            case long l:
                if (l < 0 && l >= -9) return l;
                if (l is >= 0 and <= 0xFFFFFF) return l;
                throw new PythonRaiseException("ValueError", "a colour number must be between 0x000000 and 0xFFFFFF");
            case IReadOnlyList<object?> list when list.Count is 3 or 4:
            {
                var c = new double[3];
                for (int i = 0; i < 3; i++)
                {
                    c[i] = list[i] switch
                    {
                        long li => li,
                        double di => di,
                        bool => throw new PythonRaiseException("TypeError", "colour components must be numbers"),
                        _ => throw new PythonRaiseException("TypeError", "colour components must be numbers"),
                    };
                    if (!double.IsFinite(c[i])) throw new PythonRaiseException("ValueError", "colour components must be finite");
                }
                bool unit = scale switch
                {
                    1 => true,
                    255 => false,
                    _ => list.Take(3).Any(x => x is double) && c.All(x => x is >= 0 and <= 1),
                };
                double f = unit ? 255 : 1;
                byte B(double x) => (byte)Math.Clamp(Math.Round(x * f), 0, 255);
                return ((long)B(c[0]) << 16) | ((long)B(c[1]) << 8) | B(c[2]);
            }
        }
        throw new PythonRaiseException("TypeError", "a colour must be a name, '#rrggbb' or (r, g, b)");
    }

    private static long Code(DrawColor c) => c.IsToken ? -(long)c.Token - 1 : c.Value & 0xFFFFFF;

    private DrawColor FromCode(long code, bool mapBlackWhite)
    {
        if (code < 0) return DrawColor.FromToken((DrawToken)Math.Clamp(-code - 1, 0, (long)DrawToken.Accent5));
        if (mapBlackWhite && _options.SchemeBlackAndWhite)
        {
            if (code == 0) return DrawColor.Ink;
            if (code == 0xFFFFFF) return DrawColor.Background;
        }
        return DrawColor.FromRgb((byte)(code >> 16), (byte)(code >> 8), (byte)code);
    }
}
