using System.Runtime.InteropServices;
using Nattsang.Core;
using Nattsang.Playback;
using static Nattsang.Gui.Ui;

namespace Nattsang.Gui;

// The library panel: search, the tabs, and the list of where it is, with
// the way there in its top border ("library › playlists › Night drive";
// a part clicked goes back there, as Backspace goes back one). A double
// click or Enter on a track plays the list from it; on an album, artist or
// playlist it opens it. `a` queues the selected track, `o` opens its album
// and `r` its artist. Typing in the search filters the list; Enter searches
// the songs.
//
// The list is a list view: only the rows on screen are made, and they're
// filled from the shown list as they scroll in, so a long list costs what
// a short one does. Its next page is asked for as the end comes near.
//
// An open playlist shows beside the playlists' names (Split): the divider
// between them can be dragged anywhere along it, and the names' width is
// remembered.
sealed unsafe partial class App
{
    nint search, trackList, crumbs, note, moreNote, listScroll;
    nint rowModel, rowSelection;
    readonly nint[] tabButtons = new nint[Keys.Tabs.Length];
    readonly Navigation nav;
    Listing shown = new("", "");
    readonly List<int> shownRows = []; // the list's rows the filter lets through, in order
    string filterText = "";

    // The rows the list view has made, by their list item, and what each
    // shows now (-1: nothing).
    sealed class RowWidgets
    {
        public nint Box, Cell, Number, Eq, Title, Side;
        public int Shows = -1;
    }
    readonly Dictionary<nint, RowWidgets> made = [];

    nint BuildLibrary()
    {
        var slash = Label("/", "key");
        search = N.gtk_entry_new();
        N.gtk_entry_set_placeholder_text(search, "search songs");
        N.gtk_widget_set_hexpand(search, true);
        On(search, "changed", Filter);
        var searchBox = Add(Box(Horizontal, 8, "search"), slash, search);

        var filters = Box(Horizontal, 22, "filters");
        for (int i = 0; i < Keys.Tabs.Length; i++)
        {
            var tab = Keys.Tabs[i];
            tabButtons[i] = Button(tab, null, () => ShowTab(tab));
            N.gtk_box_append(filters, tabButtons[i]);
        }

        rowModel = N.gtk_string_list_new(0);
        rowSelection = N.gtk_single_selection_new(rowModel);
        N.gtk_single_selection_set_autoselect(rowSelection, false);
        N.gtk_single_selection_set_can_unselect(rowSelection, true);
        var factory = N.gtk_signal_list_item_factory_new();
        OnArg(factory, "setup", MakeRow);
        OnArg(factory, "bind", FillRow);
        OnArg(factory, "unbind", item => { if (made.TryGetValue(item, out var r)) r.Shows = -1; });
        OnArg(factory, "teardown", ForgetRow);
        trackList = Css(N.gtk_list_view_new(rowSelection, factory), "tracks");
        OnPosition(trackList, "activate", position => Activate((int)position));

        listScroll = N.gtk_scrolled_window_new();
        N.gtk_scrolled_window_set_policy(listScroll, 2, 1); // never, automatic
        N.gtk_scrolled_window_set_child(listScroll, trackList);
        N.gtk_widget_set_vexpand(listScroll, true);
        var adjustment = N.gtk_scrolled_window_get_vadjustment(listScroll);
        On(adjustment, "value-changed", NearEnd);
        On(adjustment, "changed", NearEnd);

        // Loading, empty and failed lists say so where the rows go; a page
        // on its way after the rows, below them.
        note = Label("", "note");
        moreNote = Label("", "note");
        N.gtk_label_set_wrap(note, true);
        N.gtk_label_set_wrap(moreNote, true);

        var panel = Add(Box(Vertical, 10, "panel", "library"), searchBox, filters, Box(Horizontal, 0, "rule"), BuildSplit());
        N.gtk_widget_set_hexpand(panel, true);

        var overlay = N.gtk_overlay_new();
        N.gtk_overlay_set_child(overlay, panel);
        crumbs = Box(Horizontal, 0, "panel-title", "crumbs");
        N.gtk_widget_set_halign(crumbs, Start);
        N.gtk_widget_set_valign(crumbs, Start);
        N.gtk_overlay_add_overlay(overlay, crumbs);
        return overlay;
    }

    static readonly Action Nothing = () => { };

    // A panel with its title cut into the top border.
    static nint Titled(nint panel, string title, out nint label)
    {
        var overlay = N.gtk_overlay_new();
        N.gtk_overlay_set_child(overlay, panel);
        label = Label(title, "panel-title");
        N.gtk_widget_set_halign(label, Start);
        N.gtk_widget_set_valign(label, Start);
        N.gtk_widget_set_can_target(label, false);
        N.gtk_overlay_add_overlay(overlay, label);
        return overlay;
    }

