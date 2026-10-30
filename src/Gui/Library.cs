using Nattsang.Core;
using static Nattsang.Gui.Ui;

namespace Nattsang.Gui;

// The library panel: search, the sections, and the album's tracks. A double
// click or Enter on a track plays it; `a` queues the selected one.
sealed unsafe partial class App
{
    nint search, trackList;
    readonly List<(nint Row, nint Number, nint Eq)> rows = [];

    nint BuildLibrary()
    {
        var slash = Label("/", "key");
        search = N.gtk_entry_new();
        N.gtk_entry_set_placeholder_text(search, "search songs, albums, artists");
        N.gtk_widget_set_hexpand(search, true);
        On(search, "changed", Filter);
        var searchBox = Add(Box(Horizontal, 8, "search"), slash, search);

        // Only the album has sample data; the other sections are there to look at.
        var filters = Add(Box(Horizontal, 22, "filters"),
            Button("liked", null, Nothing),
            Button("playlists", null, Nothing),
            Button("albums", null, Nothing, "label"),
            Button("artists", null, Nothing));

        trackList = Css(N.gtk_list_box_new(), "tracks");
        N.gtk_list_box_set_selection_mode(trackList, 1); // GTK_SELECTION_SINGLE
        N.gtk_list_box_set_activate_on_single_click(trackList, false);
        foreach (var t in player.Tracks)
        {
            var number = Label(t.Number.ToString());
            var eq = Area(36, 21);
            N.gtk_widget_set_visible(eq, false);
            OnDraw(eq, DrawEq);
            eqAreas.Add(eq);
            var cell = Add(Box(Horizontal, 0), number, eq);
            N.gtk_widget_set_size_request(cell, 36, -1);
            var title = Label(t.Title);
            N.gtk_widget_set_hexpand(title, true);
            N.gtk_label_set_ellipsize(title, 3); // PANGO_ELLIPSIZE_END
            var time = Label(Time.Format(t.Seconds));
            N.gtk_label_set_xalign(time, 1);
            N.gtk_widget_set_size_request(time, 56, -1);
            N.gtk_list_box_append(trackList, Add(Box(Horizontal, 8), cell, title, time));
            rows.Add((N.gtk_list_box_get_row_at_index(trackList, rows.Count), number, eq));
        }
        OnArg(trackList, "row-activated", row => player.Play(N.gtk_list_box_row_get_index(row)));

        var scroll = N.gtk_scrolled_window_new();
        N.gtk_scrolled_window_set_policy(scroll, 2, 1); // never, automatic
        N.gtk_scrolled_window_set_child(scroll, trackList);
        N.gtk_widget_set_vexpand(scroll, true);

        var panel = Add(Box(Vertical, 10, "panel", "library"), searchBox, filters, Box(Horizontal, 0, "rule"), scroll);
        N.gtk_widget_set_hexpand(panel, true);
        return Titled(panel, $"library › albums › {Sample.Album}");
    }

    static readonly Action Nothing = () => { };

    // A panel with its title cut into the top border.
    static nint Titled(nint panel, string title)
    {
        var overlay = N.gtk_overlay_new();
        N.gtk_overlay_set_child(overlay, panel);
        var label = Label(title, "panel-title");
        N.gtk_widget_set_halign(label, Start);
        N.gtk_widget_set_valign(label, Start);
        N.gtk_widget_set_can_target(label, false);
        N.gtk_overlay_add_overlay(overlay, label);
        return overlay;
    }

    void MarkCurrentRow()
    {
        for (int i = 0; i < rows.Count; i++)
        {
            var current = i == player.Index;
            Class(rows[i].Row, "current", current);
            N.gtk_widget_set_visible(rows[i].Number, !current);
            N.gtk_widget_set_visible(rows[i].Eq, current);
        }
    }

    // Shows the tracks whose title, artist or album has the search text in it.
    void Filter()
    {
        var text = Str(N.gtk_editable_get_text(search)).Trim();
        for (int i = 0; i < rows.Count; i++)
        {
            var t = player.Tracks[i];
            var match = text.Length == 0
                || t.Title.Contains(text, StringComparison.OrdinalIgnoreCase)
                || t.Artist.Contains(text, StringComparison.OrdinalIgnoreCase)
                || t.Album.Contains(text, StringComparison.OrdinalIgnoreCase);
            N.gtk_widget_set_visible(rows[i].Row, match);
        }
    }

    void FocusList()
    {
        var row = N.gtk_list_box_get_selected_row(trackList);
        if (row == 0 || !N.gtk_widget_get_visible(row))
        {
            row = 0;
            for (int i = 0; i < rows.Count && row == 0; i++)
                if (N.gtk_widget_get_visible(rows[(i + player.Index) % rows.Count].Row)) row = rows[(i + player.Index) % rows.Count].Row;
            if (row != 0) N.gtk_list_box_select_row(trackList, row);
        }
        N.gtk_widget_grab_focus(row != 0 ? row : trackList);
    }

    void AddSelected()
    {
        var row = N.gtk_list_box_get_selected_row(trackList);
        if (row == 0 || !N.gtk_widget_get_visible(row)) return;
        queue.Add(player.Tracks[N.gtk_list_box_row_get_index(row)]);
    }
}
