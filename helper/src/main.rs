//! nattsang-helper: plays music for the Nattsang window.
//!
//! The window starts it and talks to it over its stdin and stdout, a line
//! each way (docs/protocol.md). It signs in with librespot, once, through the
//! login page in the browser, and keeps the credentials in ~/.cache/nattsang.
//! It plays at 320 kbps, as recorded (no normalisation), through PulseAudio
//! (sink.rs, which also measures the levels the visualizer shows), and listens on nothing but the sign-in redirect on 127.0.0.1 while that
//! page is open: no Spotify Connect, no discovery. When its stdin closes (the
//! window has gone) it exits. The library (liked songs, playlists, saved
//! albums, followed artists), the metadata and the search come from the
//! service's own (unofficial) endpoints through the session's spclient, not
//! the public Web API, one request at a time.

use std::collections::HashMap;
use std::fmt::Write as _;
use std::fs::{self, File};
use std::io::{BufRead, Write};
use std::os::fd::FromRawFd;
use std::os::unix::fs::{DirBuilderExt, PermissionsExt};
use std::path::PathBuf;
use std::sync::{Mutex, OnceLock};
use std::time::{Duration, Instant};

use librespot_core::{
    Error, Session, SessionConfig, SpotifyUri, authentication::Credentials, cache::Cache,
    error::ErrorKind, spclient::RequestStrategy,
};
use http::{
    HeaderMap, HeaderValue, Method,
    header::{ACCEPT, CONTENT_TYPE},
};
use librespot_metadata::{
    Album, Artist, Metadata, Playlist, Track,
    audio::{UniqueFields, item::CoverImage},
};
use librespot_oauth::OAuthClientBuilder;
use librespot_protocol::{
    extended_metadata::{BatchedEntityRequest, EntityRequest, ExtensionQuery},
    extension_kind::ExtensionKind,
    playlist4_external::SelectedListContent,
};
use librespot_playback::{
    config::{Bitrate, PlayerConfig},
    mixer::NoOpVolume,
    player::{Player, PlayerEvent, PlayerEventChannel},
};
use protobuf::{CodedInputStream, CodedOutputStream, Message as _};
use tokio::sync::mpsc;

mod levels;
mod sink;

const PROTOCOL: &str = "1";

// The sign-in page sends the browser back to http://127.0.0.1:5588/login,
// librespot's own default: a listener that takes that one request and closes.
const OAUTH_PORT: u16 = 5588;
const SCOPES: &[&str] = &[
    "streaming",
    "user-read-private",
    "user-library-read",
    "playlist-read-private",
    "playlist-read-collaborative",
];
const SIGNED_IN_PAGE: &str = "Signed in. You can close this tab and go back to Nattsang.";

fn main() {
    // stdout carries the protocol and nothing else. Keep a copy of it for
    // the events (closed on exec, so a browser started for the sign-in
    // doesn't hold it open) and point fd 1 at stderr: whatever a library
    // prints (librespot's sign-in prints the login URL) goes there instead.
    let fd = unsafe { libc::fcntl(1, libc::F_DUPFD_CLOEXEC, 3) };
    if fd < 0 || unsafe { libc::dup2(2, 1) } < 0 {
        eprintln!("nattsang-helper: can't set up stdout");
        std::process::exit(1);
    }
    let _ = OUT.set(Mutex::new(unsafe { File::from_raw_fd(fd) }));

    // Before any thread starts: PulseAudio (and PipeWire) name the stream by these.
    unsafe {
        std::env::set_var("PULSE_PROP_application.name", "Nattsang");
        std::env::set_var("PULSE_PROP_stream.description", "Music");
    }
    if log::set_logger(&LOGGER).is_ok() {
        log::set_max_level(match std::env::var("NATTSANG_LOG").as_deref() {
            Ok("debug") => log::LevelFilter::Debug,
            Ok("info") => log::LevelFilter::Info,
            _ => log::LevelFilter::Warn,
        });
    }

    let (commands, rx) = mpsc::unbounded_channel();
    std::thread::spawn(move || {
        for line in std::io::stdin().lock().lines() {
            let Ok(line) = line else { break };
            if commands.send(line).is_err() {
                break;
            }
        }
        std::process::exit(0); // stdin closed: the window has gone
    });

    // It measures the music's levels for the visualizer, when asked.
    emit(&["hello", PROTOCOL, "levels"]);
    let runtime = tokio::runtime::Builder::new_current_thread()
        .enable_all()
        .build()
        .unwrap_or_else(|e| fail(&format!("can't start: {e}")));
    runtime.block_on(run(rx));
}

