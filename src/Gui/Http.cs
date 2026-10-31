using System.Text;
using static Nattsang.Gui.Ui;

namespace Nattsang.Gui;

// GET requests through libsoup, on the GLib main loop: nothing waits, no
// thread, and the answer comes back on the main thread. The lyrics and the
// cover use it. Without libsoup 3 on the system, it says so to every request.
sealed unsafe class Http
{
    readonly nint session;
    readonly string? missing; // why there's no session

    public Http(string userAgent)
    {
        try
        {
            session = N.soup_session_new();
            N.soup_session_set_user_agent(session, userAgent);
            N.soup_session_set_timeout(session, 20);
        }
        catch (DllNotFoundException) { missing = "the lyrics need libsoup 3, which isn't installed"; }
    }

    // done(status, body) once the answer has come: status 0 when none did,
    // the body then saying why.
    public void Get(string url, Action<int, string> done) =>
        Fetch(url, (status, body, why) => done(status, body != null ? Encoding.UTF8.GetString(body) : why));

    // done(status, body, why): the body's bytes, or null and why there's none.
    public void Fetch(string url, Action<int, byte[]?, string> done)
    {
        if (session == 0) { done(0, null, missing ?? "no session"); return; }
        var message = N.soup_message_new("GET", url);
        if (message == 0) { done(0, null, "not a URL"); return; }
        N.soup_session_send_and_read_async(session, message, 0, 0, AsyncReady, Once((source, result) =>
        {
            nint error = 0;
            var bytes = N.soup_session_send_and_read_finish(source, result, &error);
            var status = (int)N.soup_message_get_status(message);
            N.g_object_unref(message);
            if (bytes == 0)
            {
                var why = error != 0 ? Str(*(nint*)(error + 8)) : "no answer";
                if (error != 0) N.g_error_free(error);
                done(0, null, why);
                return;
            }
            nint size;
            var data = N.g_bytes_get_data(bytes, &size);
            var body = data == 0 ? [] : new ReadOnlySpan<byte>((byte*)data, (int)size).ToArray();
            N.g_bytes_unref(bytes);
            done(status, body, "");
        }));
    }
}
