using System.Runtime.InteropServices;
using Nattsang.Playback;
using static Nattsang.Gui.Ui;

namespace Nattsang.Gui;

// MPRIS (org.mpris.MediaPlayer2 and its .Player) on the session bus, so
// media keys, playerctl and status bars see what plays and steer it. It
// uses the application's own bus connection (GApplication has one) and
// GDBus on the main loop: calls come in on the main thread, and it says
// what changed (PropertiesChanged, Seeked) when the player changes, never
// by itself. Playback.Mpris is the mapping; this is the D-Bus side.
sealed unsafe class MprisBus
{
    const string ObjectPath = "/org/mpris/MediaPlayer2";
    const string Root = "org.mpris.MediaPlayer2", Player = "org.mpris.MediaPlayer2.Player";

    // Volume and Rate are writable, as the spec has them; a write does nothing.
    const string Xml = """
        <node>
          <interface name="org.mpris.MediaPlayer2">
            <method name="Raise"/>
            <method name="Quit"/>
            <property name="CanQuit" type="b" access="read"/>
            <property name="CanRaise" type="b" access="read"/>
            <property name="HasTrackList" type="b" access="read"/>
            <property name="Identity" type="s" access="read"/>
            <property name="SupportedUriSchemes" type="as" access="read"/>
            <property name="SupportedMimeTypes" type="as" access="read"/>
          </interface>
          <interface name="org.mpris.MediaPlayer2.Player">
            <method name="Next"/>
            <method name="Previous"/>
            <method name="Pause"/>
            <method name="PlayPause"/>
            <method name="Stop"/>
            <method name="Play"/>
            <method name="Seek"><arg direction="in" type="x" name="Offset"/></method>
            <method name="SetPosition"><arg direction="in" type="o" name="TrackId"/><arg direction="in" type="x" name="Position"/></method>
            <method name="OpenUri"><arg direction="in" type="s" name="Uri"/></method>
            <signal name="Seeked"><arg type="x" name="Position"/></signal>
            <property name="PlaybackStatus" type="s" access="read"/>
            <property name="Rate" type="d" access="readwrite"/>
            <property name="Metadata" type="a{sv}" access="read"/>
            <property name="Volume" type="d" access="readwrite"/>
            <property name="Position" type="x" access="read"/>
            <property name="MinimumRate" type="d" access="read"/>
            <property name="MaximumRate" type="d" access="read"/>
            <property name="CanGoNext" type="b" access="read"/>
            <property name="CanGoPrevious" type="b" access="read"/>
            <property name="CanPlay" type="b" access="read"/>
            <property name="CanPause" type="b" access="read"/>
            <property name="CanSeek" type="b" access="read"/>
            <property name="CanControl" type="b" access="read"/>
          </interface>
        </node>
        """;

    // What PropertiesChanged tells about: everything of the player's that
    // changes but Position (which moves by itself, as the spec says).
    static readonly string[] Followed = ["PlaybackStatus", "Metadata", "CanGoNext", "CanGoPrevious", "CanPlay", "CanPause", "CanSeek"];

    static MprisBus? live; // the callbacks find it here: one window, one of these

    readonly IPlayback player;
    readonly Action raise, quit;
    readonly nint connection;
    uint rootId, playerId, nameId;
    readonly Dictionary<string, string> told = []; // what was last said of each followed property
    string lastUri = "";
    double lastPosition, lastAt;
    bool lastPlaying;

    MprisBus(nint connection, IPlayback player, Action raise, Action quit)
    {
        this.connection = connection;
        this.player = player;
        this.raise = raise;
        this.quit = quit;
    }

    // On the application's connection, as Nattsang (or, for a second
    // window, an instance of it); null when there's no bus.
    public static MprisBus? Start(nint app, IPlayback player, Action raise, Action quit, bool second)
    {
        var connection = N.g_application_get_dbus_connection(app);
        if (connection == 0) return null;
        nint error = 0;
        var node = N.g_dbus_node_info_new_for_xml(Xml, &error); // kept: the registrations use it
        if (node == 0) { Fail("can't read its interface", error); return null; }
        var vtable = (nint*)NativeMemory.AllocZeroed(11, (nuint)sizeof(nint)); // kept as long as the objects
        vtable[0] = (nint)(delegate* unmanaged<nint, nint, nint, nint, nint, nint, nint, nint, void>)&Call;
        vtable[1] = (nint)(delegate* unmanaged<nint, nint, nint, nint, nint, nint*, nint, nint>)&Get;
        vtable[2] = (nint)(delegate* unmanaged<nint, nint, nint, nint, nint, nint, nint*, nint, int>)&Set;

        var bus = new MprisBus(connection, player, raise, quit);
        bus.rootId = N.g_dbus_connection_register_object(connection, ObjectPath, N.g_dbus_node_info_lookup_interface(node, Root), (nint)vtable, 0, 0, &error);
        if (bus.rootId != 0)
            bus.playerId = N.g_dbus_connection_register_object(connection, ObjectPath, N.g_dbus_node_info_lookup_interface(node, Player), (nint)vtable, 0, 0, &error);
        if (bus.playerId == 0)
        {
            Fail("can't put its object on the bus", error);
            bus.Stop();
            return null;
        }
        bus.nameId = N.g_bus_own_name_on_connection(connection, Mpris.BusName(second, Environment.ProcessId), 0, 0, 0, 0, 0);
        live = bus;
        player.Changed += bus.Changed;
        player.Order.Queue.Changed += bus.Changed;
        bus.Changed(); // what it says from now on is what changed from here
        return bus;
    }