async fn run(mut commands: mpsc::UnboundedReceiver<String>) {
    let cache = cache().unwrap_or_else(|e| fail(&e));
    let session = sign_in(&cache).await.unwrap_or_else(|e| fail(&e));
    emit(&["signed-in", &session.username()]);
    // librespot tries a request ten times, at once, while the service answers
    // 5xx or times out; three is enough, and kinder to it.
    session.spclient().set_strategy(RequestStrategy::TryTimes(3));
    tokio::spawn(account(session.clone()));

    let config = PlayerConfig {
        bitrate: Bitrate::Bitrate320,
        normalisation: false,
        ..PlayerConfig::default()
    };
    // The output is our own (sink.rs): PulseAudio, with the visualizer's tap.
    let player = Player::new(config, session.clone(), Box::new(NoOpVolume), || Box::new(sink::Tap::new()));
    tokio::spawn(events(player.get_player_event_channel()));
    // The lists, asked for one after another: one request at a time.
    let (lists, asked) = mpsc::unbounded_channel();
    tokio::spawn(library(session.clone(), asked));

    // Commands sent before the sign-in waited here, and run now.
    while let Some(line) = commands.recv().await {
        command(&line, &player, &lists);
    }
}

// ---- signing in ---------------------------------------------------------------

// ~/.cache/nattsang (or $XDG_CACHE_HOME/nattsang), only the owner may read it:
// the credentials go there, nothing else (no audio cache).
fn cache() -> Result<Cache, String> {
    let base = std::env::var_os("XDG_CACHE_HOME")
        .filter(|d| !d.is_empty())
        .map(PathBuf::from)
        .or_else(|| std::env::var_os("HOME").map(|h| PathBuf::from(h).join(".cache")))
        .ok_or("no HOME to keep the sign-in in")?;
    let dir = base.join("nattsang");
    let made = fs::DirBuilder::new().recursive(true).mode(0o700).create(&dir);
    made.and_then(|()| fs::set_permissions(&dir, fs::Permissions::from_mode(0o700)))
        .map_err(|e| format!("can't use {}: {e}", dir.display()))?;
    Cache::new(Some(&dir), None, None, None).map_err(|e| format!("can't use {}: {e}", dir.display()))
}

// With saved credentials it signs in by itself. Without them, or when they're
// refused, it opens the login page in the browser and waits for it.
async fn sign_in(cache: &Cache) -> Result<Session, String> {
    if let Some(credentials) = cache.credentials() {
        emit(&["connecting"]);
        let session = Session::new(SessionConfig::default(), Some(cache.clone()));
        match session.connect(credentials, true).await {
            Ok(()) => return Ok(session),
            Err(e) if e.kind == ErrorKind::PermissionDenied => {
                log::warn!("the saved sign-in was refused ({e}); signing in again")
            }
            Err(e) => return Err(format!("can't connect: {e}")),
        }
    }

    emit(&["needs-signin"]);
    let client_id = SessionConfig::default().client_id;
    let token = tokio::task::spawn_blocking(move || {
        let redirect = format!("http://127.0.0.1:{OAUTH_PORT}/login");
        OAuthClientBuilder::new(&client_id, &redirect, SCOPES.to_vec())
            .open_in_browser()
            .with_custom_message(SIGNED_IN_PAGE)
            .build()?
            .get_access_token()
    })
    .await
    .map_err(|e| format!("sign-in stopped: {e}"))?
    .map_err(|e| format!("sign-in failed: {e}"))?;

    emit(&["connecting"]);
    let session = Session::new(SessionConfig::default(), Some(cache.clone()));
    session
        .connect(Credentials::with_access_token(token.access_token), true)
        .await
        .map_err(|e| format!("can't connect: {e}"))?;
    Ok(session)
}

