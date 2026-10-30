using System.Runtime.InteropServices;

namespace Nattsang.Gui;

// GTK 4, GLib, Pango and cairo, bound by hand: only the calls the window makes.
static unsafe partial class N
{
    const string GObject = "libgobject-2.0.so.0", GLib = "libglib-2.0.so.0", Gio = "libgio-2.0.so.0";
    const string Gtk = "libgtk-4.so.1", Cairo = "libcairo.so.2";
    const string Pango = "libpango-1.0.so.0", PangoCairo = "libpangocairo-1.0.so.0";
    const StringMarshalling U = StringMarshalling.Utf8;

    [LibraryImport(Gtk)] public static partial uint gtk_get_minor_version();

    // GLib
    [LibraryImport(GObject, StringMarshalling = U)] public static partial ulong g_signal_connect_data(nint instance, string signal, nint handler, nint data, nint destroy, int flags);
    [LibraryImport(GObject)] public static partial void g_object_unref(nint obj);
    // Varargs (name, &value, NULL); only ever called with one int-sized property.
    [LibraryImport(GObject, StringMarshalling = U)] public static partial void g_object_get(nint obj, string name, int* value, nint end);
    [LibraryImport(GLib)] public static partial uint g_timeout_add_full(int priority, uint ms, nint func, nint data, nint destroy);
    [LibraryImport(GLib)] public static partial uint g_idle_add_full(int priority, nint func, nint data, nint destroy);
    [LibraryImport(GLib)] [return: MarshalAs(UnmanagedType.Bool)] public static partial bool g_source_remove(uint id);
    [LibraryImport(GLib)] public static partial long g_get_monotonic_time();
    [LibraryImport(Gio)] public static partial int g_application_run(nint app, int argc, nint argv);
    [LibraryImport(Gio)] public static partial void g_application_quit(nint app);
    [LibraryImport(Gio, StringMarshalling = U)] public static partial void g_set_application_name(string name);

    // application and window
    [LibraryImport(Gtk, StringMarshalling = U)] public static partial nint gtk_application_new(string id, int flags);
    [LibraryImport(Gtk)] public static partial nint gtk_application_window_new(nint app);
    [LibraryImport(Gtk, StringMarshalling = U)] public static partial void gtk_window_set_title(nint w, string title);
    [LibraryImport(Gtk)] public static partial void gtk_window_set_default_size(nint w, int width, int height);
    [LibraryImport(Gtk)] public static partial void gtk_window_set_decorated(nint w, [MarshalAs(UnmanagedType.Bool)] bool v);
    [LibraryImport(Gtk)] public static partial void gtk_window_set_child(nint w, nint child);
    [LibraryImport(Gtk)] public static partial void gtk_window_present(nint w);
    [LibraryImport(Gtk)] public static partial nint gtk_root_get_focus(nint root);
    [LibraryImport(Gtk)] public static partial nint gtk_settings_get_default();
    [LibraryImport(Gtk)] public static partial nint gtk_css_provider_new();
    [LibraryImport(Gtk, StringMarshalling = U)] public static partial void gtk_css_provider_load_from_string(nint provider, string css);
    [LibraryImport(Gtk)] public static partial void gtk_style_context_add_provider_for_display(nint display, nint provider, uint priority);
    [LibraryImport(Gtk)] public static partial nint gdk_display_get_default();

