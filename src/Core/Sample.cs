namespace Nattsang.Core;

// The album the window shows until it can talk to a player: Meteora with
// the durations the design was drawn with, Numb playing at 1:12.
public static class Sample
{
    public const string Artist = "Linkin Park", Album = "Meteora (Bonus Edition)";
    public const int Playing = 12, Position = 72, QueueLength = 7;

    static readonly (string Title, string Time)[] list =
    [
        ("Foreword", "0:13"), ("Don't Stay", "3:07"), ("Somewhere I Belong", "3:33"),
        ("Lying from You", "2:55"), ("Hit the Floor", "2:44"), ("Easier to Run", "3:24"),
        ("Faint", "2:42"), ("Figure.09", "3:17"), ("Breaking the Habit", "3:16"),
        ("From the Inside", "2:55"), ("Nobody's Listening", "2:58"), ("Session", "2:24"),
        ("Numb", "3:07"),
    ];

    public static readonly Track[] Tracks = [.. list.Select((t, i) => new Track(i + 1, t.Title, Artist, Album, Time.Parse(t.Time)))];

    public static IEnumerable<Track> Queue => Tracks.Take(QueueLength);
}
