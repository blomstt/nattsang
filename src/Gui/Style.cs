using Nattsang.Core;

namespace Nattsang.Gui;

// The window's stylesheet, from the palette. It goes in at priority 801,
// above the user's gtk.css (800), and resets what themes give buttons,
// entries and lists, so the window looks the same under any GTK theme.
static class Style
{
    // The window's padding (Side across, Top above), the dock's and the key
    // bar's, and the dock's border: App.Fit adds them up to know what fits.
    public const int Side = 20, Top = 10, DockSide = 16, DockTop = 10, KeysSide = 20, KeysTop = 8, Line = 1;

    // The ×'s box, its glyph's size, and where the panels' top border is
    // (their 10 px margin below the window's padding): the box's middle sits on it.
    const int CloseSize = 33, CloseGlyph = 20, Border = Top + 10;

    public static string Css(Palette p)
    {
        string C(string name) => p.Hex(name);
        return $$"""
        window.nattsang {
          background: {{C("background")}};
          color: {{C("text")}};
          font-family: "Cascadia Code", monospace;
          font-size: 14px;
          font-weight: 400;
          font-feature-settings: "calt" 0, "liga" 0;
          line-height: 1.5;
        }
        window.nattsang label { min-height: 21px; }
        window.nattsang selection { background: {{C("muted")}}; color: {{C("bright")}}; }

        .main { padding: {{Top}}px {{Side}}px 0; }

        /* panels: a box-drawn border with the title cut into it */
        .panel { border: 1px solid {{C("border")}}; margin-top: 10px; }
        .panel-title { background: {{C("background")}}; color: {{C("bright")}}; padding: 0 6px; margin-left: 12px; }
        .library { padding: 18px 14px 14px; }
        .now { padding: 28px 24px 16px; min-width: 480px; }

        /* buttons (.btn): no theme, hover lifts, press shrinks */
        button.btn {
          background: none; border: none; box-shadow: none; text-shadow: none;
          border-radius: 0; padding: 0; margin: 0; min-width: 0; min-height: 0;
          color: inherit; font: inherit; outline: 0 solid transparent;
          transition: transform 120ms ease-out, background-color 200ms, color 200ms, border-color 200ms;
        }
        button.btn > label { min-height: 0; }
        button.btn:hover { background: {{C("raised")}}; color: {{C("bright")}}; }
        button.btn:active { transform: scale(.92); }
        button.btn:focus-visible { outline: 1px solid {{C("accent")}}; outline-offset: 2px; }
        button.btn.bright, button.btn.bright:hover { color: {{C("bright")}}; }
        button.btn.accent, button.btn.accent:hover { color: {{C("accent")}}; }
        button.btn.playing, button.btn.playing:hover { color: {{C("playing")}}; }
        button.btn.label, button.btn.label:hover { color: {{C("label")}}; }

        /* the window's ×: cut into the top border at the right, as the panel titles are at the left */
        button.btn.close {
          background: {{C("background")}}; color: {{C("bright")}}; font-size: {{CloseGlyph}}px; line-height: 1;
          min-width: {{CloseSize}}px; min-height: {{CloseSize}}px; padding: 0 6px; margin: {{Border - CloseSize / 2}}px {{Side + 12}}px 0 0;
        }
        button.btn.close:hover { background: {{C("raised")}}; }

        /* search */
        .search { border: 1px solid {{C("border")}}; padding: 6px 10px; }
        .search entry {
          background: none; border: none; box-shadow: none; outline: none;
          min-height: 0; padding: 0; margin: 0; border-radius: 0;
          color: {{C("bright")}}; caret-color: {{C("bright")}};
        }
        .search entry > text > placeholder { color: alpha({{C("bright")}}, .54); }
        .key { color: {{C("accent")}}; }
        .filters { padding: 0 8px; }
        .rule { min-height: 1px; background: {{C("border")}}; }

        /* lists */
        list.tracks, list.queue-list { background: none; }
        scrolledwindow, viewport { background: none; }
        list.tracks > row {
          padding: 3px 8px; margin-bottom: 10px; background: none; color: {{C("text")}};
          outline: 0 solid transparent; min-height: 0; border-radius: 0;
          transition: background-color 200ms, color 200ms;
        }
        list.tracks > row:last-child { margin-bottom: 0; }
        list.tracks > row:hover, list.tracks > row:selected { background: {{C("hover")}}; color: {{C("text")}}; }
        list.tracks > row:focus-visible { outline: 1px solid {{C("accent")}}; outline-offset: -1px; }
        list.tracks > row.current, list.tracks > row.current:hover, list.tracks > row.current:selected { background: {{C("raised")}}; color: {{C("playing")}}; }
        list.tracks > row.current:focus-visible { outline: 1px solid {{C("accent")}}; outline-offset: -1px; }
        list.tracks > row label, list.queue-list > row label { color: inherit; }

        /* now playing */
        .song { font-size: 36px; font-weight: 700; color: {{C("playing")}}; line-height: 1.1; min-height: 0; }
        .sub { color: {{C("bright")}}; }
        .tabs { border-bottom: 1px solid {{C("border")}}; }
        button.btn.tab {
          padding: 4px 12px; color: {{C("text")}}; border-bottom: 2px solid transparent;
          transition: color 200ms, border-color 300ms;
        }
        button.btn.tab:hover { color: {{C("bright")}}; }
        button.btn.tab.on { color: {{C("accent")}}; border-bottom-color: {{C("accent")}}; }

        /* queue */
        .qnow { background: {{C("raised")}}; color: {{C("playing")}}; padding: 4px 8px; }
        .qhead { color: {{C("label")}}; padding: 10px 8px 4px; }
        list.queue-list > row {
          padding: 4px 8px; margin-bottom: 2px; background: none; color: {{C("text")}};
          outline: 0 solid transparent; min-height: 0; border-radius: 0;
          transition: background-color 200ms, color 200ms;
        }
        list.queue-list > row:hover { background: {{C("hover")}}; }
        .handle { color: {{C("handle")}}; }
        button.btn.x { min-width: 24px; min-height: 24px; opacity: 0; transition: opacity 200ms; }
        list.queue-list > row:hover button.btn.x, button.btn.x:focus-visible { opacity: 1; }
        .qfoot { border-top: 1px solid {{C("border")}}; padding: 8px; }

        /* dock */
        .dock { border: {{Line}}px solid {{C("border")}}; padding: {{DockTop}}px {{DockSide}}px; }
        button.btn.ctl { min-width: 44px; min-height: 44px; }
        button.btn.play { min-width: 50px; min-height: 50px; border: 1px solid {{C("playing")}}; font-size: 20px; }

        /* key bar */
        .keys { background: {{C("raised")}}; padding: {{KeysTop}}px {{KeysSide}}px; }
        button.btn.arrow { min-width: 28px; min-height: 28px; }
        .viz-name { color: {{C("playing")}}; }
        """;
    }
}
