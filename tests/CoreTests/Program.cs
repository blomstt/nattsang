using Nattsang.Core;
using Nattsang.Playback;

var failed = 0;
var run = 0;

void Check(string name, bool ok, string detail = "")
{
    run++;
    if (ok) return;
    failed++;
    Console.WriteLine($"FAIL {name}{(detail.Length > 0 ? ": " + detail : "")}");
}

void Equal<T>(string name, T expected, T actual) =>
    Check(name, EqualityComparer<T>.Default.Equals(expected, actual), $"expected {expected}, got {actual}");

// ---- time ------------------------------------------------------------------

Equal("time: 0", "0:00", Time.Format(0));
Equal("time: 72 s", "1:12", Time.Format(72));
Equal("time: 187 s", "3:07", Time.Format(187));
Equal("time: a fraction rounds down", "1:12", Time.Format(72.99));
Equal("time: a minute exactly", "1:00", Time.Format(60));
Equal("time: an hour stays minutes", "60:00", Time.Format(3600));
Equal("time: negative shows 0:00", "0:00", Time.Format(-5));
Equal("time: NaN shows 0:00", "0:00", Time.Format(double.NaN));
Equal("time: parse 3:07", 187, Time.Parse("3:07"));
Equal("time: parse 0:13", 13, Time.Parse("0:13"));
foreach (var bad in new[] { "", "3", "3:7", ":07", "3:60", "3:070", "a:07" })
    Check($"time: parse rejects \"{bad}\"", Throws(() => Time.Parse(bad)));

// ---- the sample album --------------------------------------------------------

Equal("sample: 13 tracks", 13, Sample.Tracks.Length);
Equal("sample: Numb plays", "Numb", Sample.Tracks[Sample.Playing].Title);
Equal("sample: Numb is 3:07", 187, Sample.Tracks[Sample.Playing].Seconds);
Equal("sample: numbered from 1", 1, Sample.Tracks[0].Number);
Equal("sample: the queue is the first 7", "Foreword,Don't Stay,Somewhere I Belong,Lying from You,Hit the Floor,Easier to Run,Faint",
    string.Join(",", Sample.Queue.Select(t => t.Title)));

// ---- lyrics -------------------------------------------------------------------

var lines = Lyrics.Placeholder(187);
Equal("lyrics: 48 lines for 3:07, as the design", 48, lines.Length);
Equal("lyrics: placeholder text", "[lyric line 1]", lines[0]);
Equal("lyrics: last placeholder", "[lyric line 48]", lines[^1]);
Equal("lyrics: a 13 s track has 5", 5, Lyrics.Placeholder(13).Length);
Equal("lyrics: line at 0 s", 0, Lyrics.LineAt(0, 48));
Equal("lyrics: line at 3.99 s", 0, Lyrics.LineAt(3.99, 48));
Equal("lyrics: line at 4 s", 1, Lyrics.LineAt(4, 48));
Equal("lyrics: line at 1:12 (the design's start)", 18, Lyrics.LineAt(72, 48));
Equal("lyrics: line at the end", 46, Lyrics.LineAt(187, 48));
Equal("lyrics: never past the last line", 47, Lyrics.LineAt(10_000, 48));
Equal("lyrics: negative time is the first line", 0, Lyrics.LineAt(-3, 48));
Equal("lyrics: NaN is the first line", 0, Lyrics.LineAt(double.NaN, 48));
Equal("lyrics: no lines", 0, Lyrics.LineAt(30, 0));
Equal("lyrics: tone current", Lyrics.Tone.Current, Lyrics.ToneAt(0));
Equal("lyrics: tone next", Lyrics.Tone.Next, Lyrics.ToneAt(1));
Equal("lyrics: tone the one before", Lyrics.Tone.Near, Lyrics.ToneAt(-1));
Equal("lyrics: tone two ahead", Lyrics.Tone.Near, Lyrics.ToneAt(2));
Equal("lyrics: tone three ahead", Lyrics.Tone.Far, Lyrics.ToneAt(3));
Equal("lyrics: tone two back", Lyrics.Tone.Far, Lyrics.ToneAt(-2));
Equal("lyrics: offset holds line 6 in place", 0.0, Lyrics.Offset(6));
Equal("lyrics: offset at line 18", -396.0, Lyrics.Offset(18));
Equal("lyrics: offset at line 0 pushes down", 198.0, Lyrics.Offset(0));

// ---- the queue ------------------------------------------------------------------

var q = new Queue(Sample.Queue);
var changes = 0;
q.Changed += () => changes++;
Equal("queue: 7 tracks", 7, q.Count);
Equal("queue: summary as the design", "7 tracks · 18:38", q.Summary);
q.Add(Sample.Tracks[12]);
Equal("queue: add appends", "Numb", q.Items[^1].Title);
Equal("queue: add counts", 8, q.Count);
q.Add(Sample.Tracks[12]);
Equal("queue: a track can be queued twice", 9, q.Count);
q.RemoveAt(0);
Equal("queue: remove the first", "Don't Stay", q.Items[0].Title);
q.RemoveAt(99);
q.RemoveAt(-1);
Equal("queue: remove out of range does nothing", 8, q.Count);
Equal("queue: changes so far", 3, changes);
var before = q.Items.Select(t => t.Title).OrderBy(t => t).ToList();
q.Shuffle(new Random(1));
Equal("queue: shuffle keeps the tracks", string.Join(",", before), string.Join(",", q.Items.Select(t => t.Title).OrderBy(t => t)));
q.Clear();
Equal("queue: clear", 0, q.Count);
Equal("queue: empty summary", "0 tracks · 0:00", q.Summary);
var c = changes;
q.Clear();
q.Shuffle(new Random(1));
Equal("queue: clearing or shuffling nothing changes nothing", c, changes);
q.Add(Sample.Tracks[0]);
Equal("queue: one track", "1 track · 0:13", q.Summary);

// ---- palette ----------------------------------------------------------------------

var d = Palette.Default;
Equal("palette: default text", 0x958f92u, d.Text);
Equal("palette: default accent", 0x80d4d9u, d.Accent);
Equal("palette: default playing", 0xffb1c4u, d.Playing);
Equal("palette: hex", "#00010b", d.Hex("background"));
var p = Palette.Parse("""
    /* matugen output */
    @define-color accent #112233;
    @define-color   playing   #abc ;
    @define-color label #GGHHII;
    @define-color nonsense #ffffff;
    @define-color border #12345;
    /* @define-color text #ffffff; */
    @define-color raised rgb(1, 2, 3);
    @define-color bright #A0B0C0;
    """);
Equal("palette: override", 0x112233u, p.Accent);
Equal("palette: #rgb expands", 0xaabbccu, p.Playing);
Equal("palette: bad hex keeps the default", 0xf8bb71u, p.Label);
Equal("palette: short hex keeps the default", "#34323a", p.Hex("border"));
Equal("palette: commented out keeps the default", 0x958f92u, p.Text);
Equal("palette: rgb() keeps the default", "#17161d", p.Hex("raised"));
Equal("palette: upper case hex", 0xa0b0c0u, p.Bright);
Equal("palette: empty file is the default", d.Accent, Palette.Parse("").Accent);
Equal("palette: garbage is the default", d.Accent, Palette.Parse("}{ @define-color").Accent);
var open = Palette.Parse("@define-color accent #010203;\n/* unclosed\n@define-color playing #040506;");
Equal("palette: before an unclosed comment counts", 0x010203u, open.Accent);
Equal("palette: an unclosed comment runs to the end", 0xffb1c4u, open.Playing);
var tight = Palette.Parse("@define-color\taccent\n#0a0b0c;@define-color playing #0d0e0f ;/**/@define-color label #101112;");
Equal("palette: tabs and newlines are space", 0x0a0b0cu, tight.Accent);
Equal("palette: space before the semicolon", 0x0d0e0fu, tight.Playing);
Equal("palette: an empty comment", 0x101112u, tight.Label);
Equal("palette: no space after the name keeps the default", d.Accent, Palette.Parse("@define-color accent#010203;").Accent);
Equal("palette: no semicolon keeps the default", d.Accent, Palette.Parse("@define-color accent #010203").Accent);
Equal("palette: a broken one doesn't hide the next", 0x010203u, Palette.Parse("@define-color accent; @define-color accent #010203;").Accent);
Equal("palette: every default has a name", Palette.Defaults.Length, Palette.Defaults.Select(x => x.Name).Distinct().Count());

// ---- flex wrap lines ---------------------------------------------------------------

Flex.Item Item(int basis, double grow = 0, int min = 0) => new() { Min = min, Basis = basis, Grow = grow };
string Lines(Flex.Item[] items, int width, int gap)
{
    var lines = new List<(int From, int To)>();
    Flex.Lines(items, width, gap, lines);
    return string.Join(" | ", lines.Select(l => string.Join(" ", items[l.From..l.To].Select(c => c.Width))));
}
Equal("flex: natural width", 330, Flex.Natural([Item(100), Item(200)], 30));
Equal("flex: natural of nothing", 0, Flex.Natural([], 30));
Equal("flex: one line, no growing", "100 200", Lines([Item(100), Item(200)], 400, 10));
Equal("flex: spare width to the one that grows", "190 200", Lines([Item(100, 1), Item(200)], 400, 10));
Equal("flex: spare width by weight", "130 260", Lines([Item(100, 1), Item(200, 2)], 400, 10));
Equal("flex: too wide wraps", "300 | 300", Lines([Item(200, 1), Item(200, 1)], 300, 10));
Equal("flex: exactly fits stays on one line", "100 100", Lines([Item(100), Item(100)], 210, 10));
Equal("flex: one too wide shrinks, not below its min", "350", Lines([Item(400, 0, 350)], 300, 10));
Equal("flex: one too wide shrinks to the width", "300", Lines([Item(400, 0, 50)], 300, 10));
Equal("flex: three lines", "100 | 100 | 100", Lines([Item(100), Item(100), Item(100)], 150, 10));
Equal("flex: nothing, no lines", "", Lines([], 300, 10));

// ---- what the window has room for ----------------------------------------------------

string Names(Room.Part parts) => string.Join(",", Enum.GetValues<Room.Part>().Where(p => p != 0 && parts.HasFlag(p)));
Equal("room: level 0 hides nothing", "", Names(Room.Hidden(0)));
Equal("room: level 1 hides the key hints only", "Hints", Names(Room.Hidden(1)));
Equal("room: level 3 hides hints, visualizer, panel", "Hints,Visualizer,Panel", Names(Room.Hidden(3)));
Equal("room: the last level hides all four", "Hints,Visualizer,Panel,Times", Names(Room.Hidden(Room.Levels - 1)));
// What each level needed in the window on this machine (stage 1's sample, Cascadia Code 14 px).
(int, int)[] needs = [(1217, 752), (1114, 749), (1056, 745), (554, 342), (470, 342)];
string HiddenAt(int w, int h) => Names(Room.Hides(needs, w, h));
Equal("room: wide shows everything", "", HiddenAt(1654, 1000));
Equal("room: exactly the key bar's width shows everything", "", HiddenAt(1217, 760));
Equal("room: a pixel less hides the hints", "Hints", HiddenAt(1216, 760));
Equal("room: then the visualizer", "Hints,Visualizer", HiddenAt(1113, 760));
Equal("room: then the panel", "Hints,Visualizer,Panel", HiddenAt(1055, 760));
Equal("room: ~900 wide", "Hints,Visualizer,Panel", HiddenAt(900, 760));
Equal("room: ~600 wide keeps the times", "Hints,Visualizer,Panel", HiddenAt(600, 760));
Equal("room: then the times", "Hints,Visualizer,Panel,Times", HiddenAt(553, 760));
Equal("room: the least window", "Hints,Visualizer,Panel,Times", HiddenAt(470, 342));
Equal("room: smaller than any level is the last", "Hints,Visualizer,Panel,Times", HiddenAt(300, 200));
// Too short but wide enough: only the panel hides.
Equal("room: short but wide hides only the panel", "Panel", HiddenAt(1400, 500));
Equal("room: a pixel short hides only the panel", "Panel", HiddenAt(1400, 751));
Equal("room: exactly tall enough hides nothing", "", HiddenAt(1400, 752));
Equal("room: short at level 1 keeps the visualizer", "Hints,Panel", HiddenAt(1200, 500));
Equal("room: short at level 2 adds the panel", "Hints,Visualizer,Panel", HiddenAt(1100, 500));
Equal("room: short where the panel hides by width changes nothing", "Hints,Visualizer,Panel", HiddenAt(900, 400));
Equal("room: short and narrow still hides the times by width only", "Hints,Visualizer,Panel,Times", HiddenAt(500, 342));
Equal("room: short keeps the times", "Hints,Visualizer,Panel", HiddenAt(600, 342));
Check("room: no height hides the hints, visualizer or times",
    Enumerable.Range(0, 80).All(i => (Room.Hides(needs, 1300, 300 + i * 10) & ~Room.Part.Panel) == Room.Part.None));
