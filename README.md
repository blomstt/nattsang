# Nattsang

A music player that looks like it lives in a terminal: one GTK 4 window,
box-drawn panels set in FreeSerif, colours from your wallpaper. Written in C# and
compiled ahead of time to a single native binary.

It plays from Spotify through a small helper program, `nattsang-helper`,
built on [librespot](https://github.com/librespot-org/librespot). That needs
a Spotify Premium account.

This is the fifth stage: it signs in, and the library panel shows your
account: your liked songs, your playlists, your saved albums and the
artists you follow. Open a playlist, album or artist to see what's in it,
search for songs, and play from any list: the list plays on from there,
one track into the next without a gap, after whatever you've queued,
shuffled or repeated if you like, at Nattsang's own volume. The playing
song's cover shows beside its title. The
lyrics come from [LRCLIB](https://lrclib.net) and follow the song line by
line, media keys, `playerctl` and status bars see and steer it over
MPRIS, and the visualizer moves with the music.

## Build and run

Needs the .NET 10 SDK, GTK 4.12 or newer and the FreeSerif font (GNU
FreeFont: `gnu-free-fonts` on Arch, `fonts-freefont-ttf` on Debian);
libsoup 3 and glib-networking for the lyrics and the covers (without them
the lyrics panel says so, the cover shows its placeholder, and the rest works); for the helper, Rust 1.85 or newer, OpenSSL and PulseAudio's libraries (PipeWire's
PulseAudio server works).

Build the helper first; the .NET build then puts it beside the window's
binary:

    cargo build --release --manifest-path helper/Cargo.toml
    dotnet run                      # from the repository
    dotnet publish -c Release       # native binary in bin/Release/net10.0/linux-x64/publish/nattsang

Without the helper, or with `--simulate`, the window plays the sample album
on a simulated clock and makes no sound. `NATTSANG_HELPER=/path/to/helper`
runs a helper from somewhere else.

Nattsang runs one window at a time: starting it again brings the open one
forward. To run a second one beside it (to try a build while the other
plays), give it another application id: `NATTSANG_APP_ID=dev.blomst.NattsangTest`.

Tests (no GTK needed), and the helper's (the visualizer's measuring):

    dotnet run --project tests/CoreTests
    cargo test --release --manifest-path helper/Cargo.toml

## Signing in

The first time, Nattsang opens Spotify's login page in your browser, and the
window says it's waiting. Sign in there with your Premium account and allow
access; the page then says you can close it, and Nattsang loads your
library.
If the browser is already signed in to Spotify, this can go straight through
without asking.
If the browser doesn't open, the link is printed in the terminal Nattsang
was started from.

If the account isn't Premium, the window says so and plays nothing.

The sign-in is kept in `~/.cache/nattsang/` (only you can read it), so it
happens once. To sign out, delete that folder.

While the login page is open, the helper listens on 127.0.0.1 port 5588 for
the browser to come back; nothing else listens. It isn't a Spotify Connect
device, so phones don't see it. It plays at 320 kbps, as recorded (no volume
normalisation), through PulseAudio, keeping a fifth of a second of sound
buffered there (so a pause or a change of volume is heard that soon).

The library, the search and the track details come from the service's own
internal endpoints, the ones its official client uses, not the public Web
API. They are unofficial and can change without notice;
[docs/protocol.md](docs/protocol.md) lists them. They're asked one request
at a time; if the service answers "too many requests", the list says so and
nothing is asked for half a minute.

## The library

The tabs above the list are your liked songs, your playlists, your saved
albums and the artists you follow. `Enter` or a double click on a
playlist, album or artist opens it (an artist opens to their albums); on a
song, it plays that song and then the rest of the list it's in. The top
border says where you are ("library › playlists › Night drive"): click a
part of it to go back there, or press `Backspace` to go back one.

`/` goes to the search box. What you type there filters the list you're
looking at; `Enter` searches all songs and opens what it finds. Only songs
can be searched for: open a song's album with `o` and its artist with
`r`.

The playlists tab lists each playlist's name and how many songs it has.
An open playlist shows its songs beside the names, so another is one
click away: drag the line between them (anywhere along it) to make the
names wider or narrower, from 180 to 520 px; the width is remembered.

Long lists load as you scroll down. Each list is loaded once, the first
time you open it, and kept until Nattsang closes.

`a` adds the selected song to the queue. What's queued plays before the
rest of the list, in order; the queue view (`q`) shows the queue and then
what comes next from the list.

## Shuffle, repeat and volume

The buttons either side of the transport: shuffle (`⇄`, lit with a dot
under it when on) plays the list you're playing from in a random order,
each song once, the one playing first; turned off, the list goes on in its
own order from there. Repeat (`↻`) goes from off, to the list (lit), to
one song (lit, with a `1`): off stops after the list's last song, the
list starts it again (shuffled again, when shuffle is on), one song plays
the same song again. `n` and `p` move through the order either way, and
what's queued still plays first.