    // widgets
    [LibraryImport(Gtk)] public static partial void gtk_widget_set_hexpand(nint w, [MarshalAs(UnmanagedType.Bool)] bool v);
    [LibraryImport(Gtk)] public static partial void gtk_widget_set_vexpand(nint w, [MarshalAs(UnmanagedType.Bool)] bool v);
    [LibraryImport(Gtk)] public static partial void gtk_widget_set_halign(nint w, int align);
    [LibraryImport(Gtk)] public static partial void gtk_widget_set_valign(nint w, int align);
    [LibraryImport(Gtk, StringMarshalling = U)] public static partial void gtk_widget_add_css_class(nint w, string css);
    [LibraryImport(Gtk, StringMarshalling = U)] public static partial void gtk_widget_remove_css_class(nint w, string css);
    [LibraryImport(Gtk)] public static partial void gtk_widget_set_size_request(nint w, int width, int height);
    [LibraryImport(Gtk)] public static partial void gtk_widget_queue_draw(nint w);
    [LibraryImport(Gtk)] public static partial void gtk_widget_queue_allocate(nint w);
    [LibraryImport(Gtk)] public static partial void gtk_widget_queue_resize(nint w);
    [LibraryImport(Gtk)] public static partial void gtk_widget_set_visible(nint w, [MarshalAs(UnmanagedType.Bool)] bool v);
    [LibraryImport(Gtk)] [return: MarshalAs(UnmanagedType.Bool)] public static partial bool gtk_widget_get_visible(nint w);
    [LibraryImport(Gtk)] public static partial void gtk_widget_set_child_visible(nint w, [MarshalAs(UnmanagedType.Bool)] bool v);
    [LibraryImport(Gtk)] [return: MarshalAs(UnmanagedType.Bool)] public static partial bool gtk_widget_get_child_visible(nint w);
    [LibraryImport(Gtk)] public static partial void gtk_widget_set_opacity(nint w, double opacity);
    [LibraryImport(Gtk)] public static partial void gtk_widget_set_overflow(nint w, int overflow);
    [LibraryImport(Gtk)] public static partial void gtk_widget_set_can_target(nint w, [MarshalAs(UnmanagedType.Bool)] bool v);
    [LibraryImport(Gtk)] public static partial void gtk_widget_set_can_focus(nint w, [MarshalAs(UnmanagedType.Bool)] bool v);
    [LibraryImport(Gtk)] public static partial void gtk_widget_set_parent(nint w, nint parent);
    [LibraryImport(Gtk)] public static partial nint gtk_widget_get_parent(nint w);
    [LibraryImport(Gtk)] public static partial int gtk_widget_get_width(nint w);
    [LibraryImport(Gtk)] public static partial nint gtk_widget_get_first_child(nint w);
    [LibraryImport(Gtk)] public static partial nint gtk_widget_get_next_sibling(nint w);
    [LibraryImport(Gtk)] [return: MarshalAs(UnmanagedType.Bool)] public static partial bool gtk_widget_is_ancestor(nint w, nint ancestor);
    [LibraryImport(Gtk)] [return: MarshalAs(UnmanagedType.Bool)] public static partial bool gtk_widget_grab_focus(nint w);
    [LibraryImport(Gtk)] public static partial void gtk_widget_add_controller(nint w, nint controller);
    [LibraryImport(Gtk)] public static partial void gtk_widget_set_layout_manager(nint w, nint manager);
    [LibraryImport(Gtk)] public static partial void gtk_widget_measure(nint w, int orientation, int forSize, int* min, int* nat, int* minBaseline, int* natBaseline);
    [LibraryImport(Gtk)] public static partial void gtk_widget_allocate(nint w, int width, int height, int baseline, nint transform);
    [LibraryImport(Gtk)] public static partial uint gtk_widget_add_tick_callback(nint w, nint callback, nint data, nint destroy);
    [LibraryImport(Gtk)] public static partial void gtk_widget_remove_tick_callback(nint w, uint id);
    [LibraryImport(Gtk)] public static partial long gdk_frame_clock_get_frame_time(nint clock);
    [LibraryImport(Gtk)] public static partial uint gdk_keyval_to_lower(uint keyval);
    // Varargs (property, value, ..., -1); only ever called with one string property.
    [LibraryImport(Gtk, StringMarshalling = U)] public static partial void gtk_accessible_update_property(nint accessible, int property, string value, int end);
    [LibraryImport(Gtk)] public static partial void gtk_accessible_reset_relation(nint accessible, int relation);
    [LibraryImport(Gtk)] public static partial nint gtk_custom_layout_new(nint requestMode, nint measure, nint allocate);
    [LibraryImport(Gtk)] public static partial nint gsk_transform_translate(nint next, float* point);

    [LibraryImport(Gtk)] public static partial nint gtk_box_new(int orientation, int spacing);
    [LibraryImport(Gtk)] public static partial void gtk_box_append(nint box, nint child);
    [LibraryImport(Gtk)] public static partial nint gtk_overlay_new();
    [LibraryImport(Gtk)] public static partial void gtk_overlay_set_child(nint overlay, nint child);
    [LibraryImport(Gtk)] public static partial void gtk_overlay_add_overlay(nint overlay, nint w);
    [LibraryImport(Gtk, StringMarshalling = U)] public static partial nint gtk_label_new(string? text);
    [LibraryImport(Gtk, StringMarshalling = U)] public static partial void gtk_label_set_text(nint l, string text);
    [LibraryImport(Gtk, StringMarshalling = U)] public static partial void gtk_label_set_markup(nint l, string markup);
    [LibraryImport(Gtk)] public static partial void gtk_label_set_xalign(nint l, float x);
    [LibraryImport(Gtk)] public static partial void gtk_label_set_ellipsize(nint l, int mode);
    [LibraryImport(Gtk)] public static partial void gtk_label_set_wrap(nint l, [MarshalAs(UnmanagedType.Bool)] bool v);
    [LibraryImport(Gtk)] public static partial void gtk_label_set_width_chars(nint l, int n);
    [LibraryImport(Gtk, StringMarshalling = U)] public static partial nint gtk_button_new_with_label(string label);
    [LibraryImport(Gtk, StringMarshalling = U)] public static partial void gtk_button_set_label(nint b, string label);
    [LibraryImport(Gtk)] public static partial nint gtk_entry_new();
    [LibraryImport(Gtk, StringMarshalling = U)] public static partial void gtk_entry_set_placeholder_text(nint e, string text);
    [LibraryImport(Gtk)] [return: MarshalAs(UnmanagedType.Bool)] public static partial bool gtk_entry_grab_focus_without_selecting(nint e);
    [LibraryImport(Gtk)] public static partial nint gtk_editable_get_text(nint e);
    [LibraryImport(Gtk)] public static partial nint gtk_scrolled_window_new();
    [LibraryImport(Gtk)] public static partial void gtk_scrolled_window_set_child(nint sw, nint child);
    [LibraryImport(Gtk)] public static partial void gtk_scrolled_window_set_policy(nint sw, int h, int v);
    [LibraryImport(Gtk)] public static partial nint gtk_list_box_new();
    [LibraryImport(Gtk)] public static partial void gtk_list_box_append(nint lb, nint child);
    [LibraryImport(Gtk)] public static partial void gtk_list_box_remove_all(nint lb);
    [LibraryImport(Gtk)] public static partial void gtk_list_box_set_selection_mode(nint lb, int mode);
    [LibraryImport(Gtk)] public static partial void gtk_list_box_set_activate_on_single_click(nint lb, [MarshalAs(UnmanagedType.Bool)] bool v);
    [LibraryImport(Gtk)] public static partial nint gtk_list_box_get_selected_row(nint lb);
    [LibraryImport(Gtk)] public static partial void gtk_list_box_select_row(nint lb, nint row);
    [LibraryImport(Gtk)] public static partial nint gtk_list_box_get_row_at_index(nint lb, int index);
    [LibraryImport(Gtk)] public static partial int gtk_list_box_row_get_index(nint row);
    [LibraryImport(Gtk)] public static partial nint gtk_list_box_row_get_type();
    [LibraryImport(Gtk)] public static partial nint gtk_widget_get_ancestor(nint w, nint type);
    [LibraryImport(Gtk)] public static partial nint gtk_drawing_area_new();
    [LibraryImport(Gtk)] public static partial void gtk_drawing_area_set_draw_func(nint area, nint func, nint data, nint destroy);
    [LibraryImport(Gtk)] public static partial void gtk_drawing_area_set_content_height(nint area, int height);
    [LibraryImport(Gtk)] public static partial void gtk_drawing_area_set_content_width(nint area, int width);
    [LibraryImport(Gtk)] public static partial nint gtk_event_controller_key_new();
    [LibraryImport(Gtk)] public static partial void gtk_event_controller_set_propagation_phase(nint controller, int phase);

