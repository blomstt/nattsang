using System.Runtime.InteropServices;
using Nattsang.Core;
using static Nattsang.Gui.Ui;

namespace Nattsang.Gui;

// Where the library's list goes: the list alone, or, with a playlist open,
// the playlists' names at the left, a divider, and the open one's songs
// under its name. The names are as wide as the divider was dragged to
// (PlaylistColumn: 180 to 520 px, remembered between launches), narrower
// when the panel hasn't room for that and the songs. The divider takes a
// drag anywhere along it: a 13 px strip with its 1 px line in the middle,
// lit while the pointer is on it.
sealed unsafe partial class App
{
    const int Grip = 13;
    nint split, namesScroll, namesList, divider, songs, songsHead, songsName, songsCount;
    Listing? names; // the playlists, while one is open beside them
    int playlistWidth, dragWidth;
    bool dividerLit, dragging;

    sealed class NameRow
    {
        public nint Row, Name, Count;
        public string Uri = "";
    }
    readonly List<NameRow> nameRows = [];

    nint BuildSplit()
    {
        namesList = Css(N.gtk_list_box_new(), "names");
        N.gtk_list_box_set_selection_mode(namesList, 0); // GTK_SELECTION_NONE
        N.gtk_list_box_set_activate_on_single_click(namesList, true);
        OnArg(namesList, "row-activated", row => OpenPlaylist(N.gtk_list_box_row_get_index(row)));
        namesScroll = N.gtk_scrolled_window_new();
        N.gtk_scrolled_window_set_policy(namesScroll, 2, 1); // never, automatic
        N.gtk_scrolled_window_set_child(namesScroll, namesList);

        divider = Area(Grip, -1);
        OnDraw(divider, DrawDivider);
        N.gtk_widget_set_cursor_from_name(divider, "col-resize");
        Name(divider, "Resize the playlist column");
        OnHover(divider, on => { dividerLit = on; N.gtk_widget_queue_draw(divider); });
        OnDrag(divider, (_, _) =>
        {
            dragging = true;
            dragWidth = N.gtk_widget_get_width(namesScroll); // from where it's drawn, which may be squeezed
            N.gtk_widget_queue_draw(divider);
        }, (dx, _) =>
        {
            var w = PlaylistColumn.Clamp((int)Math.Round(dragWidth + dx));
            if (w == playlistWidth) return;
            playlistWidth = w;
            N.gtk_widget_queue_resize(split);
        }, () =>
        {
            dragging = false;
            N.gtk_widget_queue_draw(divider);
            remembered.PlaylistWidth = playlistWidth;
            Remember();
        });

        songsName = Label("", "songs-name");
        N.gtk_label_set_ellipsize(songsName, 3);
        N.gtk_widget_set_hexpand(songsName, true);
        songsCount = Label("", "songs-count");
        songsHead = Add(Box(Horizontal, 10, "songs-head"), songsName, songsCount);
        songs = Add(Box(Vertical, 10), songsHead, note, listScroll, moreNote);

        split = N.gtk_box_new(Horizontal, 0);
        N.gtk_widget_set_layout_manager(split, N.gtk_custom_layout_new(0,
            (nint)(delegate* unmanaged<nint, int, int, int*, int*, int*, int*, void>)&MeasureSplit,
            (nint)(delegate* unmanaged<nint, int, int, int, void>)&AllocateSplit));
        N.gtk_widget_set_vexpand(split, true);
        N.gtk_widget_set_parent(namesScroll, split);
        N.gtk_widget_set_parent(divider, split);
        N.gtk_widget_set_parent(songs, split);
        ShowSplit();
        return split;
    }

    // Beside the names only right under the playlists tab: an album or a
    // search opened from there has the panel to itself.
    bool Splits => nav.Under == Keys.Playlists && nav.Path.Count == 2;

    void ShowSplit()
    {
        var on = Splits;
        names = on ? lists.Open(Keys.Playlists, Keys.Playlists) : null;
        N.gtk_widget_set_visible(namesScroll, on);
        N.gtk_widget_set_visible(divider, on);
        N.gtk_widget_set_visible(songsHead, on);
        Class(songs, "songs", on);
        if (on) ShowNames();
        ShowSongsHead();
    }

    // The names, the open one marked; the rest of them asked for as they
    // come, so they're all there to pick from.
    void ShowNames()
    {
        if (names == null) return;
        var open = nav.Here.Key;
        for (int i = 0; i < names.Entries.Count; i++)
        {
            var e = names.Entries[i];
            if (i == nameRows.Count) nameRows.Add(NameRowFor());
            var r = nameRows[i];
            var here = e.Uri == open;
            N.gtk_label_set_text(r.Name, here ? "▸ " + Keys.Named(e.Name) : Keys.Named(e.Name));
            N.gtk_label_set_text(r.Count, e.Side);
            Class(r.Row, "open", here);
            r.Uri = e.Uri;
        }
        while (nameRows.Count > names.Entries.Count)
        {
            N.gtk_list_box_remove(namesList, nameRows[^1].Row);
            nameRows.RemoveAt(nameRows.Count - 1);
        }
        if (names.More && !names.Loading) lists.More(names);
    }