// ---- commands -------------------------------------------------------------------

fn command(line: &str, player: &Player, lists: &mpsc::UnboundedSender<(String, bool)>) {
    let fields: Vec<&str> = line.split('\t').collect();
    match fields.as_slice() {
        [""] => {}
        ["load", uri, rest @ ..] => match SpotifyUri::from_uri(uri) {
            Ok(id) if id.is_playable() => {
                let play = rest.first() != Some(&"0");
                let ms = rest.get(1).and_then(|m| m.parse().ok()).unwrap_or(0);
                player.load(id, play, ms);
            }
            _ => error(&format!("not a track: {uri}")),
        },
        ["play"] => player.play(),
        ["pause"] => player.pause(),
        ["stop"] => player.stop(),
        ["seek", ms] => match ms.parse() {
            Ok(ms) => player.seek(ms),
            Err(_) => error(&format!("not a position: {ms}")),
        },
        ["preload", uri] => match SpotifyUri::from_uri(uri) {
            Ok(id) if id.is_playable() => player.preload(id),
            _ => error(&format!("not a track: {uri}")),
        },
        ["levels", on] => sink::LEVELS.store(*on == "1", std::sync::atomic::Ordering::Relaxed),
        ["volume", gain] => match gain.parse::<f64>() {
            Ok(g) if g.is_finite() => sink::set_gain(g),
            _ => error(&format!("not a volume: {gain}")),
        },
        ["get", key] => drop(lists.send((key.to_string(), true))),
        ["more", key] => drop(lists.send((key.to_string(), false))),
        _ => error(&format!("unknown command: {line}")),
    }
}

// ---- the library's lists ------------------------------------------------------
//
// A list comes a page at a time (docs/protocol.md, "Lists"): `get` sends the
// first, `more` the next, each as `list`, its items and `list-end`. What
// was read of the collection is kept for the session, so liked songs and
// saved albums (one set) are read once, and a page only reads as far as it
// needs. Each list remembers where its next page starts.

// What a list's rows are, and so which metadata they need.
#[derive(Clone, Copy, PartialEq)]
enum Rows {
    Tracks,
    Albums,
    Artists,
    Playlists,
}

// Where a list's next page comes from.
enum Source {
    // The URIs of one collection set that start with prefix, from at on.
    Saved { set: &'static str, prefix: &'static str, at: usize },
    // A list of URIs known in full (a playlist's, an album's, an artist's
    // albums, a search), from at on.
    Uris { uris: Vec<String>, at: usize },
    // The user's playlists, from at on, of length.
    Rootlist { at: usize, length: usize },
}

struct List {
    rows: Rows,
    source: Source,
    sent: usize, // items sent so far
}

// One collection set, read in full the first time a list needs it: its
// pages come in the order of their URIs, so only all of it can be put
// newest first, as the official client shows it.
struct Set {
    uris: Vec<String>,
}

const ROWS: usize = 100; // items per page of a list: one metadata request
const MAX_PAGES: usize = 200; // collection pages per set, at most (60,000 items)
// After a 429, how long nothing is asked of the service.
const REST: Duration = Duration::from_secs(30);

async fn library(session: Session, mut asked: mpsc::UnboundedReceiver<(String, bool)>) {
    let mut sets: HashMap<&'static str, Set> = HashMap::new();
    let mut lists: HashMap<String, List> = HashMap::new();
    let mut resting: Option<Instant> = None;
    while let Some((key, first)) = asked.recv().await {
        if let Some(until) = resting.filter(|u| *u > Instant::now()) {
            let wait = (until - Instant::now()).as_secs() + 1;
            emit(&["list-error", &key, &format!("the service is turning requests away for now (429); try again in {wait} s")]);
            continue;
        }
        if first {
            lists.remove(&key);
        }
        if let Err(e) = page(&session, &key, &mut lists, &mut sets).await {
            if e.kind == ErrorKind::ResourceExhausted {
                resting = Some(Instant::now() + REST);
            }
            lists.remove(&key);
            emit(&["list-error", &key, &why(&e)]);
        }
    }
}