The volume at the right of the key bar is Nattsang's own: the system's
stays as it is. `+` and `-` turn it up and down by 5 %, and so does the
wheel over it; click or drag on its bar to set it. It's remembered, with
the playlist names' width, in `~/.local/state/nattsang/state`.

## Lyrics

The lyrics panel shows the playing song's lyrics from LRCLIB, a free,
open lyrics database. Synced lyrics follow the song: the line being sung
is marked `▸`, and the panel moves on as the song does. A song that only
has unsynced lyrics shows them going by evenly over its length, none of
them marked. An instrumental says so, and a song LRCLIB doesn't know says
"no lyrics found for this song".

A song is looked up when it starts: by its name, artist, album and length,
and if LRCLIB doesn't find it that way, by a search on its name and first
artist (a version of another length gives its words, unsynced). Nothing
else is sent. What it finds is kept in `~/.cache/nattsang/lyrics/`, so a
song is looked up once; that nothing was found is kept for a week, then
asked again. If LRCLIB can't be reached, the panel says why, and the song
is asked again the next time it plays. With `--simulate` the sample album
shows placeholder lines and nothing is asked.

## Media keys and MPRIS

Nattsang is an MPRIS player named `nattsang` on the session bus, so media
keys bound to `playerctl`, and bars and widgets that show what plays, work
with it:

    playerctl -p nattsang play-pause      # also play, pause, stop, next, previous
    playerctl -p nattsang metadata        # title, artist, album, length, link, cover
    playerctl -p nattsang position 30     # seek; position alone says where it is

A second window (`NATTSANG_APP_ID`) is `nattsang.instance<pid>`.
`playerctl` and desktops call raise (brings the window forward) and quit
(closes it, as the `×` does). Volume (Nattsang's own, 0 to 1), Shuffle
and LoopStatus can be read and set too:

    playerctl -p nattsang volume 0.5
    playerctl -p nattsang shuffle toggle
    playerctl -p nattsang loop Playlist     # None, Playlist or Track

## The visualizer

The 28 pills at the right of the dock follow the music, lowest notes at
the left: the helper measures what goes to the speakers (30 times a second,
28 bands from 40 Hz to 16 kHz), each measure says when it will be heard, and
each pill springs to its band's level as it's heard. Paused, stopped, or
with the visualizer off (`v`) or hidden for room, nothing is measured and
nothing moves. With `--simulate`, or a helper that doesn't measure, the pills
move on their own, as in the design.

## Keys

| key         | does                              |
|-------------|-----------------------------------|
| `/`         | search songs, or filter the list  |
| `space`     | play / pause                      |
| `n` `p`     | next / previous track             |
| `a`         | add the selected track to the queue |
| `q`         | switch between lyrics and queue   |
| `v`         | visualizer on / off               |
| `+` `-`     | volume up / down                  |
| `o`         | open the selected song's album    |
| `r`         | open the selected song's artist   |
| `Backspace` | back                              |

In the search box, `Enter` searches and `Esc` goes back to the list. The
`×` at the top right closes Nattsang.

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

Values are `#rgb` or `#rrggbb`; anything else is ignored. The file is
watched: the window takes new colours as soon as it changes, without a
restart (and without looking at it in between).

To have them follow the wallpaper with [matugen](https://github.com/InioX/matugen),
add a template that writes that file, for example:

    [templates.nattsang]
    input_path = '~/.config/matugen/templates/nattsang-colors.css'
    output_path = '~/.config/nattsang/colors.css'

with lines like `@define-color accent {{colors.blue.default.hex}};` for
each name above.

## The cover

The playing song's cover shows at the left of its title, as the service
has it (up to 640 px). Each is fetched once and kept in
`~/.cache/nattsang/covers/`. A new song's cover replaces the last one
when it has come in; a song with none shows a placeholder.

## Layout

    src/Core/       the model: tracks, lists, where the library is, the queue and
                    what plays next (shuffle, repeat), time, lyrics (LRC, LRCLIB,
                    the cache), the volume, what's remembered, covers on disk,
                    colours, motion curves
    src/Playback/   what plays and where the lists come from: IPlayback and
                    ILibrary; SpotifyPlayback and SpotifyLibrary, which follow
                    the helper over its protocol; the simulated sample; finding
                    lyrics; what MPRIS says
    src/Gui/        the GTK window, bound by hand (Native.cs), no packages;
                    Helper.cs runs the helper, Http.cs asks LRCLIB and fetches
                    covers (libsoup), Split.cs is an open playlist beside the names,
                    MprisBus.cs is MPRIS (GDBus)
    helper/         nattsang-helper, in Rust: librespot, signing in, playing;
                    sink.rs the PulseAudio output, levels.rs the visualizer's measure
    docs/protocol.md  what the window and the helper say to each other
    tests/CoreTests checks for Core and Playback

## Licence

MIT, see LICENSE.