Check("room: height only ever adds the panel to what width hides",
    Enumerable.Range(0, 120).All(i => (Room.Hides(needs, 1300 - i * 7, 400) & ~Room.Part.Panel) == (Room.Hides(needs, 1300 - i * 7, 2000) & ~Room.Part.Panel)));
var shrinking = Enumerable.Range(0, 120).Select(i => Room.Hides(needs, 1300 - i * 7, 800)).ToArray();
Check("room: hides one more at a time as it narrows, in the order, never fewer",
    shrinking.Zip(shrinking.Skip(1)).All(p => p.Second == p.First || p.Second == (Room.Part)(((int)p.First << 1) | 1)));
Check("room: comes back in reverse as it widens",
    shrinking.Reverse().SequenceEqual(Enumerable.Range(0, 120).Select(i => Room.Hides(needs, 1300 - 119 * 7 + i * 7, 800))));
Equal("room: the least window is the last level", Room.Hidden(Room.Levels - 1), Room.Hides(needs, needs[^1].Item1, needs[^1].Item2));

// ---- easing and the pills ---------------------------------------------------------

Equal("ease: 0", 0.0, Ease.Out.At(0));
Equal("ease: 1", 1.0, Ease.Out.At(1));
Check("ease: linear-ish curve is monotonic", Enumerable.Range(1, 99).All(i => Ease.Css.At(i / 100.0) >= Ease.Css.At((i - 1) / 100.0)));
Check("ease: the spring overshoots", Enumerable.Range(0, 101).Any(i => Ease.Spring.At(i / 100.0) > 1.05));
Check("ease: ease-in-out is symmetric", Math.Abs(Ease.InOut.At(.3) + Ease.InOut.At(.7) - 1) < 1e-4);
Check("ease: alternate comes back", Math.Abs(Ease.InOut.Alternate(1.5, 1) - Ease.InOut.At(.5)) < 1e-9);
Equal("ease: alternate at the turn", 1.0, Ease.Out.Alternate(1, 1));
Equal("pills: 28", 28, Pills.Timing.Length);
Check("pills: durations within .45-.9 s", Pills.Timing.All(t => t.Duration >= .45 && t.Duration <= .9));
Check("pills: delays within 0-1 s", Pills.Timing.All(t => t.Delay >= 0 && t.Delay <= 1));
Check("pills: not all the same", Pills.Timing.Select(t => t.Duration).Distinct().Count() > 10);
Check("pills: scale stays near .18-1 (the spring overshoots a little)",
    Enumerable.Range(0, 2000).All(i => Pills.Scale(i % 28, i * .013) is > .05 and < 1.2));
Equal("pills: first third is accent", 0, Pills.Band(9));
Equal("pills: then label", 1, Pills.Band(10));
Equal("pills: last third is playing", 2, Pills.Band(19));
// The canvas's generator run in JavaScript double arithmetic gives
// 0.57/0.82, 0.81/0.70, 0.74/0.79 for the first three pills.
Equal("pills: the canvas's timing, pill 1", (.57, .82), Pills.Timing[0]);
Equal("pills: the canvas's timing, pill 2", (.81, .70), Pills.Timing[1]);
Equal("pills: the canvas's timing, pill 3", (.74, .79), Pills.Timing[2]);

// ---- simulated playback -----------------------------------------------------------

Listing AlbumOf(params Track[] tracks)
{
    var l = new Listing("spotify:album:m", "Meteora (Bonus Edition)");
    l.Tracks.AddRange(tracks);
    return l;
}

double now = 100;
var sampleAlbum = AlbumOf(Sample.Tracks);
var player = new SimulatedPlayback(new Order(new Queue()), sampleAlbum, 12, 72, true, () => now);
Equal("play: starts at 1:12", 72.0, player.Position);
now += 10;
Equal("play: runs while playing", 82.0, player.Position);
player.Toggle();
now += 30;
Equal("play: holds while paused", 82.0, player.Position);
Check("play: paused", !player.Playing);
player.Toggle();
now += 1;
Equal("play: runs again", 83.0, player.Position);
now += 104;
Equal("play: shows the full length for a second", 187.0, player.Position);
now += 1;
Equal("play: then starts the track again", 0.0, player.Position);
player.Next();
Equal("play: next wraps to the first track", "Foreword", player.Track.Title);
Equal("play: next starts at 0", 0.0, player.Position);
player.Previous();
Equal("play: previous wraps to the last", "Numb", player.Track.Title);
player.Toggle();
player.Next();
Check("play: next keeps paused", !player.Playing);
player.Play(sampleAlbum, 3);
Check("play: playing a track starts it", player.Playing);
Equal("play: the chosen track", "Lying from You", player.Track.Title);
var fired = 0;
player.Changed += () => fired++;
player.Next();
player.Toggle();
Equal("play: changes are announced", 2, fired);
player.Order.Queue.Add(Sample.Tracks[12]);
player.Next();
Equal("play: next takes the queue first", "Numb", player.Track.Title);
Equal("play: and takes it off the queue", 0, player.Order.Queue.Count);
player.Next();
Equal("play: then the list goes on where it was", "Easier to Run", player.Track.Title);

// ---- what plays next: the queue, then the list ------------------------------------------

var orderQueue = new Queue();
var order = new Order(orderQueue);
var orderChanges = 0;
order.Changed += () => orderChanges++;
Equal("order: nothing before anything plays", Track.None, order.Current);
Equal("order: nothing upcoming", null, order.Upcoming(false));
Equal("order: nothing to move on to", null, order.Advance(false));
Equal("order: nor back to", null, order.Back(true));
var five = AlbumOf([.. Sample.Tracks.Take(5)]);
Equal("order: plays the row it's given", "Somewhere I Belong", order.Play(five, 2).Title);
Equal("order: at that row", 2, order.At);
Check("order: the list is its context", order.List == five);
Equal("order: announced", 1, orderChanges);
Equal("order: the list's next comes next", "Lying from You", order.Upcoming(false)?.Title);
orderQueue.Add(Sample.Tracks[12]);
orderQueue.Add(Sample.Tracks[11]);
Equal("order: a queued track comes before the list", "Numb", order.Upcoming(false)?.Title);
Equal("order: the queue's first plays", "Numb", order.Advance(false)?.Title);
Check("order: it came from the queue", order.FromQueue);
Equal("order: and left it", "Session", string.Join(",", orderQueue.Items.Select(t => t.Title)));
Equal("order: the list's place stays", 2, order.At);
Equal("order: the queue's next", "Session", order.Advance(false)?.Title);
Equal("order: then the list goes on after its last", "Lying from You", order.Advance(false)?.Title);
Check("order: from the list again", !order.FromQueue);
Equal("order: at its next row", 3, order.At);
Equal("order: what the queue view shows after the queue", "Hit the Floor", string.Join(",", order.Next(50).Select(t => t.Title)));
Equal("order: the last row", "Hit the Floor", order.Advance(false)?.Title);
Equal("order: past the end, nothing", null, order.Advance(false));
Equal("order: still on the last", "Hit the Floor", order.Current.Title);
Equal("order: nothing shown after the last", 0, order.Next(50).Count());
Equal("order: next wraps when asked", "Foreword", order.Advance(true)?.Title);
Equal("order: back wraps to the last", "Hit the Floor", order.Back(true)?.Title);
Equal("order: back", "Lying from You", order.Back(false)?.Title);
order.Play(five, 0);
Equal("order: back from the first, without wrap, nothing", null, order.Back(false));
orderQueue.Add(Sample.Tracks[12]);
order.Advance(false);
Equal("order: back from a queued track is the list's track before it", "Foreword", order.Back(false)?.Title);
Equal("order: playing a row keeps the queue", 0, orderQueue.Count);
orderQueue.Add(Sample.Tracks[12]);
order.Play(five, 4);
Equal("order: playing a row keeps the queue, still", 1, orderQueue.Count);
var growing = AlbumOf([.. Sample.Tracks.Take(2)]);
order.Play(growing, 1);
orderQueue.Clear();
Equal("order: at the end of what's in", null, order.Upcoming(false));
growing.Tracks.Add(Sample.Tracks[2]);
Equal("order: a page that came in is next", "Somewhere I Belong", order.Upcoming(false)?.Title);
Equal("order: out of range clamps", "Somewhere I Belong", order.Play(growing, 99).Title);
var emptyList = AlbumOf();
Equal("order: an empty list plays nothing", Track.None, order.Play(emptyList, 0));

// ---- shuffle and repeat ---------------------------------------------------------------------

var ten = AlbumOf([.. Enumerable.Range(0, 10).Select(i => new Track($"spotify:track:t{i}", i + 1, $"t{i}", "a", "b", 100))]);
string Titles(IEnumerable<Track> ts) => string.Join(",", ts.Select(t => t.Title));
var mixed = new Order(new Queue(), new Random(7));
var modeChanges = 0;
mixed.Changed += () => modeChanges++;
mixed.Play(ten, 3);
modeChanges = 0;
mixed.SetShuffle(true);
Check("shuffle: on", mixed.Shuffle);
Equal("shuffle: turning it on is a change", 1, modeChanges);
Equal("shuffle: the track it's on stays", "t3", mixed.Current.Title);
var round = mixed.Next(50).ToList();
Equal("shuffle: every other track comes once", "t0,t1,t2,t4,t5,t6,t7,t8,t9", string.Join(",", round.Select(t => t.Title).Order()));
Check("shuffle: in another order than the list's", Titles(round) != "t4,t5,t6,t7,t8,t9,t0,t1,t2" && Titles(round) != "t0,t1,t2,t4,t5,t6,t7,t8,t9", Titles(round));
Equal("shuffle: upcoming is the order's next", round[0].Title, mixed.Upcoming(false)?.Title);
var played = new List<string>();
for (int i = 0; i < 9; i++) played.Add(mixed.Advance(false)!.Title);
Equal("shuffle: next goes through the order as shown", Titles(round), string.Join(",", played));
Equal("shuffle: at the order's end, without repeat, nothing", null, mixed.Advance(false));
Equal("shuffle: and nothing follows", null, mixed.Following());
Equal("shuffle: back goes back through it", round[^2].Title, mixed.Back(false)?.Title);
mixed.Advance(false);
var last = mixed.Current.Title;
var again = mixed.Upcoming(true);
Check("shuffle: round again (n), a new round, not starting with the last", again != null && again.Title != last, again?.Title ?? "");
Equal("shuffle: upcoming said what the new round starts with", again?.Title, mixed.Advance(true)?.Title);
Equal("shuffle: the new round has them all", 9, mixed.Next(50).Count());
mixed.Play(ten, 5);
Equal("shuffle: playing a row starts there", "t5", mixed.Current.Title);
Equal("shuffle: and shuffles the rest after it", 9, mixed.Next(50).Count());
Check("shuffle: without it in", mixed.Next(50).All(t => t.Title != "t5"));
var queuedMix = mixed.Queue;
queuedMix.Add(ten.Tracks[0]);
Equal("shuffle: the queue still comes first", "t0", mixed.Upcoming(false)?.Title);
Equal("shuffle: and plays first", "t0", mixed.Advance(false)?.Title);
Equal("shuffle: the list's place stays", 5, mixed.At);
var growMix = AlbumOf([.. ten.Tracks.Take(4)]);
mixed.Play(growMix, 0);
growMix.Tracks.AddRange(ten.Tracks.Skip(4));
Equal("shuffle: a page that came in is shuffled into what's to come", "t1,t2,t3,t4,t5,t6,t7,t8,t9", string.Join(",", mixed.Next(50).Select(t => t.Title).Order()));
mixed.Play(ten, 2);
mixed.Advance(false);
var atNow = mixed.At;
modeChanges = 0;
mixed.SetShuffle(false);
Check("shuffle: off", !mixed.Shuffle);
Equal("shuffle: off is a change", 1, modeChanges);
Equal("shuffle: off, the list's own order on from where it is", Titles(ten.Tracks.Skip(atNow + 1)), Titles(mixed.Next(50)));
mixed.SetShuffle(false);
Equal("shuffle: off again changes nothing", 1, modeChanges);
var fresh = new Order(new Queue(), new Random(1));
fresh.SetShuffle(true);
fresh.Play(ten, 0);
Equal("shuffle: on before anything plays, a row still starts there", "t0", fresh.Current.Title);
var none = new Order(new Queue(), new Random(1));
none.SetShuffle(true);
Equal("shuffle: on with no list, nothing comes", null, none.Advance(true));