    public void Stop()
    {
        player.Changed -= Changed;
        player.Order.Queue.Changed -= Changed;
        if (nameId != 0) N.g_bus_unown_name(nameId);
        if (playerId != 0) N.g_dbus_connection_unregister_object(connection, playerId);
        if (rootId != 0) N.g_dbus_connection_unregister_object(connection, rootId);
        nameId = playerId = rootId = 0;
        if (live == this) live = null;
    }

    static void Fail(string what, nint error)
    {
        Console.Error.WriteLine($"nattsang: MPRIS {what}: {(error != 0 ? Str(*(nint*)(error + 8)) : "")}");
        if (error != 0) N.g_error_free(error);
    }

    // ---- what it says -----------------------------------------------------------------

    // A property's value: a string, bool, double, long, string[] or the
    // metadata's list (Mpris.Metadata).
    object Value(string name) => name switch
    {
        "CanQuit" or "CanRaise" or "CanControl" => true,
        "HasTrackList" => false,
        "Identity" => Mpris.Identity,
        "SupportedUriSchemes" or "SupportedMimeTypes" => Array.Empty<string>(),
        "PlaybackStatus" => Mpris.Status(player),
        "Rate" or "Volume" or "MinimumRate" or "MaximumRate" => 1.0,
        "Metadata" => Mpris.Metadata(player.Track, player.Cover),
        "Position" => Mpris.Microseconds(player.Position),
        "CanGoNext" or "CanGoPrevious" or "CanPause" => player.Track.Uri.Length > 0,
        "CanPlay" => player.Track.Uri.Length > 0 || player.Order.Queue.Count > 0,
        "CanSeek" => !player.Stopped && player.Track.Seconds > 0,
        _ => throw new ArgumentException($"no property {name}"),
    };

    // The player changed: which followed properties did, and whether the
    // position jumped (a seek) rather than ran on.
    void Changed()
    {
        var changed = new List<(string, object)>();
        foreach (var name in Followed)
        {
            var v = Value(name);
            var said = Describe(v);
            if (told.TryGetValue(name, out var before) && before == said) continue;
            var first = !told.ContainsKey(name);
            told[name] = said;
            if (!first) changed.Add((name, v));
        }
        if (changed.Count > 0)
        {
            var args = stackalloc nint[] { N.g_variant_new_string(Player), Variant(changed), Variant(Array.Empty<string>()) };
            Emit("org.freedesktop.DBus.Properties", "PropertiesChanged", N.g_variant_new_tuple(args, 3));
        }

        // Back, or further on than the time since allows (a track that
        // plays may also stand still a moment, loading).
        var now = Clock;
        var pos = player.Position;
        var jumped = pos < lastPosition - 1 || pos > lastPosition + (lastPlaying ? now - lastAt : 0) + 1;
        if (player.Track.Uri == lastUri && lastUri.Length > 0 && jumped)
        {
            var at = stackalloc nint[] { N.g_variant_new_int64(Mpris.Microseconds(pos)) };
            Emit(Player, "Seeked", N.g_variant_new_tuple(at, 1));
        }
        (lastUri, lastPosition, lastAt, lastPlaying) = (player.Track.Uri, pos, now, player.Playing);
    }

    void Emit(string iface, string signal, nint parameters)
    {
        nint error = 0;
        if (!N.g_dbus_connection_emit_signal(connection, null, ObjectPath, iface, signal, parameters, &error)) Fail($"can't say {signal}", error);
    }