    NameRow NameRowFor()
    {
        var r = new NameRow { Name = Label(""), Count = Label("", "count") };
        N.gtk_label_set_ellipsize(r.Name, 3);
        N.gtk_widget_set_hexpand(r.Name, true);
        N.gtk_label_set_xalign(r.Count, 1);
        var box = Add(Box(Horizontal, 10), r.Name, r.Count);
        N.gtk_list_box_append(namesList, box);
        r.Row = N.gtk_widget_get_parent(box); // the row the list box put it in
        return r;
    }

    // The open playlist's name, and how many songs it has (as its row
    // says, else as many as are in).
    void ShowSongsHead()
    {
        if (!Splits) return;
        var count = names?.Entries.Find(e => e.Uri == shown.Key)?.Side is { Length: > 0 } side && int.TryParse(side, out var n) ? n : shown.Count;
        N.gtk_label_set_text(songsName, Keys.Named(shown.Title));
        N.gtk_label_set_text(songsCount, count == 1 ? "1 song" : $"{count} songs");
    }

    // A name picked: that playlist instead of the open one.
    void OpenPlaylist(int index)
    {
        if (names == null || index < 0 || index >= names.Entries.Count) return;
        var e = names.Entries[index];
        if (e.Uri == nav.Here.Key) { FocusList(); return; }
        Leave();
        nav.BackTo(0);
        nav.Open(e.Uri, Keys.Named(e.Name));
        ShowHere();
    }

    void DrawDivider(nint cr, int width, int height)
    {
        Rgb(cr, dividerLit || dragging ? palette.Accent : palette.Border);
        N.cairo_rectangle(cr, Grip / 2, 0, 1, height);
        N.cairo_fill(cr);
    }

    // ---- layout ------------------------------------------------------------------

    // The width the names get in a split of width.
    int NamesWidth(int width) => PlaylistColumn.Shown(playlistWidth, width, Grip);

    // As Wrap's: no exception gets into GTK; a failed measure asks for nothing.
    [UnmanagedCallersOnly]
    static void MeasureSplit(nint widget, int orientation, int forSize, int* min, int* nat, int* minBaseline, int* natBaseline)
    {
        *min = *nat = 0;
        *minBaseline = *natBaseline = -1;
        try
        {
            var a = fitted!;
            var beside = N.gtk_widget_get_visible(a.namesScroll);
            int m, n;
            if (orientation == Horizontal)
            {
                N.gtk_widget_measure(a.songs, Horizontal, -1, &m, &n, null, null);
                *min = m + (beside ? PlaylistColumn.Min + Grip : 0);
                *nat = n + (beside ? PlaylistColumn.Clamp(a.playlistWidth) + Grip : 0);
                return;
            }
            var namesW = beside && forSize >= 0 ? a.NamesWidth(forSize) : 0;
            N.gtk_widget_measure(a.songs, Vertical, forSize >= 0 ? Math.Max(0, forSize - (beside ? namesW + Grip : 0)) : -1, &m, &n, null, null);
            *min = m;
            *nat = n;
            if (!beside) return;
            N.gtk_widget_measure(a.namesScroll, Vertical, beside && forSize >= 0 ? namesW : -1, &m, &n, null, null);
            *min = Math.Max(*min, m);
            *nat = Math.Max(*nat, n);
        }
        catch (Exception e)
        {
            Console.Error.WriteLine(e);
            *min = *nat = 0;
        }
    }

    [UnmanagedCallersOnly]
    static void AllocateSplit(nint widget, int width, int height, int baseline)
    {
        try
        {
            var a = fitted!;
            var point = stackalloc float[2];
            void Place(nint child, int x, int w)
            {
                int m, n;
                N.gtk_widget_measure(child, Vertical, w, &m, &n, null, null); // GTK wants children measured before they're allocated
                point[0] = x;
                point[1] = 0;
                N.gtk_widget_allocate(child, w, height, -1, N.gsk_transform_translate(0, point));
            }
            if (!N.gtk_widget_get_visible(a.namesScroll))
            {
                Place(a.songs, 0, width);
                return;
            }
            var namesW = a.NamesWidth(width);
            Place(a.namesScroll, 0, namesW);
            Place(a.divider, namesW, Grip);
            Place(a.songs, namesW + Grip, Math.Max(0, width - namesW - Grip));
        }
        catch (Exception e) { Console.Error.WriteLine(e); }
    }
}