// The list's next page, or its first when it hasn't one yet.
async fn page(
    session: &Session,
    key: &str,
    lists: &mut HashMap<String, List>,
    sets: &mut HashMap<&'static str, Set>,
) -> Result<(), Error> {
    let mut title = String::new();
    if !lists.contains_key(key) {
        let (rows, source, name) = start(session, key).await?;
        title = name;
        lists.insert(key.to_string(), List { rows, source, sent: 0 });
    }
    let Some(list) = lists.get_mut(key) else { return Ok(()) };
    if let Source::Rootlist { at, length } = &mut list.source {
        let (items, read, total) = rootlist(session, *at).await?;
        *at += read;
        *length = total;
        emit(&["list", key, &list.sent.to_string(), ""]);
        for (uri, name, owner, tracks) in &items {
            emit(&["item-playlist", uri, name, owner, &tracks.to_string()]);
        }
        list.sent += items.len();
        let more = read > 0 && *at < *length;
        emit(&["list-end", key, if more { "1" } else { "0" }]);
        return Ok(());
    }

    let (uris, more) = next_uris(session, &mut list.source, sets).await?;
    let from = list.sent.to_string();
    let sent = match list.rows {
        Rows::Tracks => {
            let found = metadata::<Track>(session, ExtensionKind::TRACK_V4, &uris).await?;
            emit(&["list", key, &from, &title]);
            for t in uris.iter().filter_map(|u| found.get(u).map(|t| (u, t))) {
                let (uri, t) = t;
                let album = t.album.id.to_uri().unwrap_or_default();
                let artist = t.artists.first().and_then(|a| a.id.to_uri().ok()).unwrap_or_default();
                let artists = names(t.artists.iter().map(|a| a.name.as_str()));
                emit(&["item-track", uri, &t.name, &artists, &t.album.name, &t.duration.max(0).to_string(), &album, &artist]);
            }
            uris.iter().filter(|u| found.contains_key(*u)).count()
        }
        Rows::Albums => {
            let found = metadata::<Album>(session, ExtensionKind::ALBUM_V4, &uris).await?;
            emit(&["list", key, &from, &title]);
            for (uri, a) in uris.iter().filter_map(|u| found.get(u).map(|a| (u, a))) {
                let year = a.date.as_utc().year();
                let year = if year > 0 { year.to_string() } else { String::new() };
                emit(&["item-album", uri, &a.name, &names(a.artists.iter().map(|a| a.name.as_str())), &year]);
            }
            uris.iter().filter(|u| found.contains_key(*u)).count()
        }
        Rows::Artists => {
            let found = metadata::<Artist>(session, ExtensionKind::ARTIST_V4, &uris).await?;
            emit(&["list", key, &from, &title]);
            for (uri, a) in uris.iter().filter_map(|u| found.get(u).map(|a| (u, a))) {
                emit(&["item-artist", uri, &a.name]);
            }
            uris.iter().filter(|u| found.contains_key(*u)).count()
        }
        Rows::Playlists => 0,
    };
    list.sent += sent;
    emit(&["list-end", key, if more { "1" } else { "0" }]);
    Ok(())
}

