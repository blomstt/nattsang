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
Equal("lyrics: offset at line 18", -336.0, Lyrics.Offset(18));
Equal("lyrics: offset at line 0 pushes down", 168.0, Lyrics.Offset(0));

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

double now = 100;
var player = new SimulatedPlayback(Sample.Tracks, 12, 72, true, () => now);
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
Equal("play: next wraps to the first track", 0, player.Index);
Equal("play: next starts at 0", 0.0, player.Position);
player.Previous();
Equal("play: previous wraps to the last", 12, player.Index);
player.Toggle();
player.Next();
Check("play: next keeps paused", !player.Playing);
player.Play(3);
Check("play: playing a track starts it", player.Playing);
Equal("play: the chosen track", "Lying from You", player.Track.Title);
var fired = 0;
player.Changed += () => fired++;
player.Next();
player.Toggle();
Equal("play: changes are announced", 2, fired);

Console.WriteLine(failed == 0 ? $"all {run} checks passed" : $"{failed} of {run} checks failed");
return failed == 0 ? 0 : 1;

static bool Throws(Action a)
{
    try { a(); return false; }
    catch (FormatException) { return true; }
}
