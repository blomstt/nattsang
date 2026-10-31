# The helper protocol

Nattsang plays music through a separate program, `nattsang-helper`
(`helper/`), which embeds librespot. The window starts it as a child process
and talks to it over the child's stdin (commands) and stdout (events). Its
stderr is the window's: librespot's warnings and the sign-in link end up in
the terminal Nattsang was started from.

Why pipes and not a socket: nothing has to be named, found or cleaned up, and
the helper's life is tied to the window's. When the window closes, or dies,
the helper's stdin closes and the helper exits. When the helper exits, the
window sees its stdout close and its exit status.

## Lines

- UTF-8, one message per line, ended by `\n`.
- Fields are separated by a tab (`\t`). The first field says what the
  message is; the rest are its arguments, in order.
- A field never holds a tab or a line break; the sender turns them into
  spaces. Fields may hold spaces.
- Times are whole milliseconds, written as plain digits.
- Tracks, albums, artists and playlists are named by their URI:
  `spotify:track:…`, `spotify:album:…`, `spotify:artist:…`,
  `spotify:playlist:…`.
- A receiver ignores a message it doesn't know and extra fields after the
  ones it expects, so either side can grow.

## Commands (window → helper)

| command | fields | does |
|---|---|---|
| `load` | uri, play (`1`/`0`, default `1`), position ms (default `0`) | loads the track, playing or paused |
| `play` | | plays the loaded track |
| `pause` | | pauses it |
| `seek` | position ms | moves within it |
| `stop` | | stops and unloads it (`stopped` follows) |
| `preload` | uri | fetches that track ahead, so a `load` of it starts at once |
| `get` | key | sends the first page of that list (below, "Lists") |
| `more` | key | sends its next page |
| `levels` | `1` or `0` | starts or stops measuring the music for the visualizer (below, "Levels"); off until asked |

Commands sent before the helper has signed in wait, and run once it has.

## Events (helper → window)

| event | fields | means |
|---|---|---|
| `hello` | protocol version (`1`), then what it can do beyond that: `levels` | the helper has started; `levels`: it can measure the music for the visualizer (below, "Levels") |
| `connecting` | | signing in with saved credentials, or after the browser sign-in |
| `needs-signin` | | there are no saved credentials: the login page has opened in the browser, and the helper waits for it |
| `signed-in` | user name | signed in; commands run from now on |
| `account` | type (`premium`, `free`…) | the account's type, just after signing in. Only `premium` can play: for any other, the helper exits with code 1 right after |
| `list` | key, from, name | a page of the list follows, its first item at position `from` (0 for the first page); name is the list's own name when the helper knows it (a playlist's, an album's, an artist's), else empty |
| `item-track` | uri, title, artist(s), album, duration ms, album uri, first artist's uri | a track of the list |
| `item-album` | uri, name, artist(s), year | an album of the list |
| `item-artist` | uri, name | an artist of the list |
| `item-playlist` | uri, name, owner (empty for your own), number of tracks | a playlist of the list |
| `list-end` | key, more (`1`/`0`) | that was the page; `1`: `more` sends another |
| `list-error` | key, message | the page didn't come; a message a person can read |
| `track` | uri, title, artist, album, duration ms, cover image URL (empty when there's none) | the loaded track's metadata; the cover is for the desktop's media widgets (MPRIS), the window shows none |
| `loading` | uri, position ms | loading the track |
| `playing` | uri, position ms | playing, from that position, now |
| `paused` | uri, position ms | paused at that position |
| `position` | uri, position ms | the position jumped (a seek, or a correction) |
| `near-end` | uri | about 30 s are left of the track and it's all downloaded: time to `preload` the next one |
| `end` | uri | the track played to its end |
| `stopped` | uri | the track was stopped |
| `unavailable` | uri | the track can't be played |
| `levels` | heard at (µs, monotonic clock), then 28 levels, `0` to `100` | the music's loudness in the visualizer's bands, lowest first, as it will be heard at that time |
| `error` | message | something failed; a message a person can read |

Position events name their track, so the window can ignore a late report for
a track it has already moved away from. There are no periodic position
reports: the window runs its own clock from the last `playing` event, so
nothing wakes while paused, and little while playing.

What comes next is the window's to say: the first track of its queue, else
the next track of the list the playing one was started from. The helper
has no queue of its own.

No gaps between tracks: on `near-end` the window sends `preload` with the
track that comes next, and on `end` it sends `load` for it. The player keeps
its audio output open from one track to the next, and the preloaded track
starts straight away.

When the helper can't go on (it can't sign in or reach the service), it
sends `error` and exits with code 1. On closed stdin it exits with code 0.

