namespace Nattsang.Core;

// What plays next, in order. Duplicates are fine: a track can be queued twice.
public sealed class Queue
{
    readonly List<Track> items = [];

    public event Action? Changed;

    public Queue(IEnumerable<Track>? tracks = null)
    {
        if (tracks != null) items.AddRange(tracks);
    }

    public IReadOnlyList<Track> Items => items;
    public int Count => items.Count;
    public int TotalSeconds => items.Sum(t => t.Seconds);

    // "7 tracks · 18:38"
    public string Summary => $"{Count} {(Count == 1 ? "track" : "tracks")} · {Time.Format(TotalSeconds)}";

    public void Add(Track track)
    {
        items.Add(track);
        Changed?.Invoke();
    }

    public void RemoveAt(int index)
    {
        if (index < 0 || index >= items.Count) return;
        items.RemoveAt(index);
        Changed?.Invoke();
    }

    public void Clear()
    {
        if (items.Count == 0) return;
        items.Clear();
        Changed?.Invoke();
    }

    // Fisher-Yates.
    public void Shuffle(Random random)
    {
        if (items.Count < 2) return;
        for (int i = items.Count - 1; i > 0; i--)
        {
            var j = random.Next(i + 1);
            (items[i], items[j]) = (items[j], items[i]);
        }
        Changed?.Invoke();
    }
}