    // Pango
    [LibraryImport(PangoCairo)] public static partial nint pango_cairo_create_layout(nint cr);
    [LibraryImport(PangoCairo)] public static partial void pango_cairo_show_layout(nint cr, nint layout);
    [LibraryImport(Pango, StringMarshalling = U)] public static partial void pango_layout_set_text(nint layout, string text, int length);
    [LibraryImport(Pango)] public static partial void pango_layout_set_font_description(nint layout, nint desc);
    [LibraryImport(Pango)] public static partial void pango_layout_set_attributes(nint layout, nint attrs);
    [LibraryImport(Pango)] public static partial void pango_layout_get_size(nint layout, int* width, int* height);
    [LibraryImport(Pango, StringMarshalling = U)] public static partial nint pango_font_description_from_string(string text);
    [LibraryImport(Pango)] public static partial void pango_font_description_set_absolute_size(nint desc, double size);
    [LibraryImport(Pango)] public static partial void pango_font_description_set_weight(nint desc, int weight);
    [LibraryImport(Pango)] public static partial nint pango_attr_list_new();
    [LibraryImport(Pango)] public static partial void pango_attr_list_insert(nint list, nint attr);
    [LibraryImport(Pango, StringMarshalling = U)] public static partial nint pango_attr_font_features_new(string features);

    // cairo
    [LibraryImport(Cairo)] public static partial void cairo_set_source_rgba(nint cr, double r, double g, double b, double a);
    [LibraryImport(Cairo)] public static partial void cairo_set_source(nint cr, nint pattern);
    [LibraryImport(Cairo)] public static partial void cairo_rectangle(nint cr, double x, double y, double w, double h);
    [LibraryImport(Cairo)] public static partial void cairo_arc(nint cr, double xc, double yc, double r, double a1, double a2);
    [LibraryImport(Cairo)] public static partial void cairo_new_path(nint cr);
    [LibraryImport(Cairo)] public static partial void cairo_close_path(nint cr);
    [LibraryImport(Cairo)] public static partial void cairo_fill(nint cr);
    [LibraryImport(Cairo)] public static partial void cairo_move_to(nint cr, double x, double y);
    [LibraryImport(Cairo)] public static partial void cairo_save(nint cr);
    [LibraryImport(Cairo)] public static partial void cairo_restore(nint cr);
    [LibraryImport(Cairo)] public static partial void cairo_translate(nint cr, double x, double y);
    [LibraryImport(Cairo)] public static partial void cairo_scale(nint cr, double x, double y);
    [LibraryImport(Cairo)] public static partial nint cairo_pattern_create_linear(double x0, double y0, double x1, double y1);
    [LibraryImport(Cairo)] public static partial nint cairo_pattern_create_radial(double cx0, double cy0, double r0, double cx1, double cy1, double r1);
    [LibraryImport(Cairo)] public static partial void cairo_pattern_add_color_stop_rgba(nint pattern, double offset, double r, double g, double b, double a);
    [LibraryImport(Cairo)] public static partial void cairo_pattern_destroy(nint pattern);
}
