using System.Runtime.InteropServices;

namespace Nattsang.Gui;

// GTK 4, GLib, Pango and cairo, bound by hand: only the calls the window makes.
static unsafe partial class N
{
    const string GObject = "libgobject-2.0.so.0", GLib = "libglib-2.0.so.0", Gio = "libgio-2.0.so.0";
    const string Gtk = "libgtk-4.so.1", Cairo = "libcairo.so.2", Libc = "libc.so.6";
    const string Pango = "libpango-1.0.so.0", PangoCairo = "libpangocairo-1.0.so.0";
    const string Soup = "libsoup-3.0.so.0";
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
    // The helper: spawned with pipes on its stdin and stdout, its output and
    // its exit watched on the main loop. GError is { domain, code, message }.
    [LibraryImport(GLib)] [return: MarshalAs(UnmanagedType.Bool)] public static partial bool g_spawn_async_with_pipes(nint dir, nint* argv, nint* envp, int flags, nint setup, nint data, int* pid, int* stdin, int* stdout, int* stderr, nint* error);
    [LibraryImport(GLib)] public static partial void g_spawn_close_pid(int pid);
    [LibraryImport(GLib)] public static partial void g_error_free(nint error);
    [LibraryImport(GLib)] public static partial uint g_unix_fd_add_full(int priority, int fd, uint condition, nint func, nint data, nint notify);
    [LibraryImport(GLib)] [return: MarshalAs(UnmanagedType.Bool)] public static partial bool g_unix_set_fd_nonblocking(int fd, [MarshalAs(UnmanagedType.Bool)] bool nonblock, nint error);
    [LibraryImport(GLib)] public static partial uint g_child_watch_add_full(int priority, int pid, nint func, nint data, nint notify);
    [LibraryImport(Libc, SetLastError = true)] public static partial nint read(int fd, byte* buffer, nint count);
    [LibraryImport(Libc, SetLastError = true)] public static partial nint write(int fd, byte* buffer, nint count);
    [LibraryImport(Libc)] public static partial int close(int fd);
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
    [LibraryImport(Gtk)] public static partial void gtk_label_set_yalign(nint l, float y);
    [LibraryImport(Gtk)] public static partial void gtk_label_set_ellipsize(nint l, int mode);
    [LibraryImport(Gtk)] public static partial void gtk_label_set_wrap(nint l, [MarshalAs(UnmanagedType.Bool)] bool v);
    [LibraryImport(Gtk)] public static partial void gtk_label_set_width_chars(nint l, int n);
    [LibraryImport(Gtk, StringMarshalling = U)] public static partial nint gtk_button_new_with_label(string label);
    [LibraryImport(Gtk, StringMarshalling = U)] public static partial void gtk_button_set_label(nint b, string label);
    [LibraryImport(Gtk)] public static partial nint gtk_button_new();
    [LibraryImport(Gtk)] public static partial void gtk_button_set_child(nint b, nint child);
    [LibraryImport(Gtk)] public static partial nint gtk_entry_new();
    [LibraryImport(Gtk, StringMarshalling = U)] public static partial void gtk_entry_set_placeholder_text(nint e, string text);
    [LibraryImport(Gtk)] [return: MarshalAs(UnmanagedType.Bool)] public static partial bool gtk_entry_grab_focus_without_selecting(nint e);
    [LibraryImport(Gtk)] public static partial nint gtk_editable_get_text(nint e);
    [LibraryImport(Gtk)] public static partial nint gtk_scrolled_window_new();
    [LibraryImport(Gtk)] public static partial void gtk_scrolled_window_set_child(nint sw, nint child);
    [LibraryImport(Gtk)] public static partial void gtk_scrolled_window_set_policy(nint sw, int h, int v);
    [LibraryImport(Gtk)] public static partial nint gtk_list_box_new();
    [LibraryImport(Gtk)] public static partial void gtk_list_box_unselect_all(nint lb);
    [LibraryImport(Gtk)] public static partial void gtk_list_box_append(nint lb, nint child);
    [LibraryImport(Gtk)] public static partial void gtk_list_box_remove_all(nint lb);
    [LibraryImport(Gtk)] public static partial void gtk_list_box_remove(nint lb, nint child);
    [LibraryImport(Gtk)] public static partial void gtk_list_box_set_placeholder(nint lb, nint placeholder);
    [LibraryImport(Gtk)] public static partial void gtk_list_box_set_selection_mode(nint lb, int mode);
    [LibraryImport(Gtk)] public static partial void gtk_list_box_set_activate_on_single_click(nint lb, [MarshalAs(UnmanagedType.Bool)] bool v);
    [LibraryImport(Gtk)] public static partial nint gtk_list_box_get_selected_row(nint lb);
    [LibraryImport(Gtk)] public static partial void gtk_list_box_select_row(nint lb, nint row);
    [LibraryImport(Gtk)] public static partial nint gtk_list_box_get_row_at_index(nint lb, int index);
    [LibraryImport(Gtk)] public static partial int gtk_list_box_row_get_index(nint row);
    [LibraryImport(Gtk)] public static partial nint gtk_list_box_row_get_type();
    [LibraryImport(Gtk)] public static partial nint gtk_button_get_type();
    [LibraryImport(Gtk)] public static partial nint gtk_widget_get_ancestor(nint w, nint type);
    // The library's list: a list view over a list of empty strings, one per
    // row shown (the rows' data stays in C#), with rows made and filled by
    // a signal factory.
    [LibraryImport(Gtk)] public static partial nint gtk_string_list_new(nint strings);
    [LibraryImport(Gtk)] public static partial void gtk_string_list_splice(nint list, uint position, uint removals, nint* additions);
    [LibraryImport(Gio)] public static partial uint g_list_model_get_n_items(nint list);
    [LibraryImport(Gtk)] public static partial nint gtk_signal_list_item_factory_new();
    [LibraryImport(Gtk)] public static partial void gtk_list_item_set_child(nint item, nint child);
    [LibraryImport(Gtk)] public static partial uint gtk_list_item_get_position(nint item);
    [LibraryImport(Gtk)] public static partial nint gtk_single_selection_new(nint model);
    [LibraryImport(Gtk)] public static partial void gtk_single_selection_set_autoselect(nint selection, [MarshalAs(UnmanagedType.Bool)] bool v);
    [LibraryImport(Gtk)] public static partial void gtk_single_selection_set_can_unselect(nint selection, [MarshalAs(UnmanagedType.Bool)] bool v);
    [LibraryImport(Gtk)] public static partial uint gtk_single_selection_get_selected(nint selection);
    [LibraryImport(Gtk)] public static partial void gtk_single_selection_set_selected(nint selection, uint position);
    [LibraryImport(Gtk)] public static partial nint gtk_list_view_new(nint model, nint factory);
    [LibraryImport(Gtk)] public static partial void gtk_list_view_scroll_to(nint view, uint position, int flags, nint scroll);
    [LibraryImport(Gtk)] public static partial nint gtk_scrolled_window_get_vadjustment(nint sw);
    [LibraryImport(Gtk)] public static partial double gtk_adjustment_get_value(nint adjustment);
    [LibraryImport(Gtk)] public static partial double gtk_adjustment_get_upper(nint adjustment);
    [LibraryImport(Gtk)] public static partial double gtk_adjustment_get_page_size(nint adjustment);
    [LibraryImport(Gtk)] public static partial void gtk_box_remove(nint box, nint child);
    [LibraryImport(Gtk, StringMarshalling = U)] public static partial void gtk_editable_set_text(nint e, string text);
    [LibraryImport(Gtk)] public static partial nint gtk_drawing_area_new();
    [LibraryImport(Gtk)] public static partial void gtk_drawing_area_set_draw_func(nint area, nint func, nint data, nint destroy);
    [LibraryImport(Gtk)] public static partial void gtk_drawing_area_set_content_height(nint area, int height);
    [LibraryImport(Gtk)] public static partial void gtk_drawing_area_set_content_width(nint area, int width);
    [LibraryImport(Gtk)] public static partial nint gtk_event_controller_key_new();
    // Pointer input: a drag (the divider, the volume bar), the wheel (the
    // volume), the pointer coming and going (the divider's line lights up).
    [LibraryImport(Gtk)] public static partial nint gtk_gesture_drag_new();
    [LibraryImport(Gtk)] public static partial nint gtk_event_controller_scroll_new(int flags);
    [LibraryImport(Gtk)] public static partial nint gtk_event_controller_motion_new();
    [LibraryImport(Gtk, StringMarshalling = U)] public static partial void gtk_widget_set_cursor_from_name(nint w, string? name);
    // The cover: a picture of a texture made from the image file's bytes.
    [LibraryImport(Gtk)] public static partial nint gtk_picture_new();
    [LibraryImport(Gtk)] public static partial void gtk_picture_set_paintable(nint picture, nint paintable);
    [LibraryImport(Gtk)] public static partial void gtk_picture_set_content_fit(nint picture, int fit);
    [LibraryImport(Gtk)] public static partial void gtk_picture_set_can_shrink(nint picture, [MarshalAs(UnmanagedType.Bool)] bool v);
    [LibraryImport(Gtk)] public static partial nint gdk_texture_new_from_bytes(nint bytes, nint* error);
    [LibraryImport(GLib)] public static partial nint g_bytes_new(byte* data, nint size);
    // The colours file, watched (inotify, through GLib): no polling.
    [LibraryImport(Gio, StringMarshalling = U)] public static partial nint g_file_new_for_path(string path);
    [LibraryImport(Gio)] public static partial nint g_file_monitor_file(nint file, int flags, nint cancellable, nint* error);
    [LibraryImport(Gtk)] public static partial void gtk_event_controller_set_propagation_phase(nint controller, int phase);