## Lists

The library panel's lists are asked for by key:

| key | rows | what |
|---|---|---|
| `liked` | tracks | your Liked Songs, newest first |
| `playlists` | playlists | your playlists, in your order (folders shown flat) |
| `albums` | albums | your saved albums, newest first |
| `artists` | artists | the artists you follow, newest first |
| `spotify:playlist:…` | tracks | a playlist's tracks (episodes and local files left out) |
| `spotify:album:…` | tracks | an album's tracks, in order |
| `spotify:artist:…` | albums | an artist's albums, then singles and EPs, then compilations |
| `search:songs:<words>` | tracks | the songs the search finds for the words |

Only songs can be searched for: the search the helper uses finds tracks
only. Albums and artists are reached from a song (its album, its artist).
Keys for an album or artist search (`search:albums:`, `search:artists:`)
would join the table if another endpoint is used for them.

A list comes a page at a time, up to 100 items a page: `get` sends the
first, and `more` the next one, while `list-end` said `1`. The window
asks for the next page as the end of what it shows comes near, and keeps
every list it got for the session, so switching tabs asks for nothing.
An item whose details didn't come is left out of its page, so `from` counts
the items sent, not the list's positions. Pages of one list come in order,
and one page at a time: the helper answers the commands one after another.

## Levels

The visualizer's 28 pills follow the music when the helper measures it.
It says it can in `hello` (a third field, `levels`); the window then sends
`levels 1` while the visualizer can be seen (on with `v`, and not hidden for
room) and `levels 0` when it can't. A helper that doesn't say `levels`
isn't asked, and the pills move on their own, as they do with the sample.

While asked, and only while it plays, the helper measures the samples on
their way to PulseAudio, unchanged: 30 times a second, the last 2048
samples (46 ms), mixed to mono, go through an FFT, and each band's power
(28 bands, evenly by octave from 40 Hz to 16 kHz) becomes a level from
`0` (−60 dB or quieter) to `100` (−12 dB or louder, against a full-scale
sine; the highs lifted 1.5 dB an octave above 1 kHz and lowered below it).
Paused, stopped or not asked, nothing is measured or sent.