// What a key's rows are, where they come from, and its name ("" when the
// window's own will do). Playlists, albums and artists are read here, once;
// the tabs and a search need nothing yet.
async fn start(session: &Session, key: &str) -> Result<(Rows, Source, String), Error> {
    let saved = |set, prefix| Source::Saved { set, prefix, at: 0 };
    Ok(match key {
        "liked" => (Rows::Tracks, saved(SAVED, "spotify:track:"), String::new()),
        "albums" => (Rows::Albums, saved(SAVED, "spotify:album:"), String::new()),
        "artists" => (Rows::Artists, saved(FOLLOWED, "spotify:artist:"), String::new()),
        "playlists" => (Rows::Playlists, Source::Rootlist { at: 0, length: 0 }, String::new()),
        _ if key.starts_with("search:songs:") => {
            let uris = search(session, &key["search:songs:".len()..]).await?;
            (Rows::Tracks, Source::Uris { uris, at: 0 }, String::new())
        }
        // "search:albums:" and "search:artists:" would go here: they need
        // another endpoint than context-resolve, which finds tracks only.
        _ => {
            let uri = SpotifyUri::from_uri(key)?;
            match &uri {
                SpotifyUri::Playlist { .. } => {
                    let p = Playlist::get(session, &uri).await?;
                    let uris = p.tracks().filter_map(|t| t.to_uri().ok()).filter(|u| u.starts_with("spotify:track:")).collect();
                    (Rows::Tracks, Source::Uris { uris, at: 0 }, p.name().to_string())
                }
                SpotifyUri::Album { .. } => {
                    let a = Album::get(session, &uri).await?;
                    let uris = a.tracks().map(SpotifyUri::to_uri).collect::<Result<_, _>>()?;
                    (Rows::Tracks, Source::Uris { uris, at: 0 }, a.name)
                }
                // Its albums, then its singles and EPs, then its compilations.
                SpotifyUri::Artist { .. } => {
                    let a = Artist::get(session, &uri).await?;
                    let all = a.albums_current().chain(a.singles_current()).chain(a.compilations_current());
                    let uris = all.map(SpotifyUri::to_uri).collect::<Result<_, _>>()?;
                    (Rows::Albums, Source::Uris { uris, at: 0 }, a.name)
                }
                _ => return Err(Error::invalid_argument(format!("not a list: {key}"))),
            }
        }
    })
}

// The next page's URIs (ROWS of them, or what's left), and whether more
// come after. A collection set is read on as far as that needs.
async fn next_uris(
    session: &Session,
    source: &mut Source,
    sets: &mut HashMap<&'static str, Set>,
) -> Result<(Vec<String>, bool), Error> {
    match source {
        Source::Uris { uris, at } => {
            let page: Vec<String> = uris.iter().skip(*at).take(ROWS).cloned().collect();
            *at += page.len();
            Ok((page, *at < uris.len()))
        }
        Source::Saved { set, prefix, at } => {
            if !sets.contains_key(set) {
                sets.insert(set, read_set(session, set).await?);
            }
            let uris = &sets[set].uris;
            let mut page = Vec::new();
            while *at < uris.len() && page.len() < ROWS {
                if uris[*at].starts_with(*prefix) {
                    page.push(uris[*at].clone());
                }
                *at += 1;
            }
            let more = uris[*at..].iter().any(|u| u.starts_with(*prefix));
            Ok((page, more))
        }
        Source::Rootlist { .. } => Ok((Vec::new(), false)),
    }
}

// All of one collection set, newest first: a page of PAGE items a request,
// one request after another, MAX_PAGES at most.
async fn read_set(session: &Session, set: &str) -> Result<Set, Error> {
    let (mut items, mut token) = (Vec::new(), String::new());
    for page in 1..=MAX_PAGES {
        let (uris, next) = collection_page(session, set, &token).await?;
        log::info!("{set}, page {page}: {} items", uris.len());
        items.extend(uris);
        if next.is_empty() || next == token {
            break;
        }
        token = next;
    }
    items.sort_by_key(|(_, added)| std::cmp::Reverse(*added));
    Ok(Set { uris: items.into_iter().map(|(uri, _)| uri).collect() })
}

