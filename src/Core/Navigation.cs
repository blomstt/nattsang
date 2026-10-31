namespace Nattsang.Core;

// Where the library panel is: a tab, and what was opened from it, one
// place after another ("library › artists › Linkin Park › Meteora"). Going
// back drops the last place; a tab starts over from its top.
public sealed class Navigation
{
    public sealed record Place(string Key, string Name);

    readonly List<Place> path = [];

    public Navigation(string tab) => Tab(tab);

    public IReadOnlyList<Place> Path => path;
    public Place Here => path[^1];

    // The tab it's under: the first place.
    public string Under => path[0].Key;

    public void Tab(string tab)
    {
        path.Clear();
        path.Add(new(tab, tab));
    }

    // Opens a place from here. Opening where it already is does nothing; a
    // search opened from a search's results takes that search's place.
    public void Open(string key, string name)
    {
        if (Here.Key == key) return;
        if (Keys.IsSearch(key) && Keys.IsSearch(Here.Key)) path.RemoveAt(path.Count - 1);
        path.Add(new(key, name));
    }

    // Back one place; false at a tab's top, where there's nowhere to go.
    public bool Back()
    {
        if (path.Count < 2) return false;
        path.RemoveAt(path.Count - 1);
        return true;
    }

    // Back to the place at depth (0 is the tab); false when it's already there.
    public bool BackTo(int depth)
    {
        if (depth < 0 || depth >= path.Count - 1) return false;
        path.RemoveRange(depth + 1, path.Count - depth - 1);
        return true;
    }

    // A place's name once the player has said what it's called.
    public void Rename(string key, string name)
    {
        if (name.Length == 0) return;
        for (int i = 1; i < path.Count; i++)
            if (path[i].Key == key && !Keys.IsSearch(key)) path[i] = path[i] with { Name = name };
    }

    // "library › playlists › Night drive"
    public string Crumbs => "library › " + string.Join(" › ", path.Select(p => p.Name));

    // A search's place is named for its words.
    public static string SearchName(string words) => $"search “{words.Trim()}”";
}