var rep = new Order(new Queue());
Equal("repeat: off to begin with", Order.Repeats.Off, rep.Repeat);
var repChanges = 0;
rep.Changed += () => repChanges++;
rep.CycleRepeat();
Equal("repeat: then the list", Order.Repeats.List, rep.Repeat);
rep.CycleRepeat();
Equal("repeat: then one song", Order.Repeats.One, rep.Repeat);
rep.CycleRepeat();
Equal("repeat: then off again", Order.Repeats.Off, rep.Repeat);
Equal("repeat: each is a change", 3, repChanges);
var three = AlbumOf([.. ten.Tracks.Take(3)]);
rep.Play(three, 2);
Equal("repeat off: after the last, nothing follows", null, rep.Following());
Equal("repeat off: and it ends", null, rep.Finish());
rep.Repeat = Order.Repeats.List;
Equal("repeat list: after the last, the first follows", "t0", rep.Following()?.Title);
Equal("repeat list: and plays", "t0", rep.Finish()?.Title);
Equal("repeat list: on through the list", "t1", rep.Finish()?.Title);
rep.Repeat = Order.Repeats.One;
Equal("repeat one: the same track follows", "t1", rep.Following()?.Title);
Equal("repeat one: and plays again", "t1", rep.Finish()?.Title);
Equal("repeat one: still at its row", 1, rep.At);
rep.Queue.Add(ten.Tracks[9]);
Equal("repeat one: even with a queue", "t1", rep.Finish()?.Title);
Equal("repeat one: n still goes on (the queue first)", "t9", rep.Advance(true)?.Title);
rep.Repeat = Order.Repeats.Off;
var nothingYet = new Order(new Queue()) { Repeat = Order.Repeats.One };
Equal("repeat one: before anything plays, nothing follows", null, nothingYet.Following());
var shuffledRepeat = new Order(new Queue(), new Random(3)) { Repeat = Order.Repeats.List };
shuffledRepeat.SetShuffle(true);
shuffledRepeat.Play(three, 0);
var seen = new List<string> { shuffledRepeat.Current.Title };
for (int i = 0; i < 5; i++) seen.Add(shuffledRepeat.Finish()!.Title);
Equal("repeat list, shuffled: each round has every track once", "t0,t1,t2|t0,t1,t2", string.Join(",", seen.Take(3).Order()) + "|" + string.Join(",", seen.Skip(3).Order()));

// The player: preload and the end follow repeat.
var repSent = new List<string>();
string RepSent() { var all = string.Join(" | ", repSent); repSent.Clear(); return all; }
var repPlayer = new SpotifyPlayback(l => repSent.Add(l.TrimEnd('\n')), () => 0, new Order(new Queue()));
repPlayer.Play(three, 2);
repPlayer.Receive("playing\tspotify:track:t2\t0");
RepSent();
repPlayer.Order.Repeat = Order.Repeats.List;
repPlayer.Receive("near-end\tspotify:track:t2");
Equal("helper: repeating the list, the first is preloaded after the last", "preload\tspotify:track:t0", RepSent());
repPlayer.Receive("end\tspotify:track:t2");
Equal("helper: and plays", "load\tspotify:track:t0\t1\t0", RepSent());
repPlayer.Receive("playing\tspotify:track:t0\t0");
repPlayer.Order.Repeat = Order.Repeats.One;
repPlayer.Receive("near-end\tspotify:track:t0");
Equal("helper: repeating one, nothing to preload (it's loaded)", "", RepSent());
repPlayer.Receive("end\tspotify:track:t0");
Equal("helper: and it's loaded again at its end", "load\tspotify:track:t0\t1\t0", RepSent());
Check("helper: playing on", repPlayer.Playing);
repPlayer.Order.Repeat = Order.Repeats.Off;
repPlayer.SetVolume(0.343);
Equal("helper: the volume is a command", "volume\t0.343", RepSent());
repPlayer.SetVolume(0);
Equal("helper: silent is 0", "volume\t0", RepSent());

Equal("mpris: loop status none", "None", Mpris.LoopStatus(Order.Repeats.Off));
Equal("mpris: loop status playlist", "Playlist", Mpris.LoopStatus(Order.Repeats.List));
Equal("mpris: loop status track", "Track", Mpris.LoopStatus(Order.Repeats.One));
Equal("mpris: none back", Order.Repeats.Off, Mpris.RepeatOf("None"));
Equal("mpris: playlist back", Order.Repeats.List, Mpris.RepeatOf("Playlist"));
Equal("mpris: track back", Order.Repeats.One, Mpris.RepeatOf("Track"));
Equal("mpris: a status it doesn't have", null, Mpris.RepeatOf("track"));

// ---- the volume -------------------------------------------------------------------------

Equal("volume: full is a gain of 1", 1.0, Volume.GainOf(100));
Equal("volume: none is silent", 0.0, Volume.GainOf(0));
Check("volume: 50 % is about -18 dB", Math.Abs(20 * Math.Log10(Volume.GainOf(50)) + 18.06) < 0.01, Volume.GainOf(50).ToString());
Check("volume: 10 % is -60 dB", Math.Abs(20 * Math.Log10(Volume.GainOf(10)) + 60) < 0.01);
Check("volume: each step quieter than the last", Enumerable.Range(1, 100).All(p => Volume.GainOf(p) > Volume.GainOf(p - 1)));
Equal("volume: out of range is brought in", 1.0, Volume.GainOf(150));
Equal("volume: below is silent", 0.0, Volume.GainOf(-5));
Equal("volume: gain as the helper takes it", "0.125", Volume.GainText(Volume.GainOf(50)));
Equal("volume: a long fraction is cut to six places", "0.000125", Volume.GainText(Volume.GainOf(5)));
var vol = new Volume(70);
var volChanges = 0;
vol.Changed += () => volChanges++;
vol.Up();
Equal("volume: + steps up 5", 75, vol.Percent);
vol.Down(); vol.Down();
Equal("volume: - steps down 5", 65, vol.Percent);
vol.Set(98); vol.Up();
Equal("volume: no further than 100", 100, vol.Percent);
var volBefore = volChanges;
vol.Up();
Equal("volume: at 100, + changes nothing", volBefore, volChanges);
vol.Set(3); vol.Down();
Equal("volume: no lower than 0", 0, vol.Percent);
Equal("volume: each change said", 7, volChanges);
Equal("volume: a click at the bar's start", 0, Volume.AtX(0, 120));
Equal("volume: a click in its middle", 50, Volume.AtX(60, 120));
Equal("volume: a drag past its end", 100, Volume.AtX(400, 120));
Equal("volume: a drag before its start", 0, Volume.AtX(-30, 120));
Equal("volume: a bar not laid out yet", 0, Volume.AtX(10, 0));
Equal("volume: MPRIS's 0.7", 70, Volume.FromMpris(0.7));
Equal("volume: MPRIS's 1.5 is full", 100, Volume.FromMpris(1.5));
Equal("volume: MPRIS's -1 is silent", 0, Volume.FromMpris(-1));
Equal("volume: MPRIS's NaN is silent", 0, Volume.FromMpris(double.NaN));
Equal("volume: as MPRIS has it", 0.7, new Volume(70).Mpris);
Equal("volume: full to begin with", 100, new Volume().Percent);

// ---- what's remembered between launches ------------------------------------------------

var rem = Remembered.Parse("volume 35\nplaylist-width 300\n");
Equal("remembered: the volume", 35, rem.Volume);
Equal("remembered: the playlist width", 300, rem.PlaylistWidth);
Equal("remembered: written as read", "volume 35\nplaylist-width 300\n", rem.Format());
Equal("remembered: and read back", 35, Remembered.Parse(rem.Format()).Volume);
var odd = Remembered.Parse("  volume   80  \r\nwhat 3\nplaylist-width wide\nvolume\n\n");
Equal("remembered: spaces and CRLF are fine", 80, odd.Volume);
Equal("remembered: a value that isn't a number is left at its default", PlaylistColumn.Default, odd.PlaylistWidth);
Equal("remembered: out of range is brought in (volume)", 100, Remembered.Parse("volume 400").Volume);
Equal("remembered: out of range is brought in (narrow)", PlaylistColumn.Min, Remembered.Parse("playlist-width 20").PlaylistWidth);
Equal("remembered: out of range is brought in (wide)", PlaylistColumn.Max, Remembered.Parse("playlist-width 9000").PlaylistWidth);
Equal("remembered: negative volume is silent", 0, Remembered.Parse("volume -3").Volume);
Equal("remembered: nothing, the defaults", "volume 100\nplaylist-width 270\n", Remembered.Parse("").Format());
var remDir = Path.Combine(Path.GetTempPath(), "nattsang-tests-" + Environment.ProcessId);
var remPath = Path.Combine(remDir, "nattsang", "state");
Equal("remembered: no file, the defaults", 100, Remembered.Load(remPath).Volume);
new Remembered { Volume = 45, PlaylistWidth = 410 }.Save(remPath);
var back = Remembered.Load(remPath);
Equal("remembered: saved and loaded (volume)", 45, back.Volume);
Equal("remembered: saved and loaded (width)", 410, back.PlaylistWidth);
Check("remembered: no part file left", !File.Exists(remPath + ".part"));
new Remembered { Volume = 60 }.Save(remPath);
Equal("remembered: saved over", 60, Remembered.Load(remPath).Volume);
Remembered.Load(null);
Directory.Delete(remDir, true);
var stateWas = Environment.GetEnvironmentVariable("XDG_STATE_HOME");
Environment.SetEnvironmentVariable("XDG_STATE_HOME", "/x/state");
Equal("remembered: under XDG_STATE_HOME", "/x/state/nattsang/state", Remembered.FilePath());
Environment.SetEnvironmentVariable("XDG_STATE_HOME", "");
Check("remembered: else under ~/.local/state", Remembered.FilePath()?.EndsWith("/.local/state/nattsang/state") == true, Remembered.FilePath() ?? "");
Environment.SetEnvironmentVariable("XDG_STATE_HOME", stateWas);