    // ---- where it is ---------------------------------------------------------

    void ShowTab(string tab)
    {
        Leave();
        nav.Tab(tab);
        ShowHere();
    }

    void Open(string key, string name)
    {
        Leave();
        nav.Open(key, name);
        ShowHere();
    }

    void Back()
    {
        Leave();
        if (nav.Back()) ShowHere();
    }

    // Where the selection was in each place, so coming back finds it there.
    readonly Dictionary<string, uint> selectedIn = [];

    void Leave() => selectedIn[nav.Here.Key] = N.gtk_single_selection_get_selected(rowSelection);

    // Shows the place it's at: its list (asked for the first time it's
    // shown), the tab it's under, the way there.
    void ShowHere()
    {
        var here = nav.Here;
        shown = lists.Open(here.Key, here.Name);
        nav.Rename(here.Key, shown.Title);
        for (int i = 0; i < Keys.Tabs.Length; i++) Class(tabButtons[i], "label", Keys.Tabs[i] == nav.Under);
        Class(trackList, "lines", shown.Rows == Rows.Playlists);
        ShowCrumbs();
        ShowSplit();
        Refill();
        // The row it was on here before, else the playing track's.
        var from = shown.Rows == Rows.Tracks ? shown.Tracks.FindIndex(t => t.Uri == player.Track.Uri) : -1;
        var row = from >= 0 ? shownRows.IndexOf(from) : -1;
        if (selectedIn.TryGetValue(here.Key, out var before) && before < (uint)shownRows.Count) row = (int)before;
        N.gtk_single_selection_set_selected(rowSelection, row >= 0 ? (uint)row : uint.MaxValue);
        if (shownRows.Count > 0) N.gtk_list_view_scroll_to(trackList, (uint)Math.Max(0, row), 0, 0);
        if (window != 0 && !Searching) FocusList();
    }

    // "library › playlists › name": every part but the last is a button
    // that goes back there. Too long for the border, the names opened along
    // the way shorten, with an ellipsis.
    void ShowCrumbs()
    {
        var text = nav.Crumbs;
        if (text == shownCrumbs) return;
        shownCrumbs = text;
        for (var c = N.gtk_widget_get_first_child(crumbs); c != 0; c = N.gtk_widget_get_first_child(crumbs))
            N.gtk_box_remove(crumbs, c);
        var parts = nav.Path.Count;
        for (int depth = -1; depth < parts; depth++)
        {
            var name = Label(depth < 0 ? "library" : nav.Path[depth].Name);
            if (depth > 0) N.gtk_label_set_ellipsize(name, 3); // PANGO_ELLIPSIZE_END
            if (depth >= 0) N.gtk_box_append(crumbs, Label(" › "));
            if (depth == parts - 1) { N.gtk_box_append(crumbs, name); continue; }
            var b = Css(N.gtk_button_new(), "btn", "crumb");
            N.gtk_button_set_child(b, name);
            OnClicked(b, crumbClicked);
            N.gtk_box_append(crumbs, b);
        }
    }

    string shownCrumbs = "";

    // A part's depth is its place among the buttons: library and the tab go
    // to the tab's top.
    Action<nint> crumbClicked => crumbAction ??= button =>
    {
        var depth = -1;
        for (var c = N.gtk_widget_get_first_child(crumbs); c != 0 && c != button; c = N.gtk_widget_get_next_sibling(c))
            if (N.gtk_widget_get_ancestor(c, N.gtk_button_get_type()) != 0) depth++;
        Leave();
        if (nav.BackTo(Math.Max(0, depth))) ShowHere();
    };
    Action<nint>? crumbAction;

    // ---- the rows --------------------------------------------------------------

    // A list got a page, ended or failed: the shown one shows it, the one
    // that's playing goes on further.
    void ListChanged(Listing l)
    {
        if (l == player.Order.List)
        {
            NextChanged();
            if (player.Order.Shuffle) MoreOfPlaying();
        }
        if (l == names) ShowNames();
        if (l != shown) return;
        ShowSongsHead();
        nav.Rename(l.Key, l.Title);
        ShowCrumbs();
        var first = shownRows.Count == 0;
        Refill();
        // Its first rows: the focus goes to them, unless it's in the search.
        if (first && shownRows.Count > 0 && !Searching) FocusList();
    }