// A page of the user's playlists from position from: each one's URI, name,
// owner ("" for the user's own) and number of tracks; how many places it
// read (folders take places too), and how many there are in all.
async fn rootlist(session: &Session, from: usize) -> Result<(Vec<(String, String, String, i32)>, usize, usize), Error> {
    let bytes = session.spclient().get_rootlist(from, Some(ROWS)).await?;
    let list = SelectedListContent::parse_from_bytes(&bytes)?;
    let user = session.username();
    let Some(contents) = list.contents.as_ref() else { return Ok((Vec::new(), 0, 0)) };
    let mut items = Vec::new();
    for (i, item) in contents.items.iter().enumerate() {
        // Folders are a start-group and an end-group around their playlists: shown flat.
        if !item.uri().contains(":playlist:") {
            continue;
        }
        let meta = contents.meta_items.get(i);
        let name = meta.and_then(|m| m.attributes.as_ref()).map(|a| a.name().to_string()).unwrap_or_default();
        let owner = meta.map(|m| m.owner_username()).filter(|o| *o != user).unwrap_or_default();
        items.push((item.uri().to_string(), name, owner.to_string(), meta.map(|m| m.length()).unwrap_or(0)));
    }
    Ok((items, contents.items.len(), list.length().max(0) as usize))
}

// ---- the service's own endpoints ----------------------------------------------
//
// What the official client uses, through librespot's session and its
// spclient (docs/protocol.md, "Where the data comes from"). They aren't a
// public API: they can change without notice. Nothing here retries a request
// (librespot itself tries one up to three times, on a 5xx or a timeout only),
// and a refusal, a 429 among them, stops what asked for it.

const COLLECTION_PAGING: &str = "/collection/v2/paging";
const COLLECTION_TYPE: &str = "application/vnd.collection-v2.spotify.proto";
// The collection set with the saved tracks and the saved albums, mixed; their
// URIs tell them apart. Followed artists are in "artist".
const SAVED: &str = "collection";
const FOLLOWED: &str = "artist";
const PAGE: i32 = 300; // items per collection page
const BATCH: usize = 100; // entities per metadata request

// One page of one of the user's collection sets: the URIs still in it, with
// when each was added, and the token for the next page ("" after the last). The messages are
// collection2v2.proto's PageRequest and PageResponse, which librespot doesn't
// compile, written and read by hand.
async fn collection_page(session: &Session, set: &str, token: &str) -> Result<(Vec<(String, i32)>, String), Error> {
    let body = page_request(&session.username(), set, token)?;
    let mut headers = HeaderMap::new();
    headers.insert(CONTENT_TYPE, HeaderValue::from_static(COLLECTION_TYPE));
    headers.insert(ACCEPT, HeaderValue::from_static(COLLECTION_TYPE));
    let answer = session.spclient().request(&Method::POST, COLLECTION_PAGING, Some(headers), Some(&body)).await?;
    Ok(collection_items(&answer)?)
}

// A PageRequest: username (1), set (2), pagination_token (3, left out on the
// first page), limit (4).
fn page_request(username: &str, set: &str, token: &str) -> protobuf::Result<Vec<u8>> {
    let mut body = Vec::new();
    let mut out = CodedOutputStream::vec(&mut body);
    out.write_string(1, username)?;
    out.write_string(2, set)?;
    if !token.is_empty() {
        out.write_string(3, token)?;
    }
    out.write_int32(4, PAGE)?;
    out.flush()?;
    drop(out);
    Ok(body)
}

// A PageResponse: items (1) are CollectionItems with uri (1), added_at (2)
// and is_removed (3); next_page_token is 2. Anything else is skipped.
fn collection_items(bytes: &[u8]) -> protobuf::Result<(Vec<(String, i32)>, String)> {
    let (mut uris, mut next) = (Vec::new(), String::new());
    let mut input = CodedInputStream::from_bytes(bytes);
    while let Some(tag) = input.read_raw_tag_or_eof()? {
        match tag {
            10 => {
                let item = input.read_bytes()?;
                let (mut uri, mut added, mut removed) = (String::new(), 0, false);
                let mut fields = CodedInputStream::from_bytes(&item);
                while let Some(tag) = fields.read_raw_tag_or_eof()? {
                    match tag {
                        10 => uri = fields.read_string()?,
                        16 => added = fields.read_int32()?,
                        24 => removed = fields.read_bool()?,
                        _ => protobuf::rt::skip_field_for_tag(tag, &mut fields)?,
                    }
                }
                if !removed && !uri.is_empty() {
                    uris.push((uri, added));
                }
            }
            18 => next = input.read_string()?,
            _ => protobuf::rt::skip_field_for_tag(tag, &mut input)?,
        }
    }
    Ok((uris, next))
}