// The playlist column beside an open playlist.
Equal("column: as wanted, with room", 270, PlaylistColumn.Shown(270, 1000, 13));
Equal("column: no wider than 520", 520, PlaylistColumn.Shown(900, 2000, 13));
Equal("column: no narrower than 180", 180, PlaylistColumn.Shown(50, 1000, 13));
Equal("column: narrower to leave the songs room", 600 - 13 - PlaylistColumn.SongsMin, PlaylistColumn.Shown(500, 600, 13));
Equal("column: but never under 180", 180, PlaylistColumn.Shown(500, 300, 13));
Equal("column: clamp", 520, PlaylistColumn.Clamp(521));

// ---- playlist names -------------------------------------------------------------------------

Equal("names: as it is", "Discover Weekly", Keys.Named("Discover Weekly"));
Equal("names: without the spaces around it", "Nekochan", Keys.Named("Nekochan "));
Equal("names: runs of spaces made one", "dúr glad o Fangorn", Keys.Named("  dúr  glad o Fangorn   "));
Equal("names: a tab is a space", "a b", Keys.Named("a\tb"));
Equal("names: the service's own name stays whole", "of 17615273. metal", Keys.Named("of 17615273. metal"));
Equal("names: none", "untitled", Keys.Named("   "));
Equal("names: emoji kept", "ts tuff 🥀", Keys.Named("ts tuff 🥀"));

// ---- the colours file, read again ------------------------------------------------------------

var matugenFile = """
    /* written by matugen from the wallpaper */
    @define-color background #030306;
    @define-color text #988c9e;
    @define-color bright #b8abbf;
    @define-color accent #80d4d9;
    @define-color playing #ffb1c4;
    @define-color label #f8bb71;
    @define-color border #3b353e;
    @define-color raised #1d1a20;
    @define-color hover #131116;
    @define-color groove #29252b;
    @define-color dim #7c7381;
    @define-color handle #5e5762;
    @define-color muted #4a434d;
    """;
var fromMatugen = Palette.Parse(matugenFile);
Equal("colours: every name read (background)", "#030306", fromMatugen.Hex("background"));
Equal("colours: every name read (muted)", "#4a434d", fromMatugen.Hex("muted"));
Equal("colours: every name read (border)", 0x3b353eu, fromMatugen.Border);
Check("colours: not the same as the built-in", !fromMatugen.Same(Palette.Default));
Check("colours: the same file again is the same", fromMatugen.Same(Palette.Parse(matugenFile)));
Check("colours: one colour changed is a change", !fromMatugen.Same(Palette.Parse(matugenFile.Replace("#80d4d9", "#80d4da"))));
Check("colours: a comment changed is not", fromMatugen.Same(Palette.Parse(matugenFile.Replace("from the wallpaper", "again"))));
var halfWritten = Palette.Parse(matugenFile[..(matugenFile.IndexOf("@define-color label", StringComparison.Ordinal) + 22)]);
Equal("colours: a file cut short keeps what came", "#ffb1c4", halfWritten.Hex("playing"));
Equal("colours: and the built-in for the rest", Palette.Default.Hex("muted"), halfWritten.Hex("muted"));
Check("colours: an empty file is the built-in", Palette.Parse("").Same(Palette.Default));
var templateDir = Path.Combine(Path.GetTempPath(), "nattsang-colors-" + Environment.ProcessId);
var configWas = Environment.GetEnvironmentVariable("XDG_CONFIG_HOME");
Environment.SetEnvironmentVariable("XDG_CONFIG_HOME", templateDir);
Directory.CreateDirectory(Path.Combine(templateDir, "nattsang"));
File.WriteAllText(Palette.FilePath(), matugenFile);
Check("colours: loaded from the file", Palette.Load().Same(fromMatugen));
File.WriteAllText(Palette.FilePath(), matugenFile.Replace("#ffb1c4", "#ff0000"));
Equal("colours: loaded again after it changed", "#ff0000", Palette.Load().Hex("playing"));
File.Delete(Palette.FilePath());
Check("colours: gone, the built-in", Palette.Load().Same(Palette.Default));
Directory.Delete(templateDir, true);
Environment.SetEnvironmentVariable("XDG_CONFIG_HOME", configWas);

// ---- covers ---------------------------------------------------------------------------------

Equal("covers: the service's are named by their id", "ab67616d0000b2735c0d2a8b6e2bc8f9e0f8f8f8", Covers.Key("https://i.scdn.co/image/ab67616d0000b2735c0d2a8b6e2bc8f9e0f8f8f8"));
Check("covers: any other is a hash", Covers.Key("https://example.com/a/cover.jpg").StartsWith("url-"));
Check("covers: never a path", !Covers.Key("https://i.scdn.co/image/../../etc").Contains('/'));
Equal("covers: no URL, no name", "", Covers.Key(""));
Check("covers: two URLs, two names", Covers.Key("https://e.com/1.jpg") != Covers.Key("https://e.com/2.jpg"));
var coverDir = Path.Combine(Path.GetTempPath(), "nattsang-covers-" + Environment.ProcessId);
Equal("covers: none kept", null, Covers.Load(coverDir, "https://e.com/1.jpg"));
Covers.Save(coverDir, "https://e.com/1.jpg", [1, 2, 3]);
Equal("covers: kept", "1,2,3", string.Join(",", Covers.Load(coverDir, "https://e.com/1.jpg") ?? []));
Directory.Delete(coverDir, true);

// ---- where the library panel is ----------------------------------------------------------

var nav = new Navigation(Keys.Playlists);
Equal("nav: a tab is its top", "library › playlists", nav.Crumbs);
Equal("nav: under the tab", Keys.Playlists, nav.Under);
Check("nav: nowhere to go back to at the top", !nav.Back());
nav.Open("spotify:playlist:x", "Night drive");
Equal("nav: open a playlist", "library › playlists › Night drive", nav.Crumbs);
Equal("nav: here", "spotify:playlist:x", nav.Here.Key);
nav.Open("spotify:playlist:x", "Night drive");
Equal("nav: opening where it is doesn't stack", 2, nav.Path.Count);
Check("nav: back", nav.Back());
Equal("nav: back at the tab", "library › playlists", nav.Crumbs);
nav.Tab(Keys.Artists);
nav.Open("spotify:artist:lp", "Linkin Park");
nav.Open("spotify:album:m", "Meteora");
Equal("nav: an artist's album", "library › artists › Linkin Park › Meteora", nav.Crumbs);
Check("nav: back to the artist by its part", nav.BackTo(1));
Equal("nav: at the artist", "library › artists › Linkin Park", nav.Crumbs);
Check("nav: its own part goes nowhere", !nav.BackTo(1));
Check("nav: a part past the end goes nowhere", !nav.BackTo(7));
nav.Open("spotify:album:m", "Meteora");
Check("nav: the tab's part goes to its top", nav.BackTo(0));
Equal("nav: at the tab's top", "library › artists", nav.Crumbs);
nav.Open("spotify:artist:lp", "Linkin Park");
nav.Tab(Keys.Albums);
Equal("nav: a tab starts over", "library › albums", nav.Crumbs);
nav.Open(Keys.Search("numb"), Navigation.SearchName("numb"));
Equal("nav: a search opens from where it is", "library › albums › search “numb”", nav.Crumbs);
nav.Open(Keys.Search("faint "), Navigation.SearchName("faint "));
Equal("nav: a search from a search's results takes its place", "library › albums › search “faint”", nav.Crumbs);
nav.Open("spotify:album:m", "spotify:album:m");
nav.Rename("spotify:album:m", "Meteora (Bonus Edition)");
Equal("nav: the player's name for a place", "library › albums › search “faint” › Meteora (Bonus Edition)", nav.Crumbs);
nav.Rename("spotify:album:m", "");
Equal("nav: an empty name changes nothing", "Meteora (Bonus Edition)", nav.Here.Name);
nav.Rename(Keys.Albums, "Albums!");
Equal("nav: a tab keeps its name", "albums", nav.Path[0].Name);
Check("nav: back from the album to the search", nav.Back() && Keys.IsSearch(nav.Here.Key));

// ---- the lists' keys --------------------------------------------------------------------

Equal("keys: the tabs in the design's order", "liked,playlists,albums,artists", string.Join(",", Keys.Tabs));
Equal("keys: liked has tracks", Rows.Tracks, Keys.RowsOf(Keys.Liked));
Equal("keys: playlists has playlists", Rows.Playlists, Keys.RowsOf(Keys.Playlists));
Equal("keys: albums has albums", Rows.Albums, Keys.RowsOf(Keys.Albums));
Equal("keys: artists has artists", Rows.Artists, Keys.RowsOf(Keys.Artists));
Equal("keys: a playlist has tracks", Rows.Tracks, Keys.RowsOf("spotify:playlist:x"));
Equal("keys: a named playlist has tracks", Rows.Tracks, Keys.RowsOf("spotify:user:me:playlist:x"));
Equal("keys: an album has tracks", Rows.Tracks, Keys.RowsOf("spotify:album:x"));
Equal("keys: an artist has albums", Rows.Albums, Keys.RowsOf("spotify:artist:x"));
Equal("keys: a search has tracks", Rows.Tracks, Keys.RowsOf(Keys.Search("numb")));
Equal("keys: a search's key", "search:songs:numb", Keys.Search("  numb "));
Check("keys: a search is a search", Keys.IsSearch(Keys.Search("x")) && !Keys.IsSearch("spotify:album:x"));
Equal("keys: an empty search says so", "no songs found", Keys.Empty(Keys.Search("x")));

// ---- the helper's protocol ------------------------------------------------------

Equal("protocol: a command line", "load\tspotify:track:abc\t1\t0\n", Protocol.Line("load", "spotify:track:abc", "1", "0"));
Equal("protocol: one field", "pause\n", Protocol.Line("pause"));
Equal("protocol: a tab or line break in a field becomes a space", "get\tsearch:songs:Linkin Park  x\n", Protocol.Line("get", "search:songs:Linkin\tPark\r\nx"));
Equal("protocol: fields", "playing|spotify:track:abc|72000", string.Join("|", Protocol.Fields("playing\tspotify:track:abc\t72000\n")));
Equal("protocol: fields without a line break", "paused|u|5", string.Join("|", Protocol.Fields("paused\tu\t5")));
Equal("protocol: a CRLF line", "end|u", string.Join("|", Protocol.Fields("end\tu\r\n")));
Equal("protocol: an empty field stays", "error||x", string.Join("|", Protocol.Fields("error\t\tx")));
Equal("protocol: spaces are part of a field", "Somewhere I Belong", Protocol.Fields("x\tSomewhere I Belong")[1]);
Check("protocol: ms to seconds", Protocol.Seconds("72000", out var s72) && s72 == 72.0);
Check("protocol: 0 ms", Protocol.Seconds("0", out var s0) && s0 == 0);
Check("protocol: ms keep their fraction", Protocol.Seconds("187533", out var s187) && s187 == 187.533);
foreach (var bad in new[] { "", "-5", "+5", "1.5", "12a", " 12", "99999999999999999999" })
    Check($"protocol: rejects ms \"{bad}\"", !Protocol.Seconds(bad, out _));