    bool Searching
    {
        get
        {
            var focus = window != 0 ? N.gtk_root_get_focus(window) : 0;
            return focus != 0 && (focus == search || N.gtk_widget_is_ancestor(focus, search));
        }
    }

    // The rows the filter lets through, from scratch, or (the list only
    // grew) the new ones added at the end.
    void Refill()
    {
        var text = Str(N.gtk_editable_get_text(search)).Trim();
        var grew = text == filterText && shownList == shown && examined <= shown.Count;
        filterText = text;
        shownList = shown;
        if (!grew)
        {
            shownRows.Clear();
            examined = 0;
        }
        var before = shownRows.Count;
        for (; examined < shown.Count; examined++)
            if (Matches(examined, text)) shownRows.Add(examined);
        if (grew) Splice((uint)before, 0, shownRows.Count - before);
        else Splice(0, N.g_list_model_get_n_items(rowModel), shownRows.Count);
        ShowNotes();
    }
    Listing? shownList;
    int examined; // how many of its rows the filter has looked at

    // Replaces removals rows at position with additions new (empty) ones.
    void Splice(uint position, uint removals, int additions)
    {
        if (removals == 0 && additions == 0) return;
        var empty = (nint)emptyString;
        var list = new nint[additions + 1];
        for (int i = 0; i < additions; i++) list[i] = empty;
        fixed (nint* p = list) N.gtk_string_list_splice(rowModel, position, removals, p);
    }
    static readonly byte* emptyString = (byte*)NativeMemory.AllocZeroed(1);

    bool Matches(int i, string text)
    {
        if (text.Length == 0) return true;
        bool Has(string s) => s.Contains(text, StringComparison.OrdinalIgnoreCase);
        if (shown.Rows == Rows.Tracks)
        {
            var t = shown.Tracks[i];
            return Has(t.Title) || Has(t.Artist) || Has(t.Album);
        }
        var e = shown.Entries[i];
        return Has(e.Name) || Has(e.Detail);
    }

    // Loading, empty, failed: said where the rows go, or after them.
    void ShowNotes()
    {
        var empty = shown.Count == 0;
        var what = shown.Error != null ? $"couldn't load {(empty ? "it" : "more")}: {shown.Error}"
            : shown.Loading ? (empty ? $"loading {nav.Here.Name}…" : "loading more…")
            : empty ? Keys.Empty(shown.Key)
            : shownRows.Count == 0 ? $"nothing here has “{filterText}” in it; Enter searches the songs"
            : "";
        var top = what.Length > 0 && shownRows.Count == 0;
        N.gtk_label_set_text(note, top ? what : "");
        N.gtk_widget_set_visible(note, top);
        N.gtk_label_set_text(moreNote, top ? "" : what);
        N.gtk_widget_set_visible(moreNote, !top && what.Length > 0);
    }

    // Near the end of what's in (two screens from it), the next page;
    // not while filtering, which looks only through what's in.
    void NearEnd()
    {
        if (!shown.More || shown.Loading || filterText.Length > 0) return;
        var a = N.gtk_scrolled_window_get_vadjustment(listScroll);
        var page = N.gtk_adjustment_get_page_size(a);
        if (N.gtk_adjustment_get_value(a) + page * 3 < N.gtk_adjustment_get_upper(a)) return;
        lists.More(shown);
        ShowNotes();
    }

    void MakeRow(nint item)
    {
        var r = new RowWidgets { Number = Label(""), Eq = Area(36, 21) };
        N.gtk_widget_set_visible(r.Eq, false);
        OnDraw(r.Eq, DrawEq);
        eqAreas.Add(r.Eq);
        var cell = r.Cell = Add(Box(Horizontal, 0), r.Number, r.Eq);
        N.gtk_widget_set_size_request(cell, 36, -1);
        r.Title = Label("");
        N.gtk_widget_set_hexpand(r.Title, true);
        N.gtk_label_set_ellipsize(r.Title, 3); // PANGO_ELLIPSIZE_END
        r.Side = Label("", "time");
        N.gtk_label_set_xalign(r.Side, 1);
        N.gtk_widget_set_size_request(r.Side, 56, -1);
        r.Box = Add(Box(Horizontal, 8, "trow"), cell, r.Title, r.Side);
        N.gtk_list_item_set_child(item, r.Box);
        made[item] = r;
    }

    void ForgetRow(nint item)
    {
        if (made.Remove(item, out var r)) eqAreas.Remove(r.Eq);
    }

