using System.Runtime.InteropServices;
using System.Text;
using static Nattsang.Gui.Ui;

namespace Nattsang.Gui;

// The player helper (helper/): a child process spoken to over its stdin and
// stdout, a line each way (docs/protocol.md); its stderr is ours. Its output
// is read on the GLib main loop as it comes, so nothing waits or polls. It
// lives as long as the window: Stop closes its stdin, and it exits.
sealed unsafe class Helper(string path)
{
    const int DoNotReapChild = 1 << 1, CloexecPipes = 1 << 8; // GSpawnFlags
    const int EINTR = 4, EAGAIN = 11;

    int pid, input = -1, output = -1;
    uint reading, watching;
    readonly List<byte> partial = []; // a line read only in part, so far
    Action<string> received = _ => { }, ended = _ => { };

    // Where NATTSANG_HELPER says, or next to the binary (the build copies it
    // there); null when there's none.
    public static string? Find()
    {
        var set = Environment.GetEnvironmentVariable("NATTSANG_HELPER");
        if (!string.IsNullOrEmpty(set)) return set;
        var beside = Path.Combine(AppContext.BaseDirectory, "nattsang-helper");
        return File.Exists(beside) ? beside : null;
    }

    // received(line) for every line it writes; ended(why) once, when it has
    // exited, after its last lines.
    public void Start(Action<string> received, Action<string> ended)
    {
        this.received = received;
        this.ended = ended;
        var file = Marshal.StringToCoTaskMemUTF8(path);
        try
        {
            var argv = stackalloc nint[] { file, 0 };
            int p, i, o;
            nint error = 0;
            if (!N.g_spawn_async_with_pipes(0, argv, null, DoNotReapChild | CloexecPipes, 0, 0, &p, &i, &o, null, &error))
            {
                var message = error != 0 ? Str(*(nint*)(error + 8)) : "";
                if (error != 0) N.g_error_free(error);
                ended($"It couldn't start ({message}).");
                return;
            }
            (pid, input, output) = (p, i, o);
        }
        finally { Marshal.FreeCoTaskMem(file); }
        reading = OnReadable(output, Read);
        watching = OnExit(pid, Exited);
    }

    // Writes a command line. Once it has gone, commands go nowhere.
    public void Send(string line)
    {
        if (input < 0) return;
        var bytes = Encoding.UTF8.GetBytes(line);
        fixed (byte* b = bytes)
            for (nint done = 0; done < bytes.Length;)
            {
                var n = N.write(input, b + done, bytes.Length - done);
                if (n >= 0) { done += n; continue; }
                if (Marshal.GetLastPInvokeError() == EINTR) continue;
                CloseInput(); // EPIPE: it has gone, and Exited will say so
                return;
            }
    }

    // The window is closing: its stdin closes, and it exits by itself.
    public void Stop()
    {
        Cancel(ref reading);
        Cancel(ref watching);
        CloseInput();
        CloseOutput();
    }

    // One read per wake-up: GLib wakes us again while there's more. At the
    // end the reading stops; Exited says it has gone.
    bool Read()
    {
        if (ReadSome() != 0) return true;
        reading = 0;
        CloseOutput();
        return false;
    }

    // Reads what's there and hands on each whole line: the bytes read, 0 at
    // the end (or a broken pipe), -1 when there's nothing just now.
    int ReadSome()
    {
        var buffer = stackalloc byte[4096];
        var n = (int)N.read(output, buffer, 4096);
        if (n < 0) return Marshal.GetLastPInvokeError() is EINTR or EAGAIN ? -1 : 0;
        var start = 0;
        for (var k = 0; k < n; k++)
        {
            if (buffer[k] != '\n') continue;
            partial.AddRange(new ReadOnlySpan<byte>(buffer + start, k - start));
            var line = Encoding.UTF8.GetString(CollectionsMarshal.AsSpan(partial));
            partial.Clear();
            start = k + 1;
            try { received(line); }
            catch (Exception e) { Console.Error.WriteLine(e); } // one bad line mustn't stop the reading
        }
        partial.AddRange(new ReadOnlySpan<byte>(buffer + start, n - start));
        return n;
    }

    // It has exited: what it wrote last is in the pipe, so read that (without
    // waiting: a process it started may still hold the pipe open), then say so.
    void Exited(int status)
    {
        watching = 0;
        N.g_spawn_close_pid(pid);
        CloseInput();
        if (output >= 0)
        {
            Cancel(ref reading);
            N.g_unix_set_fd_nonblocking(output, true, 0);
            while (ReadSome() > 0) { }
            CloseOutput();
        }
        ended((status & 0x7f) == 0 ? $"It exited with code {status >> 8 & 0xff}." : $"It was killed by signal {status & 0x7f}.");
    }

    void CloseOutput()
    {
        if (output >= 0) N.close(output);
        output = -1;
    }

    void CloseInput()
    {
        if (input >= 0) N.close(input);
        input = -1;
    }
}