Equal("protocol: seconds to ms", "72500", Protocol.Milliseconds(72.5));
Equal("protocol: no negative ms", "0", Protocol.Milliseconds(-3));
var it = Protocol.ItemTrack(Protocol.Fields("item-track\tspotify:track:numb\tNumb\tLinkin Park\tMeteora\t185586\tspotify:album:m\tspotify:artist:lp"), 13);
Equal("protocol: a list's track", new Track("spotify:track:numb", 13, "Numb", "Linkin Park", "Meteora", 185, "spotify:album:m", "spotify:artist:lp"), it);
Equal("protocol: a track without its album and artist", "", Protocol.ItemTrack(Protocol.Fields("item-track\tu\tT\tA\tB\t1000"), 1)?.AlbumUri);
Check("protocol: a track with a field missing", Protocol.ItemTrack(Protocol.Fields("item-track\tu\tT\tA\tB"), 1) == null);
Check("protocol: a track with no uri", Protocol.ItemTrack(Protocol.Fields("item-track\t\tT\tA\tB\t1000"), 1) == null);
Check("protocol: a track with a bad length", Protocol.ItemTrack(Protocol.Fields("item-track\tu\tT\tA\tB\t-1"), 1) == null);
Check("protocol: extra fields are fine", Protocol.ItemTrack(Protocol.Fields("item-track\tu\tT\tA\tB\t1000\ta\tb\tmore"), 1) != null);
Equal("protocol: an album row", new Entry("spotify:album:m", "Meteora", "Linkin Park", "2003"), Protocol.ItemEntry(Protocol.Fields("item-album\tspotify:album:m\tMeteora\tLinkin Park\t2003")));
Equal("protocol: an artist row", new Entry("spotify:artist:lp", "Linkin Park", "", ""), Protocol.ItemEntry(Protocol.Fields("item-artist\tspotify:artist:lp\tLinkin Park")));
Equal("protocol: a playlist row", new Entry("spotify:playlist:x", "Night drive", "", "42"), Protocol.ItemEntry(Protocol.Fields("item-playlist\tspotify:playlist:x\tNight drive\t\t42")));
Check("protocol: a row with no uri", Protocol.ItemEntry(Protocol.Fields("item-album\t\tMeteora")) == null);
Check("protocol: a row with no name", Protocol.ItemEntry(Protocol.Fields("item-album\tu")) == null);
Equal("protocol: get is a command line", "get\tliked\n", Protocol.Line("get", Keys.Liked));
Equal("protocol: more is a command line", "more\tspotify:playlist:x\n", Protocol.Line("more", "spotify:playlist:x"));

// ---- the library through the helper (fed lines as the helper writes them) ----------------

var asked = new List<string>();
string Asked() { var all = string.Join(" | ", asked); asked.Clear(); return all; }
var lib = new SpotifyLibrary(l => asked.Add(l.TrimEnd('\n')));
var libChanged = new List<string>();
lib.Changed += l => libChanged.Add(l.Key);
var liked = lib.Open(Keys.Liked, "liked");
Equal("library: a list is asked for once", "get\tliked", Asked());
Check("library: and loads", liked.Loading && liked.Count == 0);
Check("library: it's kept", lib.Open(Keys.Liked, "liked") == liked);
Equal("library: kept, it isn't asked for again", "", Asked());
lib.Receive("list\tliked\t0\t");
lib.Receive("item-track\tspotify:track:a\tForeword\tLinkin Park\tMeteora\t13000\tspotify:album:m\tspotify:artist:lp");
lib.Receive("item-track\tbroken");
lib.Receive("item-album\tspotify:album:m\tMeteora\tLinkin Park\t2003");
lib.Receive("item-track\tspotify:track:b\tNumb\tLinkin Park\tMeteora\t185000");
Equal("library: nothing announced before the page's end", 0, libChanged.Count);
lib.Receive("list-end\tliked\t1");
Equal("library: the page's tracks, a broken line and a wrong kind left out", "Foreword,Numb", string.Join(",", liked.Tracks.Select(t => t.Title)));
Equal("library: numbered in the list's order", 2, liked.Tracks[1].Number);
Check("library: done loading, more to come", !liked.Loading && liked.More);
Equal("library: the page's end is announced", "liked", string.Join(",", libChanged));
lib.More(liked);
Equal("library: more asks for the next page", "more\tliked", Asked());
lib.More(liked);
Equal("library: not twice while one is on its way", "", Asked());
lib.Receive("list\tliked\t5\t");
lib.Receive("item-track\tspotify:track:x\tX\tY\tZ\t1000");
lib.Receive("list-end\tliked\t0");
Equal("library: a page out of step is left out", 2, liked.Count);
lib.Receive("list\tliked\t2\t");
lib.Receive("item-track\tspotify:track:c\tFaint\tLinkin Park\tMeteora\t162000");
lib.Receive("list-end\tliked\t0");
Equal("library: the next page adds on", "Foreword,Numb,Faint", string.Join(",", liked.Tracks.Select(t => t.Title)));
Check("library: and that was all", !liked.More);
lib.More(liked);
Equal("library: no more asks for nothing", "", Asked());
var albums = lib.Open(Keys.Albums, "albums");
Asked();
lib.Receive("list\talbums\t0\t");
lib.Receive("item-album\tspotify:album:m\tMeteora (Bonus Edition)\tLinkin Park\t2003");
lib.Receive("item-track\tspotify:track:a\tForeword\tLinkin Park\tMeteora\t13000");
lib.Receive("list-end\talbums\t0");
Equal("library: album rows", "Meteora (Bonus Edition)|Linkin Park|2003", $"{albums.Entries[0].Name}|{albums.Entries[0].Detail}|{albums.Entries[0].Side}");
Equal("library: a track in a list of albums is left out", 1, albums.Count);
var meteora = lib.Open("spotify:album:m", "Meteora");
Asked();
lib.Receive("list\tspotify:album:m\t0\tMeteora (Bonus Edition)");
lib.Receive("list-end\tspotify:album:m\t0");
Equal("library: the player's name for it", "Meteora (Bonus Edition)", meteora.Title);
Equal("library: an empty album", 0, meteora.Count);
var artists = lib.Open(Keys.Artists, "artists");
Asked();
lib.Receive("list-error\tartists\tthe service is turning requests away for now (429); try again in 30 s");
Equal("library: a failed list says why", "the service is turning requests away for now (429); try again in 30 s", artists.Error);
Check("library: and isn't loading", !artists.Loading);
Check("library: a failed list is asked for again", lib.Open(Keys.Artists, "artists") == artists && Asked() == "get\tartists");
Check("library: and its error is gone while it loads", artists.Error == null && artists.Loading);
lib.Receive("list-end\tunknown\t1");
lib.Receive("item-track\tspotify:track:z\tZ\tZ\tZ\t1000");
lib.Receive("list\tunknown\t0\t");
Equal("library: lines for lists it never asked for change nothing", 3, liked.Count);
var pl = lib.Open("spotify:playlist:x", "Night drive");
Asked();
lib.Ended("It exited with code 1.");
Check("library: the helper gone, what was loading has failed", pl.Error?.StartsWith("The player stopped.") == true && !pl.Loading);
Check("library: and a new list fails at once, asking nothing", lib.Open("spotify:playlist:y", "y").Error != null && Asked() == "");

var sample = new SampleLibrary();
Equal("sample library: Meteora is the saved album", Sample.Album, sample.Open(Keys.Albums, "albums").Entries[0].Name);
Equal("sample library: and opens to its tracks", 13, sample.Open(Sample.AlbumUri, Sample.Album).Count);
Equal("sample library: the artist opens to the album", Sample.AlbumUri, sample.Open(Sample.ArtistUri, Sample.Artist).Entries[0].Uri);
Equal("sample library: a search looks through the album", "Numb", string.Join(",", sample.Open(Keys.Search("numb"), "numb").Tracks.Select(t => t.Title)));
Equal("sample library: tracks know their album and artist", Sample.ArtistUri, Sample.Tracks[0].ArtistUri);

// ---- playing through the helper (fed lines as the helper writes them) ------------------

double clock = 1000;
var sent = new List<string>();
var real = new SpotifyPlayback(l => sent.Add(l.TrimEnd('\n')), () => clock, new Order(new Queue()));
var realChanges = 0;
real.Changed += () => realChanges++;
string Sent() { var all = string.Join(" | ", sent); sent.Clear(); return all; }
Equal("helper: starts out starting", SpotifyPlayback.Starting, real.Status);
Equal("helper: no track before signing in", Track.None, real.Track);
real.Toggle();
real.Next();
real.Previous();
Equal("helper: nothing to play before signing in sends nothing", "", Sent());
real.Receive("hello\t1");
Equal("helper: hello in its version is quiet", SpotifyPlayback.Starting, real.Status);
real.Receive("needs-signin");
Equal("helper: needs sign-in shows it", SpotifyPlayback.SignIn, real.Status);
real.Receive("connecting");
Equal("helper: connecting", SpotifyPlayback.Connecting, real.Status);
real.Receive("signed-in\tblomst");
Equal("helper: signed in says who", "Signed in as blomst", real.Status?.Title);
Equal("helper: signed in asks for nothing itself", "", Sent());
Equal("helper: nothing chosen yet", Track.None, real.Track);
var album3 = AlbumOf(
    new Track("spotify:track:a", 1, "Foreword", "Linkin Park", "Meteora (Bonus Edition)", 13),
    new Track("spotify:track:b", 2, "Don't Stay", "Linkin Park", "Meteora (Bonus Edition)", 187),
    new Track("spotify:track:c", 3, "Somewhere I Belong", "Linkin Park", "Meteora (Bonus Edition)", 213));
real.Play(album3, 0);
Equal("helper: playing a row loads it, playing", "load\tspotify:track:a\t1\t0", Sent());
Check("helper: playing as asked", real.Playing);
Equal("helper: and plays from that list", album3, real.Order.List);
clock += 5;
Equal("helper: held at 0:00 while it loads", 0.0, real.Position);
real.Receive("playing\tspotify:track:a\t0");
Equal("helper: playing clears the sign-in status", null, real.Status);
clock += 3;
Equal("helper: runs once it plays", 3.0, real.Position);
real.Toggle();
Equal("helper: space while playing pauses", "pause", Sent());
Check("helper: still playing until it says so", real.Playing);
real.Receive("paused\tspotify:track:a\t3200");
Check("helper: paused", !real.Playing);
clock += 60;
Equal("helper: holds while paused", 3.2, real.Position);
real.Toggle();
Equal("helper: space while paused plays", "play", Sent());
real.Receive("playing\tspotify:track:a\t3200");
clock += 100;
Equal("helper: never past the track's end", 13.0, real.Position);
var c0 = realChanges;
real.Play(album3, 2);
Equal("helper: playing another row loads it", "load\tspotify:track:c\t1\t0", Sent());
Equal("helper: and shows it at once", "Somewhere I Belong", real.Track.Title);
real.Receive("playing\tspotify:track:a\t12000");
Equal("helper: a report for the track it left is old news", 0.0, real.Position);
real.Receive("track\tspotify:track:c\tSomewhere I Belong\tLinkin Park\tMeteora (Bonus Edition)\t213800");
Equal("helper: the track's own length wins", 213, real.Track.Seconds);
real.Receive("track\tspotify:track:c\tSomewhere I Belong\tLinkin Park\tMeteora (Bonus Edition)\t213900");
Equal("helper: the same length changes nothing", 213, real.Track.Seconds);
real.Receive("playing\tspotify:track:c\t0");
clock += 10;
real.Receive("position\tspotify:track:c\t100000");
Equal("helper: a position report moves it", 100.0, real.Position);
clock += 1;
Equal("helper: and it runs on from there", 101.0, real.Position);
real.Seek(42.5);
Equal("helper: seek", "seek\t42500", Sent());
Check("helper: changes were announced", realChanges > c0);
real.Receive("end\tspotify:track:c");
Equal("helper: the end of the list's last track stops there", "", Sent());
Check("helper: not playing after the list's end", !real.Playing);
Equal("helper: still on the last track", "Somewhere I Belong", real.Track.Title);
Equal("helper: at its end", 213.0, real.Position);
real.Next();
Equal("helper: next wraps to the first, paused as it was", "load\tspotify:track:a\t0\t0", Sent());
real.Receive("paused\tspotify:track:a\t0");
real.Toggle();
real.Receive("playing\tspotify:track:a\t0");
Sent();
real.Receive("end\tspotify:track:a");
Equal("helper: the end of a track plays the next", "load\tspotify:track:b\t1\t0", Sent());
real.Previous();
Equal("helper: previous", "load\tspotify:track:a\t1\t0", Sent());
real.Receive("unavailable\tspotify:track:a");
Equal("helper: unavailable says so", "Can't play Foreword", real.Status?.Title);
Check("helper: unavailable stops", !real.Playing);
real.Toggle();
Equal("helper: space after that loads it again", "load\tspotify:track:a\t1\t0", Sent());
real.Receive("playing\tspotify:track:a\t0");
Equal("helper: playing clears the status", null, real.Status);
real.Receive("stopped\tspotify:track:a");
Check("helper: stopped", !real.Playing);
real.Receive("");
real.Receive("nonsense\tfrom\tthe future");
real.Receive("playing");
real.Receive("playing\tspotify:track:a");
real.Receive("playing\tspotify:track:a\tsoon");
real.Receive("list\tliked\t0\t");
real.Receive("item-track\tspotify:track:a\tForeword\tLinkin Park\tMeteora\t13000");
Check("helper: lines it doesn't know, or short ones, change nothing", !real.Playing && real.Status == null);
real.Receive("error\tcan't find it");
Equal("helper: an error shows", new Status("Something went wrong", "can't find it"), real.Status);
real.Ended("It exited with code 1.");
Equal("helper: gone after an error shows the error", new Status("The player stopped", "can't find it"), real.Status);