    void FillRow(nint item)
    {
        if (!made.TryGetValue(item, out var r)) return;
        var position = (int)N.gtk_list_item_get_position(item);
        if (position < 0 || position >= shownRows.Count) return;
        var i = shownRows[position];
        r.Shows = i;
        N.gtk_label_set_text(r.Number, (i + 1).ToString());
        string title, side;
        if (shown.Rows == Rows.Tracks)
        {
            var t = shown.Tracks[i];
            // An album's tracks are all by it: the title alone, as the design.
            var alone = shown.Key.Contains(":album:", StringComparison.Ordinal);
            title = alone ? Escape(t.Title) : WithDetail(t.Title, t.Artist);
            side = Time.Format(t.Seconds);
        }
        else if (shown.Rows == Rows.Playlists)
        {
            // A name and how many songs: whose it is isn't said.
            var e = shown.Entries[i];
            title = Escape(Keys.Named(e.Name));
            side = e.Side;
        }
        else
        {
            var e = shown.Entries[i];
            // An artist's albums are by it: no need to say so on each.
            var by = Keys.RowsOf(shown.Key) == Rows.Albums && shown.Key != Keys.Albums && e.Detail == shown.Title;
            title = WithDetail(e.Name, by ? "" : e.Detail);
            side = e.Side;
        }
        N.gtk_widget_set_visible(r.Cell, shown.Rows != Rows.Playlists);
        Class(r.Side, "count", shown.Rows == Rows.Playlists);
        N.gtk_label_set_markup(r.Title, title);
        N.gtk_label_set_text(r.Side, side);
        MarkRow(r);
    }

    string WithDetail(string name, string detail) => detail.Length == 0 ? Escape(name)
        : $"{Escape(name)} <span foreground=\"{palette.Hex("dim")}\">· {Escape(detail)}</span>";

    // The playing track's row: its background and colour, the bars for its number.
    void MarkRow(RowWidgets r)
    {
        var current = r.Shows >= 0 && shown.Rows == Rows.Tracks && r.Shows < shown.Tracks.Count
            && shown.Tracks[r.Shows].Uri == player.Track.Uri;
        Class(r.Box, "current", current);
        N.gtk_widget_set_visible(r.Number, !current);
        N.gtk_widget_set_visible(r.Eq, current);
    }

    void MarkCurrentRow()
    {
        foreach (var r in made.Values) MarkRow(r);
    }

    // Enter or a double click: a track plays its list from there; anything
    // else opens.
    void Activate(int position)
    {
        if (position < 0 || position >= shownRows.Count) return;
        var i = shownRows[position];
        if (shown.Rows == Rows.Tracks) player.Play(shown, i);
        else Open(shown.Entries[i].Uri, shown.Entries[i].Name);
    }

    // The selected row's index in the shown list; -1 for none.
    int Selected()
    {
        var position = N.gtk_single_selection_get_selected(rowSelection);
        return position < (uint)shownRows.Count ? shownRows[(int)position] : -1;
    }

    Track? SelectedTrack()
    {
        var i = Selected();
        return shown.Rows == Rows.Tracks && i >= 0 ? shown.Tracks[i] : null;
    }

    // The filter follows the text; Enter searches the songs (Key).
    void Filter()
    {
        if (Str(N.gtk_editable_get_text(search)).Trim() != filterText) Refill();
    }

    void Search()
    {
        var words = Str(N.gtk_editable_get_text(search)).Trim();
        N.gtk_editable_set_text(search, "");
        if (words.Length > 0) Open(Keys.Search(words), Navigation.SearchName(words));
    }

    // Focus goes to the list: to the selected row, else the playing track's,
    // else the first.
    void FocusList()
    {
        var position = N.gtk_single_selection_get_selected(rowSelection);
        if (position >= (uint)shownRows.Count)
        {
            var playing = shown.Rows == Rows.Tracks ? shown.Tracks.FindIndex(t => t.Uri == player.Track.Uri) : -1;
            position = (uint)Math.Max(0, shownRows.IndexOf(playing));
        }
        N.gtk_widget_grab_focus(trackList);
        if (shownRows.Count > 0) N.gtk_list_view_scroll_to(trackList, position, 1 | 2, 0); // GTK_LIST_SCROLL_FOCUS | SELECT
    }

    void AddSelected()
    {
        if (SelectedTrack() is { } t) player.Order.Queue.Add(t);
    }

    // o and r: the selected track's album, its artist.
    void OpenAlbum()
    {
        if (SelectedTrack() is { AlbumUri.Length: > 0 } t) Open(t.AlbumUri, t.Album);
    }

    void OpenArtist()
    {
        if (SelectedTrack() is { ArtistUri.Length: > 0 } t) Open(t.ArtistUri, t.Artist);
    }
}