Each frame says when it will be heard, in microseconds on the monotonic
clock (`CLOCK_MONOTONIC`, which is GLib's `g_get_monotonic_time` and the
window's frame clock): the time it's measured, plus PulseAudio's latency
(what it still has to play), less how far before the end of what was just
written the middle of those 2048 samples is. Frames come ahead of time, as
far ahead as PulseAudio buffers; the window keeps them until they're due
and moves each pill between the frames around now, on a spring.

    → hello	1	levels
    ← levels	1
    → levels	81234567890	62	70	58	…   (28 levels)

## Example

    → hello	1	levels
    → needs-signin
    → connecting
    → signed-in	someone
    → account	premium
    ← get	albums
    → list	albums	0
    → item-album	spotify:album:…	Meteora (Bonus Edition)	Linkin Park	2003
    → list-end	albums	0
    ← get	spotify:album:…
    → list	spotify:album:…	0	Meteora (Bonus Edition)
    → item-track	spotify:track:…	Foreword	Linkin Park	Meteora (Bonus Edition)	13000	spotify:album:…	spotify:artist:…
    → …
    → list-end	spotify:album:…	0
    ← load	spotify:track:…	1	0
    → loading	spotify:track:…	0
    → track	spotify:track:…	Foreword	Linkin Park	Meteora (Bonus Edition)	13000	https://i.scdn.co/image/…
    → playing	spotify:track:…	0
    ← pause
    → paused	spotify:track:…	5210
    ← play
    → playing	spotify:track:…	5210
    → near-end	spotify:track:…
    ← preload	spotify:track:…   (the next one)
    → end	spotify:track:…
    ← load	spotify:track:…	1	0
    → track	spotify:track:…	Don't Stay	Linkin Park	Meteora (Bonus Edition)	187933	https://i.scdn.co/image/…
    → playing	spotify:track:…	0

## Where the data comes from

The helper gets the library, the search and the metadata from the
service's own internal endpoints, the ones its official client uses,
through librespot's signed-in session (its spclient, with the session's
login5 token and client token). There's no developer app and no public Web
API: the Web API with librespot's shared client id answers 429 (too many
requests) at once.

These endpoints are **unofficial and undocumented**. The service can change
or close them at any time, and then this part breaks until the helper is
changed to match.

| what | endpoint | used by | notes |
|---|---|---|---|
| saved tracks and albums | `POST /collection/v2/paging`, `Content-Type` and `Accept` `application/vnd.collection-v2.spotify.proto` | `liked`, `albums` | One page of a collection set: `PageRequest` {username, set, pagination_token, limit} → `PageResponse` {items {uri, added_at, is_removed}, next_page_token} (`collection2v2.proto` in librespot-protocol). The set `collection` holds the saved tracks and the saved albums together; their URIs tell them apart. Its pages come in the order of their URIs, so the helper reads the whole set once (300 items a request, at most 200 requests), puts it newest first by `added_at` and keeps it for the session: liked songs and albums share it. |
| followed artists | the same, set `artist` | `artists` | Read and kept the same way. |
| playlists | `GET /playlist/v2/user/<user>/rootlist?decorate=…&from=…&length=…` | `playlists` | librespot's `get_rootlist`, 100 places a request; the names, owners and lengths come with it (`meta_items`). |
| a playlist's tracks | `GET /playlist/v2/playlist/<id>` | `spotify:playlist:…` | librespot's `Playlist::get`: all the track URIs at once; their details a page at a time. |
| an album's tracks | `ALBUM_V4` through extended metadata | `spotify:album:…` | librespot's `Album::get`. |
| an artist's albums | `ARTIST_V4` through extended metadata | `spotify:artist:…` | librespot's `Artist::get`: the current release of each album, single and compilation. |
| track, album and artist details | `POST /extended-metadata/v0/extended-metadata` (`TRACK_V4`, `ALBUM_V4`, `ARTIST_V4`) | every list | What librespot itself loads tracks through. The helper asks for up to 100 in one request: one request a page. |
| search | `GET /context-resolve/v1/spotify:search:<words>` | `search:songs:` | librespot's `get_context`. It finds tracks only. |

These were picked because they are what the official desktop client reads
the library through, they have kept their shape for years, librespot either
calls them itself or has a function for them, and other players built on
librespot use the same ones (go-librespot reads the collection the same
way). The official client's search goes through a GraphQL endpoint whose
queries are named by hashes that change with its releases, so the helper
uses context-resolve instead.

Each request is made once, and one at a time: the lists are fetched one
after another, never side by side. librespot tries a request up to three
times when the connection fails or the service answers 5xx, and not at all
on any other answer. A 429 (too many requests) fails the list that asked
(`list-error`), and for the next 30 seconds every list asked for fails at
once with the same message, without a request; after that, opening the
list again asks again. Nothing retries by itself.
