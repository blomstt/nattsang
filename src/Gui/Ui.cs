using System.Runtime.InteropServices;

namespace Nattsang.Gui;

// Signals, timers, frame ticks and small widget helpers. A handler is a C#
// delegate kept under a slot number; GTK gets a static unmanaged trampoline
// and the slot as its data. The window is built once and lives as long as
// the process, so signal slots are never freed; a timer's or a tick
// callback's slot is freed when it ends, and reused.
static unsafe class Ui
{
    static readonly List<Delegate?> handlers = [];
    static readonly Stack<int> free = [];

    static nint Keep(Delegate d)
    {
        if (free.TryPop(out var slot))
        {
            handlers[slot] = d;
            return slot;
        }
        handlers.Add(d);
        return handlers.Count - 1;
    }

    static T Handler<T>(nint slot) where T : Delegate => (T)handlers[(int)slot]!;

    // clicked, activate, changed, destroy, shutdown
    public static void On(nint obj, string signal, Action handler) =>
        N.g_signal_connect_data(obj, signal, (nint)(delegate* unmanaged<nint, nint, void>)&Plain, Keep(handler), 0, 0);

    // row-activated (the row), row-selected (the row or 0)
    public static void OnArg(nint obj, string signal, Action<nint> handler) =>
        N.g_signal_connect_data(obj, signal, (nint)(delegate* unmanaged<nint, nint, nint, void>)&WithArg, Keep(handler), 0, 0);

    // clicked, with one delegate shared by many buttons (rows rebuilt
    // often): it gets the button, and is kept once.
    public static void OnClicked(nint button, Action<nint> handler)
    {
        if (!shared.TryGetValue(handler, out var slot)) shared[handler] = slot = Keep(handler);
        N.g_signal_connect_data(button, "clicked", (nint)(delegate* unmanaged<nint, nint, void>)&WithSelf, slot, 0, 0);
    }

    static readonly Dictionary<Delegate, nint> shared = [];

    // A key pressed anywhere in widget, before the focused widget sees it:
    // (keyval, modifier state) → handled.
    public static void OnKey(nint widget, Func<uint, uint, bool> handler)
    {
        var controller = N.gtk_event_controller_key_new();
        N.gtk_event_controller_set_propagation_phase(controller, 1); // GTK_PHASE_CAPTURE
        N.g_signal_connect_data(controller, "key-pressed", (nint)(delegate* unmanaged<nint, uint, uint, uint, nint, int>)&Key, Keep(handler), 0, 0);
        N.gtk_widget_add_controller(widget, controller);
    }

    // draw(cr, width, height)
    public static void OnDraw(nint area, Action<nint, int, int> draw) =>
        N.gtk_drawing_area_set_draw_func(area, (nint)(delegate* unmanaged<nint, nint, int, int, nint, void>)&Draw, Keep(draw), 0);

    // A one-shot timer, ms from now: its source id, for Cancel.
    public static uint After(uint ms, Action action) =>
        N.g_timeout_add_full(0, ms, (nint)(delegate* unmanaged<nint, int>)&Timeout, Keep(action), (nint)(delegate* unmanaged<nint, void>)&Forget);

    public static void Cancel(ref uint source)
    {
        if (source != 0) N.g_source_remove(source);
        source = 0;
    }

    // Runs action on the GLib main thread, soon; safe to call from any
    // thread. For a player that hears from another thread (a socket
    // reader): everything else here, and GTK, is main thread only.
    public static void Post(Action action)
    {
        lock (posted)
        {
            posted.Enqueue(action);
            if (postPending) return;
            postPending = true;
        }
        N.g_idle_add_full(0, (nint)(delegate* unmanaged<nint, int>)&RunPosted, 0, 0); // G_PRIORITY_DEFAULT
    }

    static readonly Queue<Action> posted = [];
    static bool postPending;

    // step(frame time in seconds) runs once a frame while it returns true;
    // its id, for StopTicking.
    public static uint Ticking(nint widget, Func<double, bool> step) =>
        N.gtk_widget_add_tick_callback(widget, (nint)(delegate* unmanaged<nint, nint, nint, int>)&Tick, Keep(step), (nint)(delegate* unmanaged<nint, void>)&Forget);

    public static void StopTicking(nint widget, ref uint id)
    {
        if (id != 0) N.gtk_widget_remove_tick_callback(widget, id);
        id = 0;
    }

    // Now, on the frame clock's scale (seconds).
    public static double Clock => N.g_get_monotonic_time() / 1e6;

    // gtk-enable-animations: off, things jump instead of moving.
    public static bool Animations
    {
        get
        {
            int on = 1;
            N.g_object_get(N.gtk_settings_get_default(), "gtk-enable-animations", &on, 0);
            return on != 0;
        }
    }

    [UnmanagedCallersOnly]
    static void Plain(nint self, nint data)
    {
        try { Handler<Action>(data)(); }
        catch (Exception e) { Report(e); }
    }

    [UnmanagedCallersOnly]
    static void WithSelf(nint self, nint data)
    {
        try { Handler<Action<nint>>(data)(self); }
        catch (Exception e) { Report(e); }
    }

    [UnmanagedCallersOnly]
    static void WithArg(nint self, nint arg, nint data)
    {
        try { Handler<Action<nint>>(data)(arg); }
        catch (Exception e) { Report(e); }
    }

    [UnmanagedCallersOnly]
    static void Draw(nint area, nint cr, int w, int h, nint data)
    {
        try { Handler<Action<nint, int, int>>(data)(cr, w, h); }
        catch (Exception e) { Report(e); }
    }