    // A value as text, to tell whether it changed. Each type by name: a
    // virtual ToString would keep every type's formatting in the binary.
    static string Describe(object v) => v switch
    {
        List<(string Key, object Value)> m => string.Join("\n", m.Select(e => e.Key + "=" + Describe(e.Value))),
        string[] a => string.Join("\t", a),
        string s => s,
        bool b => b ? "1" : "0",
        long l => l.ToString(System.Globalization.CultureInfo.InvariantCulture),
        Mpris.ObjectPath p => p.Path,
        _ => "?",
    };

    // A value as a (floating) GVariant.
    static nint Variant(object v)
    {
        switch (v)
        {
            case string s: return N.g_variant_new_string(s);
            case bool b: return N.g_variant_new_boolean(b ? 1 : 0);
            case double d: return N.g_variant_new_double(d);
            case long l: return N.g_variant_new_int64(l);
            case Mpris.ObjectPath p: return N.g_variant_new_object_path(p.Path);
            case string[] a:
            {
                var strings = stackalloc nint[a.Length + 1];
                for (int i = 0; i < a.Length; i++) strings[i] = Marshal.StringToCoTaskMemUTF8(a[i]);
                try { return N.g_variant_new_strv(strings, a.Length); }
                finally { for (int i = 0; i < a.Length; i++) Marshal.FreeCoTaskMem(strings[i]); }
            }
            case List<(string Key, object Value)> m:
            {
                var entries = new nint[m.Count];
                for (int i = 0; i < m.Count; i++)
                    entries[i] = N.g_variant_new_dict_entry(N.g_variant_new_string(m[i].Key), N.g_variant_new_variant(Variant(m[i].Value)));
                var type = N.g_variant_type_new("{sv}");
                try { fixed (nint* e = entries) return N.g_variant_new_array(type, e, entries.Length); }
                finally { N.g_variant_type_free(type); }
            }
            default: throw new ArgumentException($"no variant for {v.GetType().Name}");
        }
    }

    // ---- what it's asked ----------------------------------------------------------------

    // A method: answered at once, then done (Quit, done first, would take
    // this object off the bus before the answer).
    void Called(string method, nint parameters, nint invocation)
    {
        if (method == "OpenUri")
        {
            N.g_dbus_method_invocation_return_dbus_error(invocation, "org.freedesktop.DBus.Error.NotSupported", "Nattsang doesn't open URIs");
            return;
        }
        long Int64(int i)
        {
            var c = N.g_variant_get_child_value(parameters, i);
            try { return N.g_variant_get_int64(c); }
            finally { N.g_variant_unref(c); }
        }
        string Text(int i)
        {
            var c = N.g_variant_get_child_value(parameters, i);
            try { return Str(N.g_variant_get_string(c, null)); }
            finally { N.g_variant_unref(c); }
        }
        // Read before answering: the answer may let go of the parameters.
        var offset = method == "Seek" ? Int64(0) : 0;
        var (trackId, position) = method == "SetPosition" ? (Text(0), Int64(1)) : ("", 0L);
        N.g_dbus_method_invocation_return_value(invocation, 0);
        switch (method)
        {
            case "Raise": raise(); break;
            case "Quit": quit(); break;
            case "Next": player.Next(); break;
            case "Previous": player.Previous(); break;
            case "PlayPause": player.Toggle(); break;
            case "Play": if (!player.Playing) player.Toggle(); break;
            case "Pause": if (player.Playing) player.Toggle(); break;
            case "Stop": player.Stop(); break;
            case "Seek":
                if (player.Stopped) break;
                if (Mpris.SeekTo(player.Position, offset, player.Track.Seconds) is { } to) player.Seek(to);
                else player.Next();
                break;
            case "SetPosition":
                if (!player.Stopped && Mpris.SetTo(player.Track, trackId, position) is { } at) player.Seek(at);
                break;
        }
    }

    [UnmanagedCallersOnly]
    static void Call(nint connection, nint sender, nint path, nint iface, nint method, nint parameters, nint invocation, nint data)
    {
        try
        {
            if (live != null) live.Called(Str(method), parameters, invocation);
            else N.g_dbus_method_invocation_return_dbus_error(invocation, "org.freedesktop.DBus.Error.Failed", "closing");
        }
        catch (Exception e) { Console.Error.WriteLine(e); }
    }

    [UnmanagedCallersOnly]
    static nint Get(nint connection, nint sender, nint path, nint iface, nint name, nint* error, nint data)
    {
        try { return live != null ? Variant(live.Value(Str(name))) : 0; }
        catch (Exception e) { Console.Error.WriteLine(e); return 0; }
    }

    // Volume and Rate stay 1: setting them does nothing.
    [UnmanagedCallersOnly]
    static int Set(nint connection, nint sender, nint path, nint iface, nint name, nint value, nint* error, nint data) => 1;
}