    // Pango
    [LibraryImport(PangoCairo)] public static partial nint pango_cairo_create_layout(nint cr);
    [LibraryImport(PangoCairo)] public static partial void pango_cairo_show_layout(nint cr, nint layout);
    [LibraryImport(Pango, StringMarshalling = U)] public static partial void pango_layout_set_text(nint layout, string text, int length);
    [LibraryImport(Pango)] public static partial void pango_layout_set_font_description(nint layout, nint desc);
    [LibraryImport(Pango)] public static partial void pango_layout_set_attributes(nint layout, nint attrs);
    [LibraryImport(Pango)] public static partial void pango_layout_get_size(nint layout, int* width, int* height);
    [LibraryImport(Pango)] public static partial void pango_layout_set_width(nint layout, int width);
    [LibraryImport(Pango)] public static partial void pango_layout_set_ellipsize(nint layout, int mode);
    [LibraryImport(Pango)] public static partial void pango_layout_set_alignment(nint layout, int alignment);
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

    // D-Bus (MPRIS), on the application's own session bus connection.
    // GError is { domain, code, message }; a GDBusInterfaceVTable is three
    // callbacks and eight pointers of padding.
    [LibraryImport(Gio)] public static partial nint g_application_get_dbus_connection(nint app);
    [LibraryImport(Gio, StringMarshalling = U)] public static partial nint g_dbus_node_info_new_for_xml(string xml, nint* error);
    [LibraryImport(Gio, StringMarshalling = U)] public static partial nint g_dbus_node_info_lookup_interface(nint info, string name);
    [LibraryImport(Gio, StringMarshalling = U)] public static partial uint g_dbus_connection_register_object(nint connection, string path, nint iface, nint vtable, nint data, nint free, nint* error);
    [LibraryImport(Gio)] [return: MarshalAs(UnmanagedType.Bool)] public static partial bool g_dbus_connection_unregister_object(nint connection, uint id);
    [LibraryImport(Gio, StringMarshalling = U)] public static partial uint g_bus_own_name_on_connection(nint connection, string name, int flags, nint acquired, nint lost, nint data, nint free);
    [LibraryImport(Gio)] public static partial void g_bus_unown_name(uint id);
    [LibraryImport(Gio, StringMarshalling = U)] [return: MarshalAs(UnmanagedType.Bool)] public static partial bool g_dbus_connection_emit_signal(nint connection, string? destination, string path, string iface, string signal, nint parameters, nint* error);
    [LibraryImport(Gio)] public static partial void g_dbus_method_invocation_return_value(nint invocation, nint parameters);
    [LibraryImport(Gio, StringMarshalling = U)] public static partial void g_dbus_method_invocation_return_dbus_error(nint invocation, string name, string message);
    // GVariant: made floating, so whatever they're handed to takes them.
    [LibraryImport(GLib, StringMarshalling = U)] public static partial nint g_variant_new_string(string s);
    [LibraryImport(GLib, StringMarshalling = U)] public static partial nint g_variant_new_object_path(string path);
    [LibraryImport(GLib)] public static partial nint g_variant_new_int64(long v);
    [LibraryImport(GLib)] public static partial nint g_variant_new_double(double v);
    [LibraryImport(GLib)] public static partial nint g_variant_new_boolean(int v);
    [LibraryImport(GLib)] public static partial nint g_variant_new_strv(nint* strv, nint length);
    [LibraryImport(GLib)] public static partial nint g_variant_new_variant(nint value);
    [LibraryImport(GLib)] public static partial nint g_variant_new_dict_entry(nint key, nint value);
    [LibraryImport(GLib)] public static partial nint g_variant_new_array(nint childType, nint* children, nint n);
    [LibraryImport(GLib)] public static partial nint g_variant_new_tuple(nint* children, nint n);
    [LibraryImport(GLib, StringMarshalling = U)] public static partial nint g_variant_type_new(string type);
    [LibraryImport(GLib)] public static partial void g_variant_type_free(nint type);
    [LibraryImport(GLib)] public static partial nint g_variant_n_children(nint v);
    [LibraryImport(GLib)] public static partial nint g_variant_get_child_value(nint v, nint i);
    [LibraryImport(GLib)] public static partial long g_variant_get_int64(nint v);
    [LibraryImport(GLib)] public static partial double g_variant_get_double(nint v);
    [LibraryImport(GLib)] public static partial int g_variant_get_boolean(nint v);
    [LibraryImport(GLib)] public static partial nint g_variant_get_string(nint v, nint* length);
    [LibraryImport(GLib, StringMarshalling = U)] [return: MarshalAs(UnmanagedType.Bool)] public static partial bool g_variant_is_of_type(nint v, nint type);
    [LibraryImport(GLib)] public static partial void g_variant_unref(nint v);

    // HTTP (the lyrics), with libsoup on the main loop: a request's answer
    // comes to a GAsyncReadyCallback, its body as GBytes.
    [LibraryImport(Soup)] public static partial nint soup_session_new();
    [LibraryImport(Soup, StringMarshalling = U)] public static partial void soup_session_set_user_agent(nint session, string agent);
    [LibraryImport(Soup)] public static partial void soup_session_set_timeout(nint session, uint seconds);
    [LibraryImport(Soup, StringMarshalling = U)] public static partial nint soup_message_new(string method, string uri);
    [LibraryImport(Soup)] public static partial void soup_session_send_and_read_async(nint session, nint message, int priority, nint cancellable, nint callback, nint data);
    [LibraryImport(Soup)] public static partial nint soup_session_send_and_read_finish(nint session, nint result, nint* error);
    [LibraryImport(Soup)] public static partial uint soup_message_get_status(nint message);
    [LibraryImport(GLib)] public static partial nint g_bytes_get_data(nint bytes, nint* size);
    [LibraryImport(GLib)] public static partial void g_bytes_unref(nint bytes);
}
