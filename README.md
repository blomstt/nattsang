# Nattsang

A music player that looks like it lives in a terminal: one GTK 4 window,
monospace, box-drawn panels, colours from your wallpaper. Written in C# and
compiled ahead of time to a single native binary.

This is the first stage: the whole window, working on sample data (one
album's track list, with placeholder lyrics). It plays no audio yet.

## Build and run

Needs the .NET 10 SDK, GTK 4.12 or newer and the Cascadia Code font.

    dotnet run                      # from the repository
    dotnet publish -c Release       # native binary in bin/Release/net10.0/linux-x64/publish/nattsang

Tests (no GTK needed):

    dotnet run --project tests/CoreTests

## Keys

| key     | does                              |
|---------|-----------------------------------|
| `/`     | search the library                |
| `space` | play / pause                      |
| `n` `p` | next / previous track             |
| `a`     | add the selected track to the queue |
| `q`     | switch between lyrics and queue   |
| `v`     | visualizer on / off               |

In the search box, `Esc` or `Enter` goes back to the list. The `×` at the
top right closes Nattsang.

## Window size

The window opens wide enough for everything. Made narrower, it hides parts
one at a time, in this order: the key hints, the visualizer, the lyrics and
queue panel, the times beside the progress bar. Made shorter, it hides only
the lyrics and queue panel. Parts fade out as they hide and fade back in as
the window grows.
The library always stays.

## Colours

The window has built-in colours. To change them, write
`~/.config/nattsang/colors.css` with GTK colour definitions, any of:

    @define-color background #00010b;
    @define-color text       #958f92;
    @define-color bright     #ada6a9;
    @define-color accent     #80d4d9;
    @define-color playing    #ffb1c4;
    @define-color label      #f8bb71;
    @define-color border     #34323a;
    @define-color raised     #17161d;
    @define-color hover      #111018;
    @define-color groove     #26252c;
    @define-color dim        #7d787b;
    @define-color handle     #635f65;
    @define-color muted      #4a484e;

The file is read at start. Values are `#rgb` or `#rrggbb`; anything else is ignored.

## Layout

    src/Core/       the model: tracks, time, lyrics timing, the queue, colours, motion curves
    src/Playback/   what plays: IPlayback, and SimulatedPlayback for now
    src/Gui/        the GTK window, bound by hand (Native.cs), no packages
    tests/CoreTests checks for Core and Playback

## Licence

MIT, see LICENSE.