var gone = new SpotifyPlayback(_ => { }, () => clock, new Order(new Queue()));
gone.Ended("It was killed by signal 9.");
Equal("helper: gone with no error says how", new Status("The player stopped", "It was killed by signal 9."), gone.Status);
var other = new SpotifyPlayback(_ => { }, () => clock, new Order(new Queue()));
other.Receive("hello\t2");
Equal("helper: another protocol version says so", "The player doesn't fit", other.Status?.Title);

// The queue plays before the rest of the list, and is preloaded like it.
var queued = new Queue();
var pre = new SpotifyPlayback(l => sent.Add(l.TrimEnd('\n')), () => clock, new Order(queued));
queued.Add(album3.Tracks[2]);
pre.Toggle();
Equal("helper: space with nothing played plays the queue", "load\tspotify:track:c\t1\t0", Sent());
Equal("helper: which leaves it", 0, queued.Count);
pre.Receive("playing\tspotify:track:c\t0");
pre.Receive("end\tspotify:track:c");
Equal("helper: the queue's last, and no list: it stops", "", Sent());
pre.Play(album3, 0);
pre.Receive("playing\tspotify:track:a\t0");
Sent();
pre.Receive("near-end\tspotify:track:a");
Equal("helper: near the end it preloads the list's next", "preload\tspotify:track:b", Sent());
pre.Receive("near-end\tspotify:track:b");
Equal("helper: near the end of a track it isn't on, nothing", "", Sent());
pre.Receive("near-end");
Equal("helper: a near-end without a track, nothing", "", Sent());
queued.Add(album3.Tracks[2]);
pre.Receive("near-end\tspotify:track:a");
Equal("helper: a queued track is preloaded before the list's next", "preload\tspotify:track:c", Sent());
pre.Receive("end\tspotify:track:a");
Equal("helper: the end then loads the queued one", "load\tspotify:track:c\t1\t0", Sent());
Equal("helper: and takes it off the queue", 0, queued.Count);
pre.Receive("playing\tspotify:track:c\t0");
pre.Receive("near-end\tspotify:track:c");
Equal("helper: after the queue, the list goes on where it was", "preload\tspotify:track:b", Sent());
pre.Receive("end\tspotify:track:c");
Equal("helper: and plays on from there", "load\tspotify:track:b\t1\t0", Sent());
pre.Receive("playing\tspotify:track:b\t0");
pre.Receive("end\tspotify:track:b");
pre.Receive("playing\tspotify:track:c\t0");
Sent();
pre.Receive("near-end\tspotify:track:c");
Equal("helper: near the end of the list's last track there's nothing to preload", "", Sent());
queued.Add(album3.Tracks[0]);
pre.Next();
Equal("helper: n plays the queue first too", "load\tspotify:track:a\t1\t0", Sent());

// The account: premium plays; any other type says why, and keeps saying it
// when the helper exits.
var premium = new SpotifyPlayback(_ => { }, () => clock, new Order(new Queue()));
premium.Receive("signed-in\tblomst");
var signedIn = premium.Status;
premium.Receive("account\tpremium");
Equal("helper: a premium account changes nothing", signedIn, premium.Status);
var free = new SpotifyPlayback(_ => { }, () => clock, new Order(new Queue()));
free.Receive("account\tfree");
Equal("helper: a free account says playback needs Premium", SpotifyPlayback.NeedsPremium("free"), free.Status);
Check("helper: and names the account's type", free.Status?.Detail.StartsWith("This is a free account;") == true, free.Status?.Detail ?? "");
free.Ended("It exited with code 1.");
Equal("helper: gone after that still says it needs Premium", "Playback needs Premium", free.Status?.Title);
var unknown = new SpotifyPlayback(_ => { }, () => clock, new Order(new Queue()));
unknown.Receive("account");
Check("helper: an account with no type isn't Premium either", unknown.Status?.Detail.StartsWith("This account isn't Premium;") == true, unknown.Status?.Detail ?? "");
Equal("helper: preload is a command line", "preload\tspotify:track:x\n", Protocol.Line("preload", "spotify:track:x"));

// ---- synced lyrics: LRC --------------------------------------------------------------

var lrc = Lyrics.Synced("[ar:Linkin Park]\n[ti:Numb]\n[00:21.88] I'm tired of being what you want me to be\r\n[00:26.40]Feeling so faithless, lost under the surface\n[00:31.00]\n[01:02.5][02:10.25]I've become so numb\n[00:35]plain stamp\nno stamp here\n[xx:yy]bad\n");
Equal("lrc: a break goes before a late first line", Lyrics.Break, lrc.Lines[0]);
Equal("lrc: the intro break is at 0", 0.0, lrc.Times[0]);
Equal("lrc: kind", Lyrics.Kind.Synced, lrc.Kind);
Equal("lrc: words trimmed", "I'm tired of being what you want me to be", lrc.Lines[1]);
Equal("lrc: centiseconds", 21.88, lrc.Times[1]);
Equal("lrc: \\r dropped", "Feeling so faithless, lost under the surface", lrc.Lines[2]);
Equal("lrc: a line without words is a break", Lyrics.Break, lrc.Lines[3]);
Equal("lrc: no fraction", 35.0, lrc.Times[4]);
Equal("lrc: in time order", "plain stamp", lrc.Lines[4]);
Equal("lrc: two times, the line twice", 2, lrc.Lines.Count(l => l == "I've become so numb"));
Equal("lrc: tenths", 62.5, lrc.Times[5]);
Equal("lrc: last at its time", 130.25, lrc.Times[^1]);
Equal("lrc: tags, untimed and bad lines left out", 7, lrc.Lines.Length);
Check("lrc: times never go back", lrc.Times.Zip(lrc.Times.Skip(1)).All(p => p.First <= p.Second));
var early = Lyrics.Synced("[00:00.40]first\n[00:05.00]second");
Equal("lrc: no break before a line that starts within a second", "first", early.Lines[0]);
Equal("lrc: offset moves lines earlier", 1.5, Lyrics.Synced("[offset:+500]\n[00:02.00]a").Times[^1]);
Equal("lrc: negative offset later", 2.25, Lyrics.Synced("[offset:-250]\n[00:02.00]a").Times[^1]);
Equal("lrc: never before 0", 0.0, Lyrics.Synced("[offset:+5000]\n[00:02.00]a").Times[0]);
Equal("lrc: word times of enhanced LRC left out", "so numb", Lyrics.Synced("[00:00.10]<00:00.10>so <00:00.60>numb").Lines[0]);
Equal("lrc: a < that isn't a time stays", "a <3 b", Lyrics.Synced("[00:00.10]a <3 b").Lines[0]);
Equal("lrc: mm:ss:xx", 12.34, Lyrics.Synced("[00:12:34]a").Times[^1]);
Equal("lrc: three-digit fractions", 12.345, Lyrics.Synced("[00:12.345]a").Times[^1]);
Equal("lrc: minutes past 99", 6000.0, Lyrics.Synced("[100:00.00]late").Times[^1]);
Equal("lrc: nothing timed is nothing", Lyrics.Kind.None, Lyrics.Synced("just words\nmore words").Kind);
Equal("lrc: empty is nothing", Lyrics.Kind.None, Lyrics.Synced("").Kind);
Equal("lrc: equal times keep their order", "one,two", string.Join(",", Lyrics.Synced("[00:01.00]one\n[00:01.00]two").Lines.Skip(1)));

// ---- the line at the position --------------------------------------------------------

double[] at = [0, 21.88, 26.4, 31, 35];
Equal("line at: before any is the first", 0, Lyrics.LineAt(-1, at));
Equal("line at: 0 s", 0, Lyrics.LineAt(0, at));
Equal("line at: just before a line", 0, Lyrics.LineAt(21.87, at));
Equal("line at: on its time", 1, Lyrics.LineAt(21.88, at));
Equal("line at: between", 2, Lyrics.LineAt(30, at));
Equal("line at: after the last", 4, Lyrics.LineAt(500, at));
Equal("line at: NaN is the first", 0, Lyrics.LineAt(double.NaN, at));
Equal("line at: no lines", 0, Lyrics.LineAt(10, Array.Empty<double>()));
double[] late = [5, 10];
Equal("line at: before a first line that comes late", 0, Lyrics.LineAt(2, late));
Equal("paced: plain lines go by evenly", 5, Lyrics.PacedAt(50, 100, 10));
Equal("paced: the last at the end", 9, Lyrics.PacedAt(100, 100, 10));
Equal("paced: never past it", 9, Lyrics.PacedAt(500, 100, 10));
Equal("paced: no length is the first", 0, Lyrics.PacedAt(50, 0, 10));
var plain = Lyrics.Plain("\n\nFirst line\r\n\nSecond  \n\n");
Equal("plain: blank lines at the ends left out", "First line||Second", string.Join("|", plain.Lines));
Equal("plain: kind", Lyrics.Kind.Plain, plain.Kind);
Equal("plain: nothing is nothing", Lyrics.Kind.None, Lyrics.Plain(" \n ").Kind);

// ---- JSON --------------------------------------------------------------------------------

var json = Json.Parse("""{"id":1,"trackName":"Don\u2019t \"Stay\"","duration":187.0,"instrumental":false,"plainLyrics":null,"list":[1,-2.5e1,true,{}],"esc":"a\\b\/c\n\ud83c\udfb5"}""");
Equal("json: a number", 1.0, Json.Number(json, "id"));
Equal("json: escapes", "Don’t \"Stay\"", Json.Text(json, "trackName"));
Equal("json: null is no text", null, Json.Text(json, "plainLyrics"));
Check("json: false", !Json.Flag(json, "instrumental"));
Equal("json: an array", 4, ((List<object?>)((Dictionary<string, object?>)json!)["list"]!).Count);
Equal("json: exponent", -25.0, ((List<object?>)((Dictionary<string, object?>)json!)["list"]!)[1]);
Equal("json: surrogate pairs", "a\\b/c\n🎵", Json.Text(json, "esc"));
Equal("json: empty array", 0, ((List<object?>)Json.Parse(" [ ] ")!).Count);
foreach (var bad in new[] { "", "{", "[1,]", "{\"a\" 1}", "tru", "\"open", "[1] 2", "{\"a\":1,}", "\"\\x\"" })
    Check($"json: rejects {bad}", Throws(() => Json.Parse(bad)));

// ---- LRCLIB --------------------------------------------------------------------------------