// The metadata of many albums or tracks, by URI, BATCH to a request (the
// extended metadata endpoint librespot fetches one at a time through). One
// that comes back without data, or that doesn't parse, is left out.
async fn metadata<T>(session: &Session, kind: ExtensionKind, uris: &[String]) -> Result<HashMap<String, T>, Error>
where
    T: Metadata,
    for<'a> T: TryFrom<&'a T::Message, Error = Error>,
{
    let mut found = HashMap::new();
    for chunk in uris.chunks(BATCH) {
        let request = BatchedEntityRequest {
            entity_request: chunk
                .iter()
                .map(|uri| EntityRequest {
                    entity_uri: uri.clone(),
                    query: vec![ExtensionQuery { extension_kind: kind.into(), ..Default::default() }],
                    ..Default::default()
                })
                .collect(),
            ..Default::default()
        };
        let answer = session.spclient().get_extended_metadata(request).await?;
        for data in answer.extended_metadata.iter().flat_map(|a| &a.extension_data) {
            let Some(any) = data.extension_data.as_ref() else { continue };
            match T::Message::parse_from_bytes(&any.value).map_err(Error::from).and_then(|m| T::try_from(&m)) {
                Ok(item) => drop(found.insert(data.entity_uri.clone(), item)),
                Err(e) => log::warn!("{}: {e}", data.entity_uri),
            }
        }
    }
    Ok(found)
}

// The tracks the search finds for the words, best first: context-resolve on
// a spotify:search: URI, as librespot's spclient documents it. It finds tracks
// only; their metadata says which album each is on.
async fn search(session: &Session, words: &str) -> Result<Vec<String>, Error> {
    let words: String = encode(words.trim()).replace("%20", "+");
    let context = session.spclient().get_context(&format!("spotify:search:{words}")).await?;
    let tracks = context.pages.iter().flat_map(|p| &p.tracks);
    Ok(tracks.filter_map(|t| t.uri.clone()).filter(|u| u.starts_with("spotify:track:")).collect())
}

// What went wrong, for a person. A 429 says the service is turning requests
// away: nothing here retries it, and the lists ask nothing for REST after.
fn why(e: &Error) -> String {
    match e.kind {
        ErrorKind::ResourceExhausted => format!("the service is turning requests away for now (429); try again in {} s", REST.as_secs()),
        _ => e.to_string(),
    }
}

// The account's type ("premium", "free"...) arrives just after the sign-in.
// librespot ends the process for anything but premium, and the logger
// reports that one (below); this waits up to five seconds for the rest.
async fn account(session: Session) {
    for _ in 0..50 {
        if let Some(kind) = session.get_user_attribute("type") {
            emit(&["account", &kind]);
            return;
        }
        tokio::time::sleep(Duration::from_millis(100)).await;
    }
    log::warn!("the account's type didn't arrive");
}

// ---- events ---------------------------------------------------------------------

async fn events(mut events: PlayerEventChannel) {
    while let Some(event) = events.recv().await {
        match event {
            PlayerEvent::TrackChanged { audio_item } => {
                let (artists, album) = match &audio_item.unique_fields {
                    UniqueFields::Track { artists, album, .. } => {
                        (names(artists.iter().map(|a| a.name.as_str())), album.clone())
                    }
                    UniqueFields::Local { artists, album, .. } => {
                        (artists.clone().unwrap_or_default(), album.clone().unwrap_or_default())
                    }
                    UniqueFields::Episode { show_name, .. } => (show_name.clone(), String::new()),
                };
                let uri = uri(&audio_item.track_id);
                let ms = audio_item.duration_ms.to_string();
                emit(&["track", &uri, &audio_item.name, &artists, &album, &ms, cover(&audio_item.covers)]);
            }
            PlayerEvent::Loading { track_id, position_ms, .. } => {
                emit(&["loading", &uri(&track_id), &position_ms.to_string()])
            }
            PlayerEvent::Playing { track_id, position_ms, .. } => {
                emit(&["playing", &uri(&track_id), &position_ms.to_string()])
            }
            PlayerEvent::Paused { track_id, position_ms, .. } => {
                emit(&["paused", &uri(&track_id), &position_ms.to_string()])
            }
            PlayerEvent::Seeked { track_id, position_ms, .. }
            | PlayerEvent::PositionCorrection { track_id, position_ms, .. } => {
                emit(&["position", &uri(&track_id), &position_ms.to_string()])
            }
            // Half a minute before the end, once this one is all downloaded:
            // time to preload the next, so it follows without a gap.
            PlayerEvent::TimeToPreloadNextTrack { track_id, .. } => emit(&["near-end", &uri(&track_id)]),
            PlayerEvent::EndOfTrack { track_id, .. } => emit(&["end", &uri(&track_id)]),
            PlayerEvent::Unavailable { track_id, .. } => emit(&["unavailable", &uri(&track_id)]),
            PlayerEvent::Stopped { track_id, .. } => emit(&["stopped", &uri(&track_id)]),
            _ => {}
        }
    }
}