    [UnmanagedCallersOnly]
    static int Key(nint self, uint keyval, uint keycode, uint state, nint data)
    {
        try { return Handler<Func<uint, uint, bool>>(data)(keyval, state) ? 1 : 0; }
        catch (Exception e) { Report(e); return 0; }
    }

    [UnmanagedCallersOnly]
    static int Timeout(nint data)
    {
        try { Handler<Action>(data)(); }
        catch (Exception e) { Report(e); }
        return 0; // G_SOURCE_REMOVE: once
    }

    [UnmanagedCallersOnly]
    static int Tick(nint widget, nint clock, nint data)
    {
        try { return Handler<Func<double, bool>>(data)(N.gdk_frame_clock_get_frame_time(clock) / 1e6) ? 1 : 0; }
        catch (Exception e) { Report(e); return 0; }
    }

    // Everything posted so far, in order; then the idle source ends.
    [UnmanagedCallersOnly]
    static int RunPosted(nint data)
    {
        while (true)
        {
            Action? action;
            lock (posted)
            {
                if (!posted.TryDequeue(out action))
                {
                    postPending = false;
                    return 0; // G_SOURCE_REMOVE
                }
            }
            try { action(); }
            catch (Exception e) { Report(e); }
        }
    }

    // A timer or tick callback that's done drops its delegate.
    [UnmanagedCallersOnly]
    static void Forget(nint data)
    {
        handlers[(int)data] = null;
        free.Push((int)data);
    }

    // An exception must never unwind into GTK: the callbacks catch and report it.
    static void Report(Exception e) => Console.Error.WriteLine(e);

    // ---- widgets -----------------------------------------------------------

    public const int Horizontal = 0, Vertical = 1;
    public const int Fill = 0, Start = 1, End = 2, Center = 3;

    public static string Str(nint p) => p == 0 ? "" : Marshal.PtrToStringUTF8(p) ?? "";

    public static nint Css(nint w, params string[] classes)
    {
        foreach (var c in classes) if (c.Length > 0) N.gtk_widget_add_css_class(w, c);
        return w;
    }

    public static void Class(nint w, string css, bool on)
    {
        if (on) N.gtk_widget_add_css_class(w, css);
        else N.gtk_widget_remove_css_class(w, css);
    }

    public static nint Box(int orientation, int spacing, params string[] classes) => Css(N.gtk_box_new(orientation, spacing), classes);

    public static nint Add(nint box, params nint[] children)
    {
        foreach (var c in children) N.gtk_box_append(box, c);
        return box;
    }

    public static nint Label(string text, params string[] classes)
    {
        var l = Css(N.gtk_label_new(text), classes);
        N.gtk_label_set_xalign(l, 0);
        return l;
    }

    public static nint Markup(string markup, params string[] classes)
    {
        var l = Label("", classes);
        N.gtk_label_set_markup(l, markup);
        return l;
    }

    // A button in the design's style (.btn): hover and press feedback come from the stylesheet.
    public static nint Button(string text, string? accessibleName, Action clicked, params string[] classes)
    {
        var b = Css(N.gtk_button_new_with_label(text), ["btn", .. classes]);
        if (accessibleName != null) Name(b, accessibleName);
        On(b, "clicked", clicked);
        return b;
    }

    // A button made with a label is labelled by that label, which wins over
    // the LABEL property, so screen readers would read the glyph: drop it first.
    public static void Name(nint widget, string name)
    {
        N.gtk_accessible_reset_relation(widget, 10); // GTK_ACCESSIBLE_RELATION_LABELLED_BY
        N.gtk_accessible_update_property(widget, 4, name, -1); // GTK_ACCESSIBLE_PROPERTY_LABEL
    }

    public static nint Spacer()
    {
        var s = N.gtk_box_new(Horizontal, 0);
        N.gtk_widget_set_hexpand(s, true);
        return s;
    }

    public static nint Area(int width, int height)
    {
        var a = N.gtk_drawing_area_new();
        if (width > 0) N.gtk_drawing_area_set_content_width(a, width);
        if (height > 0) N.gtk_drawing_area_set_content_height(a, height);
        return a;
    }

    public static string Escape(string text) => text.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;");

    // ---- cairo -------------------------------------------------------------

    public static void Rgb(nint cr, uint rgb, double alpha = 1) =>
        N.cairo_set_source_rgba(cr, (rgb >> 16 & 0xff) / 255.0, (rgb >> 8 & 0xff) / 255.0, (rgb & 0xff) / 255.0, alpha);

    public static void Stop(nint pattern, double offset, uint rgb, double alpha = 1) =>
        N.cairo_pattern_add_color_stop_rgba(pattern, offset, (rgb >> 16 & 0xff) / 255.0, (rgb >> 8 & 0xff) / 255.0, (rgb & 0xff) / 255.0, alpha);

    public static uint Mix(uint a, uint b, double t)
    {
        t = Math.Clamp(t, 0, 1);
        uint C(int shift) => (uint)Math.Round((a >> shift & 0xff) + ((int)(b >> shift & 0xff) - (int)(a >> shift & 0xff)) * t);
        return C(16) << 16 | C(8) << 8 | C(0);
    }

    // A capsule: a w × h rectangle with fully round ends, top-left at (x, y).
    public static void Capsule(nint cr, double x, double y, double w, double h)
    {
        var r = Math.Min(w, h) / 2;
        N.cairo_new_path(cr);
        N.cairo_arc(cr, x + w - r, y + r, r, -Math.PI / 2, 0);
        N.cairo_arc(cr, x + w - r, y + h - r, r, 0, Math.PI / 2);
        N.cairo_arc(cr, x + r, y + h - r, r, Math.PI / 2, Math.PI);
        N.cairo_arc(cr, x + r, y + r, r, Math.PI, 3 * Math.PI / 2);
        N.cairo_close_path(cr);
    }
}