var numb = new Track("spotify:track:2nLtzopw4rPReszdYBJU6h", 13, "Numb", "Linkin Park", "Meteora (Bonus Edition)", 185);
Equal("lrclib: get asks for all four", "https://lrclib.net/api/get?track_name=Numb&artist_name=Linkin%20Park&album_name=Meteora%20%28Bonus%20Edition%29&duration=185", Lrclib.GetUrl(numb));
var feat = numb with { Title = "Numb / Encore - Remastered 2011", Artist = "JAY-Z, Linkin Park" };
Equal("lrclib: search by the name without the suffix, and the first artist", "https://lrclib.net/api/search?track_name=Numb%20%2F%20Encore&artist_name=JAY-Z", Lrclib.SearchUrl(feat));
Equal("lrclib: feat. left out of the search", "Bad Habit", Lrclib.SearchName("Bad Habit (feat. Someone)"));
Equal("lrclib: with left out", "Song", Lrclib.SearchName("Song [with B]"));
Equal("lrclib: other brackets kept for the search", "Don't Stop (Turn It Up)", Lrclib.SearchName("Don't Stop (Turn It Up)"));
Check("lrclib: get needs an album", !Lrclib.CanGet(numb with { Album = "" }));
Check("lrclib: and a length", !Lrclib.CanGet(numb with { Seconds = 0 }));
Check("lrclib: the user agent names the app and the repository", Lrclib.UserAgent.StartsWith("Nattsang/") && Lrclib.UserAgent.Contains("github.com/blomstt/nattsang"));
Equal("lrclib: names compare without case, marks or brackets", Lrclib.Normal("Don't Stay"), Lrclib.Normal("Don’t Stay - Live (2003)"));
Check("lrclib: different names differ", Lrclib.Normal("Numb") != Lrclib.Normal("Numb / Encore"));
Equal("lrclib: letters of any script count", "夜の歌", Lrclib.Normal("夜の歌 (Live)"));
Equal("lrclib: record with synced lyrics", Lyrics.Kind.Synced, Lrclib.FromRecord(Json.Parse("""{"syncedLyrics":"[00:01.00]a","plainLyrics":"a"}"""))?.Kind);
Equal("lrclib: record with plain only", Lyrics.Kind.Plain, Lrclib.FromRecord(Json.Parse("""{"syncedLyrics":null,"plainLyrics":"a"}"""))?.Kind);
Equal("lrclib: synced that doesn't parse falls back to plain", "b", Lrclib.FromRecord(Json.Parse("""{"syncedLyrics":"no times","plainLyrics":"b"}"""))?.Text);
Equal("lrclib: instrumental", Lyrics.Kind.Instrumental, Lrclib.FromRecord(Json.Parse("""{"instrumental":true,"plainLyrics":null,"syncedLyrics":null}"""))?.Kind);
Equal("lrclib: a timed \"Instrumental\" is instrumental", Lyrics.Kind.Instrumental, Lrclib.FromRecord(Json.Parse("""{"syncedLyrics":"[00:00.00]Instrumental"}"""))?.Kind);
Equal("lrclib: so is a plain \"[Instrumental]\"", Lyrics.Kind.Instrumental, Lrclib.FromRecord(Json.Parse("""{"plainLyrics":"[Instrumental]"}"""))?.Kind);
Equal("lrclib: a song with the word in it isn't", Lyrics.Kind.Synced, Lrclib.FromRecord(Json.Parse("""{"syncedLyrics":"[00:01.00]Instrumental\n[00:05.00]then words"}"""))?.Kind);
Equal("lrclib: a record with nothing", null, Lrclib.FromRecord(Json.Parse("""{"instrumental":false}""")));
var found = Json.Parse("""
[{"trackName":"Numb / Encore","duration":185,"syncedLyrics":"[00:01.00]wrong song"},
 {"trackName":"Numb (Live)","duration":200,"syncedLyrics":"[00:01.00]live words\n[00:02.00]more"},
 {"trackName":"Numb","duration":187.5,"plainLyrics":"studio words"},
 {"trackName":"Numb","duration":184,"syncedLyrics":"[00:01.00]studio synced"}]
""");
var chosen = Lrclib.Choose(numb, found);
Equal("lrclib: chooses the same name within 3 s, synced first", Lyrics.Kind.Synced, chosen.Kind);
Check("lrclib: and its words", chosen.Text.Contains("studio synced"), chosen.Text);
var onlyLive = Lrclib.Choose(numb, Json.Parse("""[{"trackName":"Numb (Live)","duration":200,"syncedLyrics":"[00:01.00]live words\n[00:02.00]more"}]"""));
Equal("lrclib: another length gives the words unsynced", Lyrics.Kind.Plain, onlyLive.Kind);
Equal("lrclib: without their times", "live words\nmore", onlyLive.Text);
Equal("lrclib: another song's lyrics are never chosen", Lyrics.Kind.None, Lrclib.Choose(numb, Json.Parse("""[{"trackName":"Numb / Encore","duration":185,"syncedLyrics":"[00:01.00]x"}]""")).Kind);
Equal("lrclib: nothing found", Lyrics.Kind.None, Lrclib.Choose(numb, Json.Parse("[]")).Kind);
Equal("lrclib: not a list", Lyrics.Kind.None, Lrclib.Choose(numb, Json.Parse("{}")).Kind);
Equal("lrclib: instrumental found", Lyrics.Kind.Instrumental, Lrclib.Choose(numb, Json.Parse("""[{"trackName":"Numb","duration":185,"instrumental":true}]""")).Kind);

// ---- the lyrics cache --------------------------------------------------------------------

Equal("cache key: a track of the service's by its id", "spotify-track-2nLtzopw4rPReszdYBJU6h", LyricsCache.Key(numb));
var sampleKey = LyricsCache.Key(Sample.Tracks[12]);
Check("cache key: another by a hash", sampleKey.StartsWith("song-") && sampleKey.Length == 21, sampleKey);
Equal("cache key: the same song, the same key", sampleKey, LyricsCache.Key(Sample.Tracks[12] with { Number = 99 }));
Check("cache key: another length, another key", sampleKey != LyricsCache.Key(Sample.Tracks[12] with { Seconds = 188 }));
Check("cache key: an odd uri can't leave the folder", LyricsCache.Key(numb with { Uri = "spotify:track:../../x" }).StartsWith("song-"));
foreach (var a in new[] { new Lrclib.Answer(Lyrics.Kind.Synced, "[00:01.00]a\n[00:02.00]b"), new Lrclib.Answer(Lyrics.Kind.Plain, "x\n\ny"), new Lrclib.Answer(Lyrics.Kind.Instrumental, ""), Lrclib.Answer.None })
    Equal($"cache: {a.Kind} reads back as written", a, LyricsCache.Parse(LyricsCache.Format(a, 20392), out _));
LyricsCache.Parse(LyricsCache.Format(Lrclib.Answer.None, 20392), out var missDay);
Equal("cache: a miss says the day it was asked", 20392L, missDay);
Equal("cache: an unknown file is nothing", null, LyricsCache.Parse("whatever\ntext", out _));
Equal("cache: a miss without its day is nothing", null, LyricsCache.Parse("none x\n", out _));
var cacheDir = Path.Combine(Path.GetTempPath(), $"nattsang-tests-{Environment.ProcessId}");
var cache = new LyricsCache(cacheDir);
Equal("cache: nothing kept at first", null, cache.Load(numb, DateTime.UtcNow));
cache.Save(numb, new Lrclib.Answer(Lyrics.Kind.Synced, "[00:01.00]a"));
Equal("cache: what was saved loads", Lyrics.Kind.Synced, cache.Load(numb, DateTime.UtcNow)?.Kind);
cache.Save(Sample.Tracks[0], Lrclib.Answer.None, new DateTime(2026, 10, 31, 23, 0, 0, DateTimeKind.Utc));
Equal("cache: a miss is kept", Lyrics.Kind.None, cache.Load(Sample.Tracks[0], new DateTime(2026, 11, 1, 1, 0, 0, DateTimeKind.Utc))?.Kind);
Equal("cache: for a week", Lyrics.Kind.None, cache.Load(Sample.Tracks[0], new DateTime(2026, 11, 7, 23, 0, 0, DateTimeKind.Utc))?.Kind);
Equal("cache: a miss older than a week is asked again", null, cache.Load(Sample.Tracks[0], new DateTime(2026, 11, 8, 0, 30, 0, DateTimeKind.Utc)));
Equal("cache: lyrics found are kept for good", Lyrics.Kind.Synced, cache.Load(numb, DateTime.UtcNow + TimeSpan.FromDays(800))?.Kind);
Check("cache: nothing half-written left", !Directory.GetFiles(cacheDir).Any(f => f.EndsWith(".part")));

// ---- finding lyrics: cache, get, search, and the track moving on -----------------------------

var gets = new List<(string Url, Action<int, string> Done)>();
var shown = new List<string>();
void Shown(Track t, Lyrics.Song? s, string? e) => shown.Add($"{t.Title}:{(s == null ? "error " + e : s.Kind + " " + string.Join("/", s.Lines))}");
Directory.Delete(cacheDir, true);
var source = new LyricsSource(new LyricsCache(cacheDir), (u, d) => gets.Add((u, d)));
source.Find(numb, Shown);
Equal("find: asks get first", 1, gets.Count);
Check("find: by its exact details", gets[0].Url.Contains("/api/get?"), gets[0].Url);
gets[0].Done(200, """{"trackName":"Numb","syncedLyrics":"[00:00.50]I'm tired","plainLyrics":"I'm tired"}""");
Equal("find: shows what get found", "Numb:Synced I'm tired", string.Join(" | ", shown));
shown.Clear(); gets.Clear();
source.Find(numb, Shown);
Equal("find: the second time from the cache, without a request", 0, gets.Count);
Equal("find: at once", "Numb:Synced I'm tired", string.Join(" | ", shown));
shown.Clear();
var stay = Sample.Tracks[1] with { Uri = "spotify:track:stay" };
source.Find(stay, Shown);
gets[0].Done(404, """{"code":404}""");
Equal("find: not found by get searches", 2, gets.Count);
Check("find: the search", gets[1].Url.Contains("/api/search?"), gets[1].Url);
gets[1].Done(200, "[]");
Equal("find: nothing found says so", "Don't Stay:None ", string.Join(" | ", shown));
shown.Clear(); gets.Clear();
source.Find(stay, Shown);
Equal("find: a miss is kept too", "Don't Stay:None ", string.Join(" | ", shown));
shown.Clear(); gets.Clear();
var faint = Sample.Tracks[6] with { Uri = "spotify:track:faint" };
var belong = Sample.Tracks[2] with { Uri = "spotify:track:belong" };
source.Find(faint, Shown);
source.Find(belong, Shown); // the track changed before the first answer came
gets[0].Done(200, """{"syncedLyrics":"[00:01.00]faint words"}""");
Equal("find: an answer for a track that's gone isn't shown", "", string.Join(" | ", shown));
gets[1].Done(200, """{"syncedLyrics":"[00:01.00]belong words"}""");
Equal("find: the newest track's is", "Somewhere I Belong:Synced ♪/belong words", string.Join(" | ", shown));
shown.Clear(); gets.Clear();
source.Find(faint, Shown);
Equal("find: but the gone one's was kept: no request", 0, gets.Count);
Equal("find: and shows from the cache", "Faint:Synced ♪/faint words", string.Join(" | ", shown));
shown.Clear(); gets.Clear();
var hit = Sample.Tracks[4] with { Uri = "spotify:track:hit" };
source.Find(hit, Shown);
gets[0].Done(0, "Could not connect: Network is unreachable");
Equal("find: no answer says why", "Hit the Floor:error Could not connect: Network is unreachable", string.Join(" | ", shown));
Equal("find: and doesn't search", 1, gets.Count);
shown.Clear(); gets.Clear();
source.Find(hit, Shown);
Equal("find: an error isn't kept: asked again", 1, gets.Count);
gets[0].Done(503, """{"name":"ServerOverloaded"}""");
Equal("find: get too busy goes on to the search", 2, gets.Count);
gets[1].Done(503, "busy");
Equal("find: a busy search says so", "Hit the Floor:error LRCLIB is busy just now (503)", string.Join(" | ", shown));
shown.Clear(); gets.Clear();
source.Find(hit, Shown);
gets[0].Done(400, "");
Equal("find: a refusal says the status, no search", "Hit the Floor:error LRCLIB answered 400", string.Join(" | ", shown));
Equal("find: one request", 1, gets.Count);
shown.Clear(); gets.Clear();
var easier = Sample.Tracks[5] with { Uri = "spotify:track:easier" };
source.Find(easier, Shown);
source.Find(belong, Shown);
gets[0].Done(404, "");
Equal("find: a gone track's miss doesn't go on to search", 1, gets.Count);
shown.Clear(); gets.Clear();
source.Find(easier with { Album = "" }, Shown);
Check("find: without an album it searches straight away", gets.Count == 1 && gets[0].Url.Contains("/api/search?"));
gets[0].Done(200, "not json");
Equal("find: a search answer that doesn't read", "Easier to Run:error LRCLIB's answer didn't read", string.Join(" | ", shown));
Directory.Delete(cacheDir, true);