// ---- the protocol's lines ---------------------------------------------------------

static OUT: OnceLock<Mutex<File>> = OnceLock::new();

// One event: its fields joined by tabs; a tab or line break inside a field
// becomes a space. One write per line, so lines from two tasks never mix.
pub(crate) fn emit(fields: &[&str]) {
    let mut line = String::new();
    for (i, f) in fields.iter().enumerate() {
        if i > 0 {
            line.push('\t');
        }
        line.extend(f.chars().map(|c| if matches!(c, '\t' | '\n' | '\r') { ' ' } else { c }));
    }
    line.push('\n');
    let Some(out) = OUT.get() else { return };
    let mut out = out.lock().unwrap_or_else(|e| e.into_inner());
    if out.write_all(line.as_bytes()).is_err() {
        std::process::exit(0); // nobody reads: the window has gone
    }
}

fn error(message: &str) {
    emit(&["error", message]);
}

// Says why and stops: the window shows the message.
fn fail(message: &str) -> ! {
    error(message);
    std::process::exit(1)
}

fn uri(id: &SpotifyUri) -> String {
    id.to_uri().unwrap_or_default()
}

// The cover for a desktop's media widget: the largest up to 640 px wide
// (the service's are 64, 300 and 640), else whichever there is.
fn cover(covers: &[CoverImage]) -> &str {
    let fits = covers.iter().filter(|c| c.width <= 640).max_by_key(|c| c.width);
    fits.or(covers.first()).map(|c| c.url.as_str()).unwrap_or("")
}

fn names<'a>(names: impl Iterator<Item = &'a str>) -> String {
    names.collect::<Vec<_>>().join(", ")
}

// Percent-encodes a query value.
fn encode(text: &str) -> String {
    let mut out = String::new();
    for b in text.bytes() {
        if b.is_ascii_alphanumeric() || b"-_.~".contains(&b) {
            out.push(b as char);
        } else {
            let _ = write!(out, "%{b:02X}");
        }
    }
    out
}

// librespot's log, warnings and worse, on stderr (NATTSANG_LOG=info or debug for more).
struct Stderr;

impl log::Log for Stderr {
    fn enabled(&self, m: &log::Metadata) -> bool {
        m.level() <= log::max_level()
    }

    fn log(&self, r: &log::Record) {
        // For an account that isn't Premium, librespot-core 0.8 logs this and
        // ends the process (session.rs, check_catalogue): pass the account
        // type on first, so the window can say why.
        if r.level() == log::Level::Error && r.target() == "librespot_core::session" {
            let text = r.args().to_string();
            let kind = text.strip_prefix("librespot does not support ").and_then(|t| t.strip_suffix(" accounts."));
            if let Some(kind) = kind {
                emit(&["account", kind.trim_matches('"')]);
            }
        }
        if self.enabled(r.metadata()) {
            eprintln!("nattsang-helper: {} {}: {}", r.level(), r.target(), r.args());
        }
    }

    fn flush(&self) {}
}

static LOGGER: Stderr = Stderr;