// ---- MPRIS ---------------------------------------------------------------------------------

Equal("mpris: the bus name", "org.mpris.MediaPlayer2.nattsang", Mpris.BusName(false, 42));
Equal("mpris: a second window is an instance", "org.mpris.MediaPlayer2.nattsang.instance42", Mpris.BusName(true, 42));
Equal("mpris: trackid from the id", "/dev/blomst/Nattsang/track/2nLtzopw4rPReszdYBJU6h", Mpris.TrackId(numb));
Equal("mpris: other uris made a path", "/dev/blomst/Nattsang/track/sample_13", Mpris.TrackId(Sample.Tracks[12]));
Equal("mpris: no track", "/org/mpris/MediaPlayer2/TrackList/NoTrack", Mpris.TrackId(Track.None));
var meta = Mpris.Metadata(numb, "https://i.scdn.co/image/ab67616d00001e02");
string Meta(string key) => meta.FirstOrDefault(m => m.Key == key).Value switch
{
    null => "-",
    string[] a => "[" + string.Join(",", a) + "]",
    Mpris.ObjectPath o => "o:" + o.Path,
    var v => v.ToString()!,
};
Equal("mpris: metadata trackid", "o:/dev/blomst/Nattsang/track/2nLtzopw4rPReszdYBJU6h", Meta("mpris:trackid"));
Equal("mpris: metadata length in µs", "185000000", Meta("mpris:length"));
Check("mpris: length is a long (x)", meta.First(m => m.Key == "mpris:length").Value is long);
Equal("mpris: metadata title", "Numb", Meta("xesam:title"));
Equal("mpris: metadata artists, one string as joined", "[JAY-Z, Linkin Park]", Mpris.Metadata(feat, "").First(m => m.Key == "xesam:artist").Value is string[] a1 ? "[" + string.Join(",", a1) + "]" : "-");
Equal("mpris: metadata album", "Meteora (Bonus Edition)", Meta("xesam:album"));
Equal("mpris: metadata url", "https://open.spotify.com/track/2nLtzopw4rPReszdYBJU6h", Meta("xesam:url"));
Equal("mpris: metadata cover", "https://i.scdn.co/image/ab67616d00001e02", Meta("mpris:artUrl"));
Equal("mpris: no cover, no artUrl", "-", Mpris.Metadata(numb, "").FirstOrDefault(m => m.Key == "mpris:artUrl").Key ?? "-");
Equal("mpris: the sample has no url", null, Mpris.Url(Sample.Tracks[0]));
Equal("mpris: nothing playing is the trackid only", "mpris:trackid", string.Join(",", Mpris.Metadata(Track.None, "x").Select(m => m.Key)));
Equal("mpris: microseconds", 72_500_000L, Mpris.Microseconds(72.5));
Equal("mpris: seek forward", 80.0, Mpris.SeekTo(70, 10_000_000, 185));
Equal("mpris: seek before the start is the start", 0.0, Mpris.SeekTo(5, -10_000_000, 185));
Equal("mpris: seek past the end is the next track", null, Mpris.SeekTo(180, 10_000_000, 185));
Equal("mpris: set position on the track", 30.0, Mpris.SetTo(numb, Mpris.TrackId(numb), 30_000_000));
Equal("mpris: set position for another track does nothing", null, Mpris.SetTo(numb, "/dev/blomst/Nattsang/track/x", 30_000_000));
Equal("mpris: set position past the end does nothing", null, Mpris.SetTo(numb, Mpris.TrackId(numb), 200_000_000));
Equal("mpris: set position before 0 does nothing", null, Mpris.SetTo(numb, Mpris.TrackId(numb), -1));
var sim = new SimulatedPlayback(new Order(new Queue()), AlbumOf(Sample.Tracks), 12, 72, true, () => now);
Equal("mpris: playing", "Playing", Mpris.Status(sim));
sim.Toggle();
Equal("mpris: paused", "Paused", Mpris.Status(sim));
sim.Stop();
Equal("mpris: stopped", "Stopped", Mpris.Status(sim));
Equal("stop: back to 0:00", 0.0, sim.Position);
sim.Toggle();
Check("stop: space plays again", sim.Playing && !sim.Stopped);
sim.Seek(100);
Equal("seek: the sample moves", 100.0, sim.Position);
var stopper = new SpotifyPlayback(l => sent.Add(l.TrimEnd('\n')), () => clock, new Order(new Queue()));
Check("helper: stopped before anything plays", stopper.Stopped);
stopper.Stop();
Equal("helper: stop with nothing loaded sends nothing", "", Sent());
stopper.Play(album3, 1);
Sent();
Check("helper: not stopped once loaded", !stopper.Stopped);
stopper.Receive("track\tspotify:track:b\tDon't Stay\tLinkin Park\tMeteora\t187933\thttps://i.scdn.co/image/cover");
Equal("helper: the cover from the track event", "https://i.scdn.co/image/cover", stopper.Cover);
stopper.Stop();
Equal("helper: stop sends stop", "stop", Sent());
stopper.Receive("stopped\tspotify:track:b");
Check("helper: stopped once it says so", stopper.Stopped && !stopper.Playing);
Equal("mpris: the helper's stop is Stopped", "Stopped", Mpris.Status(stopper));
stopper.Play(album3, 2);
Equal("helper: another track's cover isn't kept", "", stopper.Cover);

// ---- the music's levels: the protocol, the frames, the springs ------------------------

string LevelLine(long us, int value) => "levels\t" + us + string.Concat(Enumerable.Repeat("\t" + value, 28));
Check("levels line: reads", Protocol.Levels(Protocol.Fields(LevelLine(5_250_000, 40)), out var lvAt, out var lvValues));
Equal("levels line: heard at, in seconds", 5.25, lvAt);
Equal("levels line: 28 levels", 28, lvValues.Length);
Equal("levels line: 0 to 1", 0.4f, lvValues[0]);
Check("levels line: too few levels", !Protocol.Levels(Protocol.Fields("levels\t100\t1\t2"), out _, out _));
Check("levels line: over 100", !Protocol.Levels(Protocol.Fields(LevelLine(100, 101)), out _, out _));
Check("levels line: a sign", !Protocol.Levels(Protocol.Fields(LevelLine(100, -1)), out _, out _));
Check("levels line: a bad time", !Protocol.Levels(Protocol.Fields(LevelLine(100, 1).Replace("levels\t100", "levels\tx")), out _, out _));
Check("levels line: extra fields are fine", Protocol.Levels(Protocol.Fields(LevelLine(100, 1) + "\textra"), out _, out _));
Equal("levels: the command", "levels\t1\n", Protocol.Line("levels", "1"));

var lv = new Levels();
var into = new double[Levels.Bands];
float[] Flat(float v) => Enumerable.Repeat(v, Levels.Bands).ToArray();
lv.At(10, into);
Check("levels: none yet is silence", into.All(v => v == 0));
lv.Add(10.0, Flat(0.2f));
lv.Add(10.1, Flat(0.6f));
lv.At(9.9, into);
Check("levels: before the first frame is silence", into.All(v => v == 0));
lv.At(10.05, into);
Check("levels: between two frames, between their levels", Math.Abs(into[0] - 0.4) < 1e-6, into[0].ToString());
lv.At(10.1, into);
Check("levels: on a frame, its levels", Math.Abs(into[5] - 0.6) < 1e-6);
lv.At(10.2, into);
Check("levels: just after the last, it holds", Math.Abs(into[5] - 0.6) < 1e-6);
lv.At(10.5, into);
Check("levels: long after the last, silence", into.All(v => v == 0));
Equal("levels: past frames dropped but the last", 1, lv.Count);
lv.Add(20, Flat(.5f)); lv.Add(21, Flat(.5f)); lv.Add(20.5, Flat(.9f));
Equal("levels: a frame from before replaces those after it", 3, lv.Count);
lv.At(20.5, into);
Check("levels: and is the one heard", Math.Abs(into[0] - 0.9) < 1e-6);
var lvCount = lv.Count;
lv.Add(30, new float[3]);
Equal("levels: a frame of the wrong size is left out", lvCount, lv.Count);
for (int i = 0; i < 1000; i++) lv.Add(40 + i, Flat(0));
Check("levels: at most 256 kept", lv.Count <= 256);

var spring = new Pills.Spring();
var peak = 0.0;
for (int i = 0; i < 60; i++) { spring.Step(1, 1 / 60.0); peak = Math.Max(peak, spring.X); }
Check("spring: gets there within a second", Math.Abs(spring.X - 1) < 0.02, spring.X.ToString());
Check("spring: overshoots a little, as the design's curve", peak > 1.03 && peak < 1.2, peak.ToString());
var quick = new Pills.Spring();
for (int i = 0; i < 6; i++) quick.Step(1, 1 / 60.0);
Check("spring: most of the way in a tenth of a second", quick.X > .6, quick.X.ToString());
var flung = new Pills.Spring();
flung.Step(1, 5);
Check("spring: a long stall doesn't fling it", flung.X <= 1.2 && flung.X >= 0);
Equal("pills: silence is the low height", Pills.Low, Pills.LevelScale(0));
Equal("pills: full is full", 1.0, Pills.LevelScale(1));
Check("pills: an overshoot is clamped", Pills.LevelScale(2) <= 1.12 && Pills.LevelScale(-1) >= Pills.Low / 2);

var feedSent = new List<string>();
var feed = new LevelFeed(l => feedSent.Add(l.TrimEnd('\n')));
var feedChanges = 0;
feed.Changed += () => feedChanges++;
feed.Want(true);
Equal("feed: nothing asked before the helper says it measures", 0, feedSent.Count);
feed.Receive("hello\t1");
Check("feed: a helper that doesn't say so doesn't measure", !feed.Measures);
Equal("feed: and isn't asked", 0, feedSent.Count);
feed.Receive("hello\t1\tlevels");
Check("feed: one that says so measures", feed.Measures);
Equal("feed: that's a change", 1, feedChanges);
Equal("feed: and is asked, as wanted", "levels\t1", string.Join(" | ", feedSent));
feed.Receive(LevelLine(1_000_000, 50));
Equal("feed: frames are kept", 1, feed.Levels.Count);
feedSent.Clear();
feed.Want(true);
Equal("feed: wanting again asks nothing", 0, feedSent.Count);
feed.Want(false);
Equal("feed: not wanted, it's told", "levels\t0", string.Join(" | ", feedSent));
Equal("feed: and the frames are dropped", 0, feed.Levels.Count);
feed.Receive(LevelLine(2_000_000, 50));
Equal("feed: a late frame after levels 0 is left out", 0, feed.Levels.Count);
feed.Ended();
Check("feed: a helper gone measures nothing", !feed.Measures);
feedSent.Clear();
feed.Want(true);
Equal("feed: and isn't asked", 0, feedSent.Count);

Console.WriteLine(failed == 0 ? $"all {run} checks passed" : $"{failed} of {run} checks failed");
return failed == 0 ? 0 : 1;

static bool Throws(Action a)
{
    try { a(); return false; }
    catch (FormatException) { return true; }
}
